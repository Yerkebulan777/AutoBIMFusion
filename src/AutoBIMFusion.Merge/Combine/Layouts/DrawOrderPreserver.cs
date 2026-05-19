using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
///     Захват и восстановление draw order (`SortentsTable`) при клонировании
///     набора entity между BTR. Используется чтобы после `DeepCloneObjects`
///     относительный порядок выше/ниже в целевом блоке совпадал с исходным.
/// </summary>
internal static class DrawOrderPreserver
{
    internal static IReadOnlyList<ObjectId> Capture(Database db, ObjectId sourceBtrId, ObjectIdCollection filterIds,
        Logger log)
    {
        ArgumentNullException.ThrowIfNull(filterIds);

        if (sourceBtrId.IsNull || filterIds.Count == 0) return [];

        HashSet<ObjectId> filter = [.. filterIds.Cast<ObjectId>()];

        using var trx = db.TransactionManager.StartTransaction();
        var btr = (BlockTableRecord)trx.GetObject(sourceBtrId, OpenMode.ForRead);

        if (btr.DrawOrderTableId.IsNull)
        {
            trx.Commit();
            log.Debug("DrawOrderPreserver.Capture: DrawOrderTableId is null");
            return [];
        }

        var sortents = (DrawOrderTable)trx.GetObject(btr.DrawOrderTableId, OpenMode.ForRead);
        var fullOrder = sortents.GetFullDrawOrder(0);

        List<ObjectId> filtered = new(filter.Count);
        foreach (ObjectId id in fullOrder)
            if (filter.Contains(id))
                filtered.Add(id);

        trx.Commit();
        log.Debug("DrawOrderPreserver.Capture: fullOrder={FullOrder}, filtered={Filtered}", fullOrder.Count, filtered.Count);
        return filtered;
    }

    internal static void Restore(Database db, ObjectId targetBtrId, IReadOnlyList<ObjectId> sourceOrder, IdMapping map,
        Logger log)
    {
        ArgumentNullException.ThrowIfNull(sourceOrder);
        ArgumentNullException.ThrowIfNull(map);

        if (targetBtrId.IsNull || sourceOrder.Count == 0) return;

        Dictionary<ObjectId, ObjectId> sourceToTarget = [];
        foreach (IdPair pair in map)
            if (pair.IsCloned && pair.IsPrimary)
                sourceToTarget[pair.Key] = pair.Value;

        ObjectIdCollection orderedTargets = [];
        var missingMapping = 0;
        foreach (var sourceId in sourceOrder)
            if (sourceToTarget.TryGetValue(sourceId, out var targetId))
                _ = orderedTargets.Add(targetId);
            else
                missingMapping++;

        if (orderedTargets.Count <= 1)
        {
            log.Debug("DrawOrderPreserver.Restore: нечего упорядочивать (orderedTargets={Count})", orderedTargets.Count);
            return;
        }

        using var trx = db.TransactionManager.StartTransaction();
        var btr = (BlockTableRecord)trx.GetObject(targetBtrId, OpenMode.ForRead);

        if (btr.DrawOrderTableId.IsNull)
        {
            trx.Commit();
            log.Debug("DrawOrderPreserver.Restore: target DrawOrderTableId is null");
            return;
        }

        var sortents = (DrawOrderTable)trx.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
        sortents.SetRelativeDrawOrder(orderedTargets);

        trx.Commit();
        log.Debug("DrawOrderPreserver.Restore: reordered={Reordered}, missingMapping={MissingMapping}", orderedTargets.Count, missingMapping);
    }
}
