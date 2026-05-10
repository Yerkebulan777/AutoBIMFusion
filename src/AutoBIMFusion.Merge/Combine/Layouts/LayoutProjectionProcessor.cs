using AutoBIMFusion.AutoCAD;
using Serilog.Core;

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

        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log), log);

        return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
        }

        Extents3d? titleBlockBounds = FindTitleBlockBounds(db, layoutName);
        using ObjectIdCollection filteredIds = FilterEntitiesByBounds(db, paperIds, titleBlockBounds);

        if (filteredIds.Count == 0)
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

        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, matrix, log);
        return new LayoutProjectionResult(frameBounds, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
    }

    /// <summary>
    /// Находит рамку-штамп (BlockReference с максимальной площадью) в указанном листе.
    /// </summary>
    private static Extents3d? FindTitleBlockBounds(Database db, string layoutName)
    {
        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        Extents3d? bestExtents = null;
        double bestArea = 0.0;

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in paperIds)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not BlockReference br)
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

        tr.Commit();
        return bestExtents;
    }

    /// <summary>
    /// Фильтрует объекты листа, оставляя только те, что пересекаются с рамкой-штампом
    /// или находятся внутри неё. Сама рамка всегда включается в результат.
    /// </summary>
    private static ObjectIdCollection FilterEntitiesByBounds(Database db, ObjectIdCollection paperIds, Extents3d? boundingBox)
    {
        if (!boundingBox.HasValue)
        {
            return paperIds;
        }

        ObjectIdCollection filtered = [];
        Extents3d bounds = boundingBox.Value;

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in paperIds)
        {
            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            // Обязательно добавляем саму рамку-штамп (BlockReference с совпадающими габаритами)
            if (ent is BlockReference)
            {
                Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
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

        tr.Commit();
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

    private static Extents3d? MovePaperToModelSpace(Database db, string layoutName, Matrix3d matrix, Logger log)
    {
        ObjectId paperBtrId = LayoutUtil.GetLayoutBtrId(db, layoutName);
        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        Extents3d? titleBlockBounds = FindTitleBlockBounds(db, layoutName);
        using ObjectIdCollection filteredIds = FilterEntitiesByBounds(db, paperIds, titleBlockBounds);

        if (filteredIds.Count == 0)
        {
            return null;
        }

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    private static void EraseBlockContents(Database db, ObjectId btrId)
    {
        if (!btrId.IsNull)
        {
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
}
