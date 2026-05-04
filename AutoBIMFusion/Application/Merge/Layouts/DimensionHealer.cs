using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Исправляет размерные объекты и стили размеров после клонирования из исходного чертежа.
/// Удаляет DSTYLE XData-переопределения, нормализует Dimlfac и сбрасывает ImperialOverride-масштабы.
/// </summary>
internal static class DimensionHealer
{
    private const double Tolerance = 1e-3;
    private const double ImperialOverrideFactor = 304.8;

    // Значение Dimscale, которое AutoCAD записывает при конвертации Imperial-чертежей:
    // 304.8 = мм/фут.
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
        double AfterDimrnd,
        double BeforeDimlfac,
        double AfterDimlfac);

    private sealed record DimensionOverrideSample(
        string Handle,
        string StyleId,
        bool OverridesCleared,
        bool DimlfacReset,
        double BeforeTextRotation,
        double AfterTextRotation,
        double BeforeDimlfac,
        double AfterDimlfac);

    /// <summary>Статистика операции исправления размеров.</summary>
    internal readonly record struct DimensionHealResult(
        int OverridesCleared,
        int TextRotationsReset,
        int DimensionDimlfacNormalized,
        int DimensionsScanned,
        int DimensionsOpenedForWrite,
        int DimlfacHealed,
        int DimscaleNormalized,
        int VisualPropsRescaled);

    /// <summary>
    /// Исправляет стили размеров и указанные размерные объекты в целевой базе данных.
    /// </summary>
    /// <param name="targetDb">Целевая база данных AutoCAD.</param>
    /// <param name="dimensionIds">ObjectId размерных объектов, подлежащих исправлению.</param>
    /// <returns>Статистика по числу исправленных объектов и стилей.</returns>
    internal static DimensionHealResult Heal(Database targetDb, IReadOnlyCollection<ObjectId> dimensionIds)
    {
        ArgumentNullException.ThrowIfNull(targetDb);
        ArgumentNullException.ThrowIfNull(dimensionIds);

        int overridesCleared = 0;
        int dimensionsScanned = 0;
        int textRotationsReset = 0;
        int dimensionsOpenedForWrite = 0;
        int dimensionDimlfacNormalized = 0;

        List<StyleHealSample> styleSamples = [];
        List<DimensionOverrideSample> dimensionSamples = [];

        using Transaction tr = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled) = HealDimensionStyles(targetDb, tr, styleSamples);

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

            (bool overridesWereCleared, bool textRotationWasReset, bool dimlfacWasReset, double beforeTextRotation, double beforeDimlfac) = HealDimension(dimension);

            if (overridesWereCleared || textRotationWasReset || dimlfacWasReset)
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

                if (dimlfacWasReset)
                {
                    dimensionDimlfacNormalized++;
                }

                if (dimensionSamples.Count < MaxDebugSamples)
                {
                    ObjectId styleId = dimension.DimensionStyle;
                    dimensionSamples.Add(new DimensionOverrideSample(
                        dimension.Handle.ToString(),
                        styleId.IsNull ? "<null>" : styleId.Handle.ToString(),
                        overridesWereCleared,
                        dimlfacWasReset,
                        beforeTextRotation,
                        dimension.TextRotation,
                        beforeDimlfac,
                        dimension.Dimlfac));
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
                "Dimension style healer sample: style={StyleName}, handle={Handle}, dimscale {BeforeDimscale}->{AfterDimscale}, dimtxt {BeforeDimtxt}->{AfterDimtxt}, dimgap {BeforeDimgap}->{AfterDimgap}, dimexo {BeforeDimexo}->{AfterDimexo}, dimrnd {BeforeDimrnd}->{AfterDimrnd}, dimlfac {BeforeDimlfac}->{AfterDimlfac}.",
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
                sample.AfterDimrnd,
                sample.BeforeDimlfac,
                sample.AfterDimlfac);
        }

        logger.Information(
            "Dimension entity healer summary: overridesCleared={OverridesCleared}, textRotationsReset={TextRotationsReset}, dimlfacNormalized={DimlfacNormalized}, dimensionsScanned={DimensionsScanned}, dimensionsOpenedForWrite={DimensionsOpenedForWrite}.",
            overridesCleared,
            textRotationsReset,
            dimensionDimlfacNormalized,
            dimensionsScanned,
            dimensionsOpenedForWrite);

        foreach (DimensionOverrideSample sample in dimensionSamples)
        {
            logger.Debug(
                "Dimension entity healer sample: handle={Handle}, styleId={StyleId}, overridesCleared={OverridesCleared}, dimlfacReset={DimlfacReset}, textRotation {BeforeTextRotation}->{AfterTextRotation}, dimlfac {BeforeDimlfac}->{AfterDimlfac}.",
                sample.Handle,
                sample.StyleId,
                sample.OverridesCleared,
                sample.DimlfacReset,
                sample.BeforeTextRotation,
                sample.AfterTextRotation,
                sample.BeforeDimlfac,
                sample.AfterDimlfac);
        }

        return new DimensionHealResult(
            overridesCleared,
            textRotationsReset,
            dimensionDimlfacNormalized,
            dimensionsScanned,
            dimensionsOpenedForWrite,
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount,
            styleVisualPropsRescaled);
    }

    /// <summary>
    /// Исправляет один размерный объект: удаляет DSTYLE XData-переопределения,
    /// сбрасывает TextRotation в 0 и нормализует Dimlfac в 1.0.
    /// </summary>
    /// <remarks>Может открыть объект на запись (UpgradeOpen), если требуются изменения.</remarks>
    internal static (bool OverridesCleared, bool TextRotationReset, bool DimlfacReset, double BeforeTextRotation, double BeforeDimlfac) HealDimension(Dimension dimension)
    {
        ObjectId styleId = dimension.DimensionStyle;
        double beforeTextRotation = dimension.TextRotation;
        double beforeDimlfac = dimension.Dimlfac;
        bool hasTextRotation = !AreClose(beforeTextRotation, 0.0);
        bool hasDimlfacDrift = !beforeDimlfac.Equals(1.0);
        bool overridesCleared = false;

        try
        {
            overridesCleared = DimensionUtils.TryRemoveDimensionStyleOverrides(dimension);
        }
        catch (System.Exception ex)
        {
            LoggerFactory.GetSharedLogger().Warning(
                ex,
                "Dimension healer could not clear overrides for handle={Handle}.",
                dimension.Handle.ToString());
        }

        if ((hasTextRotation || hasDimlfacDrift) && !dimension.IsWriteEnabled)
        {
            dimension.UpgradeOpen();
        }

        if (hasTextRotation)
        {
            dimension.TextRotation = 0.0;
        }

        if (hasDimlfacDrift)
        {
            dimension.Dimlfac = 1.0;
        }

        // После удаления XData повторное присвоение того же стиля заставляет AutoCAD
        // перечитать свойства из таблицы стилей и сбросить кэшированные override-значения.
        if (overridesCleared && !styleId.IsNull)
        {
            dimension.DimensionStyle = styleId;
        }

        return (overridesCleared, hasTextRotation, hasDimlfacDrift, beforeTextRotation, beforeDimlfac);
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
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is not DimStyleTableRecord style || style.IsDependent)
            {
                continue;
            }

            bool hasVisualScaleOverride = IsImperialOverride(style.Dimscale);
            bool dimlfacNeedsNormalization = !style.Dimlfac.Equals(1.0);
            if (!hasVisualScaleOverride && !dimlfacNeedsNormalization)
            {
                continue;
            }

            double beforeDimscale = style.Dimscale;
            double beforeDimtxt = style.Dimtxt;
            double beforeDimgap = style.Dimgap;
            double beforeDimexo = style.Dimexo;
            double beforeDimrnd = style.Dimrnd;
            double beforeDimlfac = style.Dimlfac;
            style.UpgradeOpen();

            if (hasVisualScaleOverride)
            {
                visualPropsRescaledCount += NormalizeStyleVisualScale(style, beforeDimscale);
                dimscaleNormalizedCount++;
            }

            if (dimlfacNeedsNormalization)
            {
                style.Dimlfac = 1.0;
                dimlfacHealedCount++;
            }

            if ((hasVisualScaleOverride || dimlfacNeedsNormalization) && samples.Count < MaxDebugSamples)
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
                    style.Dimrnd,
                    beforeDimlfac,
                    style.Dimlfac));
            }
        }

        return (dimlfacHealedCount, dimscaleNormalizedCount, visualPropsRescaledCount);
    }

    // Когда Dimscale равен imperial-фактору, AutoCAD умножает визуальные размеры на него при отображении.
    // Чтобы установить Dimscale=1.0 без визуального изменения чертежа, нужно предварительно
    // «запечь» этот множитель в каждое визуальное свойство.
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
        style.Dimscale = 1.0;
        return changed;
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
        if (double.IsFinite(value))
        {
            return AreClose(value, ImperialOverrideFactor);
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
