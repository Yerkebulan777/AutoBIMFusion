using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private static readonly double[] ImperialOverrideFactors = [304.8, 25.4, 12.0];
    private const double Tolerance = 1e-6;

    internal static int Heal(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        int healedCount = 0;

        using Transaction tr = targetDb.TransactionManager.StartTransaction();
        DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(targetDb.LayoutDictionaryId, OpenMode.ForRead);
        HashSet<ObjectId> visitedBlocks = [];
        Dictionary<ObjectId, double> styleDimscaleCache = [];

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
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension)
                {
                    continue;
                }

                ObjectId styleId = dimension.DimensionStyle;
                bool hasStyleDimscale = TryGetStyleDimscale(tr, styleId, styleDimscaleCache, out double styleDimscale);
                bool hasVisualScaleOverride = IsImperialOverride(dimension.Dimscale)
                    && (!hasStyleDimscale || !AreClose(dimension.Dimscale, styleDimscale));
                bool hasMeasurementScaleOverride = IsImperialOverride(dimension.Dimlfac);
                if (!hasVisualScaleOverride && !hasMeasurementScaleOverride)
                {
                    continue;
                }

                dimension.UpgradeOpen();

                _ = DimensionStyleDiagnosticUtils.TryRemoveDimensionStyleOverrides(dimension);

                if (!styleId.IsNull)
                {
                    dimension.DimensionStyle = styleId;
                }

                if (hasVisualScaleOverride && hasStyleDimscale)
                {
                    dimension.Dimscale = styleDimscale;
                }

                if (hasMeasurementScaleOverride)
                {
                    dimension.Dimlfac = 1.0;
                }

                healedCount++;
            }
        }

        tr.Commit();

        LoggerFactory.GetSharedLogger()
            .Information("Healed {Count} dimensions infected with imperial overrides.", healedCount);

        return healedCount;
    }

    private static bool TryGetStyleDimscale(
        Transaction tr,
        ObjectId styleId,
        Dictionary<ObjectId, double> cache,
        out double dimscale)
    {
        dimscale = 1.0;
        if (styleId.IsNull)
        {
            return false;
        }

        if (cache.TryGetValue(styleId, out dimscale))
        {
            return true;
        }

        if (tr.GetObject(styleId, OpenMode.ForRead, false) is not DimStyleTableRecord style)
        {
            return false;
        }

        dimscale = style.Dimscale;
        cache[styleId] = dimscale;
        return true;
    }

    private static bool IsImperialOverride(double value)
    {
        return double.IsFinite(value)
            && ImperialOverrideFactors.Any(factor => Abs(value - factor) <= Tolerance);
    }

    private static bool AreClose(double left, double right)
    {
        return double.IsFinite(left)
            && double.IsFinite(right)
            && Abs(left - right) <= Tolerance;
    }
}
