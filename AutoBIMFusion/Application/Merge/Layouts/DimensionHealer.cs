using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionHealer
{
    private const double Tolerance = 1e-3;
    private const double ImperialOverrideFactor = 304.8;
    private const double TextVisualRoundStep = 10.0;
    private const double ObjectOffsetRoundStep = 50.0;
    private const double ObjectOffsetScale = 100.0;
    private const int MaxDebugSamples = 5;

    public sealed record StyleHealSample(
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

    internal readonly record struct DimensionHealResult(
        int OverridesCleared,
        int TextRotationsReset,
        int DimensionsScanned,
        int DimensionsOpenedForWrite,
        int DimlfacHealed,
        int DimscaleNormalized,
        int VisualPropsRescaled);

    internal static DimensionHealResult Heal(Database targetDb, IReadOnlyCollection<ObjectId> dimensionIds)
    {
        ArgumentNullException.ThrowIfNull(targetDb);
        ArgumentNullException.ThrowIfNull(dimensionIds);

        int dimensionsScanned = 0;
        int dimensionsOpenedForWrite = 0;
        int overridesCleared = 0;
        int textRotationsReset = 0;
        List<StyleHealSample> styleSamples = [];
        List<DimensionOverrideSample> dimensionSamples = [];

        using Transaction tr = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled) =
            HealDimensionStyles(targetDb, tr, styleSamples);

        foreach (ObjectId id in dimensionIds)
        {
            if (id.IsNull || id.IsErased)
            {
                continue;
            }

            if (tr.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension)
            {
                continue;
            }

            dimensionsScanned++;
            bool wasOpenedForWrite = dimension.IsWriteEnabled;

            (bool overridesWereCleared, bool textRotationWasReset, double beforeTextRotation) = HealDimension(dimension);

            if (overridesWereCleared || textRotationWasReset)
            {
                if (!wasOpenedForWrite && dimension.IsWriteEnabled)
                {
                    dimensionsOpenedForWrite++;
                }

                if (overridesWereCleared)
                {
                    overridesCleared++;
                }

                if (textRotationWasReset)
                {
                    textRotationsReset++;
                }

                if (dimensionSamples.Count < MaxDebugSamples)
                {
                    ObjectId styleId = dimension.DimensionStyle;
                    dimensionSamples.Add(new DimensionOverrideSample(
                        dimension.Handle.ToString(),
                        styleId.IsNull ? "<null>" : styleId.Handle.ToString(),
                        overridesWereCleared,
                        beforeTextRotation,
                        dimension.TextRotation));
                }
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

        return new DimensionHealResult(
            overridesCleared,
            textRotationsReset,
            dimensionsScanned,
            dimensionsOpenedForWrite,
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount,
            styleVisualPropsRescaled);
    }

    internal static bool TryRemoveDimensionStyleOverrides(Dimension dim)
    {
        try
        {
            if (dim.XData == null)
            {
                return false;
            }

            using ResultBuffer rb = dim.XData;
            bool hasOverrides = false;
            foreach (TypedValue tv in rb)
            {
                if (tv.TypeCode == (int)DxfCode.ExtendedDataRegAppName && tv.Value.ToString() == "DSTYLE")
                {
                    hasOverrides = true;
                    break;
                }
            }

            if (hasOverrides)
            {
                // ResultBuffer только с именем приложения удаляет все данные этого приложения
                using ResultBuffer clearRb = new(new TypedValue((int)DxfCode.ExtendedDataRegAppName, "DSTYLE"));
                dim.XData = clearRb;
                return true;
            }
        }
        catch (System.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(ex, "Failed to remove DSTYLE overrides for dimension {Handle}", dim.Handle);
        }

        return false;
    }

    internal static (bool OverridesCleared, bool TextRotationReset, double BeforeTextRotation) HealDimension(Dimension dimension)
    {
        ObjectId styleId = dimension.DimensionStyle;
        double beforeTextRotation = dimension.TextRotation;
        bool hasTextRotation = !AreClose(beforeTextRotation, 0.0);
        bool overridesCleared = false;

        try
        {
            overridesCleared = TryRemoveDimensionStyleOverrides(dimension);
        }
        catch (System.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(
                ex,
                "Dimension healer could not clear overrides for handle={Handle}.",
                dimension.Handle.ToString());
        }

        if (hasTextRotation && !dimension.IsWriteEnabled)
        {
            dimension.UpgradeOpen();
        }

        if (hasTextRotation)
        {
            dimension.TextRotation = 0.0;
        }

        if (overridesCleared && !styleId.IsNull)
        {
            dimension.DimensionStyle = styleId;
        }

        return (overridesCleared, hasTextRotation, beforeTextRotation);
    }

    public static (int DimlfacHealedCount, int DimscaleNormalizedCount, int VisualPropsRescaledCount) HealDimensionStyles(
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
                visualPropsRescaledCount += NormalizeStyleVisualScale(style, beforeDimscale);
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

        return (dimlfacHealedCount, dimscaleNormalizedCount, visualPropsRescaledCount);
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
        style.Dimtxt = RoundTextVisualValue(style.Dimtxt);
        style.Dimgap = RoundTextVisualValue(style.Dimgap);
        style.Dimexo = RoundObjectOffsetValue(ScaleObjectOffsetValue(style.Dimexo));
        style.Dimscale = 1.0;
        return changed;
    }

    private static double RoundTextVisualValue(double value)
    {
        return !double.IsFinite(value) || value == 0.0
            ? value
            : Round(value / TextVisualRoundStep, 0, MidpointRounding.AwayFromZero) * TextVisualRoundStep;
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

    private static double ScaleObjectOffsetValue(double value)
    {
        return !double.IsFinite(value) || value == 0.0 ? value : value * ObjectOffsetScale;
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
