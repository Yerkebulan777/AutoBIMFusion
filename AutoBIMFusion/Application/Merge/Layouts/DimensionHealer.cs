using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private const double Tolerance = 1e-3;
    private const double ImperialOverrideFactor = 304.8;
    private const int VisualTextRoundingDigits = 3;

    internal static int Heal(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        int bothCount = 0;
        int healedCount = 0;
        int measurementScaleCount = 0;
        int visualScaleNormalizedCount = 0;
        int entityVisualPropsRescaled = 0;

        using Transaction tr = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled) = HealDimensionStyles(targetDb, tr);

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
                bool hasStyle = TryGetStyleDimscale(tr, styleId, styleDimscaleCache, out double styleDimscale);
                bool hasVisualScaleOverride = IsImperialOverride(dimension.Dimscale)
                    && (!hasStyle || !AreClose(dimension.Dimscale, styleDimscale));
                bool hasMeasurementScaleOverride = IsImperialOverride(dimension.Dimlfac);
                if (!hasVisualScaleOverride && !hasMeasurementScaleOverride)
                {
                    continue;
                }

                double beforeDimscale = dimension.Dimscale;

                dimension.UpgradeOpen();

                try
                {
                    using ResultBuffer clearAcadXData = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "ACAD"));
                    dimension.XData = clearAcadXData;
                }
                catch (System.Exception)
                {
                }

                if (!styleId.IsNull)
                {
                    dimension.DimensionStyle = styleId;
                }

                if (hasVisualScaleOverride)
                {
                    entityVisualPropsRescaled += NormalizeDimensionVisualScale(dimension, beforeDimscale);
                    visualScaleNormalizedCount++;
                }

                if (hasMeasurementScaleOverride)
                {
                    measurementScaleCount++;
                }

                if (hasVisualScaleOverride && hasMeasurementScaleOverride)
                {
                    bothCount++;
                }

                dimension.Dimlfac = 1.0;
                healedCount++;
            }
        }

        tr.Commit();

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();
        logger.Information(
            "Dimension style healer summary: dimlfacHealed={DimlfacHealed}, dimscaleNormalized={DimscaleNormalized}, visualPropsRescaled={VisualPropsRescaled}.",
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount,
            styleVisualPropsRescaled);

        logger.Information(
            "Dimension entity healer summary: total={Total}, measurementFactor={MeasurementFactor}, visualScaleNormalized={VisualScaleNormalized}, both={Both}, visualPropsRescaled={VisualPropsRescaled}.",
            healedCount,
            measurementScaleCount,
            visualScaleNormalizedCount,
            bothCount,
            entityVisualPropsRescaled);

        logger.Information("Healed {Count} dimensions infected with imperial overrides.", healedCount);

        return healedCount;
    }

    private static (int DimlfacHealedCount, int DimscaleNormalizedCount, int VisualPropsRescaledCount) HealDimensionStyles(
        Database targetDb,
        Transaction tr)
    {
        int dimlfacHealedCount = 0;
        int dimscaleNormalizedCount = 0;
        int visualPropsRescaledCount = 0;

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId styleId in dimStyleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is not DimStyleTableRecord style || style.IsDependent)
            {
                continue;
            }

            bool hasVisualScaleOverride = IsImperialOverride(style.Dimscale);
            bool hasMeasurementScaleOverride = IsImperialOverride(style.Dimlfac);
            if (!hasVisualScaleOverride && !hasMeasurementScaleOverride)
            {
                continue;
            }

            double beforeDimscale = style.Dimscale;
            style.UpgradeOpen();

            if (hasVisualScaleOverride)
            {
                visualPropsRescaledCount += NormalizeStyleVisualScale(style, beforeDimscale);
                dimscaleNormalizedCount++;
            }

            if (hasMeasurementScaleOverride)
            {
                style.Dimlfac = 1.0;
                dimlfacHealedCount++;
            }
        }

        return (dimlfacHealedCount, dimscaleNormalizedCount, visualPropsRescaledCount);
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

    private static int NormalizeStyleVisualScale(DimStyleTableRecord style, double scale)
    {
        int changed = 0;
        style.Dimtxt = ScaleVisualValue(style.Dimtxt, scale, ref changed);
        style.Dimasz = ScaleVisualValue(style.Dimasz, scale, ref changed);
        style.Dimexo = ScaleVisualValue(style.Dimexo, scale, ref changed);
        style.Dimexe = ScaleVisualValue(style.Dimexe, scale, ref changed);
        style.Dimgap = ScaleVisualValue(style.Dimgap, scale, ref changed);
        style.Dimdli = ScaleVisualValue(style.Dimdli, scale, ref changed);
        style.Dimdle = ScaleVisualValue(style.Dimdle, scale, ref changed);
        style.Dimcen = ScaleVisualValue(style.Dimcen, scale, ref changed);
        style.Dimtsz = ScaleVisualValue(style.Dimtsz, scale, ref changed);
        style.Dimtvp = ScaleVisualValue(style.Dimtvp, scale, ref changed);
        style.Dimfxlen = ScaleVisualValue(style.Dimfxlen, scale, ref changed);
        style.Dimtxt = RoundTextValue(style.Dimtxt);
        style.Dimgap = RoundTextValue(style.Dimgap);
        style.Dimtfac = RoundTextValue(style.Dimtfac);
        style.Dimscale = 1.0;
        return changed;
    }

    private static int NormalizeDimensionVisualScale(Dimension dimension, double scale)
    {
        int changed = 0;
        dimension.Dimtxt = ScaleVisualValue(dimension.Dimtxt, scale, ref changed);
        dimension.Dimasz = ScaleVisualValue(dimension.Dimasz, scale, ref changed);
        dimension.Dimexo = ScaleVisualValue(dimension.Dimexo, scale, ref changed);
        dimension.Dimexe = ScaleVisualValue(dimension.Dimexe, scale, ref changed);
        dimension.Dimgap = ScaleVisualValue(dimension.Dimgap, scale, ref changed);
        dimension.Dimdli = ScaleVisualValue(dimension.Dimdli, scale, ref changed);
        dimension.Dimdle = ScaleVisualValue(dimension.Dimdle, scale, ref changed);
        dimension.Dimcen = ScaleVisualValue(dimension.Dimcen, scale, ref changed);
        dimension.Dimtsz = ScaleVisualValue(dimension.Dimtsz, scale, ref changed);
        dimension.Dimtvp = ScaleVisualValue(dimension.Dimtvp, scale, ref changed);
        dimension.Dimfxlen = ScaleVisualValue(dimension.Dimfxlen, scale, ref changed);
        dimension.Dimtxt = RoundTextValue(dimension.Dimtxt);
        dimension.Dimgap = RoundTextValue(dimension.Dimgap);
        dimension.Dimtfac = RoundTextValue(dimension.Dimtfac);
        dimension.Dimscale = 1.0;
        return changed;
    }

    private static double RoundTextValue(double value)
    {
        return double.IsFinite(value)
            ? Round(value, VisualTextRoundingDigits, MidpointRounding.AwayFromZero)
            : value;
    }

    private static double ScaleVisualValue(double value, double scale, ref int changedCount)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            return value;
        }

        changedCount++;
        return value * scale;
    }

    private static bool IsImperialOverride(double value)
    {
        return double.IsFinite(value)
            && Abs(value - ImperialOverrideFactor) <= Tolerance;
    }

    private static bool AreClose(double left, double right)
    {
        return double.IsFinite(left)
            && double.IsFinite(right)
            && Abs(left - right) <= Tolerance;
    }
}
