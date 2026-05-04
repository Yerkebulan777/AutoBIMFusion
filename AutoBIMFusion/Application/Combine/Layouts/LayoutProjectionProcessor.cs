using AutoBIMFusion.Application.Utils;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Размещает содержимое проекта в пространстве модели до объединения временного файла DWG.
/// Обеспечивает ограничение масштаба окна просмотра, сглаживание вспомогательных окон просмотра и перенос в пространство листа.
/// </summary>
internal static class LayoutProjectionProcessor
{
    private const double MinScaleMultiplier = 1.0;
    private const double MaxScaleMultiplier = 100.0;

    internal sealed record LayoutProjectionResult(Extents3d? FrameBounds, IReadOnlyDictionary<ObjectId, double> DimensionScales, double FallbackMultiplier);

    private sealed class DimensionScaleAccumulator
    {
        private readonly Dictionary<ObjectId, DimensionScaleCandidate> _candidates = [];

        internal void Register(ObjectId dimensionId, double multiplier, double viewportArea)
        {
            if (dimensionId.IsNull || dimensionId.IsErased || !double.IsFinite(multiplier) || multiplier <= 0.0)
            {
                return;
            }

            if (!_candidates.TryGetValue(dimensionId, out DimensionScaleCandidate existing)
                || viewportArea < existing.ViewportArea)
            {
                _candidates[dimensionId] = new DimensionScaleCandidate(multiplier, viewportArea);
            }
        }

        internal IReadOnlyDictionary<ObjectId, double> ToDictionary()
        {
            return _candidates.ToDictionary(pair => pair.Key, pair => pair.Value.Multiplier);
        }
    }

