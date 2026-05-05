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
/// <b>Два независимых мультипликатора (КРИТИЧЕСКИ ВАЖНО — не путать)</b>
/// </para>
/// <para>
/// В пайплайне существуют два отдельных мультипликатора с разными назначениями:
/// </para>
/// <list type="number">
///   <item>
///     <term><c>effectiveMultiplier</c></term>
///     <description>
///       Вычисляется из <b>зажатого</b> ВЭ (<c>mainClamped</c>).
///       Используется <b>только для масштабирования геометрии объектов</b> через
///       <c>ScaleModelSpaceWhenClamped</c> (clampRatio) и построения матриц трансформации.
///       Не передаётся в нормализатор размерных стилей.
///     </description>
///   </item>
///   <item>
///     <term><c>dimensionMultiplier</c></term>
///     <description>
///       Вычисляется из <b>исходного</b> ВЭ (<c>mainOriginal</c>) как <c>1.0 / mainOriginal.CustomScale</c>.
///       Используется <b>только для нормализации размерных стилей</b> — задаёт, во сколько раз нужно
///       умножить визуальные свойства стиля (Dimtxt, Dimasz и т.д.), чтобы размер выглядел корректно
///       на итоговом чертеже. Передаётся в <see cref="DimensionStyleNormalizer"/> через
///       <see cref="LayoutProjectionResult.FallbackMultiplier"/> и <see cref="LayoutProjectionResult.DimensionScales"/>.
///     </description>
///   </item>
/// </list>
/// <para>
/// <b>Почему два разных мультипликатора?</b>
/// </para>
/// <para>
/// <c>ClampMainViewportScale</c> принудительно переводит мелкие масштабы (1:50, 1:20 и т.д.)
/// в рабочий масштаб 1:100, чтобы компенсировать геометрию через clampRatio.
/// Если бы этот же мультипликатор использовался для размерных стилей, то, например, для ВЭ 1:50
/// стили получали бы коэффициент 100 вместо 50 — вдвое завышенный.
/// Для масштабов крупнее 1:100 (например, 1:200, 1:500) зажатия не происходит,
/// поэтому <c>effectiveMultiplier == dimensionMultiplier</c>, но для более мелких масштабов они расходятся.
/// </para>
/// <para>
/// <b><c>ClampMainViewportScale</c> — условие <c>multiplier &lt; MaxScaleMultiplier</c></b>
/// </para>
/// <para>
/// Зажатие срабатывает когда мультипликатор МЕНЬШЕ 100 (масштаб мельче 1:100, например 1:50 → multiplier=50).
/// НЕ менять условие на <c>&gt;</c> — это сломает масштабирование объектов для всех масштабов кроме 1:100.
/// Зажатие нужно именно для мелких масштабов, чтобы нормализовать геометрию к рабочему 1:100.
/// </para>
/// <para>
/// <b>MaxScaleMultiplier = 100.0</b> — рабочий масштаб для геометрических трансформаций объектов.
/// Нормализатор размерных стилей (<see cref="DimensionStyleNormalizer"/>) верхнего предела не имеет —
/// это намеренно, чтобы масштабы 1:200, 1:500 и крупнее давали корректные визуальные свойства.
/// </para>
/// </remarks>
internal static class LayoutProjectionProcessor
{
    private const double MaxScaleMultiplier = 100.0;

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
        // Мультипликатор для размерных стилей: берётся из исходного (нестесненного) масштаба ВЭ,
        // чтобы визуальные свойства стилей (Dimtxt, Dimasz и др.) соответствовали фактическому
        // масштабу чертежа, а не приведённому для трансформации объектов.
        double dimensionMultiplier = mainOriginal.CustomScale > 0.0 ? 1.0 / mainOriginal.CustomScale : effectiveMultiplier;
        ScaleCollector dimensionScales = new();

        log.Information(
            $"VP main#{mainOriginal.Number}: исходный scale={mainOriginal.CustomScale:F6}, " +
            $"рабочий scale={mainClamped.CustomScale:F6}, clampRatio={clampRatio:F6}, " +
            $"effectiveMultiplier={effectiveMultiplier:F6}, dimensionMultiplier={dimensionMultiplier:F6}, " +
            $"центр={ExtentsUtils.FormatPoint(mainOriginal.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        // Обработка вспомогательных видовых экранов (только если их > 1)
        if (viewports.Count > 1)
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            RegisterDimensionsInside(db, modelEntities, mainOriginal, dimensionMultiplier, dimensionScales, log);

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

                RegisterDimensionsInside(db, modelEntities, aux, dimensionMultiplier, dimensionScales, log);

                using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log, $"aux-VP #{aux.Number}");
                RegisterClonedDimensions(db, cloneResult.SourceToClone, aux, dimensionMultiplier, dimensionScales);
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
            }
        }
        else
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            RegisterDimensionsInside(db, modelEntities, mainOriginal, dimensionMultiplier, dimensionScales, log);
        }

        ScaleModelSpaceWhenClamped(db, clampRatio, mainOriginal.ViewCenter, log);

        // Содержимое бумаги проецируется через ограниченную основную область просмотра, поскольку пространство модели
        // уже было приведено к указанному выше ограниченному масштабу.
        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);
        return new LayoutProjectionResult(frameBounds, dimensionScales.ToDictionary(), dimensionMultiplier);
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

    /// <summary>
    /// Зажимает масштаб главного ВЭ до рабочего 1:100 для мелких масштабов (1:50, 1:20, ...).
    /// </summary>
    /// <remarks>
    /// Условие <c>multiplier &lt; MaxScaleMultiplier</c> намеренно: зажатие нужно именно для мелких
    /// масштабов, где multiplier &lt; 100 (например, 1:50 → multiplier=50).
    /// НЕ менять на <c>&gt;</c> — это сломает масштабирование объектов.
    /// Разница между исходным и зажатым масштабом компенсируется через clampRatio в
    /// <see cref="ScaleModelSpaceWhenClamped"/>. Для размерных стилей используется
    /// dimensionMultiplier из исходного ВЭ, а не effectiveMultiplier из зажатого.
    /// </remarks>
    private static ViewportInfo ClampMainViewportScale(ViewportInfo viewport, Logger log)
    {
        double multiplier = 1.0 / viewport.CustomScale;

        if (multiplier < MaxScaleMultiplier)
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
    /// Вычисляет мультипликатор как <c>1.0 / CustomScale</c> для указанного ВЭ.
    /// Используется для <c>effectiveMultiplier</c> (из зажатого ВЭ) и <c>dimensionMultiplier</c> (из исходного ВЭ).
    /// Результат не зажимается — применяющий код отвечает за допустимые границы.
    /// </summary>
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
