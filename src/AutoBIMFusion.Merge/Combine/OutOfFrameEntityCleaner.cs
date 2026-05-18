using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Удаляет малые сущности модели, чей центр габаритов находится за рамкой листа.
///     Критерии «малая»: диагональ bbox ≤ <see cref="MaxBlockDiagonal"/>
///     и количество прямых дочерних объектов ≤ <see cref="MaxEntityCount"/>.
///     Крупные блоки (много элементов или большой bbox) не удаляются.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class OutOfFrameEntityCleaner
{
    private const int MaxEntityCount = 100;
    private const int MaxBlockDiagonal = 100;
    
    /// <summary>
    /// Сканирует Model Space и удаляет малые сущности за рамкой листа.
    /// </summary>
    internal static void Clean(Database db, Extents3d frameBounds, Logger log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);

        log.Information("Запуск очистки объектов за рамкой листа: maxDiagonal={MaxBlockDiagonal:F2}, maxEntityCount={MaxEntityCount}", MaxBlockDiagonal, MaxEntityCount);

        CleanResult result = EraseEntitiesOutsideFrame(db, frameBounds, log);

        if (result.BlockDefinitionIds.Count > 0)
        {
            PurgeUnusedBlockDefinitions(db, result.BlockDefinitionIds);
        }

        log.Information("Очистка объектов за рамкой завершена");
    }

    private static CleanResult EraseEntitiesOutsideFrame(Database db, Extents3d frameBounds, Logger log)
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

    private static List<EntityCandidate> FindEntitiesOutsideFrame(Transaction trx, BlockTableRecord modelSpace, Extents3d frameBounds, Logger log)
    {
        List<EntityCandidate> result = [];

        foreach (ObjectId id in modelSpace)
        {
            if (!id.IsValid || id.IsErased)
            {
                continue;
            }

            if (trx.GetObject(id, OpenMode.ForRead) is Entity entity && !entity.IsErased)
            {
                Extents3d? extents = ExtentsUtils.TryGetLiveExtents(entity, trx);

                if (extents.HasValue)
                {
                    Extents3d bounds = extents.Value;
                    Point3d center = GetCenter(bounds);

                    if (IsPointInFrameXY(frameBounds, center))
                    {
                        continue;
                    }

                    if (entity is BlockReference br)
                    {
                        // Дополнительно проверяем количество прямых дочерних объектов
                        int entityCount = CountDirectChildren(trx, br.BlockTableRecord);

                        // И диагональ габаритов, чтобы не удалять крупные блоки
                        double diagonal = bounds.MaxPoint.DistanceTo(bounds.MinPoint);

                        if (entityCount < MaxEntityCount && diagonal < MaxBlockDiagonal)
                        {
                            result.Add(new EntityCandidate(id, br.BlockTableRecord));
                        }
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
        if (blockDefinitionId.IsNull || blockDefinitionId.IsErased)
        {
            return 0;
        }

        if (trx.GetObject(blockDefinitionId, OpenMode.ForRead) is not BlockTableRecord btr)
        {
            return 0;
        }

        int count = 0;
        foreach (ObjectId _ in btr)
        {
            count++;
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
