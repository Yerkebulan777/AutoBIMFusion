using System.Runtime.Versioning;
using AutoBIMFusion.Common.Helpers;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Удаляет малые сущности модели, чей центр габаритов находится за рамкой листа.
///     Критерии «малая»: диагональ bbox ≤ <see cref="MaxBlockDiagonal" />
///     и количество прямых дочерних объектов ≤ <see cref="MaxEntityCount" />.
///     Крупные блоки (много элементов или большой bbox) не удаляются.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OutOfFrameEntityCleaner
{
    private const int MaxEntityCount = 100;
    private const int MaxBlockDiagonal = 100;

    /// <summary>
    ///     Сканирует Model Space и удаляет малые сущности за рамкой листа.
    /// </summary>
    internal static void Clean(Database db, Extents3d frameBounds, Logger log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);

        var result = EraseEntitiesOutsideFrame(db, frameBounds, log);

        if (result.BlockDefinitionIds.Count > 0) PurgeUnusedBlockDefinitions(db, result.BlockDefinitionIds);
    }

    private static CleanResult EraseEntitiesOutsideFrame(Database db, Extents3d frameBounds, Logger log)
    {
        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        HashSet<ObjectId> erasedBlockDefinitions = [];

        using var trx = db.TransactionManager.StartTransaction();

        var modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        var candidates = FindEntitiesOutsideFrame(trx, modelSpace, frameBounds, log);

        foreach (var candidate in candidates)
        {
            if (candidate.BlockDefinitionId.HasValue) _ = erasedBlockDefinitions.Add(candidate.BlockDefinitionId.Value);

            if (trx.GetObject(candidate.EntityId, OpenMode.ForWrite) is Entity entity && !entity.IsErased)
            {
                entity.Erase();
            }
        }

        trx.Commit();

        return new CleanResult(erasedBlockDefinitions);
    }

    private static List<EntityCandidate> FindEntitiesOutsideFrame(Transaction trx, BlockTableRecord modelSpace,
        Extents3d frameBounds, Logger log)
    {
        List<EntityCandidate> result = [];

        foreach (var id in modelSpace)
        {
            if (!id.IsValid || id.IsErased) continue;

            if (trx.GetObject(id, OpenMode.ForRead) is Entity entity && !entity.IsErased)
            {
                var extents = ExtentsUtils.TryGetLiveExtents(entity, trx);

                if (extents.HasValue)
                {
                    var bounds = extents.Value;
                    var center = GetCenter(bounds);

                    if (IsPointInFrameXY(frameBounds, center)) continue;

                    if (entity is BlockReference br)
                    {
                        // Проверяем диагональ габаритов, чтобы не удалять крупные блоки
                        var diagonal = bounds.MaxPoint.DistanceTo(bounds.MinPoint);

                        // Дополнительно проверяем количество прямых дочерних объектов
                        var entityCount = CountDirectChildren(trx, br.BlockTableRecord);

                        if (entityCount < MaxEntityCount && diagonal < MaxBlockDiagonal)
                            result.Add(new EntityCandidate(id, br.BlockTableRecord));
                    }
                    else if (entity != null)
                    {
                        // Для обычных сущностей достаточно что они за рамкой
                        result.Add(new EntityCandidate(id, null));
                    }
                }
                else
                {
                    log.Debug("Для Entity {EntityType} не удалось вычислить BoundingBox", entity.GetType().Name);
                }
            }
        }

        return result;
    }

    private static int CountDirectChildren(Transaction trx, ObjectId blockDefinitionId)
    {
        if (blockDefinitionId.IsNull || blockDefinitionId.IsErased) return 0;

        if (trx.GetObject(blockDefinitionId, OpenMode.ForRead) is not BlockTableRecord btr) return 0;

        var count = 0;
        foreach (var _ in btr)
        {
            count++;
            if (count >= MaxEntityCount) break;
        }

        return count;
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

        if (ids.Count == 0) return;

        db.Purge(ids);

        if (ids.Count == 0) return;

        using var trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in ids)
        {
            if (id.IsErased) continue;

            trx.GetObject(id, OpenMode.ForWrite).Erase();
        }

        trx.Commit();
    }

    private sealed record EntityCandidate(ObjectId EntityId, ObjectId? BlockDefinitionId);

    private sealed record CleanResult(HashSet<ObjectId> BlockDefinitionIds);
}
