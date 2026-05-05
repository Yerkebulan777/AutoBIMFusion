using AutoBIMFusion.Application.Utils;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Размещает содержимое проекта в пространстве модели до объединения временного файла DWG.
/// Обеспечивает ограничение масштаба окна просмотра, сглаживание вспомогательных окон просмотра и перенос в пространство листа.
/// </summary>
///
/// <remarks>
/// <para>
/// <b>Система масштабирования размеров (Dimension Scaling)</b>
/// </para>
/// <para>
/// Каждый видовой экран (Viewport) на листе имеет свой масштаб (<c>CustomScale</c>).
/// При переносе содержимого листа в пространство модели необходимо нормализовать
/// размерные стили так, чтобы они выглядели одинаково независимо от исходного масштаба вьюпорта.
/// </para>
/// <para>
/// <b>Базовый мультипликатор</b> вычисляется как <c>1.0 / CustomScale</c>.
/// Например, для вьюпорта 1:50 мультипликатор = 50, для 1:100 = 100, для 2:1 = 0.5.
/// </para>
/// <para>
/// <b>MinScaleMultiplier = 0.01</b> — нижняя граница мультипликатора.
/// Это критически важно для вьюпортов с масштабом крупнее 1:1 (например, 2:1, 5:1, 10:1),
/// где <c>CustomScale &gt; 1.0</c> и мультипликатор становится меньше 1.0.
/// Без этой нижней границы (если установить MinScaleMultiplier = 1.0) такие вьюпорты
/// получают мультипликатор = 1.0, что приводит к тому, что размерные элементы
/// (текст, стрелки, выноски) не масштабируются пропорционально и выглядят
/// несоразмерно крупными на чертеже.
/// </para>
/// <para>
/// <b>MaxScaleMultiplier = 100.0</b> — верхняя граница, предотвращающая
/// чрезмерное увеличение размерных элементов при масштабах мельче 1:100.
/// </para>
/// <para>
/// <b>Двухуровневое ограничение:</b>
/// <list type="number">
///   <item><description>
///     <c>ClampMainViewportScale</c> — если мультипликатор превышает MaxScaleMultiplier,
///     масштаб самого вьюпорта принудительно уменьшается, а разница компенсируется
///     через <c>ScaleModelSpaceWhenClamped</c> (clampRatio).
///   </description></item>
///   <item><description>
///     <c>ResolveMultiplier</c> — финальный мультипликатор для размерных стилей
///     зажимается в диапазон [MinScaleMultiplier, MaxScaleMultiplier].
///   </description></item>
/// </list>
/// </para>
/// <para>
/// Полученный <c>effectiveMultiplier</c> передаётся в <see cref="DimensionStyleNormalizer"/>,
/// который создаёт копии размерных стилей с визуальными свойствами (Dimtxt, Dimasz и т.д.),
/// умноженными на этот коэффициент, и сбрасывает Dimscale = 1.0.
/// </para>
/// </remarks>
internal static class LayoutProjectionProcessor
{
    private const double MaxScaleMultiplier = 100.0;
    private const double MinScaleMultiplier = 0.01;

    internal sealed record LayoutProjectionResult(Extents3d? FrameBounds, IReadOnlyDictionary<ObjectId, double> DimensionScales, double FallbackMultiplier);

    private sealed class ScaleCollector
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

    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        return viewports.Count == 0 ? ProjectNoViewport(db, layoutName, log) : ProjectWithViewports(db, layoutName, viewports, log);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName, IReadOnlyList<ViewportInfo> viewports, Logger log)
    {
        log.Information($"Выбранный метод масштабирования: ProcessVp ({viewports.Count} viewport'ов)");

        ViewportInfo mainOriginal = ViewportInfo.PickMainViewport(viewports);
        ViewportInfo mainClamped = ClampMainViewportScale(mainOriginal, log);
        double clampRatio = mainOriginal.CustomScale / mainClamped.CustomScale;
        double effectiveMultiplier = ResolveMultiplier(mainClamped);
        ScaleCollector dimensionScales = new();

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

    private static ViewportInfo ClampMainViewportScale(ViewportInfo viewport, Logger log)
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
        ViewportInfo viewport,
        double multiplier,
        ScaleCollector dimensionScales,
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

    private static void RegisterClonedDimensions(Database db, IReadOnlyDictionary<ObjectId, ObjectId> sourceToClone, ViewportInfo viewport, double multiplier, ScaleCollector dimensionScales)
    {
        double viewportArea = ComputeArea(viewport.ModelWindow);

        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId cloneId in sourceToClone.Values)
        {
            if (!cloneId.IsNull && !cloneId.IsErased)
            {
                if (tr.GetObject(cloneId, OpenMode.ForRead, false) is Dimension)
                {
                    dimensionScales.Register(cloneId, multiplier, viewportArea);
                }
            }
        }

        tr.Commit();
    }

    /// <summary>
    /// Вычисляет итоговый мультипликатор размерного стиля для указанного vp.
    ///     /// Мультипликатор = 1.0 / CustomScale. Это число показывает, во сколько раз
    /// нужно увеличить визуальные свойства размерного стиля (текст, стрелки, выноски),
    /// чтобы они выглядели на листе так же, как в пространстве модели.
    /// </summary>
    /// <remarks>
    /// Примеры:
    /// <list type="bullet">
    ///   <item><description>Вьюпорт 1:100 (CustomScale = 0.01) → multiplier = 100</description></item>
    ///   <item><description>Вьюпорт 1:50  (CustomScale = 0.02) → multiplier = 50</description></item>
    ///   <item><description>Вьюпорт 1:1   (CustomScale = 1.0)  → multiplier = 1</description></item>
    ///   <item><description>Вьюпорт 2:1   (CustomScale = 2.0)  → multiplier = 0.5</description></item>
    ///   <item><description>Вьюпорт 10:1  (CustomScale = 10.0) → multiplier = 0.1</description></item>
    /// </list>
    /// Результат зажимается в диапазон [MinScaleMultiplier, MaxScaleMultiplier],
    /// чтобы избежать экстремальных значений, которые привели бы к нечитаемым размерам.
    /// </remarks>
    private static double ResolveMultiplier(ViewportInfo viewport)
    {
        return viewport.CustomScale > 0.0 ? 1.0 / viewport.CustomScale : 1.0;
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
