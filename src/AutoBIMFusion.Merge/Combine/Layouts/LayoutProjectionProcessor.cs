using Serilog.Core;

using AutoBIMFusion.AutoCAD.Helpers;

namespace AutoBIMFusion.Merge.Layouts;

internal static class LayoutProjectionProcessor
{
    internal sealed record LayoutProjectionResult(
        Extents3d? FrameBounds,
        double TargetVisualScale,
        double LinearScaleMultiplier);

    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        return viewports.Count == 0 ? ProjectNoViewport(db, layoutName, log) : ProjectWithViewports(db, layoutName, viewports, log);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        ViewportInfo mainOriginal = ViewportInfo.PickMainViewport(viewports);
        ViewportScaleNormalization scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale);
        ViewportInfo mainNormalized = mainOriginal with { CustomScale = scale.WorkingCustomScale };

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        if (viewports.Count > 1)
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

            foreach (ViewportInfo aux in viewports)
            {
                if (aux.VpId == mainOriginal.VpId)
                {
                    continue;
                }

                Matrix3d matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
                using ObjectIdCollection toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

                if (toClone.Count == 0)
                {
                    continue;
                }

                using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log);
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
            }
        }

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        (ObjectId paperBtrId, ObjectIdCollection filteredIds) = CollectAndFilterLayoutData(db, layoutName);
        using (filteredIds)
        {
            Extents3d? frameBounds = MovePaperToModelSpace(
                db, paperBtrId, filteredIds,
                ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log),
                log);

            return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
        }
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        (ObjectId btrId, ObjectIdCollection filteredIds) = CollectAndFilterLayoutData(db, layoutName);
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
            Matrix3d matrix = Matrix3d.Scaling(ViewportScaleNormalizer.WorkingScaleMultiplier, Point3d.Origin)
                            * Matrix3d.Displacement(Point3d.Origin - minPt);

            Extents3d? frameBounds = MovePaperToModelSpace(db, btrId, filteredIds, matrix, log);
            return new LayoutProjectionResult(frameBounds, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
        }
    }

    /// <summary>
    /// Собирает идентификаторы Paper Space, ищет рамку-штамп и фильтрует объекты
    /// в рамках единственной транзакции, заменяя цепочку из 5–8 отдельных транзакций.
    /// </summary>
    /// <returns>BTR-идентификатор листа и отфильтрованная коллекция объектов.</returns>
    private static (ObjectId BtrId, ObjectIdCollection FilteredPaperIds) CollectAndFilterLayoutData(Database db, string layoutName)
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

        // Один проход: собрать non-viewport IDs
        List<ObjectId> paperIdsList = [];
        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass))
            {
                paperIdsList.Add(id);
            }
        }

        // Поиск рамки-штампа и фильтрация в той же транзакции
        Extents3d? titleBlockBounds = FindTitleBlockBounds(trx, paperIdsList);
        ObjectIdCollection filteredIds = FilterEntitiesByBounds(trx, paperIdsList, titleBlockBounds);

        trx.Commit();
        return (btrId, filteredIds);
    }

    /// <summary>
    /// Находит рамку-штамп (BlockReference с максимальной площадью) в переданном наборе объектов.
    /// Работает в рамках переданной транзакции, не открывая новых.
    /// </summary>
    private static Extents3d? FindTitleBlockBounds(Transaction trx, List<ObjectId> paperIds)
    {
        Extents3d? bestExtents = null;
        double bestArea = 0.0;

        foreach (ObjectId id in paperIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not BlockReference br)
            {
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(br);
            if (!ext.HasValue)
            {
                continue;
            }

            double width = ext.Value.MaxPoint.X - ext.Value.MinPoint.X;
            double height = ext.Value.MaxPoint.Y - ext.Value.MinPoint.Y;
            double area = width * height;

            if (area > bestArea)
            {
                bestArea = area;
                bestExtents = ext.Value;
            }
        }

        return bestExtents;
    }

    /// <summary>
    /// Фильтрует объекты листа, оставляя только те, что пересекаются с рамкой-штампом.
    /// Работает в рамках переданной транзакции, не открывая новых.
    /// Всегда возвращает новую коллекцию — владение передаётся вызывающей стороне.
    /// </summary>
    private static ObjectIdCollection FilterEntitiesByBounds(Transaction trx, List<ObjectId> paperIds, Extents3d? boundingBox)
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
                if (ext.HasValue && ExtentsApproxEqual(ext.Value, bounds))
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

    private static bool ExtentsApproxEqual(Extents3d a, Extents3d b, double tolerance = 1e-6)
    {
        return Abs(a.MinPoint.X - b.MinPoint.X) <= tolerance
            && Abs(a.MinPoint.Y - b.MinPoint.Y) <= tolerance
            && Abs(a.MaxPoint.X - b.MaxPoint.X) <= tolerance
            && Abs(a.MaxPoint.Y - b.MaxPoint.Y) <= tolerance;
    }

    /// <summary>
    /// Масштабирует объекты Model Space вокруг указанного центра до рабочего масштаба 1:100.
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
    /// Клонирует отфильтрованные Paper Space объекты в Model Space и стирает исходный BTR.
    /// Принимает заранее вычисленные данные — не открывает дополнительных транзакций для
    /// получения списка сущностей.
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

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
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
            if (tr.GetObject(id, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
            {
                entity.Erase();
            }
        }

        tr.Commit();
    }
}
