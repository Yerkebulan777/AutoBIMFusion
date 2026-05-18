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
        ViewportScaleNormalization scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale);
        ViewportInfo mainNormalized = mainOriginal with { CustomScale = scale.WorkingCustomScale };

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        ProjectAuxViewports(db, msId, mainOriginal, viewports, log);

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        (ObjectId paperBtrId, ObjectIdCollection? filteredIds) = CollectAndFilterLayoutData(db, layoutName);

        using (filteredIds)
        {
            Matrix3d matrix = ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log);
            Extents3d? frameBounds = MovePaperToModelSpace(db, paperBtrId, filteredIds, matrix, log);

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
        if (viewports.Count <= 1)
        {
            return;
        }

        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities =
            ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        HashSet<ObjectId> claimedSourceIds = [];

        foreach (ViewportInfo aux in viewports)
        {
            if (aux.VpId == mainOriginal.VpId)
            {
                continue;
            }

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
        Matrix3d matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);

        using ObjectIdCollection candidates =
            ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, mainOriginal.ModelWindow, log);

        using ObjectIdCollection toClone = CollectUnclaimedObjectIds(candidates, claimedSourceIds, out int duplicateSkipped);

        if (toClone.Count == 0)
        {
            log.Debug($"Aux#{aux.Number}: candidates={candidates.Count}, duplicateSkipped={duplicateSkipped}, nothing to clone");
            return;
        }

        log.Debug($"Aux#{aux.Number}: candidates={candidates.Count}, toClone={toClone.Count}, duplicateSkipped={duplicateSkipped}");

        using ViewportTransformer.CloneTransformResult cloneResult =
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
        {
            if (claimedSourceIds.Add(id))
            {
                _ = result.Add(id);
            }
            else
            {
                duplicateSkipped++;
            }
        }

        return result;
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        (ObjectId btrId, ObjectIdCollection? filteredIds) = CollectAndFilterLayoutData(db, layoutName);

        using (filteredIds)
        {
            if (filteredIds.Count == 0 || btrId.IsNull)
            {
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, filteredIds, log);

            if (!paperBounds.HasValue)
            {
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            Point3d minPt = paperBounds.Value.MinPoint;

            Matrix3d matrix = Matrix3d.Scaling(ViewportScaleNormalizer.WorkingScaleMultiplier, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - minPt);

            Extents3d? frameBounds = MovePaperToModelSpace(db, btrId, filteredIds, matrix, log);

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
        using Transaction trx = db.TransactionManager.StartTransaction();

        DBDictionary layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return (ObjectId.Null, []);
        }

        ObjectId layoutId = layoutDict.GetAt(layoutName);
        Layout layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        ObjectId btrId = layout.BlockTableRecordId;
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        RXClass viewportClass = RXObject.GetClass(typeof(Viewport));

        HashSet<ObjectId> clipEntityIds = CollectViewportClipEntityIds(trx, btr, viewportClass);
        List<ObjectId> paperIdsList = CollectPaperEntityIds(btr, viewportClass, clipEntityIds);

        // Поиск рамки-штампа и фильтрация в той же транзакции
        Extents3d? titleBlockBounds = BlockReferences.FindLargestByArea(trx, paperIdsList);
        ObjectIdCollection filteredIds = FilterEntitiesByBounds(trx, paperIdsList, titleBlockBounds);

        trx.Commit();
        return (btrId, filteredIds);
    }

    private static HashSet<ObjectId> CollectViewportClipEntityIds(
        Transaction trx,
        BlockTableRecord btr,
        RXClass viewportClass)
    {
        HashSet<ObjectId> clipEntityIds = [];

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                continue;
            }

            Viewport viewport = (Viewport)trx.GetObject(id, OpenMode.ForRead);
            if (!viewport.NonRectClipEntityId.IsNull)
            {
                _ = clipEntityIds.Add(viewport.NonRectClipEntityId);
            }
        }

        return clipEntityIds;
    }

    private static List<ObjectId> CollectPaperEntityIds(
        BlockTableRecord btr,
        RXClass viewportClass,
        IReadOnlySet<ObjectId> clipEntityIds)
    {
        List<ObjectId> paperIds = [];

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass) && !clipEntityIds.Contains(id))
            {
                paperIds.Add(id);
            }
        }

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
            foreach (ObjectId id in paperIds)
            {
                _ = filtered.Add(id);
            }

            return filtered;
        }

        Extents3d bounds = boundingBox.Value;

        foreach (ObjectId id in paperIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            // Сама рамка-штамп (BlockReference с совпадающими габаритами) включается всегда
            if (ent is BlockReference br)
            {
                Extents3d? ext = ExtentsUtils.TryGetExtents(br);
                if (ext.HasValue && ExtentsUtils.ExtentsApproxEqual(ext.Value, bounds))
                {
                    _ = filtered.Add(id);
                    continue;
                }
            }

            Extents3d? entityExt = ExtentsUtils.TryGetExtents(ent);
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

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using ViewportTransformer.CloneTransformResult cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        BlockReferences.EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    internal sealed record LayoutProjectionResult(Extents3d? FrameBounds, double TargetVisualScale, double LinearScaleMultiplier);
}
