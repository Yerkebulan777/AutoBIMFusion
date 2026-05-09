using AutoBIMFusion.Application.Utils;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class LayoutProjectionProcessor
{
    private const double MaxScaleMultiplier = 100.0;

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
        ViewportInfo mainClamped = ClampMainViewportScale(mainOriginal, log);
        double clampRatio = mainOriginal.CustomScale / mainClamped.CustomScale;

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

        ScaleModelSpaceWhenClamped(db, clampRatio, mainOriginal.ViewCenter, log);

        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);

        double targetVisualScale = 1.0 / mainClamped.CustomScale;
        double linearScaleMultiplier = 1.0 / clampRatio;

        return new LayoutProjectionResult(frameBounds, targetVisualScale, linearScaleMultiplier);
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return new LayoutProjectionResult(null, MaxScaleMultiplier, 1.0);
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

        if (!paperBounds.HasValue)
        {
            return new LayoutProjectionResult(null, MaxScaleMultiplier, 1.0);
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d matrix = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - minPt);

        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, matrix, log);
        return new LayoutProjectionResult(frameBounds, MaxScaleMultiplier, 1.0);
    }

    /// <summary>
    /// Масштабирует объекты Model Space вокруг указанного центра, если clampRatio превышает 1.0.
    /// </summary>
    private static void ScaleModelSpaceWhenClamped(Database db, double clampRatio, Point3d center, Logger log)
    {
        if (clampRatio > 1.0 + 1e-9)
        {
            Matrix3d scaleMatrix = Matrix3d.Scaling(clampRatio, center);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, clampRatio, log);
        }
    }

    /// <summary>
    /// Зажимает масштаб главного ВЭ до рабочего 1:100 для мелких масштабов (1:50, 1:20, ...).
    /// </summary>
    /// <remarks>
    /// Условие <c>multiplier &lt; MaxScaleMultiplier</c> намеренно: зажатие нужно именно для мелких
    /// масштабов, где multiplier &lt; 100 (например, 1:50 → multiplier=50).
    /// НЕ менять на <c>&gt;</c> — это сломает масштабирование объектов.
    /// Разница между исходным и зажатым масштабом компенсируется через clampRatio в
    /// <see cref="ScaleModelSpaceWhenClamped"/>. linearScaleMultiplier = 1/clampRatio компенсирует
    /// числовые значения размеров, завышенные из-за масштабирования геометрии.
    /// </remarks>
    private static ViewportInfo ClampMainViewportScale(ViewportInfo viewport, Logger log)
    {
        double multiplier = 1.0 / viewport.CustomScale;

        if (multiplier < MaxScaleMultiplier)
        {
            return viewport with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        return viewport;
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
