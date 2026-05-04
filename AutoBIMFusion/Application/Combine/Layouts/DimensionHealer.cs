using AutoBIMFusion.Infrastructure.Logging;
using System.Text;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Исправляет размерные объекты и стили размеров после клонирования из исходного чертежа.
/// Удаляет DSTYLE XData-переопределения, нормализует Dimlfac и сбрасывает ImperialOverride-масштабы.
/// </summary>
internal static class DimensionHealer
{
    private const double Tolerance = 1e-5;
    private const double ImperialOverrideFactor = 304.8;

    /// <summary>Статистика операции исправления размеров.</summary>
    internal readonly record struct DimensionHealResult(
        int OverridesCleared,
        int TextRotationsReset,
        int DimensionDimlfacNormalized,
        int DimensionsScanned,
        int DimensionsOpenedForWrite,
        int DimlfacHealed,
        int DimscaleNormalized);

    /// <summary>
    /// Исправляет стили размеров и указанные размерные объекты в целевой базе данных.
    /// </summary>
    internal static DimensionHealResult Heal(Database targetDb, IReadOnlyCollection<ObjectId> dimensionIds)
    {
        int overridesCleared = 0;
        int dimensionsScanned = 0;
        int textRotationsReset = 0;
        int dimensionsOpenedForWrite = 0;
        int dimensionDimlfacNormalized = 0;

        ArgumentNullException.ThrowIfNull(targetDb);
        ArgumentNullException.ThrowIfNull(dimensionIds);

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();

        using Transaction trx = targetDb.TransactionManager.StartTransaction();

        (int healedStyleDimlfacCount, int normalizedStyleDimscaleCount) = HealDimensionStyles(targetDb, trx);

        StringBuilder warnings = new();

        foreach (ObjectId id in dimensionIds)
        {
            if (!id.IsNull && !id.IsErased)
            {
                if (trx.GetObject(id, OpenMode.ForRead, false) is Dimension dimension)
                {
                    dimensionsScanned++;
                    bool wasOpenedForWrite = dimension.IsWriteEnabled;

                    (bool overridesWereCleared, bool textRotationWasReset, bool dimlfacWasReset, string? warning) = HealDimension(dimension);

                    if (warning is not null)
                    {
                        _ = warnings.AppendLine(warning);
                    }

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
                    }
                }
            }
        }

        trx.Commit();

        if (warnings.Length > 0)
        {
            logger.Warning(warnings.ToString());
        }

        logger.Information(
            "DimensionHealer styles: dimlfac={DimlfacHealed}, dimscale={DimscaleNormalized}.",
            healedStyleDimlfacCount,
            normalizedStyleDimscaleCount);

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
            normalizedStyleDimscaleCount);
    }

    /// <summary>
    /// Исправляет один размерный объект: удаляет DSTYLE XData-переопределения,
    /// сбрасывает TextRotation в 0 и нормализует Dimlfac в 1.0.
    /// </summary>
    /// <remarks>Может открыть объект на запись (UpgradeOpen), если требуются изменения.</remarks>
    private static (bool OverridesCleared, bool TextRotationReset, bool DimlfacReset, string? WarningMessage) HealDimension(Dimension dimension)
    {
        ObjectId styleId = dimension.DimensionStyle;
        bool hasDimlfacDrift = !dimension.Dimlfac.Equals(1.0);
        bool hasTextRotation = !AreClose(dimension.TextRotation);

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

    private static (int DimlfacHealedCount, int DimscaleNormalizedCount) HealDimensionStyles(Database targetDb, Transaction tr)
    {
        int dimlfacHealedCount = 0;
        int dimscaleNormalizedCount = 0;

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);

        foreach (ObjectId styleId in dimStyleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style && !style.IsDependent)
            {
                bool hasVisualScaleOverride = IsImperialOverride(style.Dimscale);
                bool dimlfacNeedsNormalization = !style.Dimlfac.Equals(1.0);
                if (hasVisualScaleOverride || dimlfacNeedsNormalization)
                {
                    style.UpgradeOpen();

                    StringBuilder debugMessage = new();

                    if (hasVisualScaleOverride && NormalizeStyleVisualScale(style, style.Dimscale))
                    {
                        debugMessage.AppendLine($"DimStyle {style.Name} visual scale normalized from {style.Dimscale} to 1.0");

                        debugMessage.AppendLine($"New visual properties: ");
                        debugMessage.AppendLine($"Dimtxt={style.Dimtxt}");
                        debugMessage.AppendLine($"Dimasz={style.Dimasz}");
                        debugMessage.AppendLine($"Dimexo={style.Dimexo}");
                        debugMessage.AppendLine($"Dimexe={style.Dimexe}");
                        debugMessage.AppendLine($"Dimgap={style.Dimgap}");
                        debugMessage.AppendLine($"Dimdli={style.Dimdli}");
                        debugMessage.AppendLine($"Dimdle={style.Dimdle}");
                        debugMessage.AppendLine($"Dimcen={style.Dimcen}");
                        debugMessage.AppendLine($"Dimtsz={style.Dimtsz}");
                        debugMessage.AppendLine($"Dimtvp={style.Dimtvp}");
                        debugMessage.AppendLine($"Dimfxlen={style.Dimfxlen}");

                        dimscaleNormalizedCount++;
                    }

                    if (dimlfacNeedsNormalization)
                    {
                        style.Dimlfac = 1.0;
                        dimlfacHealedCount++;
                    }
                }
            }
        }

        return (dimlfacHealedCount, dimscaleNormalizedCount);
    }

    // Когда Dimscale равен imperial-фактору, AutoCAD умножает визуальные размеры на него при отображении.
    // Чтобы установить Dimscale=1.0 без визуального изменения чертежа, нужно предварительно
    // «запечь» этот множитель в каждое визуальное свойство.
    private static bool NormalizeStyleVisualScale(DimStyleTableRecord style, double scale)
    {
        bool changed = false;

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

    private static double ScaleVisualValue(double value, double scale, ref bool changed)
    {
        if (!double.IsFinite(value) || value == 0.0)
        {
            return value;
        }

        changed = true;
        return value * scale;
    }

    private static bool IsImperialOverride(double value)
    {
        return double.IsFinite(value) && AreClose(value);
    }

    private static bool AreClose(double value)
    {
        double different = Abs(value - ImperialOverrideFactor);

        return double.IsFinite(value) && different < Tolerance;
    }
}
