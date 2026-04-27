using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Windows.Forms;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Viewport экспортирует виды из Model Space в отдельный DWG.
/// Альтернатива EXPORTLAYOUT для сценариев, где нужно контролировать процесс:
/// отбор VP осуществляется по CoverageScore, вырезаются (копируются) объекты
/// из Model Space, paper-содержимое переносится в Model Space вместо VP.
///
/// Алгоритм (мульти-VP):
/// 1. Выбор VP осуществляется по CoverageScore.
/// 2. Для каждого aux VP: объекты из model-window копируются с трансформацией
///    масштаба AuxModel/MainModel, обрезаются по границам VP,
///    очищаются (EraseEntitiesOutsideMainWindow). Это нужно чтобы избежать
///    наложения объектов, чтобы при frameBounds можно было без TrimOutside
///    их использовать.
/// 3. Для главного viewport main VP (например, 1:1 -> 1:100) model-объекты
///    масштабируются через clampRatio, чтобы соответствовать
///    размеру paper-содержимого.
/// 4. Paper-содержимое (рамки, штампы) переносится в Model Space вместо VP.
/// 5. TrimOutside очищает всё за границами frameBounds для финального файла.
/// </summary>
internal static class ViewportLayoutExporter
{
    /// <summary>
    /// Максимальный "разумный" размер стороны Ole2Frame (в единицах черчения).
    /// Если AutoCAD после PASTECLIP задаёт Bounds больше этого значения —
    /// переключаемся на вычисление через WcsWidth/Height, иначе используем Position3d.
    /// Защита от багов: некоторые файлы дают значения ~10^7 единиц.
    /// </summary>
    private const double MaxReasonableOleDimension = 1e8;

    /// <summary>
    /// Максимальный размер файла изображения для встраивания в OLE (5 МБ).
    /// Большие файлы не копируются для RasterImage из-за ограничений Clipboard.
    /// </summary>
    private const long MaxOleFileSizeBytes = 5L * 1024 * 1024;

    private const double TargetScale = 0.01; // 1:100 — эталонный масштаб слияния

    public static async Task<string> ExportToTempAsync(string sourceFilePath, string fileName, OperationLogger log)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        string tempPath = BuildTempPath(fileName);
        string sourceDir = Path.GetDirectoryName(sourceFilePath) ?? string.Empty;

        HashSet<long> paperClonedHandles;
        bool needsOle;

        using (Database db = new(false, true))
        {
            db.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            if (!LayoutUtil.TryFindFirstLayout(db, out string layoutName))
            {
                log.Warn($"{fileName}: листы не найдены");
                return string.Empty;
            }

            List<LayoutViewportInfo> vps = ViewportCollector.Collect(db, layoutName);
            log.Info($"VP: найдено {vps.Count}");

            (Extents3d? frameBounds, HashSet<ObjectId> paperClonedIds) = vps.Count switch
            {
                0 => ProcessNoVp(db, layoutName, log),
                1 => ProcessSingleVp(db, layoutName, vps[0], log),
                _ => ProcessMultiVp(db, layoutName, vps, log)
            };

            if (frameBounds.HasValue)
            {
                int erased = ModelSpaceTrimmer.TrimOutside(db, frameBounds.Value, log);
                log.Info($"VP: очищено {erased} объектов");
            }

            using (new AcadWarningSuppressScope())
            {
                db.SaveAs(tempPath, DwgVersion.AC1032);
            }

            // Проверяем на наличие растров для внедрения.
            // Используем Handles, так как при открытии документа ObjectIds изменятся.
            paperClonedHandles = [.. paperClonedIds.Select(id => id.Handle.Value)];
            needsOle = CheckIfNeedsOle(db, paperClonedHandles, sourceDir, log);
        }

        if (needsOle)
        {
            await RunOleEmbeddingAsync(tempPath, paperClonedHandles, sourceDir, log);
        }

