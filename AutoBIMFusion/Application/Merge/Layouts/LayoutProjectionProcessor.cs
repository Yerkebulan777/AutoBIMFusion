using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Размещает содержимое проекта в пространстве модели до объединения временного файла DWG.
/// Обеспечивает ограничение масштаба окна просмотра, сглаживание вспомогательных окон просмотра и перенос в пространство листа.
/// </summary>
internal static class LayoutProjectionProcessor
{
    private const double MaxScaleMultiplier = 100.0;

    internal static Extents3d? ProjectLayoutToModelSpace(Database db, string layoutName, IReadOnlyList<LayoutViewportInfo> viewports, AILog log)
    {
        return viewports.Count switch
        {
            0 => ProjectNoViewport(db, layoutName, log),
            1 => ProjectSingleViewport(db, layoutName, viewports[0], log),
            _ => ProjectMultipleViewports(db, layoutName, viewports, log)
        };
    }

    private static Extents3d? ProjectMultipleViewports(Database db, string layoutName, IReadOnlyList<LayoutViewportInfo> viewports, AILog log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessMultiVp ({viewports.Count} viewport'ов)");

        LayoutViewportInfo mainOriginal = LayoutViewportInfo.PickMainViewport(viewports);
        LayoutViewportInfo mainClamped = ClampMainViewportScale(mainOriginal, log);
        double clampRatio = mainOriginal.CustomScale / mainClamped.CustomScale;

        log.Info(
            $"VP main#{mainOriginal.Number}: исходный scale={mainOriginal.CustomScale:F6}, " +
            $"рабочий scale={mainClamped.CustomScale:F6}, clampRatio={clampRatio:F6}, " +
            $"центр={ExtentsUtils.FormatPoint(mainOriginal.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        ObjectIdCollection mainIds = ViewportTransformer.SelectModelInside(modelEntities, mainOriginal.ModelWindow, log);
        log.Debug($"main-VP #{mainOriginal.Number}: selected={mainIds.Count}, dimension scale overrides skipped");

        foreach (LayoutViewportInfo aux in viewports)
        {
            if (aux.VpId == mainOriginal.VpId)
            {
                continue;
            }

            // Aux geometry must use the original main scale. Using the clamped main here
            // moves and scales aux clones by clampRatio twice.
            Matrix3d matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
            ObjectIdCollection toClone = ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, log);

            if (toClone.Count == 0)
            {
                log.Info($"aux-VP #{aux.Number}: 0 объектов");
                continue;
            }

            ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(
                db,
                toClone,
                msId,
                msId,
                matrix,
                log,
                $"aux-VP #{aux.Number}");

            _ = ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, modelEntities, mainOriginal.ModelWindow, log);
            log.Info($"aux-VP #{aux.Number}: обработано {cloned.Count} объектов");
        }

        ScaleModelSpaceWhenClamped(db, clampRatio, mainOriginal.ViewCenter, log);

        // Paper content is projected through the clamped main viewport because Model Space
        // has already been brought into the clamped scale above.
        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(mainClamped, log), log);
    }

    private static Extents3d? ProjectSingleViewport(
        Database db,
        string layoutName,
        LayoutViewportInfo viewport,
        AILog log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessSingleVp (VP #{viewport.Number})");

        LayoutViewportInfo clamped = ClampMainViewportScale(viewport, log);
        double clampRatio = viewport.CustomScale / clamped.CustomScale;

        log.Info(
            $"VP #{viewport.Number}: исходный scale={viewport.CustomScale:F6}, рабочий scale={clamped.CustomScale:F6}, " +
            $"clampRatio={clampRatio:F6}, центр={ExtentsUtils.FormatPoint(clamped.ViewCenter)}");

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities =
            ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
        ObjectIdCollection visibleIds = ViewportTransformer.SelectModelInside(modelEntities, viewport.ModelWindow, log);
        log.Debug($"VP #{viewport.Number}: selected={visibleIds.Count}, dimension scale overrides skipped");

        ScaleModelSpaceWhenClamped(db, clampRatio, viewport.ViewCenter, log);

        return MovePaperToModelSpace(db, layoutName, ViewportTransformer.BuildPaperToMainMatrix(clamped, log), log);
    }

    private static Extents3d? ProjectNoViewport(
        Database db,
        string layoutName,
        AILog log)
    {
        log.Info($"Выбранный метод масштабирования: ProcessNoVp (масштаб по умолчанию 1:100)");

        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        Extents3d? paperBounds = ModelSpaceTrimmer.ComputeBounds(db, paperIds, log);

        if (!paperBounds.HasValue)
        {
            return null;
        }

        Point3d minPt = paperBounds.Value.MinPoint;
        Matrix3d moveToOrigin = Matrix3d.Displacement(Point3d.Origin - minPt);
        Matrix3d scale = Matrix3d.Scaling(MaxScaleMultiplier, Point3d.Origin);
        Matrix3d matrix = scale * moveToOrigin;

        log.Info(
            $"ProcessNoVp: paper bounds={ExtentsUtils.FormatExtents(paperBounds.Value)}, " +
            $"ratio={MaxScaleMultiplier:F2}");

        ViewportTransformer.UnlockTextStylesHeight(db, log);
        return MovePaperToModelSpace(db, layoutName, matrix, log, "paper-no-vp");
    }

    private static void ScaleModelSpaceWhenClamped(Database db, double clampRatio, Point3d center, AILog log)
    {
        if (clampRatio <= 1.0 + 1e-9)
        {
            return;
        }

        Matrix3d scaleMatrix = Matrix3d.Scaling(clampRatio, center);
        log.Info(
            $"Применяем clampRatio={clampRatio:F6} к Model Space вокруг " +
            $"{ExtentsUtils.FormatPoint(center)}");

        ViewportTransformer.UnlockTextStylesHeight(db, log);
        ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, clampRatio, log);
    }

    private static LayoutViewportInfo ClampMainViewportScale(LayoutViewportInfo viewport, AILog log)
    {
        double multiplier = 1.0 / viewport.CustomScale;

        if (multiplier < MaxScaleMultiplier)
        {
            log.Info($"VP #{viewport.Number}: масштаб 1:{multiplier:F0} → зажат до 1:{MaxScaleMultiplier:F0}");
            return viewport with { CustomScale = 1.0 / MaxScaleMultiplier };
        }

        log.Info($"VP #{viewport.Number}: масштаб 1:{multiplier:F0}");
        return viewport;
    }

    private static Extents3d? MovePaperToModelSpace(
        Database db,
        string layoutName,
        Matrix3d matrix,
        AILog log,
        string tag = "paper")
    {
        ObjectId paperBtrId = LayoutUtil.GetLayoutBtrId(db, layoutName);
        ObjectIdCollection paperIds = LayoutUtil.GetPaperSpaceEntities(db, layoutName, excludeViewports: true);

        if (paperIds.Count == 0)
        {
            return null;
        }

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        ObjectIdCollection cloned = ViewportTransformer.DeepCloneAndTransform(db, paperIds, paperBtrId, msId, matrix, log, tag);

        EraseBlockContents(db, paperBtrId);

        return ModelSpaceTrimmer.ComputeBounds(db, cloned, log);
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
