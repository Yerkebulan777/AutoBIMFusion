using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Windows.Forms;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Viewport экспорт листа в плоский Model Space временного DWG.
/// Заменяет EXPORTLAYOUT для случаев, когда на листе присутствуют видовые экраны:
/// главный VP становится «линейкой» масштаба, вспомогательные (узлы) переносятся
/// матрицей трансформации, paper-содержимое уходит в Model Space через главный VP.
///
/// Алгоритм (мульти-VP):
/// 1. Главный VP выбирается по максимальному CoverageScore.
/// 2. Для каждого aux VP: объекты в его model-window клонируются и трансформируются
///    матрицей AuxModel→MainModel, затем оригиналы, не входящие в окно главного VP,
///    удаляются (EraseEntitiesOutsideMainWindow). Без этого шага такие оригиналы
///    остаются в базе, потому что frameBounds охватывает весь лист и TrimOutside
///    их не захватывает.
/// 3. Paper-содержимое (рамка, штамп) переносится в Model Space через главный VP.
/// 4. TrimOutside удаляет всё за пределами frameBounds как вторичная защита.
/// </summary>
internal static class ViewportLayoutExporter
{
    private const double MaxScaleMultiplier = 100.0;


    /// <summary>
    /// Максимальный "разумный" линейный размер свежевставленного Ole2Frame (в единицах чертежа).
    /// Если AutoCAD сразу после PASTECLIP сообщает Bounds больше этого значения — считаем их
    /// некорректными и пропускаем путь WcsWidth/Height, сразу задавая геометрию через Position3d.
    /// Диапазон выбран с запасом: реальные листы редко превышают ~10^7 единиц.
    /// </summary>
    private const double MaxReasonableOleDimension = 1e8;

    /// <summary>
    /// Максимальный размер файла изображения для конвертации в OLE (5 МБ).
    /// Большие файлы оставляются как RasterImage во избежание зависаний Clipboard.
    /// </summary>
    private const long MaxOleFileSizeBytes = 5L * 1024 * 1024;

    public static async Task<string> ExportToTempAsync(string sourceFilePath, string fileName, OperationLogger log)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        DocumentCollection docs = AcadApp.DocumentManager;
        Document sourceDoc = docs.Open(sourceFilePath);

        if (!LayoutUtil.TryFindFirstLayout(sourceDoc.Database, out string layoutName))
        {
            log.Warn($"{fileName}: листы не найдены");
            TryCloseDocument(sourceDoc, fileName, log);
            return string.Empty;
        }

        string tempPath = BuildTempPath(fileName);

        if (File.Exists(tempPath))
        {
            File.Delete(tempPath);
        }

