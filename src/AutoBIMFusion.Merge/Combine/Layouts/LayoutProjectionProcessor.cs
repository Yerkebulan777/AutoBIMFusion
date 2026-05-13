using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Layouts;
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

        // Разбиваем блоки внутри всех окон видовых экранов до клонирования и масштабирования,
        // чтобы текст внутри блоков не искажался при последующих трансформациях.
        List<Extents3d> allWindows = [mainOriginal.ModelWindow];

        foreach (var aux in viewports)
            if (aux.VpId != mainOriginal.VpId)
                allWindows.Add(aux.ModelWindow);

        if (viewports.Count > 1)
        {
            var modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

            foreach (var aux in viewports)
            {
                if (aux.VpId == mainOriginal.VpId) continue;

                var matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
                using var toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

                if (toClone.Count == 0) continue;

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
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);

            var paperBounds = ModelSpaceTrimmer.ComputeBounds(db, filteredIds, log);

            if (!paperBounds.HasValue)
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);

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
        using var  = db.TransactionManager.StartTransaction();

        var layoutDict = (DBDictionary).GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            .Commit();
            return (ObjectId.Null, []);
        }

        var layoutId = layoutDict.GetAt(layoutName);
        var layout = (Layout).GetObject(layoutId, OpenMode.ForRead);
        var btrId = layout.BlockTableRecordId;
        var btr = (BlockTableRecord).GetObject(btrId, OpenMode.ForRead);

        var viewportClass = RXObject.GetClass(typeof(Viewport));

        // Один проход: собрать non-viewport IDs
        List<ObjectId> paperIdsList = [];
        foreach (var id in btr)
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
                paperIdsList.Add(id);

        // Поиск рамки-штампа и фильтрация в той же транзакции
        var titleBlockBounds = FindTitleBlockBounds(, paperIdsList);
        var filteredIds = FilterEntitiesByBounds(, paperIdsList, titleBlockBounds);

        .Commit();
        return (btrId, filteredIds);
    }

    /// <summary>
    ///     Находит рамку-штамп (BlockReference с максимальной площадью) в переданном наборе объектов.
    ///     Работает в рамках переданной транзакции, не открывая новых.
    /// </summary>
    private static Extents3d? FindTitleBlockBounds(Transaction , List<ObjectId> paperIds)
    {
        Extents3d? bestExtents = null;
        var bestArea = 0.0;

        foreach (var id in paperIds)
        {
            if (.GetObject(id, OpenMode.ForRead) is not BlockReference br) continue;

            var ext = ExtentsUtils.TryGetExtents(br);
            if (!ext.HasValue) continue;

            var width = ext.Value.MaxPoint.X - ext.Value.MinPoint.X;
            var height = ext.Value.MaxPoint.Y - ext.Value.MinPoint.Y;
            var area = width * height;

            if (area > bestArea)
            {
                bestArea = area;
                bestExtents = ext.Value;
            }
        }

        return bestExtents;
    }

    /// <summary>
    ///     Фильтрует объекты листа, оставляя только те, что пересекаются с рамкой-штампом.
    ///     Работает в рамках переданной транзакции, не открывая новых.
    ///     Всегда возвращает новую коллекцию — владение передаётся вызывающей стороне.
    /// </summary>
    private static ObjectIdCollection FilterEntitiesByBounds(Transaction , List<ObjectId> paperIds,
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
            if (.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

            // Сама рамка-штамп (BlockReference с совпадающими габаритами) включается всегда
            if (ent is BlockReference br)
            {
                var ext = ExtentsUtils.TryGetExtents(br);
                if (ext.HasValue && ExtentsApproxEqual(ext.Value, bounds))
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

    private static bool ExtentsApproxEqual(Extents3d a, Extents3d b, double tolerance = 1e-6)
    {
        return Abs(a.MinPoint.X - b.MinPoint.X) <= tolerance
               && Abs(a.MinPoint.Y - b.MinPoint.Y) <= tolerance
               && Abs(a.MaxPoint.X - b.MaxPoint.X) <= tolerance
               && Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tolerance;
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
    private static Extents3d? MovePaperToModelSpace(
        Database db,
        ObjectId paperBtrId,
        ObjectIdCollection filteredIds,
        Matrix3d matrix,
        Logger log)
    {
        if (filteredIds.Count == 0 || paperBtrId.IsNull) return null;

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    private static void EraseBlockContents(Database db, ObjectId btrId)
    {
        if (btrId.IsNull) return;

        using var tr = db.TransactionManager.StartTransaction();
        var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);

        foreach (var id in btr)
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
                entity.Erase();

        tr.Commit();
    }

    internal sealed record LayoutProjectionResult(
        Extents3d? FrameBounds,
        double TargetVisualScale,
        double LinearScaleMultiplier);
}