        log.Info($"VP: экспорт завершен ({fileName})");
        return tempPath;
    }

    private static bool CheckIfNeedsOle(Database db, HashSet<long> paperClonedHandles, string sourceDir, OperationLogger log)
    {
        // Нам не нужны полные данные, только факт наличия хотя бы одного подходящего изображения
        return CollectRasterImages(db, paperClonedHandles, sourceDir, log).Count > 0;
    }

    private static async Task RunOleEmbeddingAsync(string tempPath, HashSet<long> paperClonedHandles, string sourceDir, OperationLogger log)
    {
        DocumentCollection docs = AcadApp.DocumentManager;
        Document? tempDoc = docs.Open(tempPath);

        if (tempDoc is null)
        {
            log.Warn($"Не удалось открыть временный файл для OLE-встраивания: {tempPath}");
            return;
        }

        try
        {
            docs.MdiActiveDocument = tempDoc;

            await docs.ExecuteInCommandContextAsync(async _ =>
            {
                Database db = tempDoc.Database;
                List<(ObjectId id, string path, Extents3d bounds)> imagesToConvert = CollectRasterImages(db, paperClonedHandles, sourceDir, log);

                if (imagesToConvert.Count == 0)
                {
                    return;
                }

                AcadApp.SetSystemVariable("TILEMODE", 1);
                await tempDoc.Editor.CommandAsync("._REGEN");

                foreach ((ObjectId id, string path, Extents3d bounds) in imagesToConvert)
                {
                    await EmbedSingleRasterAsync(tempDoc, db, id, path, bounds, log);
                }

                try { Clipboard.Clear(); } catch { }
            }, null);

            using (new AcadWarningSuppressScope())
            {
                tempDoc.Database.SaveAs(tempPath, DwgVersion.AC1032);
            }
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка при OLE-встраивании во временный файл");
        }
        finally
        {
            tempDoc.CloseAndDiscard();
        }
    }

    /// <summary>
    /// Multi-VP: главный VP + aux (узлы).
    ///
    /// Порядок операций и почему именно так:
    /// 1. Берём mainOriginal (исходный масштаб) и mainClamped (зажатый до 1:MaxScaleMultiplier).
    /// 2. Матрицы aux→main строим ПО mainOriginal. Если строить по mainClamped, то делитель
    ///    1/main.CustomScale в BuildMatrix окажется в clampRatio раз больше → aux-клоны
    ///    «улетают» в clampRatio раз дальше и крупнее. Это баг из ветки OldVersion.
    /// 3. После клонирования aux весь Model Space масштабируется на clampRatio вокруг
    ///    mainOriginal.ViewCenter (ApplyClampToModelSpace). Выравнивает оригиналы main
    ///    и aux-клоны под масштаб paper-содержимого.
    /// 4. Paper переносится через BuildPaperToMainMatrix(mainClamped).
    /// </summary>
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessMultiVp(Database db, string layoutName, List<LayoutViewportInfo> vps, OperationLogger log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessMultiVp ({vps.Count} viewport'ов)");

        LayoutViewportInfo mainOriginal = LayoutViewportInfo.PickMainViewport(vps);
        bool needsNorm = mainOriginal.CustomScale > TargetScale;
        LayoutViewportInfo mainClamped = needsNorm
            ? mainOriginal with { CustomScale = TargetScale }
            : mainOriginal;
        double clampRatio = needsNorm ? TargetScale / mainOriginal.CustomScale : 1.0;

        log.Info(
            $"VP main#{mainOriginal.Number}: исходный scale={mainOriginal.CustomScale:F6}, " +
            $"рабочий scale={mainClamped.CustomScale:F6}, clampRatio={clampRatio:F6}, " +
            $"центр={ExtentsUtils.FormatPoint(mainOriginal.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        foreach (LayoutViewportInfo aux in vps)
        {
            if (aux.VpId == mainOriginal.VpId)
            {
                continue;
            }

            Matrix3d m = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
            ObjectIdCollection toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

            if (toClone.Count > 0)
            {
                ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, m, log, "model-window");
                // Удаляем оригиналы aux VP, которых нет в главном VP.
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
                log.Info($"aux-VP #{aux.Number}: обработано {cloned.Count} объектов");
            }
            else
            {
                log.Info($"aux-VP #{aux.Number}: 0 объектов");
            }
        }


        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);
    }

    /// <summary>
    /// Один VP: зажимает масштаб, масштабирует Model Space если нужно, переносит Paper в Model Space.
    /// </summary>
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessSingleVp(Database db, string layoutName, LayoutViewportInfo vp, OperationLogger log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessSingleVp (VP #{vp.Number})");

        bool needsNorm = vp.CustomScale > TargetScale;
        LayoutViewportInfo clamped = needsNorm
            ? vp with { CustomScale = TargetScale }
            : vp;
        double clampRatio = needsNorm ? TargetScale / vp.CustomScale : 1.0;

        log.Info(
            $"VP #{vp.Number}: исходный scale={vp.CustomScale:F6}, рабочий scale={clamped.CustomScale:F6}, " +
            $"clampRatio={clampRatio:F6}, центр={ExtentsUtils.FormatPoint(clamped.ViewCenter)}");


        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(clamped, log), log);
    }

    /// <summary>
    /// Нет VP: масштабирует и переносит Paper-содержимое в Model Space с масштабом 1:MaxScaleMultiplier.
    /// </summary>
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessNoVp(Database db, string layoutName, OperationLogger log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:100)");

        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return (null, []);
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

        if (!paperBounds.HasValue)
        {
            return (null, []);
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d moveToOrigin = Matrix3d.Displacement(Point3d.Origin - minPt);
        Matrix3d scale = Matrix3d.Scaling(1.0 / TargetScale, Point3d.Origin);
        Matrix3d matrix = scale * moveToOrigin;

        log.Info(
            $"ProcessNoVp: paper bounds={ExtentsUtils.FormatExtents(paperBounds.Value)}, " +
            $"ratio={1.0 / TargetScale:F2}");

        return MovePaperToModelSpace(db, layoutName, matrix, log, "paper-no-vp");
    }

    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) MovePaperToModelSpace(Database db, string layoutName, Matrix3d matrix, OperationLogger log, string tag = "paper")
    {
        ObjectId paperBtrId = LayoutUtil.GetLayoutBtrId(db, layoutName);
        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return (null, []);
        }

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, paperIds, paperBtrId, msId, matrix, log, tag);

        HashSet<ObjectId> clonedSet = [];
        foreach (ObjectId id in cloned)
        {
            _ = clonedSet.Add(id);
        }

        EraseBlockContents(db, paperBtrId);

        return (ModelSpaceTrimmer.ComputeBounds(db, cloned, log), clonedSet);
    }

    private static void EraseBlockContents(Database db, ObjectId btrId)
    {
        if (btrId.IsNull)
        {
            return;
        }

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

        foreach (ObjectId id in btr)
        {
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity e && !e.IsErased)
            {
                e.Erase();
            }
        }

        tr.Commit();
    }

    private static string BuildTempPath(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid()}.dwg");
    }

    private static List<(ObjectId id, string path, Extents3d bounds)> CollectRasterImages(Database db, HashSet<long> paperClonedHandles, string sourceDir, OperationLogger log)
    {
        List<(ObjectId id, string path, Extents3d bounds)> result = [];

        int totalImages = 0;
        int nullDefCount = 0;
        int nullBoundsCount = 0;
        int fileNotFoundCount = 0;
        int skippedFromPaperCount = 0;
        int tooLargeCount = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            if (id.ObjectClass.DxfName != "IMAGE")
            {
                continue;
            }

            totalImages++;

            if (paperClonedHandles.Contains(id.Handle.Value))
            {
                skippedFromPaperCount++;
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead) is not RasterImage ri || ri.ImageDefId.IsNull)
            {
                nullDefCount++;
                continue;
            }

            if (tr.GetObject(ri.ImageDefId, OpenMode.ForRead) is not RasterImageDef def)
            {
                nullDefCount++;
                continue;
            }

            if (!ri.Bounds.HasValue)
            {
                nullBoundsCount++;
                log.Warn($"RasterImage Handle={id.Handle}: Bounds=null, path={Path.GetFileName(def.SourceFileName)}");
                continue;
            }

            string? resolvedPath = ResolveRasterPath(sourceDir, def.SourceFileName);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                fileNotFoundCount++;
                log.Warn($"RasterImage Handle={id.Handle}: файл не найден: {def.SourceFileName}");
                continue;
            }

            long fileSize = new FileInfo(resolvedPath).Length;
            if (fileSize > MaxOleFileSizeBytes)
            {
                tooLargeCount++;
                log.Info($"RasterImage Handle={id.Handle}: файл {Path.GetFileName(resolvedPath)} слишком большой ({fileSize / (1024.0 * 1024.0):F1} МБ > 5 МБ), пропускаем OLE-конвертацию");
                continue;
            }

            result.Add((id, resolvedPath, ri.Bounds.Value));
        }

        tr.Commit();

        log.Info(
            $"EmbedRasterImages: total={totalImages}, skippedFromPaper={skippedFromPaperCount}, nullDef={nullDefCount}, " +
            $"nullBounds={nullBoundsCount}, notFound={fileNotFoundCount}, tooLarge={tooLargeCount}, readyToConvert={result.Count}");

        return result;
    }

    private static async Task EmbedSingleRasterAsync(Document doc, Database db, ObjectId rasterId, string path, Extents3d targetBounds, OperationLogger log)
    {
        try
        {
            HashSet<ObjectId> snapshotBefore = GetModelSpaceSnapshot(db);
            log.Info($"OLE вставка: до вставки объектов в MS: {snapshotBefore.Count}, точка {targetBounds.MinPoint}, файл {Path.GetFileName(path)}");

            if (!TryCopyImageToClipboard(path, log))
            {
                log.Warn($"Не удалось поместить изображение в Clipboard: {path}");
                return;
            }

            await doc.Editor.CommandAsync("._PASTECLIP", targetBounds.MinPoint);

            ObjectId oleId = FindNewOle2Frame(db, snapshotBefore, log);
            if (oleId.IsNull)
            {
                log.Warn($"PASTECLIP не создал новый OLE2FRAME для {path}. Проверьте OLEQUALITY и Clipboard.");
                return;
            }

            log.Info($"Найден новый OLE2FRAME: Handle={oleId.Handle}, Id={oleId}");

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(oleId, OpenMode.ForWrite) is not Ole2Frame ole)
            {
                log.Warn($"Найденный объект не является Ole2Frame: тип={oleId.ObjectClass.DxfName}");
                tr.Commit();
                return;
            }

            bool positionedByRectangle = ResizeOleToTarget(ole, targetBounds, log);
            if (!positionedByRectangle)
            {
                AlignOleToTargetMinPoint(ole, targetBounds, log);
            }

            if (tr.GetObject(rasterId, OpenMode.ForWrite) is RasterImage originalImage)
            {
                originalImage.Erase();
                log.Info($"Удалён исходный RasterImage: {rasterId.Handle}");
            }

            tr.Commit();
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"Ошибка при встраивании OLE: {path}");
        }
    }

    private static bool TryCopyImageToClipboard(string path, OperationLogger log)
    {
        try
        {
            using System.Drawing.Image img = System.Drawing.Image.FromFile(path);
            Clipboard.SetImage(img);
            return true;
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"Не удалось скопировать изображение в Clipboard: {path}");
            return false;
        }
    }

    private static HashSet<ObjectId> GetModelSpaceSnapshot(Database db)
    {
        HashSet<ObjectId> snapshot = [];
        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            _ = snapshot.Add(id);
        }

        tr.Commit();
        return snapshot;
    }

    private static ObjectId FindNewOle2Frame(Database db, HashSet<ObjectId> snapshotBefore, OperationLogger log)
    {
        ObjectId newestOleId = ObjectId.Null;
        long newestOleHandle = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            if (snapshotBefore.Contains(id))
            {
                continue;
            }

            if (id.ObjectClass.DxfName == "OLE2FRAME")
            {
                // Если создано несколько объектов, берем последний по Handle как наиболее вероятный
                if (id.Handle.Value > newestOleHandle)
                {
                    newestOleHandle = id.Handle.Value;
                    newestOleId = id;
                }
            }
        }

        tr.Commit();
        return newestOleId;
    }

    private static bool ResizeOleToTarget(Ole2Frame ole, Extents3d targetBounds, OperationLogger log)
    {
        double targetWidth = targetBounds.MaxPoint.X - targetBounds.MinPoint.X;
        double targetHeight = targetBounds.MaxPoint.Y - targetBounds.MinPoint.Y;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            log.Warn($"Целевой размер OLE некорректен: {targetWidth:F4} x {targetHeight:F4}");
            return false;
        }

        Extents3d? initialBounds = ole.Bounds;
        if (!initialBounds.HasValue)
        {
            log.Warn("OLE Bounds не определены до масштабирования. Пробуем Position3d fallback.");
            return TryApplyPositionFallback(ole, targetBounds, log);
        }

        double initialWidth = initialBounds.Value.MaxPoint.X - initialBounds.Value.MinPoint.X;
        double initialHeight = initialBounds.Value.MaxPoint.Y - initialBounds.Value.MinPoint.Y;
        log.Info($"OLE размер до масштабирования: {initialWidth:F4} x {initialHeight:F4}, целевой: {targetWidth:F4} x {targetHeight:F4}");

        bool invalidBounds =
            initialWidth <= 0
            || initialHeight <= 0
            || initialWidth > MaxReasonableOleDimension
            || initialHeight > MaxReasonableOleDimension;

        if (invalidBounds)
        {
            log.Warn(
                $"OLE Bounds некорректны: {initialWidth:F4}x{initialHeight:F4}. " +
                "Пропускаем WcsWidth/Height, используем Position3d.");
            return TryApplyPositionFallback(ole, targetBounds, log);
        }

        ApplyWcsSize(ole, targetWidth, targetHeight);

        Extents3d? resizedBounds = ole.Bounds;
        if (!resizedBounds.HasValue)
        {
            log.Warn("После WcsWidth/WcsHeight не удалось получить Bounds. Пробуем Position3d fallback.");
            return TryApplyPositionFallback(ole, targetBounds, log);
        }

        double resizedWidth = resizedBounds.Value.MaxPoint.X - resizedBounds.Value.MinPoint.X;
        double resizedHeight = resizedBounds.Value.MaxPoint.Y - resizedBounds.Value.MinPoint.Y;
        log.Info($"OLE размер после WcsWidth/Height: {resizedWidth:F4} x {resizedHeight:F4}");

        bool resizedCorrectly = IsCloseToTarget(resizedWidth, targetWidth) && IsCloseToTarget(resizedHeight, targetHeight);
        if (resizedCorrectly)
        {
            return false;
        }

        log.Warn(
            $"WcsWidth/WcsHeight не применились корректно: текущий={resizedWidth:F4}x{resizedHeight:F4}, " +
            $"целевой={targetWidth:F4}x{targetHeight:F4}. Пробуем Position3d fallback.");
        return TryApplyPositionFallback(ole, targetBounds, log);
    }

    private static void ApplyWcsSize(Ole2Frame ole, double targetWidth, double targetHeight)
    {
        bool originalLockAspect = ole.LockAspect;
        ole.LockAspect = false;
        ole.WcsWidth = targetWidth;
        ole.WcsHeight = targetHeight;
        ole.LockAspect = originalLockAspect;
    }

    private static bool TryApplyPositionFallback(Ole2Frame ole, Extents3d targetBounds, OperationLogger log)
    {
        try
        {
            Rectangle3d sourceRectangle = ole.Position3d;
            ole.Position3d = BuildTargetRectangle(targetBounds, sourceRectangle);

            Extents3d? boundsAfterFallback = ole.Bounds;
            if (boundsAfterFallback.HasValue)
            {
                double width = boundsAfterFallback.Value.MaxPoint.X - boundsAfterFallback.Value.MinPoint.X;
                double height = boundsAfterFallback.Value.MaxPoint.Y - boundsAfterFallback.Value.MinPoint.Y;
                log.Info($"OLE размер после Position3d fallback: {width:F4} x {height:F4}");
            }

            return true;
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, "Position3d fallback не сработал.");
            return false;
        }
    }

    private static void AlignOleToTargetMinPoint(Ole2Frame ole, Extents3d targetBounds, OperationLogger log)
    {
        Extents3d? currentBounds = ole.Bounds;
        if (!currentBounds.HasValue)
        {
            log.Warn("Не удалось выровнять OLE: Bounds отсутствуют.");
            return;
        }

        Vector3d shift = targetBounds.MinPoint - currentBounds.Value.MinPoint;
        if (shift.Length <= 1e-6)
        {
            return;
        }

        ole.TransformBy(Matrix3d.Displacement(shift));

        Extents3d? movedBounds = ole.Bounds;
        if (!movedBounds.HasValue)
        {
            log.Warn("TransformBy выполнен, но Bounds OLE недоступны после сдвига.");
            return;
        }

        double movedDistance = (movedBounds.Value.MinPoint - currentBounds.Value.MinPoint).Length;
        if (movedDistance > 1e-6)
        {
            log.Info($"TransformBy сработал, смещение {movedDistance:F4}");
            return;
        }

        log.Warn("TransformBy(Displacement) не изменил OLE. Пробуем Position3d fallback.");
        _ = TryApplyPositionFallback(ole, targetBounds, log);
    }

    private static bool IsCloseToTarget(double actual, double target)
    {
        if (target <= 0)
        {
            return false;
        }

        double tolerance = Math.Max(1e-3, target * 0.02);
        return Math.Abs(actual - target) <= tolerance;
    }

    private static Rectangle3d BuildTargetRectangle(Extents3d bounds, Rectangle3d source)
    {
        Point3d lowerLeft = new(bounds.MinPoint.X, bounds.MinPoint.Y, source.LowerLeft.Z);
        Point3d upperLeft = new(bounds.MinPoint.X, bounds.MaxPoint.Y, source.UpperLeft.Z);
        Point3d lowerRight = new(bounds.MaxPoint.X, bounds.MinPoint.Y, source.LowerRight.Z);
        Point3d upperRight = new(bounds.MaxPoint.X, bounds.MaxPoint.Y, source.UpperRight.Z);

        return new Rectangle3d(lowerLeft, upperLeft, lowerRight, upperRight);
    }

    private static string? ResolveRasterPath(string sourceDir, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        if (string.IsNullOrEmpty(sourceDir))
        {
            return null;
        }

        if (Path.IsPathRooted(rawPath) && File.Exists(rawPath))
        {
            return Path.GetFullPath(rawPath);
        }

        string combined;
        try
        {
            combined = Path.GetFullPath(Path.Combine(sourceDir, rawPath));
        }
        catch
        {
            return null;
        }

        if (File.Exists(combined))
        {
            return combined;
        }

        string fileNameOnly = Path.GetFileName(rawPath);
        string inSameFolder = Path.Combine(sourceDir, fileNameOnly);
        return File.Exists(inSameFolder) ? inSameFolder : null;
    }
}







