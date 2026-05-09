using AutoBIMFusion.Application.Utils;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

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

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

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

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, paperIds, paperBtrId, msId, matrix, log);

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
