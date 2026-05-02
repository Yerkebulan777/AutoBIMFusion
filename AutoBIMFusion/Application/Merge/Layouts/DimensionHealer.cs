using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private const double Tolerance = 1e-3;
    private const double ImperialOverrideFactor = 304.8;
    private const double TextVisualRoundStep = 10.0;
    private const double ObjectOffsetRoundStep = 50.0;
    private const double DimensionRoundScale = 100.0;
    private const int MaxDebugSamples = 5;

    private sealed record StyleHealSample(
        string Name,
        string Handle,
        double BeforeDimscale,
        double AfterDimscale,
        double BeforeDimtxt,
        double AfterDimtxt,
        double BeforeDimgap,
        double AfterDimgap,
        double BeforeDimexo,
        double AfterDimexo,
        double BeforeDimrnd,
        double AfterDimrnd);

    private sealed record DimensionOverrideSample(
        string Handle,
        string StyleId,
        bool OverridesCleared,
        double BeforeTextRotation,
        double AfterTextRotation);

    internal static int Heal(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        int dimensionsScanned = 0;
        int dimensionsOpenedForWrite = 0;
        int overridesCleared = 0;
        int textRotationsReset = 0;
        List<StyleHealSample> styleSamples = [];
        List<DimensionOverrideSample> dimensionSamples = [];

        using Transaction tr = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled, int dimrndScaledCount) =
            HealDimensionStyles(targetDb, tr, styleSamples);

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
                if (tr.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension)
                {
                    continue;
                }

                dimensionsScanned++;
                bool hasAcadOverrides = HasAcadOverrideXData(dimension);
                double beforeTextRotation = dimension.TextRotation;
                bool hasTextRotation = !AreClose(beforeTextRotation, 0.0);
                if (!hasAcadOverrides && !hasTextRotation)
                {
                    continue;
                }

                ObjectId styleId = dimension.DimensionStyle;
                dimension.UpgradeOpen();
                dimensionsOpenedForWrite++;

                if (hasAcadOverrides)
                {
                    try
                    {
                        using ResultBuffer clearAcadXData = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "ACAD"));
                        dimension.XData = clearAcadXData;
                        overridesCleared++;
                    }
                    catch (System.Exception)
                    {
                        hasAcadOverrides = false;
                    }
                }

                if (hasTextRotation)
                {
                    dimension.TextRotation = 0.0;
                    textRotationsReset++;
                }

                if (hasAcadOverrides && !styleId.IsNull)
                {
                    dimension.DimensionStyle = styleId;
                }

                if (dimensionSamples.Count < MaxDebugSamples)
                {
                    dimensionSamples.Add(new DimensionOverrideSample(
                        dimension.Handle.ToString(),
                        styleId.IsNull ? "<null>" : styleId.Handle.ToString(),
                        hasAcadOverrides,
                        beforeTextRotation,
                        dimension.TextRotation));
                }
            }
        }

        tr.Commit();

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();
        logger.Information(
            "Dimension style healer summary: dimlfacHealed={DimlfacHealed}, dimscaleNormalized={DimscaleNormalized}, visualPropsRescaled={VisualPropsRescaled}, dimrndScaled={DimrndScaled}.",
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount,
            styleVisualPropsRescaled,
            dimrndScaledCount);

        foreach (StyleHealSample sample in styleSamples)
        {
            logger.Debug(
                "Dimension style healer sample: style={StyleName}, handle={Handle}, dimscale {BeforeDimscale}->{AfterDimscale}, dimtxt {BeforeDimtxt}->{AfterDimtxt}, dimgap {BeforeDimgap}->{AfterDimgap}, dimexo {BeforeDimexo}->{AfterDimexo}, dimrnd {BeforeDimrnd}->{AfterDimrnd}.",
                sample.Name,
                sample.Handle,
                sample.BeforeDimscale,
                sample.AfterDimscale,
                sample.BeforeDimtxt,
                sample.AfterDimtxt,
                sample.BeforeDimgap,
                sample.AfterDimgap,
                sample.BeforeDimexo,
                sample.AfterDimexo,
                sample.BeforeDimrnd,
                sample.AfterDimrnd);
        }

        logger.Information(
            "Dimension entity healer summary: overridesCleared={OverridesCleared}, textRotationsReset={TextRotationsReset}, dimensionsScanned={DimensionsScanned}, dimensionsOpenedForWrite={DimensionsOpenedForWrite}.",
            overridesCleared,
            textRotationsReset,
            dimensionsScanned,
            dimensionsOpenedForWrite);

        foreach (DimensionOverrideSample sample in dimensionSamples)
        {
            logger.Debug(
                "Dimension entity healer sample: handle={Handle}, styleId={StyleId}, overridesCleared={OverridesCleared}, textRotation {BeforeTextRotation}->{AfterTextRotation}.",
                sample.Handle,
                sample.StyleId,
                sample.OverridesCleared,
                sample.BeforeTextRotation,
                sample.AfterTextRotation);
        }

        logger.Information("Healed {Count} dimensions infected with imperial overrides.", overridesCleared);

        return overridesCleared;
    }

    private static (int DimlfacHealedCount, int DimscaleNormalizedCount, int VisualPropsRescaledCount, int DimrndScaledCount) HealDimensionStyles(
        Database targetDb,
        Transaction tr,
        List<StyleHealSample> samples)
    {
        int dimlfacHealedCount = 0;
        int dimscaleNormalizedCount = 0;
        int visualPropsRescaledCount = 0;
        int dimrndScaledCount = 0;

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
            double beforeDimtxt = style.Dimtxt;
            double beforeDimgap = style.Dimgap;
            double beforeDimexo = style.Dimexo;
            double beforeDimrnd = style.Dimrnd;
            style.UpgradeOpen();

            if (hasVisualScaleOverride)
            {
                (int visualPropsRescaled, bool dimrndScaled) = NormalizeStyleVisualScale(style, beforeDimscale);
                visualPropsRescaledCount += visualPropsRescaled;
                if (dimrndScaled)
                {
                    dimrndScaledCount++;
                }

                dimscaleNormalizedCount++;
            }

            if (hasMeasurementScaleOverride)
            {
                style.Dimlfac = 1.0;
                dimlfacHealedCount++;
            }

            if (hasVisualScaleOverride && samples.Count < MaxDebugSamples)
            {
                samples.Add(new StyleHealSample(
                    style.Name,
                    style.Handle.ToString(),
                    beforeDimscale,
                    style.Dimscale,
                    beforeDimtxt,
                    style.Dimtxt,
                    beforeDimgap,
                    style.Dimgap,
                    beforeDimexo,
                    style.Dimexo,
                    beforeDimrnd,
                    style.Dimrnd));
            }
        }

        return (dimlfacHealedCount, dimscaleNormalizedCount, visualPropsRescaledCount, dimrndScaledCount);
    }

    private static (int VisualPropsRescaledCount, bool DimrndScaled) NormalizeStyleVisualScale(DimStyleTableRecord style, double scale)
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
        double oldDimrnd = style.Dimrnd;
        style.Dimrnd = ScaleDimensionRoundValue(style.Dimrnd);
        style.Dimtxt = RoundTextVisualValue(style.Dimtxt);
        style.Dimgap = RoundTextVisualValue(style.Dimgap);
        style.Dimexo = RoundObjectOffsetValue(ScaleDimensionRoundValue(style.Dimexo));
        style.Dimscale = 1.0;
        return (changed, !AreClose(oldDimrnd, style.Dimrnd));
    }

    private static double RoundTextVisualValue(double value)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            return value;
        }

        return Round(value / TextVisualRoundStep, 0, MidpointRounding.AwayFromZero) * TextVisualRoundStep;
    }

    private static double RoundObjectOffsetValue(double value)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            return value;
        }

        double sign = Sign(value);
        double roundedMagnitude = Round(Abs(value) / ObjectOffsetRoundStep, 0, MidpointRounding.AwayFromZero) * ObjectOffsetRoundStep;
        return sign * Max(ObjectOffsetRoundStep, roundedMagnitude);
    }

    private static double ScaleDimensionRoundValue(double value)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            return value;
        }

        return value * DimensionRoundScale;
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

    private static bool HasAcadOverrideXData(Dimension dimension)
    {
        ResultBuffer? xdata = dimension.XData;
        if (xdata is null)
        {
            return false;
        }

        using (xdata)
        {
            foreach (TypedValue value in xdata)
            {
                if (value.TypeCode == (int)DxfCode.ExtendedDataRegAppName
                    && value.Value is string appName
                    && appName.Equals("ACAD", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool AreClose(double left, double right)
    {
        return double.IsFinite(left)
            && double.IsFinite(right)
            && Abs(left - right) <= Tolerance;
    }
}
