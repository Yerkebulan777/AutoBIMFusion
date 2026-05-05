using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Захват и восстановление draw order (`SortentsTable`) при клонировании
/// набора entity между BTR. Используется чтобы после `DeepCloneObjects`
/// относительный порядок выше/ниже в целевом блоке совпадал с исходным.
/// </summary>
internal static class DrawOrderPreserver
{
    internal static IReadOnlyList<ObjectId> Capture(
        Database db, ObjectId sourceBtrId, ObjectIdCollection filterIds, Logger log)
    {
        ArgumentNullException.ThrowIfNull(filterIds);

        if (sourceBtrId.IsNull || filterIds.Count == 0)
        {
            return [];
        }

        HashSet<ObjectId> filter = [.. filterIds.Cast<ObjectId>()];

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(sourceBtrId, OpenMode.ForRead);

        if (btr.DrawOrderTableId.IsNull)
        {
            trx.Commit();
            log.Debug("DrawOrderPreserver.Capture: DrawOrderTableId is null");
            return [];
        }

        DrawOrderTable sortents = (DrawOrderTable)trx.GetObject(btr.DrawOrderTableId, OpenMode.ForRead);
        ObjectIdCollection fullOrder = sortents.GetFullDrawOrder(0);

        List<ObjectId> filtered = new(filter.Count);
        foreach (ObjectId id in fullOrder)
        {
            if (filter.Contains(id))
            {
                filtered.Add(id);
            }
        }

        trx.Commit();
        log.Debug($"DrawOrderPreserver.Capture: fullOrder={fullOrder.Count}, filtered={filtered.Count}");
        return filtered;
    }

    internal static void Restore(
        Database db, ObjectId targetBtrId, IReadOnlyList<ObjectId> sourceOrder,
        IdMapping map, Logger log)
    {
        ArgumentNullException.ThrowIfNull(sourceOrder);
        ArgumentNullException.ThrowIfNull(map);

        if (targetBtrId.IsNull || sourceOrder.Count == 0)
        {
            return;
        }

        Dictionary<ObjectId, ObjectId> sourceToTarget = [];
        foreach (IdPair pair in map)
        {
            if (pair.IsCloned && pair.IsPrimary)
            {
                sourceToTarget[pair.Key] = pair.Value;
            }
        }

        ObjectIdCollection orderedTargets = [];
        int missingMapping = 0;
        foreach (ObjectId sourceId in sourceOrder)
        {
            if (sourceToTarget.TryGetValue(sourceId, out ObjectId targetId))
            {
                _ = orderedTargets.Add(targetId);
            }
            else
            {
                missingMapping++;
            }
        }

        if (orderedTargets.Count <= 1)
        {
            log.Debug($"DrawOrderPreserver.Restore: нечего упорядочивать (orderedTargets={orderedTargets.Count})");
            return;
        }

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = (BlockTableRecord)trx.GetObject(targetBtrId, OpenMode.ForRead);

        if (btr.DrawOrderTableId.IsNull)
        {
            trx.Commit();
            log.Debug("DrawOrderPreserver.Restore: target DrawOrderTableId is null");
            return;
        }

        DrawOrderTable sortents = (DrawOrderTable)trx.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
        sortents.SetRelativeDrawOrder(orderedTargets);

        trx.Commit();
        log.Debug($"DrawOrderPreserver.Restore: reordered={orderedTargets.Count}, missingMapping={missingMapping}");
    }
}
