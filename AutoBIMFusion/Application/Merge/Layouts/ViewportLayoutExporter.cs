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
        List<(ObjectId id, string path, Extents3d bounds)> imagesToConvert = [];

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
            BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

            int totalImages = 0;
            int nullDefCount = 0;
            int nullBoundsCount = 0;
            int fileNotFoundCount = 0;

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

                Extents3d? bounds = ri.Bounds;
                string originalPath = def.SourceFileName;
                string? path = ResolveRasterPath(doc, originalPath);

                if (!bounds.HasValue)
                {
                    nullBoundsCount++;
                    string pathForLog = path ?? originalPath;
                    _log.Warn($"RasterImage Handle={id.Handle}: Bounds=null, path={System.IO.Path.GetFileName(pathForLog)}");
                    continue;
                }

                if (string.IsNullOrEmpty(path) || !System.IO.File.Exists(path))
                {
                    fileNotFoundCount++;
                    _log.Warn($"RasterImage Handle={id.Handle}: файл не найден: {originalPath}");
                    continue;
                }

                imagesToConvert.Add((id, path, bounds.Value));
            }

            _log.Info($"EmbedRasterImages: total={totalImages}, nullDef={nullDefCount}, nullBounds={nullBoundsCount}, fileNotFound={fileNotFoundCount}, toConvert={imagesToConvert.Count}");
            tr.Commit();
        }

        if (imagesToConvert.Count == 0)
        {
            return;
        }

        LayoutManager.Current.CurrentLayout = "Model";
        AcadApp.SetSystemVariable("TILEMODE", 1);

        foreach ((ObjectId id, string path, Extents3d bounds) in imagesToConvert)
        {
            System.IO.FileStream? clipboardFs = null;
            System.Drawing.Image? clipboardImg = null;

            try
            {
                long maxHandleBefore = GetMaxHandleInModelSpace(db);
                _log.Info($"OLE вставка: до вставки max Handle = {maxHandleBefore}, точка {bounds.MinPoint}, файл {System.IO.Path.GetFileName(path)}");

                bool clipboardOk = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    try
                    {
                        clipboardFs = new System.IO.FileStream(path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.Read);
                        clipboardImg = System.Drawing.Image.FromStream(clipboardFs);
                        DataObject dataObj = new(System.Windows.Forms.DataFormats.Bitmap, clipboardImg);
                        System.Windows.Forms.Clipboard.SetDataObject(dataObj, true, 10, 200);
                        clipboardOk = true;
                        break;
                    }
                    catch (System.Exception ex)
                    {
                        _log.Warn($"Clipboard попытка {attempt + 1} неудачна для {path}: {ex.Message}");
                        clipboardImg?.Dispose();
                        clipboardFs?.Dispose();
                        clipboardImg = null;
                        clipboardFs = null;
                        System.Threading.Thread.Sleep(100);
                    }
                }

                if (!clipboardOk)
                {
                    _log.Warn($"Не удалось поместить изображение в Clipboard: {path}");
                    continue;
                }

                await doc.Editor.CommandAsync("._PASTECLIP", bounds.MinPoint);

                ObjectId newOleId = FindNewOle2Frame(db, maxHandleBefore);

                if (newOleId.IsNull)
                {
                    _log.Warn($"PASTECLIP не создал новый OLE2FRAME для {path}. Проверьте OLEQUALITY и Clipboard.");
                    continue;
                }

                _log.Info($"Найден новый OLE2FRAME: Handle={newOleId.Handle}, Id={newOleId}");

                using Transaction tr = db.TransactionManager.StartTransaction();
                if (tr.GetObject(newOleId, OpenMode.ForWrite) is Ole2Frame ole)
                {
                    double targetWidth = bounds.MaxPoint.X - bounds.MinPoint.X;
                    double targetHeight = bounds.MaxPoint.Y - bounds.MinPoint.Y;
                    double containerWidth = targetWidth;
                    double containerHeight = targetHeight;

                    if (clipboardImg is { Width: > 0, Height: > 0 } && targetWidth > 0 && targetHeight > 0)
                    {
                        (targetWidth, targetHeight) = FitSizePreservingAspect(
                            targetWidth,
                            targetHeight,
                            clipboardImg.Width,
                            clipboardImg.Height
                        );

                        _log.Info(
                            $"OLE size with preserved aspect: container={containerWidth:F4}x{containerHeight:F4}, " +
                            $"target={targetWidth:F4}x{targetHeight:F4}, " +
                            $"bitmap={clipboardImg.Width}x{clipboardImg.Height}"
                        );
                    }

                    Extents3d? oleBounds = ole.Bounds;
                    if (oleBounds.HasValue)
                    {
                        double currentWidth = oleBounds.Value.MaxPoint.X - oleBounds.Value.MinPoint.X;
                        double currentHeight = oleBounds.Value.MaxPoint.Y - oleBounds.Value.MinPoint.Y;

                        _log.Info($"OLE размер до масштабирования: {currentWidth:F4} x {currentHeight:F4}, целевой: {targetWidth:F4} x {targetHeight:F4}");

                        if (currentWidth > 0 && currentHeight > 0)
                        {
                            bool boundsLookBogus =
                                currentWidth > MaxReasonableOleDimension
                                || currentHeight > MaxReasonableOleDimension;

                            bool needsPositionResizeFallback;

                            if (boundsLookBogus)
                            {
                                // Свежевставленный PASTECLIP'ом Ole2Frame иногда сообщает
                                // абсурдные Bounds (миллиарды единиц). В этом случае WcsWidth/Height
                                // не применяются, а любые вычисления на таких Bounds
                                // (включая TransformBy(Displacement)) приводят к разрушению геометрии.
                                // Идём сразу на детерминированный путь через Position3d.
                                needsPositionResizeFallback = true;
                                _log.Warn(
                                    $"OLE Bounds выглядят некорректно: {currentWidth:F2}x{currentHeight:F2}. " +
                                    $"Пропускаем WcsWidth/Height, идём сразу на Position3d."
                                );
                            }
                            else
                            {
                                bool originalLockAspect = ole.LockAspect;
                                ole.LockAspect = false;
                                ole.WcsWidth = targetWidth;
                                ole.WcsHeight = targetHeight;
                                ole.LockAspect = originalLockAspect;

                                needsPositionResizeFallback = false;
                                Extents3d? afterBounds = ole.Bounds;
                                if (afterBounds.HasValue)
                                {
                                    double afterWidth = afterBounds.Value.MaxPoint.X - afterBounds.Value.MinPoint.X;
                                    double afterHeight = afterBounds.Value.MaxPoint.Y - afterBounds.Value.MinPoint.Y;
                                    _log.Info($"OLE размер после WcsWidth/Height: {afterWidth:F4} x {afterHeight:F4}");

                                    if (!IsCloseToTarget(afterWidth, targetWidth) || !IsCloseToTarget(afterHeight, targetHeight))
                                    {
                                        needsPositionResizeFallback = true;
                                        _log.Warn(
                                            $"WcsWidth/WcsHeight не применились корректно: текущий={afterWidth:F4}x{afterHeight:F4}, " +
                                            $"целевой={targetWidth:F4}x{targetHeight:F4}. Пробуем Position3d fallback."
                                        );
                                    }
                                }
                                else
                                {
                                    needsPositionResizeFallback = true;
                                    _log.Warn("После WcsWidth/WcsHeight не удалось получить Bounds. Пробуем Position3d fallback.");
                                }
                            }

                            bool positionFallbackApplied = false;
                            if (needsPositionResizeFallback)
                            {
                                try
                                {
                                    Rectangle3d pos = ole.Position3d;
                                    Rectangle3d newPos = BuildTargetRectangle(bounds, pos, targetWidth, targetHeight);
                                    ole.Position3d = newPos;
                                    positionFallbackApplied = true;
                                    _log.Info(
                                        $"Position3d fallback применён: rect=[({bounds.MinPoint.X:F4},{bounds.MinPoint.Y:F4}) -> " +
                                        $"({(bounds.MinPoint.X + targetWidth):F4},{(bounds.MinPoint.Y + targetHeight):F4})]"
                                    );

                                    Extents3d? fallbackBounds = ole.Bounds;
                                    if (fallbackBounds.HasValue)
                                    {
                                        double fbWidth = fallbackBounds.Value.MaxPoint.X - fallbackBounds.Value.MinPoint.X;
                                        double fbHeight = fallbackBounds.Value.MaxPoint.Y - fallbackBounds.Value.MinPoint.Y;
                                        _log.Info($"OLE размер после Position3d fallback: {fbWidth:F4} x {fbHeight:F4}");
                                    }
                                }
                                catch (System.Exception resizeEx)
                                {
                                    _log.Warn(resizeEx, "Position3d fallback для изменения размера OLE не сработал.");
                                }
                            }

                            // Коррекция позиции через TransformBy имеет смысл только если
                            // WcsWidth/Height действительно сработали. При использовании Position3d fallback
                            // прямоугольник уже выставлен по углам bounds.Min..Max — дополнительный сдвиг
                            // не нужен и может сломать корректно заданную геометрию,
                            // т.к. ole.Bounds сразу после изменения ещё может возвращать старое значение.
                            if (!positionFallbackApplied)
                            {
                                Extents3d? finalBounds = ole.Bounds;
                                if (finalBounds.HasValue)
                                {
                                    Point3d actualMin = finalBounds.Value.MinPoint;
                                    Vector3d shift = bounds.MinPoint - actualMin;
                                    if (shift.Length > 1e-6)
                                    {
                                        _log.Info($"Корректировка позиции OLE на {shift}");
                                        ole.TransformBy(Matrix3d.Displacement(shift));

                                        Extents3d? shiftedBounds = ole.Bounds;
                                        if (shiftedBounds.HasValue)
                                        {
                                            double moved = (shiftedBounds.Value.MinPoint - actualMin).Length;
                                            if (moved < 1e-6)
                                            {
                                                _log.Warn("TransformBy(Displacement) не сработал для Ole2Frame. Пробуем Position3d.");
                                                try
                                                {
                                                    Rectangle3d pos = ole.Position3d;
                                                    Rectangle3d newPos = new(
                                                        pos.LowerLeft + shift,
                                                        pos.UpperLeft + shift,
                                                        pos.LowerRight + shift,
                                                        pos.UpperRight + shift
                                                    );
                                                    ole.Position3d = newPos;
                                                }
                                                catch (System.Exception posEx)
                                                {
                                                    _log.Warn(posEx, "Position3d fallback тоже не сработал.");
                                                }
                                            }
                                            else
                                            {
                                                _log.Info($"TransformBy сработал, смещение {moved:F4}");
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        _log.Warn($"OLE Bounds не определены для {newOleId.Handle}");
                    }

                    if (tr.GetObject(id, OpenMode.ForWrite) is RasterImage originalImg)
                    {
                        originalImg.Erase();
                        _log.Info($"Удалён исходный RasterImage: {id.Handle}");
                    }

                    tr.Commit();
                }
                else
                {
                    _log.Warn($"Найденный объект не является Ole2Frame: тип={newOleId.ObjectClass.DxfName}");
                }
            }
            catch (System.Exception ex)
            {
                _log.Warn(ex, $"Ошибка при встраивании OLE: {path}");
            }
            finally
            {
                clipboardImg?.Dispose();
                clipboardFs?.Dispose();
            }
        }

        try { System.Windows.Forms.Clipboard.Clear(); } catch { }
    }

    private long GetMaxHandleInModelSpace(Database db)
    {
        long maxHandle = 0;
        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
        foreach (ObjectId id in ms)
        {
            if (id.Handle.Value > maxHandle)
            {
                maxHandle = id.Handle.Value;
            }
        }
        tr.Commit();
        return maxHandle;
    }

    private ObjectId FindNewOle2Frame(Database db, long minHandleValue)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);
        ObjectId result = ObjectId.Null;
        foreach (ObjectId id in ms)
        {
            if (id.Handle.Value > minHandleValue)
            {
                string dxfName = id.ObjectClass.DxfName;
                if (dxfName == "OLE2FRAME")
                {
                    result = id;
                }
                else if (result.IsNull)
                {
                    _log.Info($"Новый объект Handle={id.Handle.Value}, тип={dxfName} (ожидался OLE2FRAME)");
                }
            }
        }
        tr.Commit();
        return result;
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

    private static (double width, double height) FitSizePreservingAspect(
        double containerWidth,
        double containerHeight,
        double imageWidth,
        double imageHeight)
    {
        if (containerWidth <= 0 || containerHeight <= 0 || imageWidth <= 0 || imageHeight <= 0)
        {
            return (containerWidth, containerHeight);
        }

        double containerAspect = containerWidth / containerHeight;
        double imageAspect = imageWidth / imageHeight;

        if (imageAspect >= containerAspect)
        {
            return (containerWidth, containerWidth / imageAspect);
        }

        return (containerHeight * imageAspect, containerHeight);
    }

    private static Rectangle3d BuildTargetRectangle(Extents3d bounds, Rectangle3d source, double width, double height)
    {
        double targetWidth = width > 0 ? width : bounds.MaxPoint.X - bounds.MinPoint.X;
        double targetHeight = height > 0 ? height : bounds.MaxPoint.Y - bounds.MinPoint.Y;

        Point3d lowerLeft = new(bounds.MinPoint.X, bounds.MinPoint.Y, source.LowerLeft.Z);
        Point3d upperLeft = new(bounds.MinPoint.X, bounds.MinPoint.Y + targetHeight, source.UpperLeft.Z);
        Point3d lowerRight = new(bounds.MinPoint.X + targetWidth, bounds.MinPoint.Y, source.LowerRight.Z);
        Point3d upperRight = new(bounds.MinPoint.X + targetWidth, bounds.MinPoint.Y + targetHeight, source.UpperRight.Z);

        return new Rectangle3d(lowerLeft, upperLeft, lowerRight, upperRight);
    }

    private string? ResolveRasterPath(Document doc, string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return null;
        }

        string? docDir = TryGetDirectory(doc.Name);
        if (string.IsNullOrEmpty(docDir))
        {
            return null;
        }

        if (System.IO.Path.IsPathRooted(rawPath) && System.IO.File.Exists(rawPath))
        {
            return System.IO.Path.GetFullPath(rawPath);
        }

        string combined = System.IO.Path.GetFullPath(System.IO.Path.Combine(docDir, rawPath));
        if (System.IO.File.Exists(combined))
        {
            return combined;
        }

        string fileNameOnly = System.IO.Path.GetFileName(rawPath);
        string inSameFolder = System.IO.Path.Combine(docDir, fileNameOnly);
        return System.IO.File.Exists(inSameFolder) ? inSameFolder : null;
    }

    private static string? TryGetDirectory(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return null;
        }

        try
        {
            return System.IO.Path.GetDirectoryName(filePath);
        }
        catch
        {
            return null;
        }
    }
}
