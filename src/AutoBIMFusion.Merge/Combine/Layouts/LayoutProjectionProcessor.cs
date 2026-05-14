using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Helpers;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal static class LayoutProjectionProcessor
{
    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        return viewports.Count == 0
            ? ProjectNoViewport(db, layoutName, log)
            : ProjectWithViewports(db, layoutName, viewports, log);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        ViewportInfo mainOriginal = ViewportInfo.PickMainViewport(viewports);
        var scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale);
        var mainNormalized = mainOriginal with { CustomScale = scale.WorkingCustomScale };

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        List<Extents3d> allWindows = [mainOriginal.ModelWindow];

        foreach (var aux in viewports)
        {
            if (aux.VpId != mainOriginal.VpId)
            {
                allWindows.Add(aux.ModelWindow);
            }
        }

        if (viewports.Count > 1)
        {
            var modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

            foreach (var aux in viewports)
            {
                if (aux.VpId == mainOriginal.VpId)
                {
                    continue;
                }

                var matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
                using var toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

                if (toClone.Count == 0)
                {
                    continue;
                }

                using var cloneResult = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log);
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities,
                    mainOriginal.ModelWindow, log);
            }
        }

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var (paperBtrId, filteredIds) = CollectAndFilterLayoutData(db, layoutName);
        using (filteredIds)
        {
            var frameBounds = MovePaperToModelSpace(
                db, paperBtrId, filteredIds,
                ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log),
                log);

            return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
        }
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var (btrId, filteredIds) = CollectAndFilterLayoutData(db, layoutName);
        using (filteredIds)
        {
            if (filteredIds.Count == 0 || btrId.IsNull)
            {
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            var paperBounds = ModelSpaceTrimmer.ComputeBounds(db, filteredIds, log);

            if (!paperBounds.HasValue)
            {
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            var minPt = paperBounds.Value.MinPoint;
            var matrix = Matrix3d.Scaling(ViewportScaleNormalizer.WorkingScaleMultiplier, Point3d.Origin)
                         * Matrix3d.Displacement(Point3d.Origin - minPt);

            var frameBounds = MovePaperToModelSpace(db, btrId, filteredIds, matrix, log);
            return new LayoutProjectionResult(frameBounds, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
        }
    }

    /// <summary>
    ///     Собирает идентификаторы Paper Space, ищет рамку-штамп и фильтрует объекты
    ///     в рамках единственной транзакции, заменяя цепочку из 5–8 отдельных транзакций.
    /// </summary>
    /// <returns>BTR-идентификатор листа и отфильтрованная коллекция объектов.</returns>
    private static (ObjectId BtrId, ObjectIdCollection FilteredPaperIds) CollectAndFilterLayoutData(Database db,
        string layoutName)
    {
        using var trx = db.TransactionManager.StartTransaction();

        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return (ObjectId.Null, []);
        }

        var layoutId = layoutDict.GetAt(layoutName);
        Layout layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        var btrId = layout.BlockTableRecordId;
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        var viewportClass = RXObject.GetClass(typeof(Viewport));

        // Один проход: собрать non-viewport IDs
        List<ObjectId> paperIdsList = [];
        foreach (var id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                paperIdsList.Add(id);
            }
        }

        // Поиск рамки-штампа и фильтрация в той же транзакции
        var titleBlockBounds = FindTitleBlockBounds(trx, paperIdsList);
        var filteredIds = FilterEntitiesByBounds(trx, paperIdsList, titleBlockBounds);

        trx.Commit();
        return (btrId, filteredIds);
    }

    /// <summary>
    ///     Находит рамку-штамп (BlockReference с максимальной площадью) в переданном наборе объектов.
    ///     Делегирует к <see cref="BlockReferences.FindLargestByArea"/>.
    /// </summary>
    private static Extents3d? FindTitleBlockBounds(Transaction trx, List<ObjectId> paperIds)
    {
        return BlockReferences.FindLargestByArea(trx, paperIds);
    }

    /// <summary>
    ///     Фильтрует объекты листа, оставляя только те, что пересекаются с рамкой-штампом.
    ///     Работает в рамках переданной транзакции, не открывая новых.
    ///     Всегда возвращает новую коллекцию — владение передаётся вызывающей стороне.
    /// </summary>
    private static ObjectIdCollection FilterEntitiesByBounds(Transaction trx, List<ObjectId> paperIds,
        Extents3d? boundingBox)
    {
        ObjectIdCollection filtered = [];

        if (!boundingBox.HasValue)
        {
            // Рамка не найдена — включаем все объекты
            foreach (var id in paperIds)
            {
                _ = filtered.Add(id);
            }

            return filtered;
        }

        var bounds = boundingBox.Value;

        foreach (var id in paperIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            // Сама рамка-штамп (BlockReference с совпадающими габаритами) включается всегда
            if (ent is BlockReference br)
            {
                var ext = ExtentsUtils.TryGetExtents(br);
                if (ext.HasValue && ExtentsUtils.ExtentsApproxEqual(ext.Value, bounds))
                {
                    _ = filtered.Add(id);
                    continue;
                }
            }

            var entityExt = ExtentsUtils.TryGetExtents(ent);
            if (entityExt.HasValue && ExtentsUtils.AabbIntersect(bounds, entityExt.Value))
            {
                _ = filtered.Add(id);
                continue;
            }

            if (ExtentsUtils.IsEntityPointIn(ent, bounds))
            {
                _ = filtered.Add(id);
            }
        }

        return filtered;
    }

    /// <summary>
    ///     Масштабирует объекты Model Space вокруг указанного центра до рабочего масштаба 1:100.
    /// </summary>
    private static void NormalizeModelSpaceScale(Database db, double geometryScale, Point3d center, Logger log)
    {
        if (Abs(geometryScale - 1.0) > 1e-9)
        {
            Matrix3d scaleMatrix = Matrix3d.Scaling(geometryScale, center);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, geometryScale, log);
        }
    }

    /// <summary>
    ///     Клонирует отфильтрованные Paper Space объекты в Model Space и стирает исходный BTR.
    ///     Принимает заранее вычисленные данные — не открывает дополнительных транзакций для
    ///     получения списка сущностей.
    /// </summary>
    private static Extents3d? MovePaperToModelSpace(
        Database db,
        ObjectId paperBtrId,
        ObjectIdCollection filteredIds,
        Matrix3d matrix,
        Logger log)
    {
        if (filteredIds.Count == 0 || paperBtrId.IsNull)
        {
            return null;
        }

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    private static void EraseBlockContents(Database db, ObjectId btrId)
    {
        BlockReferences.EraseBlockContents(db, btrId);
    }

    internal sealed record LayoutProjectionResult(Extents3d? FrameBounds, double TargetVisualScale, double LinearScaleMultiplier);
}
