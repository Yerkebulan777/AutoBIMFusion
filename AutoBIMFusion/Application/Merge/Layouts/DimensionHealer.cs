using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private const double ImperialOverrideFactor = 304.8;
    private const double Tolerance = 1e-6;

    internal static int Heal(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        int healedCount = 0;

        using Transaction tr = targetDb.TransactionManager.StartTransaction();
        DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(targetDb.LayoutDictionaryId, OpenMode.ForRead);
        HashSet<ObjectId> visitedBlocks = [];

        foreach (DBDictionaryEntry entry in layoutDictionary)
        {
            if (tr.GetObject(entry.Value, OpenMode.ForRead, false) is not Layout layout)
            {
                continue;
            }

            ObjectId blockId = layout.BlockTableRecordId;
            if (blockId.IsNull || !visitedBlocks.Add(blockId))
            {
                continue;
            }

            if (tr.GetObject(blockId, OpenMode.ForRead, false) is not BlockTableRecord block)
            {
                continue;
            }

            foreach (ObjectId id in block)
            {
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension
                    || !IsInfected(dimension))
                {
                    continue;
                }

                dimension.UpgradeOpen();
                _ = DimensionStyleDiagnosticUtils.TryRemoveDimensionStyleOverrides(dimension);
                dimension.Dimscale = 1.0;
                dimension.Dimlfac = 1.0;
                healedCount++;
            }
        }

        tr.Commit();

        LoggerFactory.GetSharedLogger()
            .Information("Healed {Count} dimensions infected with imperial overrides.", healedCount);

        return healedCount;
    }

    private static bool IsInfected(Dimension dimension)
    {
        return IsImperialOverride(dimension.Dimscale)
            || IsImperialOverride(dimension.Dimlfac);
    }

    private static bool IsImperialOverride(double value)
    {
        return double.IsFinite(value)
            && Abs(value - ImperialOverrideFactor) <= Tolerance;
    }
}
