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
/// </summary>
internal sealed class ViewportLayoutExporter(OperationLogger log)
{
    private const double MaxScaleMultiplier = 100.0;


    /// <summary>
    /// Максимальный "разумный" линейный размер свежевставленного Ole2Frame (в единицах чертежа).
    /// Если AutoCAD сразу после PASTECLIP сообщает Bounds больше этого значения — считаем их
    /// некорректными и пропускаем путь WcsWidth/Height, сразу задавая геометрию через Position3d.
    /// Диапазон выбран с запасом: реальные листы редко превышают ~10^7 единиц.
    /// </summary>
    private const double MaxReasonableOleDimension = 1e8;

    private readonly OperationLogger _log = log;

    public async Task<string> ExportToTempAsync(string sourceFilePath, string fileName)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        DocumentCollection docs = AcadApp.DocumentManager;
        Document sourceDoc = docs.Open(sourceFilePath);

        if (!LayoutUtil.TryFindFirstLayout(sourceDoc.Database, out string layoutName))
        {
            _log.Warn($"{fileName}: листы не найдены");
            TryCloseDocument(sourceDoc, fileName);
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

                _log.Info($"Найдено viewport'ов: {vps.Count}");

                Extents3d? frameBounds = vps.Count switch
                {
                    0 => ProcessNoVp(sourceDoc.Database, layoutName),
                    1 => ProcessSingleVp(sourceDoc.Database, layoutName, vps[0]),
                    _ => ProcessMultiVp(sourceDoc.Database, layoutName, vps)
                };

                if (frameBounds.HasValue)
                {
                    int erased = ModelSpaceTrimmer.TrimOutside(sourceDoc.Database, frameBounds.Value, _log);
                    _log.Info($"Очищено за рамкой: {erased}");
                }

                AcadApp.SetSystemVariable("TILEMODE", 1);
                await sourceDoc.Editor.CommandAsync("._REGEN");

                await EmbedRasterImagesAsync(sourceDoc);
            }, null);

            using (new AcadWarningSuppressScope())
            {
                sourceDoc.Database.SaveAs(tempPath, DwgVersion.AC1032);
            }

            _log.Info($"{fileName} экспортирован");

            return tempPath;
        }
        catch (System.Exception ex)
        {
            _log.Warn(ex, $"{fileName}: ошибка экспорта {ex.Message}");
            throw new System.Exception($"\n{fileName}: Ошибка экспорта: {ex.Message}", ex);
        }
        finally
        {
            TryCloseDocument(sourceDoc, fileName);
        }
    }

    private Extents3d? ProcessMultiVp(Database db, string layoutName, List<LayoutViewportInfo> vps)
    {
        _log.Info($"Multi-VP ветка: {vps.Count} viewport'ов");

        LayoutViewportInfo main = ClampMainVpScale(LayoutViewportInfo.PickMainViewport(vps));

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, _log);

        foreach (LayoutViewportInfo aux in vps)
        {
            if (aux.VpId == main.VpId)
            {
                continue;
            }

            Matrix3d m = ViewportTransformer.BuildMatrix(main, aux, _log);
            ObjectIdCollection toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, _log);

            if (toClone.Count > 0)
            {
                ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, m, _log, "model-window");
                _log.Info($"Обработан aux-VP #{aux.Number}: {cloned.Count} объектов");
            }
            else
            {
                _log.Info($"Обработан aux-VP #{aux.Number}: 0 объектов");
            }
        }

        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(main, _log));

        _log.Info($"Всего обработано aux-VP: {vps.Count - 1}");
        return frameBounds;
    }

    private Extents3d? ProcessSingleVp(Database db, string layoutName, LayoutViewportInfo main)
    {
        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(ClampMainVpScale(main), _log));
    }


    private LayoutViewportInfo ClampMainVpScale(LayoutViewportInfo vp)
    {
        double multiplier = 1.0 / vp.CustomScale;

        if (multiplier < MaxScaleMultiplier)
        {
            _log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0} изменен на 1:{MaxScaleMultiplier:F0}");
            return vp with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        _log.Info($"VP #{vp.Number}: масштаб 1:{multiplier:F0}");

        return vp;
    }

    private Extents3d? ProcessNoVp(Database db, string layoutName)
    {
        _log.Info($"No-VP ветка: viewport'ы не найдены, масштаб по умолчанию 1:{MaxScaleMultiplier:F0}");

        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, _log);

        if (!paperBounds.HasValue)
        {
            return null;
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d moveToOrigin = Matrix3d.Displacement(Point3d.Origin - minPt);
        Matrix3d scale = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin);
        Matrix3d matrix = scale * moveToOrigin;

        return MovePaperToModelSpace(db, layoutName, matrix, "paper-no-vp");
    }

    private Extents3d? MovePaperToModelSpace(Database db, string layoutName, Matrix3d matrix, string tag = "paper")
    {
        ObjectId paperBtrId = LayoutUtil.GetLayoutBtrId(db, layoutName);
        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, paperIds, paperBtrId, msId, matrix, _log, tag);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloned, _log);
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

    private void TryCloseDocument(Document doc, string fileName)
    {
        try
        {
            doc.CloseAndDiscard();
        }
        catch (System.Exception ex)
        {
            _log.Warn(ex, $"{fileName}: не удалось закрыть документ");
        }
    }

    private static string BuildTempPath(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid()}.dwg");
    }

    private async Task EmbedRasterImagesAsync(Document doc)
    {
        Database db = doc.Database;
        List<(ObjectId id, string path, Extents3d bounds)> imagesToConvert = CollectRasterImages(doc);
        if (imagesToConvert.Count == 0)
        {
            return;
        }

        LayoutManager.Current.CurrentLayout = "Model";
        AcadApp.SetSystemVariable("TILEMODE", 1);

        foreach ((ObjectId id, string path, Extents3d bounds) in imagesToConvert)
        {
            await EmbedSingleRasterAsync(doc, db, id, path, bounds);
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

    private List<(ObjectId id, string path, Extents3d bounds)> CollectRasterImages(Document doc)
    {
        Database db = doc.Database;
        List<(ObjectId id, string path, Extents3d bounds)> result = [];

        int totalImages = 0;
        int nullDefCount = 0;
        int nullBoundsCount = 0;
        int fileNotFoundCount = 0;

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
                _log.Warn($"RasterImage Handle={id.Handle}: Bounds=null, path={Path.GetFileName(def.SourceFileName)}");
                continue;
            }

            string? resolvedPath = ResolveRasterPath(doc, def.SourceFileName);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                fileNotFoundCount++;
                _log.Warn($"RasterImage Handle={id.Handle}: файл не найден: {def.SourceFileName}");
                continue;
            }

            result.Add((id, resolvedPath, ri.Bounds.Value));
        }

        tr.Commit();

        _log.Info(
            $"EmbedRasterImages: total={totalImages}, nullDef={nullDefCount}, " +
            $"nullBounds={nullBoundsCount}, fileNotFound={fileNotFoundCount}, toConvert={result.Count}");

        return result;
    }

    private async Task EmbedSingleRasterAsync(Document doc, Database db, ObjectId rasterId, string path, Extents3d targetBounds)
    {
        try
        {
            long maxHandleBefore = GetMaxHandleInModelSpace(db);
            _log.Info($"OLE вставка: до вставки max Handle = {maxHandleBefore}, точка {targetBounds.MinPoint}, файл {Path.GetFileName(path)}");

            if (!TryCopyImageToClipboard(path))
            {
                _log.Warn($"Не удалось поместить изображение в Clipboard: {path}");
                return;
            }

            await doc.Editor.CommandAsync("._PASTECLIP", targetBounds.MinPoint);

            ObjectId oleId = FindNewOle2Frame(db, maxHandleBefore);
            if (oleId.IsNull)
            {
                _log.Warn($"PASTECLIP не создал новый OLE2FRAME для {path}. Проверьте OLEQUALITY и Clipboard.");
                return;
            }

            _log.Info($"Найден новый OLE2FRAME: Handle={oleId.Handle}, Id={oleId}");

            using Transaction tr = db.TransactionManager.StartTransaction();
            if (tr.GetObject(oleId, OpenMode.ForWrite) is not Ole2Frame ole)
            {
                _log.Warn($"Найденный объект не является Ole2Frame: тип={oleId.ObjectClass.DxfName}");
                tr.Commit();
                return;
            }

            bool positionedByRectangle = ResizeOleToTarget(ole, targetBounds);
            if (!positionedByRectangle)
            {
                AlignOleToTargetMinPoint(ole, targetBounds);
            }

            if (tr.GetObject(rasterId, OpenMode.ForWrite) is RasterImage originalImage)
            {
                originalImage.Erase();
                _log.Info($"Удалён исходный RasterImage: {rasterId.Handle}");
            }

            tr.Commit();
        }
        catch (System.Exception ex)
        {
            _log.Warn(ex, $"Ошибка при встраивании OLE: {path}");
        }
    }

    private bool TryCopyImageToClipboard(string path)
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
                _log.Warn($"Clipboard попытка {attempt} неудачна для {path}: {ex.Message}");
                Thread.Sleep(100);
            }
        }

        return false;
    }

    private long GetMaxHandleInModelSpace(Database db)
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

    private ObjectId FindNewOle2Frame(Database db, long minHandleValue)
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
                _log.Info($"Новый объект Handle={handle}, тип={id.ObjectClass.DxfName} (ожидался OLE2FRAME)");
                loggedUnexpectedType = true;
            }
        }

        tr.Commit();
        return newestOleId;
    }

    private bool ResizeOleToTarget(Ole2Frame ole, Extents3d targetBounds)
    {
        double targetWidth = targetBounds.MaxPoint.X - targetBounds.MinPoint.X;
        double targetHeight = targetBounds.MaxPoint.Y - targetBounds.MinPoint.Y;
        if (targetWidth <= 0 || targetHeight <= 0)
        {
            _log.Warn($"Целевой размер OLE некорректен: {targetWidth:F4} x {targetHeight:F4}");
            return false;
        }

        Extents3d? initialBounds = ole.Bounds;
        if (!initialBounds.HasValue)
        {
            _log.Warn("OLE Bounds не определены до масштабирования. Пробуем Position3d fallback.");
            return TryApplyPositionFallback(ole, targetBounds);
        }

        double initialWidth = initialBounds.Value.MaxPoint.X - initialBounds.Value.MinPoint.X;
        double initialHeight = initialBounds.Value.MaxPoint.Y - initialBounds.Value.MinPoint.Y;
        _log.Info($"OLE размер до масштабирования: {initialWidth:F4} x {initialHeight:F4}, целевой: {targetWidth:F4} x {targetHeight:F4}");

        bool invalidBounds =
            initialWidth <= 0
            || initialHeight <= 0
            || initialWidth > MaxReasonableOleDimension
            || initialHeight > MaxReasonableOleDimension;

        if (invalidBounds)
        {
            _log.Warn(
                $"OLE Bounds некорректны: {initialWidth:F4}x{initialHeight:F4}. " +
                "Пропускаем WcsWidth/Height, используем Position3d.");
            return TryApplyPositionFallback(ole, targetBounds);
        }

        ApplyWcsSize(ole, targetWidth, targetHeight);

        Extents3d? resizedBounds = ole.Bounds;
        if (!resizedBounds.HasValue)
        {
            _log.Warn("После WcsWidth/WcsHeight не удалось получить Bounds. Пробуем Position3d fallback.");
            return TryApplyPositionFallback(ole, targetBounds);
        }

        double resizedWidth = resizedBounds.Value.MaxPoint.X - resizedBounds.Value.MinPoint.X;
        double resizedHeight = resizedBounds.Value.MaxPoint.Y - resizedBounds.Value.MinPoint.Y;
        _log.Info($"OLE размер после WcsWidth/Height: {resizedWidth:F4} x {resizedHeight:F4}");

        bool resizedCorrectly = IsCloseToTarget(resizedWidth, targetWidth) && IsCloseToTarget(resizedHeight, targetHeight);
        if (resizedCorrectly)
        {
            return false;
        }

        _log.Warn(
            $"WcsWidth/WcsHeight не применились корректно: текущий={resizedWidth:F4}x{resizedHeight:F4}, " +
            $"целевой={targetWidth:F4}x{targetHeight:F4}. Пробуем Position3d fallback.");
        return TryApplyPositionFallback(ole, targetBounds);
    }

    private static void ApplyWcsSize(Ole2Frame ole, double targetWidth, double targetHeight)
    {
        bool originalLockAspect = ole.LockAspect;
        ole.LockAspect = false;
        ole.WcsWidth = targetWidth;
        ole.WcsHeight = targetHeight;
        ole.LockAspect = originalLockAspect;
    }

    private bool TryApplyPositionFallback(Ole2Frame ole, Extents3d targetBounds)
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
                _log.Info($"OLE размер после Position3d fallback: {width:F4} x {height:F4}");
            }

            return true;
        }
        catch (System.Exception ex)
        {
            _log.Warn(ex, "Position3d fallback не сработал.");
            return false;
        }
    }

    private void AlignOleToTargetMinPoint(Ole2Frame ole, Extents3d targetBounds)
    {
        Extents3d? currentBounds = ole.Bounds;
        if (!currentBounds.HasValue)
        {
            _log.Warn("Не удалось выровнять OLE: Bounds отсутствуют.");
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
            _log.Warn("TransformBy выполнен, но Bounds OLE недоступны после сдвига.");
            return;
        }

        double movedDistance = (movedBounds.Value.MinPoint - currentBounds.Value.MinPoint).Length;
        if (movedDistance > 1e-6)
        {
            _log.Info($"TransformBy сработал, смещение {movedDistance:F4}");
            return;
        }

        _log.Warn("TransformBy(Displacement) не изменил OLE. Пробуем Position3d fallback.");
        _ = TryApplyPositionFallback(ole, targetBounds);
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

    private string? ResolveRasterPath(Document doc, string rawPath)
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
