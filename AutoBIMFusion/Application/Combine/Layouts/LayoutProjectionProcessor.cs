using AutoBIMFusion.Application.Utils;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class LayoutProjectionProcessor
{
    private const double MaxScaleMultiplier = 100.0;

    internal sealed record LayoutProjectionResult(Extents3d? FrameBounds);

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

        log.Information(
            $"VP main#{mainOriginal.Number}: исходный scale={mainOriginal.CustomScale:F6}, " +
            $"рабочий scale={mainClamped.CustomScale:F6}, clampRatio={clampRatio:F6}, " +
            $"effectiveMultiplier={effectiveMultiplier:F6}, " +
            $"центр={ExtentsUtils.FormatPoint(mainOriginal.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        // Обработка вспомогательных видовых экранов (только если их > 1)
        if (viewports.Count > 1)
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            _ = ViewportTransformer.NormalizeDimensionsInsideViewport(db, modelEntities, mainOriginal, log);

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

                _ = ViewportTransformer.NormalizeDimensionsInsideViewport(db, modelEntities, aux, log);

                using ViewportTransformer.CloneTransformResult cloneResult = ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log, $"aux-VP #{aux.Number}");
                _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
                _ = ViewportTransformer.NormalizeDimensionsInsideViewport(db, modelEntities, mainOriginal, log);
            }
        }
        else
        {
            IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            _ = ViewportTransformer.NormalizeDimensionsInsideViewport(db, modelEntities, mainOriginal, log);
        }

        ScaleModelSpaceWhenClamped(db, clampRatio, mainOriginal.ViewCenter, log);

        // Содержимое бумаги проецируется через ограниченную основную область просмотра, поскольку пространство модели
        // уже было приведено к указанному выше ограниченному масштабу.
        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);
        _ = ViewportTransformer.FinalizeModelSpaceDimensionLinearScales(db, log);
        return new LayoutProjectionResult(frameBounds);
    }

    private static LayoutProjectionResult ProjectNoViewport(Database db, string layoutName, Logger log)
    {
        log.Information($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:100)");

        using ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return new LayoutProjectionResult(null);
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

        if (!paperBounds.HasValue)
        {
            return new LayoutProjectionResult(null);
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d matrix = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin) * Matrix3d.Displacement(Point3d.Origin - minPt);

        ViewportTransformer.UnlockTextStylesHeight(db, log);
        Extents3d? frameBounds = MovePaperToModelSpace(db, layoutName, matrix, log, "paper-no-vp");
        _ = ViewportTransformer.FinalizeModelSpaceDimensionLinearScales(db, log);
        return new LayoutProjectionResult(frameBounds);
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
    /// <see cref="ScaleModelSpaceWhenClamped"/>. Размерные стили нормализуются по масштабу
    /// конкретного исходного ВЭ до клонирования.
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

    /// <summary>
    /// Вычисляет мультипликатор как <c>1.0 / CustomScale</c> для указанного ВЭ.
    /// Используется для <c>effectiveMultiplier</c> из зажатого ВЭ.
    /// Результат не зажимается — применяющий код отвечает за допустимые границы.
    /// </summary>
    private static double ResolveMultiplier(ViewportInfo viewport)
    {
        return viewport.CustomScale > 0.0 ? 1.0 / viewport.CustomScale : 1.0;
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
