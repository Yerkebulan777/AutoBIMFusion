using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Удаляет маленькие сущности модели, чей центр габаритов находится за рамкой листа.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class SmallOutOfFrameEntityCleaner
{
    private const double MaxBoundingBoxDiagonal = 100.0;

    /// <summary>
    /// Сканирует Model Space и удаляет маленькие сущности за рамкой листа.
    /// </summary>
    internal static void Clean(Database db, Extents3d frameBounds, Logger log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);

        log.Information(
            "Запуск очистки малых объектов за рамкой листа: frameBounds={FrameBounds}, maxDiagonal={MaxDiagonal:F2}",
            ExtentsUtils.FormatExtents(frameBounds),
            MaxBoundingBoxDiagonal);

        CleanResult result = EraseSmallEntitiesOutsideFrame(db, frameBounds, log);

        if (result.BlockDefinitionIds.Count > 0)
        {
            PurgeUnusedBlockDefinitions(db, result.BlockDefinitionIds);
        }

        log.Information(
            "Очистка малых объектов за рамкой завершена: удалено {ErasedCount}, проверено блоков на purge {DefinitionCount}",
            result.ErasedCount,
            result.BlockDefinitionIds.Count);
    }

    private static CleanResult EraseSmallEntitiesOutsideFrame(
        Database db,
        Extents3d frameBounds,
        Logger log)
    {
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        HashSet<ObjectId> erasedBlockDefinitions = [];
        int erasedCount = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        List<SmallEntityCandidate> candidates = FindSmallEntitiesOutsideFrame(trx, modelSpace, frameBounds, log);

        foreach (SmallEntityCandidate candidate in candidates)
        {
            if (candidate.BlockDefinitionId.HasValue)
            {
                _ = erasedBlockDefinitions.Add(candidate.BlockDefinitionId.Value);
            }

            if (trx.GetObject(candidate.EntityId, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
            {
                entity.Erase();
                erasedCount++;
            }
        }

        trx.Commit();
        return new CleanResult(erasedCount, erasedBlockDefinitions);
    }

    private static List<SmallEntityCandidate> FindSmallEntitiesOutsideFrame(
        Transaction trx,
        BlockTableRecord modelSpace,
        Extents3d frameBounds,
        Logger log)
    {
        List<SmallEntityCandidate> result = [];

        foreach (ObjectId id in modelSpace)
        {
            if (!id.IsValid || id.IsErased)
            {
                continue;
            }

            if (trx.GetObject(id, OpenMode.ForRead) is not Entity entity || entity.IsErased)
            {
                continue;
            }

            Extents3d? extents = ExtentsUtils.TryGetLiveExtents(entity, trx);
            if (!extents.HasValue)
            {
                log.Debug(
                    "SmallOutOfFrameEntityCleaner: для Entity {EntityType} не удалось вычислить BoundingBox",
                    entity.GetType().Name);
                continue;
            }

            Extents3d bounds = extents.Value;
            double diagonal = bounds.MaxPoint.DistanceTo(bounds.MinPoint);
            Point3d center = GetCenter(bounds);
            bool small = diagonal <= MaxBoundingBoxDiagonal;
            bool centerOutsideFrame = !IsPointInFrameXY(frameBounds, center);

            log.Debug(
                "SmallOutOfFrameEntityCleaner: entity={EntityType}, bounds={Bounds}, diagonal={Diagonal:F2}, center={Center}, small={Small}, centerOutsideFrame={CenterOutsideFrame}",
                entity.GetType().Name,
                ExtentsUtils.FormatExtents(bounds),
                diagonal,
                ExtentsUtils.FormatPoint(center),
                small,
                centerOutsideFrame);

            if (!small || !centerOutsideFrame)
            {
                continue;
            }

            ObjectId? blockDefinitionId = entity is BlockReference blockReference ? blockReference.BlockTableRecord : null;
            result.Add(new SmallEntityCandidate(id, blockDefinitionId));
        }

        return result;
    }

    private static Point3d GetCenter(Extents3d bounds)
    {
        return new Point3d(
            (bounds.MinPoint.X + bounds.MaxPoint.X) / 2.0,
            (bounds.MinPoint.Y + bounds.MaxPoint.Y) / 2.0,
            (bounds.MinPoint.Z + bounds.MaxPoint.Z) / 2.0);
    }

    private static bool IsPointInFrameXY(Extents3d frameBounds, Point3d point)
    {
        return point.X >= frameBounds.MinPoint.X
               && point.X <= frameBounds.MaxPoint.X
               && point.Y >= frameBounds.MinPoint.Y
               && point.Y <= frameBounds.MaxPoint.Y;
    }

    private static void PurgeUnusedBlockDefinitions(Database db, HashSet<ObjectId> blockDefinitionIds)
    {
        using ObjectIdCollection ids = new(blockDefinitionIds.Where(id => id.IsValid && !id.IsErased).ToArray());

        if (ids.Count == 0)
        {
            return;
        }

        db.Purge(ids);

        if (ids.Count == 0)
        {
            return;
        }

        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in ids)
        {
            if (id.IsErased)
            {
                continue;
            }

            trx.GetObject(id, OpenMode.ForWrite).Erase();
        }

        trx.Commit();
    }

    private sealed record SmallEntityCandidate(ObjectId EntityId, ObjectId? BlockDefinitionId);

    private sealed record CleanResult(int ErasedCount, HashSet<ObjectId> BlockDefinitionIds);
}
