using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private static readonly double[] ImperialOverrideFactors = [304.8, 25.4, 12.0];
    private const double Tolerance = 1e-6;
    private const int MaxDebugSamples = 5;
    private const int VisualTextRoundingDigits = 3;

    private sealed record StyleSnapshot(string Name, double Dimscale);

    private sealed record StyleHealSample(
        string Name,
        string Handle,
        double BeforeDimscale,
        double AfterDimscale,
        double BeforeDimtxt,
        double AfterDimtxt,
        double BeforeDimasz,
        double AfterDimasz,
        double BeforeDimlfac,
        double AfterDimlfac);

    private sealed record DimensionHealSample(
        string Handle,
        string StyleName,
        double BeforeDimscale,
        double AfterDimscale,
        double BeforeDimtxt,
        double AfterDimtxt,
        double BeforeDimasz,
        double AfterDimasz,
        double BeforeDimlfac,
        double AfterDimlfac,
        bool VisualScaleOverride,
        bool MeasurementScaleOverride);

    internal static int Heal(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        int healedCount = 0;
        int visualScaleNormalizedCount = 0;
        int measurementScaleCount = 0;
        int bothCount = 0;
        int entityVisualPropsRescaled = 0;
        List<StyleHealSample> styleSamples = [];
        List<DimensionHealSample> samples = [];

        using Transaction tr = targetDb.TransactionManager.StartTransaction();
        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled) =
            HealDimensionStyles(targetDb, tr, styleSamples);

        DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(targetDb.LayoutDictionaryId, OpenMode.ForRead);
        HashSet<ObjectId> visitedBlocks = [];
        Dictionary<ObjectId, StyleSnapshot> styleCache = [];

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
                bool hasStyle = TryGetStyleSnapshot(tr, styleId, styleCache, out StyleSnapshot? styleSnapshot);
                double styleDimscale = styleSnapshot?.Dimscale ?? 1.0;
                bool hasVisualScaleOverride = IsImperialOverride(dimension.Dimscale)
                    && (!hasStyle || !AreClose(dimension.Dimscale, styleDimscale));
                bool hasMeasurementScaleOverride = IsImperialOverride(dimension.Dimlfac);
                if (!hasVisualScaleOverride && !hasMeasurementScaleOverride)
                {
                    continue;
                }

                double beforeDimscale = dimension.Dimscale;
                double beforeDimtxt = dimension.Dimtxt;
                double beforeDimasz = dimension.Dimasz;
                double beforeDimlfac = dimension.Dimlfac;

                dimension.UpgradeOpen();

                try
                {
                    using ResultBuffer clearAcadXData = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "ACAD"));
                    dimension.XData = clearAcadXData;
                }
                catch (System.Exception)
                {
                    // ACAD xdata is not always present; style reassignment below still refreshes the dimension.
                }

                if (!styleId.IsNull)
                {
                    dimension.DimensionStyle = styleId;
                }

                if (hasVisualScaleOverride)
                {
                    entityVisualPropsRescaled += NormalizeDimensionVisualScale(dimension, beforeDimscale);
                }

                dimension.Dimlfac = 1.0;

                if (hasVisualScaleOverride)
                {
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

                if (samples.Count < MaxDebugSamples)
                {
                    samples.Add(new DimensionHealSample(
                        dimension.Handle.ToString(),
                        styleSnapshot?.Name ?? "<unknown>",
                        beforeDimscale,
                        dimension.Dimscale,
                        beforeDimtxt,
                        dimension.Dimtxt,
                        beforeDimasz,
                        dimension.Dimasz,
                        beforeDimlfac,
                        dimension.Dimlfac,
                        hasVisualScaleOverride,
                        hasMeasurementScaleOverride));
                }

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

        foreach (StyleHealSample sample in styleSamples)
        {
            logger.Debug(
                "Dimension style healer sample: style={StyleName}, handle={Handle}, dimscale {BeforeDimscale}->{AfterDimscale}, dimtxt {BeforeDimtxt}->{AfterDimtxt}, dimasz {BeforeDimasz}->{AfterDimasz}, dimlfac {BeforeDimlfac}->{AfterDimlfac}.",
                sample.Name,
                sample.Handle,
                sample.BeforeDimscale,
                sample.AfterDimscale,
                sample.BeforeDimtxt,
                sample.AfterDimtxt,
                sample.BeforeDimasz,
                sample.AfterDimasz,
                sample.BeforeDimlfac,
                sample.AfterDimlfac);
        }

        logger.Information(
            "Dimension entity healer summary: total={Total}, measurementFactor={MeasurementFactor}, visualScaleNormalized={VisualScaleNormalized}, both={Both}, visualPropsRescaled={VisualPropsRescaled}.",
            healedCount,
            measurementScaleCount,
            visualScaleNormalizedCount,
            bothCount,
            entityVisualPropsRescaled);

        foreach (DimensionHealSample sample in samples)
        {
            logger.Debug(
                "Dimension healer sample: handle={Handle}, style={StyleName}, dimscale {BeforeDimscale}->{AfterDimscale}, dimtxt {BeforeDimtxt}->{AfterDimtxt}, dimasz {BeforeDimasz}->{AfterDimasz}, dimlfac {BeforeDimlfac}->{AfterDimlfac}, visualScale={VisualScaleOverride}, measurementFactor={MeasurementScaleOverride}.",
                sample.Handle,
                sample.StyleName,
                sample.BeforeDimscale,
                sample.AfterDimscale,
                sample.BeforeDimtxt,
                sample.AfterDimtxt,
                sample.BeforeDimasz,
                sample.AfterDimasz,
                sample.BeforeDimlfac,
                sample.AfterDimlfac,
                sample.VisualScaleOverride,
                sample.MeasurementScaleOverride);
        }

        logger.Information("Healed {Count} dimensions infected with imperial overrides.", healedCount);

        return healedCount;
    }

    private static (int DimlfacHealedCount, int DimscaleNormalizedCount, int VisualPropsRescaledCount) HealDimensionStyles(
        Database targetDb,
        Transaction tr,
        List<StyleHealSample> samples)
    {
        int dimlfacHealedCount = 0;
        int dimscaleNormalizedCount = 0;
        int visualPropsRescaledCount = 0;

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId styleId in dimStyleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is not DimStyleTableRecord style)
            {
                continue;
            }

            if (style.IsDependent)
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
            double beforeDimtxt = style.Dimtxt;
            double beforeDimasz = style.Dimasz;
            double beforeDimlfac = style.Dimlfac;

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

            if (samples.Count < MaxDebugSamples)
            {
                samples.Add(new StyleHealSample(
                    style.Name,
                    style.Handle.ToString(),
                    beforeDimscale,
                    style.Dimscale,
                    beforeDimtxt,
                    style.Dimtxt,
                    beforeDimasz,
                    style.Dimasz,
                    beforeDimlfac,
                    style.Dimlfac));
            }
        }

        return (dimlfacHealedCount, dimscaleNormalizedCount, visualPropsRescaledCount);
    }

    private static bool TryGetStyleSnapshot(
        Transaction tr,
        ObjectId styleId,
        Dictionary<ObjectId, StyleSnapshot> cache,
        out StyleSnapshot? snapshot)
    {
        snapshot = null;
        if (styleId.IsNull)
        {
            return false;
        }

        if (cache.TryGetValue(styleId, out snapshot))
        {
            return true;
        }

        if (tr.GetObject(styleId, OpenMode.ForRead, false) is not DimStyleTableRecord style)
        {
            return false;
        }

        snapshot = new StyleSnapshot(style.Name, style.Dimscale);
        cache[styleId] = snapshot;
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
        RoundStyleTextValues(style);
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
        RoundDimensionTextValues(dimension);
        dimension.Dimscale = 1.0;
        return changed;
    }

    private static void RoundStyleTextValues(DimStyleTableRecord style)
    {
        style.Dimtxt = RoundTextValue(style.Dimtxt);
        style.Dimgap = RoundTextValue(style.Dimgap);
        style.Dimtfac = RoundTextValue(style.Dimtfac);
    }

    private static void RoundDimensionTextValues(Dimension dimension)
    {
        dimension.Dimtxt = RoundTextValue(dimension.Dimtxt);
        dimension.Dimgap = RoundTextValue(dimension.Dimgap);
        dimension.Dimtfac = RoundTextValue(dimension.Dimtfac);
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
            && ImperialOverrideFactors.Any(factor => Abs(value - factor) <= Tolerance);
    }

    private static bool AreClose(double left, double right)
    {
        return double.IsFinite(left)
            && double.IsFinite(right)
            && Abs(left - right) <= Tolerance;
    }
}
