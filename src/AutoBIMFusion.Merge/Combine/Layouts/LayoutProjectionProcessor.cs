using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Helpers;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal static class LayoutProjectionProcessor
{
    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName,
        IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        return viewports.Count == 0
            ? ProjectNoViewport(db, layoutName, log)
            : ProjectWithViewports(db, layoutName, viewports, log);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName,
        IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        var mainOriginal = ViewportInfo.PickMainViewport(viewports);
        var scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale);
        var mainNormalized = mainOriginal with { CustomScale = scale.WorkingCustomScale };

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        ProjectAuxViewports(db, msId, mainOriginal, viewports, log);

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var layoutData = CollectAndFilterLayoutData(db, layoutName);

        using (layoutData.FilteredPaperIds)
        {
            var matrix = ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log);
            var frameBounds = TransformFrameBounds(layoutData.FrameBounds, matrix);
            MovePaperToModelSpace(db, layoutData.BtrId, layoutData.FilteredPaperIds, matrix, log);

            return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
        }
    }

    private static void ProjectAuxViewports(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        IReadOnlyList<ViewportInfo> viewports,
        Logger log)
    {
        if (viewports.Count <= 1) return;

        var modelEntities =
            ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        HashSet<ObjectId> claimedSourceIds = [];

        foreach (var aux in viewports)
        {
            if (aux.VpId == mainOriginal.VpId) continue;

            ProjectAuxViewport(db, msId, mainOriginal, aux, modelEntities, claimedSourceIds, log);
        }
    }

    private static void ProjectAuxViewport(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        ViewportInfo aux,
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities,
        HashSet<ObjectId> claimedSourceIds,
        Logger log)
    {
        var matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);

        using var candidates =
            ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, mainOriginal.ModelWindow, log);

        using var toClone = CollectUnclaimedObjectIds(candidates, claimedSourceIds, out var duplicateSkipped);

        if (toClone.Count == 0)
        {
            log.Debug(
                $"Aux#{aux.Number}: candidates={candidates.Count}, duplicateSkipped={duplicateSkipped}, nothing to clone");
            return;
        }

        log.Debug(
            $"Aux#{aux.Number}: candidates={candidates.Count}, toClone={toClone.Count}, duplicateSkipped={duplicateSkipped}");

        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log);

        ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow);
    }

    private static ObjectIdCollection CollectUnclaimedObjectIds(
        ObjectIdCollection candidates,
        HashSet<ObjectId> claimedSourceIds,
        out int duplicateSkipped)
    {
        ObjectIdCollection result = [];
        duplicateSkipped = 0;

        foreach (ObjectId id in candidates)
            if (claimedSourceIds.Add(id))
                _ = result.Add(id);
            else
                duplicateSkipped++;

        return result;
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var layoutData = CollectAndFilterLayoutData(db, layoutName);

        using (layoutData.FilteredPaperIds)
        {
            if (layoutData.FilteredPaperIds.Count == 0 || layoutData.BtrId.IsNull)
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);

            var paperBounds = ComputeBounds(db, layoutData.FilteredPaperIds, log);

            if (!paperBounds.HasValue)
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);

            var minPt = paperBounds.Value.MinPoint;

            var matrix = Matrix3d.Scaling(ViewportScaleNormalizer.WorkingScaleMultiplier, Point3d.Origin) *
                         Matrix3d.Displacement(Point3d.Origin - minPt);

            var frameBounds = TransformFrameBounds(layoutData.FrameBounds, matrix);
            MovePaperToModelSpace(db, layoutData.BtrId, layoutData.FilteredPaperIds, matrix, log);

            return new LayoutProjectionResult(frameBounds, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
        }
    }

    /// <summary>
    ///     Собирает идентификаторы Paper Space, ищет рамку-штамп и фильтрует объекты
    ///     в рамках единственной транзакции, заменяя цепочку из 5–8 отдельных транзакций.
    /// </summary>
    /// <returns>BTR-идентификатор листа и отфильтрованная коллекция объектов.</returns>
    private static LayoutData CollectAndFilterLayoutData(Database db, string layoutName)
    {
        using var trx = db.TransactionManager.StartTransaction();

        var layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return new LayoutData(ObjectId.Null, [], null);
        }

        var layoutId = layoutDict.GetAt(layoutName);
        var layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        var btrId = layout.BlockTableRecordId;
        var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        var viewportClass = RXObject.GetClass(typeof(Viewport));

        var clipEntityIds = CollectViewportClipEntityIds(trx, btr, viewportClass);
        var paperIdsList = CollectPaperEntityIds(btr, viewportClass, clipEntityIds);

        // Поиск рамки-штампа и фильтрация в той же транзакции
        var titleBlockBounds = BlockReferences.FindLargestBlockReferenceBoundsByArea(trx, paperIdsList);
        var filteredIds = FilterEntitiesByBounds(trx, paperIdsList, titleBlockBounds);

        trx.Commit();
        return new LayoutData(btrId, filteredIds, titleBlockBounds);
    }

    private static HashSet<ObjectId> CollectViewportClipEntityIds(
        Transaction trx,
        BlockTableRecord btr,
        RXClass viewportClass)
    {
        HashSet<ObjectId> clipEntityIds = [];

        foreach (var id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass)) continue;

            var viewport = (Viewport)trx.GetObject(id, OpenMode.ForRead);
            if (!viewport.NonRectClipEntityId.IsNull) _ = clipEntityIds.Add(viewport.NonRectClipEntityId);
        }

        return clipEntityIds;
    }

    private static List<ObjectId> CollectPaperEntityIds(
        BlockTableRecord btr,
        RXClass viewportClass,
        HashSet<ObjectId> clipEntityIds)
    {
        List<ObjectId> paperIds = [];

        foreach (var id in btr)
            if (!id.ObjectClass.IsDerivedFrom(viewportClass) && !clipEntityIds.Contains(id))
                paperIds.Add(id);

        return paperIds;
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
            foreach (var id in paperIds) _ = filtered.Add(id);

            return filtered;
        }

        var bounds = boundingBox.Value;

        foreach (var id in paperIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

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

            if (ExtentsUtils.IsEntityPointIn(ent, bounds)) _ = filtered.Add(id);
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
            var scaleMatrix = Matrix3d.Scaling(geometryScale, center);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, geometryScale, log);
        }
    }

    /// <summary>
    ///     Клонирует отфильтрованные Paper Space объекты в Model Space и стирает исходный BTR.
    ///     Принимает заранее вычисленные данные — не открывает дополнительных транзакций для
    ///     получения списка сущностей.
    /// </summary>
    private static void MovePaperToModelSpace(
        Database db,
        ObjectId paperBtrId,
        ObjectIdCollection filteredIds,
        Matrix3d matrix,
        Logger log)
    {
        if (filteredIds.Count == 0 || paperBtrId.IsNull) return;

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        BlockReferences.EraseBlockContents(db, paperBtrId);

        _ = ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    private static Extents3d? TransformFrameBounds(Extents3d? frameBounds, Matrix3d matrix)
    {
        return frameBounds.HasValue ? ExtentsUtils.Transform(frameBounds.Value, matrix) : null;
    }

    private static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds, Logger log)
    {
        var result = ExtentsUtils.ComputeBounds(db, entityIds);

        if (result.HasValue)
            log.Debug("ComputeBounds: entities={Count}, bounds={Bounds}", entityIds.Count, ExtentsUtils.FormatExtents(result.Value));

        return result;
    }

    private sealed record LayoutData(ObjectId BtrId, ObjectIdCollection FilteredPaperIds, Extents3d? FrameBounds);

    internal sealed record LayoutProjectionResult(
        Extents3d? FrameBounds,
        double TargetVisualScale,
        double LinearScaleMultiplier);
}
