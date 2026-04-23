using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
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
            }, null);

            NormalizeRasterImagePaths(sourceDoc.Database, sourceFilePath);

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

    private static void NormalizeRasterImagePaths(Database db, string sourceFilePath)
    {
        string? sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(sourceDir))
        {
            return;
        }

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId dictId = RasterImageDef.GetImageDictionary(db);
        if (dictId.IsNull)
        {
            tr.Commit();
            return;
        }

        DBDictionary dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
        foreach (DBDictionaryEntry entry in dict)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def)
            {
                continue;
            }

            string path = def.SourceFileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                continue;
            }

            // Правило 1: используем FindFile для стабильного разрешения путей
            string resolvedPath = HostApplicationServices.Current.FindFile(path, db, FindFileHint.EmbeddedImageFile);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                continue;
            }

            // Преобразуем в относительный путь относительно папки исходного файла
            string relativePath = Path.GetRelativePath(sourceDir, resolvedPath);
            def.SourceFileName = relativePath;
            def.Load(); // Правило 2: загружаем определение после смены пути
        }

        tr.Commit();
    }
}

