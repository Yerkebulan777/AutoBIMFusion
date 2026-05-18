using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Удаляет сущности модели, чей центр габаритов находится за рамкой листа.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OutOfFrameEntityCleaner
{
    /// <summary>
    /// Сканирует Model Space и удаляет сущности за рамкой листа.
    /// </summary>
    internal static void Clean(Database db, Extents3d frameBounds, Logger log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);

        log.Information(
            "Запуск очистки объектов за рамкой листа: frameBounds={FrameBounds}",
            ExtentsUtils.FormatExtents(frameBounds));

        CleanResult result = EraseEntitiesOutsideFrame(db, frameBounds, log);

        if (result.BlockDefinitionIds.Count > 0)
        {
            PurgeUnusedBlockDefinitions(db, result.BlockDefinitionIds);
        }

        log.Information(
            "Очистка объектов за рамкой завершена: удалено {ErasedCount}, проверено блоков на purge {DefinitionCount}",
            result.ErasedCount,
            result.BlockDefinitionIds.Count);
    }

    private static CleanResult EraseEntitiesOutsideFrame(
        Database db,
        Extents3d frameBounds,
        Logger log)
    {
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        HashSet<ObjectId> erasedBlockDefinitions = [];
        int erasedCount = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        List<EntityCandidate> candidates = FindEntitiesOutsideFrame(trx, modelSpace, frameBounds, log);

        foreach (EntityCandidate candidate in candidates)
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

    private static List<EntityCandidate> FindEntitiesOutsideFrame(
        Transaction trx,
        BlockTableRecord modelSpace,
        Extents3d frameBounds,
        Logger log)
    {
        List<EntityCandidate> result = [];

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
                    "OutOfFrameEntityCleaner: для Entity {EntityType} не удалось вычислить BoundingBox",
                    entity.GetType().Name);
                continue;
            }

            Extents3d bounds = extents.Value;
            Point3d center = GetCenter(bounds);
            bool centerOutsideFrame = !IsPointInFrameXY(frameBounds, center);

            log.Debug(
                "OutOfFrameEntityCleaner: entity={EntityType}, bounds={Bounds}, center={Center}, centerOutsideFrame={CenterOutsideFrame}",
                entity.GetType().Name,
                ExtentsUtils.FormatExtents(bounds),
                ExtentsUtils.FormatPoint(center),
                centerOutsideFrame);

            if (!centerOutsideFrame)
            {
                continue;
            }

            ObjectId? blockDefinitionId = entity is BlockReference blockReference ? blockReference.BlockTableRecord : null;
            result.Add(new EntityCandidate(id, blockDefinitionId));
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

    private sealed record EntityCandidate(ObjectId EntityId, ObjectId? BlockDefinitionId);

    private sealed record CleanResult(int ErasedCount, HashSet<ObjectId> BlockDefinitionIds);
}