    private readonly record struct DimensionScaleCandidate(double Multiplier, double ViewportArea);

    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName, IReadOnlyList<LayoutViewportInfo> viewports, Logger log)
    {
        return viewports.Count == 0 ? ProjectNoViewport(db, layoutName, log) : ProjectWithViewports(db, layoutName, viewports, log);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName, IReadOnlyList<LayoutViewportInfo> viewports, Logger log)
    {
        log.Information($"Выбранный метод масштабирования: ProcessVp ({viewports.Count} viewport'ов)");

        LayoutViewportInfo mainOriginal = LayoutViewportInfo.PickMainViewport(viewports);
        LayoutViewportInfo mainClamped = ClampMainViewportScale(mainOriginal, log);
        double clampRatio = mainOriginal.CustomScale / mainClamped.CustomScale;
        double effectiveMultiplier = ResolveMultiplier(mainClamped);
        DimensionScaleAccumulator dimensionScales = new();

        log.Information(
            $"VP main#{mainOriginal.Number}: исходный scale={mainOriginal.CustomScale:F6}, " +
            $"рабочий scale={mainClamped.CustomScale:F6}, clampRatio={clampRatio:F6}, " +
            $"dimensionMultiplier={effectiveMultiplier:F6}, " +
            $"центр={ExtentsUtils.FormatPoint(mainOriginal.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        // Обработка вспомогательных видовых экранов (только если их > 1)
        if (viewports.Count > 1)
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            RegisterDimensionsInside(db, modelEntities, mainOriginal, effectiveMultiplier, dimensionScales, log);

            foreach (LayoutViewportInfo aux in viewports)
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

                RegisterDimensionsInside(db, modelEntities, aux, effectiveMultiplier, dimensionScales, log);

                using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log, $"aux-VP #{aux.Number}");
                RegisterClonedDimensions(db, cloneResult.SourceToClone, aux, effectiveMultiplier, dimensionScales);
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
            }
        }
        else
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            RegisterDimensionsInside(db, modelEntities, mainOriginal, effectiveMultiplier, dimensionScales, log);
        }

        ScaleModelSpaceWhenClamped(db, clampRatio, mainOriginal.ViewCenter, log);

        // Содержимое бумаги проецируется через ограниченную основную область просмотра, поскольку пространство модели
        // уже было приведено к указанному выше ограниченному масштабу.
        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);
        return new LayoutProjectionResult(frameBounds, dimensionScales.ToDictionary(), effectiveMultiplier);
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        log.Information($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:100)");

        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return new LayoutProjectionResult(null, new Dictionary<ObjectId, double>(), MaxScaleMultiplier);
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

        if (!paperBounds.HasValue)
        {
            return new LayoutProjectionResult(null, new Dictionary<ObjectId, double>(), MaxScaleMultiplier);
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d matrix = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - minPt);

        ViewportTransformer.UnlockTextStylesHeight(db, log);
        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, matrix, log, "paper-no-vp");
        return new LayoutProjectionResult(frameBounds, new Dictionary<ObjectId, double>(), MaxScaleMultiplier);
    }


    /// <summary>
    /// Масштабирует объекты Model Space вокруг указанного центра, если clampRatio превышает 1.0.
    /// </summary>
    private static void ScaleModelSpaceWhenClamped(Database db, double clampRatio, Point3d center, Logger log)
    {
        if (clampRatio > 1.0 + 1e-9)
        {
            Matrix3d scaleMatrix = Matrix3d.Scaling(clampRatio, center);

            log.Information($"Применяем clampRatio={clampRatio:F6} к Model Space вокруг {ExtentsUtils.FormatPoint(center)}");

            ViewportTransformer.UnlockTextStylesHeight(db, log);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, clampRatio, log);
        }
    }

    private static LayoutViewportInfo ClampMainViewportScale(LayoutViewportInfo viewport, Logger log)
    {
        double multiplier = 1.0 / viewport.CustomScale;

        if (multiplier > MaxScaleMultiplier)
        {
            log.Information($"VP #{viewport.Number}: масштаб 1:{multiplier:F0} → зажат до 1:{MaxScaleMultiplier:F0}");
            return viewport with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        log.Information($"VP #{viewport.Number}: масштаб 1:{multiplier:F0}");
        return viewport;
    }

    private static Extents3d? MovePaperToModelSpace(Database db, string layoutName, Matrix3d matrix, Logger log, string tag = "paper")
    {
        ObjectId paperBtrId = LayoutUtil.GetLayoutBtrId(db, layoutName);
        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, paperIds, paperBtrId, msId, matrix, log, tag);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloneResult.ClonedIds, log);
    }

    private static void RegisterDimensionsInside(
        Database db,
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities,
        LayoutViewportInfo viewport,
        double multiplier,
        DimensionScaleAccumulator dimensionScales,
        Logger log)
    {
        double viewportArea = ComputeArea(viewport.ModelWindow);
        int matched = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ViewportTransformer.ModelEntitySnapshot snapshot in modelEntities)
        {
            if (snapshot.Id.IsNull
                || snapshot.Id.IsErased
                || !ExtentsUtils.AabbIntersect(viewport.ModelWindow, snapshot.Extents)
                || tr.GetObject(snapshot.Id, OpenMode.ForRead, false) is not Dimension)
            {
                continue;
            }

            dimensionScales.Register(snapshot.Id, multiplier, viewportArea);
            matched++;
        }

        tr.Commit();

        log.Debug($"VP #{viewport.Number}: registered {matched} model-space dimensions with effective main scale multiplier {multiplier:F6}");
    }

    private static void RegisterClonedDimensions(
        Database db,
        IReadOnlyDictionary<ObjectId, ObjectId> sourceToClone,
        LayoutViewportInfo viewport,
        double multiplier,
        DimensionScaleAccumulator dimensionScales)
    {
        double viewportArea = ComputeArea(viewport.ModelWindow);

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId cloneId in sourceToClone.Values)
        {
            if (!cloneId.IsNull
                && !cloneId.IsErased
                && tr.GetObject(cloneId, OpenMode.ForRead, false) is Dimension)
            {
                dimensionScales.Register(cloneId, multiplier, viewportArea);
            }
        }

        tr.Commit();
    }

    /// <summary>
    /// ??? Этот момент с масштабами может быть не совсем корректным!.
    /// </summary>
    private static double ResolveMultiplier(LayoutViewportInfo viewport)
    {
        double multiplier = viewport.CustomScale > 0.0 ? 1.0 / viewport.CustomScale : 1.0;
        return Math.Clamp(multiplier, MinScaleMultiplier, MaxScaleMultiplier);
    }

    private static double ComputeArea(Extents3d extents)
    {
        return Max(0.0, extents.MaxPoint.X - extents.MinPoint.X)
             * Max(0.0, extents.MaxPoint.Y - extents.MinPoint.Y);
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
