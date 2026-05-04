using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Исправляет размерные объекты и стили размеров после клонирования из исходного чертежа.
/// Удаляет DSTYLE XData-переопределения, нормализует Dimlfac и сбрасывает ImperialOverride-масштабы.
/// </summary>
internal static class DimensionHealer
{
    private const double Tolerance = 1e-3;
    // Значение Dimscale, которое AutoCAD записывает при конвертации Imperial-чертежей: 304.8 = мм/фут.
    private const double ImperialOverrideFactor = 304.8;

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

        using Transaction tr = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount, int styleVisualPropsRescaled) = HealDimensionStyles(targetDb, tr);

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

            (bool overridesWereCleared, bool textRotationWasReset, bool dimlfacWasReset, string? warning) = HealDimension(dimension);

            if (warning is not null)
            {
                LoggerFactory.GetSharedLogger().Warning(warning);
            }

            if (overridesWereCleared || textRotationWasReset || dimlfacWasReset)
            {
                if (!wasOpenedForWrite && dimension.IsWriteEnabled)
                {
                    dimensionsOpenedForWrite++;
                }

                if (overridesWereCleared) overridesCleared++;
                if (textRotationWasReset) textRotationsReset++;
                if (dimlfacWasReset) dimensionDimlfacNormalized++;
            }
        }

        tr.Commit();

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();
        logger.Information(
            "DimensionHealer styles: dimlfac={DimlfacHealed}, dimscale={DimscaleNormalized}, visualProps={VisualPropsRescaled}.",
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount,
            styleVisualPropsRescaled);

        logger.Information(
            "DimensionHealer entities: overrides={OverridesCleared}, dimlfac={DimlfacNormalized}, textRotation={TextRotationsReset}.",
            overridesCleared,
            dimensionDimlfacNormalized,
            textRotationsReset);

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
    internal static (bool OverridesCleared, bool TextRotationReset, bool DimlfacReset, string? WarningMessage) HealDimension(Dimension dimension)
    {
        ObjectId styleId = dimension.DimensionStyle;
        bool hasTextRotation = !AreClose(dimension.TextRotation, 0.0);
        bool hasDimlfacDrift = !dimension.Dimlfac.Equals(1.0);
        bool overridesCleared = false;
        string? warningMessage = null;

        try
        {
            overridesCleared = DimensionUtils.TryRemoveDimensionStyleOverrides(dimension);
        }
        catch (System.Exception ex)
        {
            warningMessage = $"Dimension healer could not clear overrides for handle={dimension.Handle}: {ex.Message}";
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

        return (overridesCleared, hasTextRotation, hasDimlfacDrift, warningMessage);
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
            bool dimlfacNeedsNormalization = !style.Dimlfac.Equals(1.0);
            if (!hasVisualScaleOverride && !dimlfacNeedsNormalization)
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

            if (dimlfacNeedsNormalization)
            {
                style.Dimlfac = 1.0;
                dimlfacHealedCount++;
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