        try
        {
            docs.MdiActiveDocument = sourceDoc;

            await docs.ExecuteInCommandContextAsync(async _ =>
            {
                using LayoutEditScope scope = new();

                LayoutManager.Current.CurrentLayout = layoutName;

                // ВАЖНО: Вся логика работы с БД, переключения Layout и вычисления Extents3d
                // должна выполняться строго ВНУТРИ асинхронного делегата ExecuteInCommandContextAsync.
                // Ранее (в ветке master) Task формировался до входа в контекст, из-за чего AutoCAD
                // мог проигнорировать смену листов, frameBounds становился null, и не происходила
                // очистка мусора за рамкой (ModelSpaceTrimmer). Из-за этого габариты чертежа
                // оставались огромными, что приводило к огромным отступам при вставке и разбросу блоков.
                List<LayoutViewportInfo> vps = ViewportCollector.Collect(sourceDoc.Database, layoutName);

                log.Info($"VP: найдено {vps.Count}");

                (Extents3d? frameBounds, HashSet<ObjectId> paperClonedIds) = vps.Count switch
                {
                    0 => ProcessNoVp(sourceDoc.Database, layoutName, log),
                    1 => ProcessSingleVp(sourceDoc.Database, layoutName, vps[0], log),
                    _ => ProcessMultiVp(sourceDoc.Database, layoutName, vps, log)
                };

                if (frameBounds.HasValue)
                {
                    int erased = ModelSpaceTrimmer.TrimOutside(sourceDoc.Database, frameBounds.Value, log);
                    log.Info($"VP: очищено {erased} объектов");
                }

                AcadApp.SetSystemVariable("TILEMODE", 1);
                await sourceDoc.Editor.CommandAsync("._REGEN");

                await EmbedRasterImagesAsync(sourceDoc, paperClonedIds, log);
            }, null);

            using (new AcadWarningSuppressScope())
            {
                sourceDoc.Database.SaveAs(tempPath, DwgVersion.AC1032);
            }

            log.Info($"VP: экспорт завершен ({fileName})");

            return tempPath;
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"VP: ошибка экспорта {fileName}");
            throw new System.Exception($"\n{fileName}: Ошибка экспорта: {ex.Message}", ex);
        }
        finally
        {
            TryCloseDocument(sourceDoc, fileName, log);
        }
    }

    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessMultiVp(Database db, string layoutName, List<LayoutViewportInfo> vps, OperationLogger log)
    {
        log.Info($"VP: мульти-режим ({vps.Count} шт)");

        LayoutViewportInfo main = ClampMainVpScale(LayoutViewportInfo.PickMainViewport(vps), log);

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        foreach (LayoutViewportInfo aux in vps)
        {
            if (aux.VpId == main.VpId)
            {
                continue;
            }

            Matrix3d m = ViewportTransformer.BuildMatrix(main, aux, log);
            ObjectIdCollection toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

            if (toClone.Count > 0)
            {
                ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, m, log, "model-window");
                // Удаляем оригиналы aux VP, которых нет в главном VP.
                // TrimOutside не справляется с этим: frameBounds охватывает весь лист в модельных
                // координатах, и aux-объекты вне главного VP могут попасть в этот диапазон.
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, main.ModelWindow, log);
                log.Info($"VP #{aux.Number}: обработано {cloned.Count} объектов");
            }
            else
            {
                log.Info($"VP #{aux.Number}: 0 объектов");
            }
        }

        (Extents3d? frameBounds, HashSet<ObjectId> paperClonedIds) = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(main, log), log);

        return (frameBounds, paperClonedIds);
    }

    /// <summary>
    /// Один VP: зажимает масштаб, масштабирует Model Space если нужно, переносит Paper в Model Space.
    /// </summary>
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessSingleVp(Database db, string layoutName, LayoutViewportInfo vp, OperationLogger log)
    {
        LayoutViewportInfo clamped = ClampMainVpScale(vp, log);

        double clampRatio = vp.CustomScale / clamped.CustomScale;
        if (clampRatio > 1.0 + 1e-9)
        {
            Matrix3d scaleMatrix = Matrix3d.Scaling(clampRatio, clamped.ViewCenter);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, clampRatio, log);
        }

        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(clamped, log), log);
    }


    private static LayoutViewportInfo ClampMainVpScale(LayoutViewportInfo vp, OperationLogger log)
    {
        double multiplier = 1.0 / vp.CustomScale;

        if (multiplier < MaxScaleMultiplier)
        {
            // Масштаб крупнее 1:100 — зажимаем, чтобы не получить слишком большой Model Space после экспорта.
            log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0} зажат до 1:{MaxScaleMultiplier:F0}");
            return vp with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0} (без зажима)");
        return vp;
    }

    /// <summary>
    /// Нет VP: масштабирует и переносит Paper-содержимое в Model Space с масштабом 1:MaxScaleMultiplier.
    /// </summary>
    private static (Extents3d? Bounds, HashSet<ObjectId> PaperClonedIds) ProcessNoVp(Database db, string layoutName, OperationLogger log)
    {
        log.Info($"VP: нет видовых экранов, масштаб 1:{MaxScaleMultiplier:F0}");

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
        Matrix3d scale = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin);
        Matrix3d matrix = scale * moveToOrigin;

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

    private static void TryCloseDocument(Document doc, string fileName, OperationLogger log)
    {
        try
        {
            doc.CloseAndDiscard();
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"{fileName}: не удалось закрыть документ");
        }
    }

    private static string BuildTempPath(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid()}.dwg");
    }

    private static async Task EmbedRasterImagesAsync(Document doc, HashSet<ObjectId> paperClonedIds, OperationLogger log)
    {
        Database db = doc.Database;
        List<(ObjectId id, string path, Extents3d bounds)> imagesToConvert = CollectRasterImages(doc, paperClonedIds, log);
        if (imagesToConvert.Count == 0)
        {
            return;
        }

        LayoutManager.Current.CurrentLayout = "Model";
        AcadApp.SetSystemVariable("TILEMODE", 1);

        foreach ((ObjectId id, string path, Extents3d bounds) in imagesToConvert)
        {
            await EmbedSingleRasterAsync(doc, db, id, path, bounds, log);
        }

        try
        {
            Clipboard.Clear();
        }
        catch
        {
            // Clipboard может быть занят внешним процессом.
        }
    }

    private static List<(ObjectId id, string path, Extents3d bounds)> CollectRasterImages(Document doc, HashSet<ObjectId> paperClonedIds, OperationLogger log)
    {
        Database db = doc.Database;
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

            if (paperClonedIds.Contains(id))
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

            string? resolvedPath = ResolveRasterPath(doc, def.SourceFileName);
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
            long maxHandleBefore = GetMaxHandleInModelSpace(db);
            log.Info($"OLE вставка: до вставки max Handle = {maxHandleBefore}, точка {targetBounds.MinPoint}, файл {Path.GetFileName(path)}");

            if (!TryCopyImageToClipboard(path, log))
            {
                log.Warn($"Не удалось поместить изображение в Clipboard: {path}");
                return;
            }

            await doc.Editor.CommandAsync("._PASTECLIP", targetBounds.MinPoint);

            ObjectId oleId = FindNewOle2Frame(db, maxHandleBefore, log);
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
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            try
            {
                using FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                using System.Drawing.Image img = System.Drawing.Image.FromStream(fs);
                DataObject data = new(DataFormats.Bitmap, img);
                Clipboard.SetDataObject(data, true, 10, 200);
                return true;
            }
            catch (System.Exception ex)
            {
                log.Warn($"Clipboard попытка {attempt} неудачна для {path}: {ex.Message}");
                Thread.Sleep(100);
            }
        }

        return false;
    }

    private static long GetMaxHandleInModelSpace(Database db)
    {
        long maxHandle = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            maxHandle = Math.Max(maxHandle, id.Handle.Value);
        }

        tr.Commit();
        return maxHandle;
    }

    private static ObjectId FindNewOle2Frame(Database db, long minHandleValue, OperationLogger log)
    {
        ObjectId newestOleId = ObjectId.Null;
        long newestOleHandle = minHandleValue;
        bool loggedUnexpectedType = false;

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            long handle = id.Handle.Value;
            if (handle <= minHandleValue)
            {
                continue;
            }

            if (id.ObjectClass.DxfName == "OLE2FRAME")
            {
                if (handle > newestOleHandle)
                {
                    newestOleHandle = handle;
                    newestOleId = id;
                }

                continue;
            }

            if (!loggedUnexpectedType)
            {
                log.Info($"Новый объект Handle={handle}, тип={id.ObjectClass.DxfName} (ожидался OLE2FRAME)");
                loggedUnexpectedType = true;
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

    private static string? ResolveRasterPath(Document doc, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string? docDir = Path.GetDirectoryName(doc.Name);
        if (string.IsNullOrEmpty(docDir))
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
            combined = Path.GetFullPath(Path.Combine(docDir, rawPath));
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
        string inSameFolder = Path.Combine(docDir, fileNameOnly);
        return File.Exists(inSameFolder) ? inSameFolder : null;
    }
}
