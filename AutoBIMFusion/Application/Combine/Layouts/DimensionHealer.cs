using AutoBIMFusion.Infrastructure.Logging;
using System.Text;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Исправляет размерные объекты и стили размеров после клонирования из исходного чертежа.
/// Удаляет DSTYLE XData-переопределения, нормализует Dimlfac и приводит визуальный масштаб стилей к единому виду.
/// </summary>
internal static class DimensionHealer
{
    private const double Tolerance = 1e-5;
    private const double ImperialOverrideFactor = 304.8;
    private const double DimtxtSanityThreshold = 100.0;

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
    /// Единоразовая постобработка: исправляет ВСЕ размерные стили и объекты-размеры
    /// в целевой базе данных. Вызывается однократно после слияния всех листов,
    /// перед сохранением результирующего DWG.
    /// Гарантирует, что HealDimensionStyles вызовется ровно один раз,
    /// исключая многократное умножение масштабов анонимных стилей *D...
    /// </summary>
    internal static DimensionHealResult HealAll(Database targetDb)
    {
        ArgumentNullException.ThrowIfNull(targetDb);

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();
        IReadOnlyList<ObjectId> allDimIds = CollectAllDimensionIds(targetDb);
        logger.Information("HealAll: собрано {Count} объектов Dimension из всех BlockTableRecord.", allDimIds.Count);

        return Heal(targetDb, allDimIds);
    }

    /// <summary>
    /// Исправляет один размерный объект: удаляет DSTYLE XData-переопределения,
    /// сбрасывает TextRotation в 0 и нормализует Dimlfac в 1.0.
    /// </summary>
    /// <remarks>
    /// КРИТИЧЕСКИ ВАЖНЫЙ ПОРЯДОК ОПЕРАЦИЙ:
    ///   1. TryRemoveDimensionStyleOverrides(dim) — очистка XData переопределений
    ///   2. dimension.DimensionStyle = styleId     — регенерация кэша из стиля
    /// Если сделать наоборот, AutoCAD создаст новый кривой анонимный стиль *D...,
    /// так как при присвоении DimensionStyle он считает XData-переопределения
    /// и генерирует анонимный стиль на их основе.
    /// </remarks>
    private static (bool OverridesCleared, bool TextRotationReset, bool DimlfacReset, string? WarningMessage) HealDimension(Dimension dimension)
    {
        ObjectId styleId = dimension.DimensionStyle;
        bool hasDimlfacDrift = !dimension.Dimlfac.Equals(1.0);
        bool hasTextRotation = Math.Abs(dimension.TextRotation) > Tolerance;

        bool overridesCleared = false;
        string? warningMessage = null;

        // ── ШАГ 1: Очистка XData переопределений ДО регенерации стиля ──
        // Это ДОЛЖНО быть выполнено первым. Иначе при присвоении DimensionStyle
        // AutoCAD увидит DSTYLE XData и создаст новый анонимный стиль *D...
        try
        {
            overridesCleared = DimensionUtils.TryRemoveDimensionStyleOverrides(dimension);
        }
        catch (System.Exception ex)
        {
            warningMessage = $"Dimension healer could not clear overrides for handle={dimension.Handle}: {ex.Message}";
        }

        // ВСЕГДА открываем размер на запись. Нам нужно сбросить кэш.
        if (!dimension.IsWriteEnabled)
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

        // ── ШАГ 2: Регенерация кэша из стиля (СТРОГО ПОСЛЕ очистки XData) ──
        // Это заставит объект Dimension удалить из своего кэша старый Dimscale=304.8,
        // прочитать новые "запеченные" значения из стиля и предотвратить
        // появление анонимных стилей *D... при WblockCloneObjects.
        if (!styleId.IsNull)
        {
            dimension.DimensionStyle = styleId;
        }

        return (overridesCleared, hasTextRotation, hasDimlfacDrift, warningMessage);
    }

    /// <summary>
    /// Собирает ObjectId всех объектов типа <see cref="Dimension"/> из всех
    /// <see cref="BlockTableRecord"/> базы данных (Model Space, Paper Space, именованные блоки).
    /// </summary>
    private static IReadOnlyList<ObjectId> CollectAllDimensionIds(Database db)
    {
        List<ObjectId> ids = [];

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in blockTable)
        {
            if (tr.GetObject(btrId, OpenMode.ForRead, false) is not BlockTableRecord btr)
            {
                continue;
            }

            foreach (ObjectId id in btr)
            {
                if (!id.IsNull && !id.IsErased && tr.GetObject(id, OpenMode.ForRead, false) is Dimension)
                {
                    ids.Add(id);
                }
            }
        }

        tr.Commit();
        return ids;
    }

    private static (int DimlfacHealedCount, int DimscaleNormalizedCount) HealDimensionStyles(Database targetDb, Transaction tr)
    {
        int dimlfacHealedCount = 0;
        int dimscaleNormalizedCount = 0;

        Serilog.Core.Logger logger = LoggerFactory.GetSharedLogger();
        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(targetDb.DimStyleTableId, OpenMode.ForRead);

        foreach (ObjectId styleId in dimStyleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style && !style.IsDependent)
            {
                bool hasVisualScaleOverride = IsImperialOverride(style.Dimscale);
                bool dimlfacNeedsNormalization = !style.Dimlfac.Equals(1.0);

                // Логируем состояние каждого стиля перед принятием решения
                logger.Information(
                    "[Обнаружен стиль: {Name}] Dimscale: {Dimscale}, Dimtxt: {Dimtxt}, Dimasz: {Dimasz}, Dimlfac: {Dimlfac}",
                    style.Name, style.Dimscale, style.Dimtxt, style.Dimasz, style.Dimlfac);

                if (hasVisualScaleOverride || dimlfacNeedsNormalization)
                {
                    style.UpgradeOpen();

                    if (hasVisualScaleOverride)
                    {
                        // ── ПРЕДОХРАНИТЕЛЬ (Sanity Check) ──────────────────────────────
                        // Если Dimtxt уже превышает порог, значит визуальные свойства
                        // стиля УЖЕ были «запечены» ранее (умножены на Dimscale).
                        // Повторное умножение привело бы к гигантским размерам
                        // (например, 762.0 × 304.8 = 232 257.6).
                        // Применяется как к именованным, так и к анонимным (*D...) стилям.
                        // ─────────────────────────────────────────────────────────────
                        if (style.Dimtxt > DimtxtSanityThreshold)
                        {
                            logger.Warning(
                                "[ПРОПУСК] Стиль {Name} пропущен (сработал предохранитель, Dimtxt={Dimtxt} слишком велик, порог={Threshold}). " +
                                "Dimscale сбрасывается в 1.0 без перемасштабирования визуальных свойств.",
                                style.Name, style.Dimtxt, DimtxtSanityThreshold);

                            // Сбрасываем Dimscale без умножения визуальных свойств —
                            // они уже содержат корректные «запечённые» значения.
                            style.Dimscale = 1.0;
                            dimscaleNormalizedCount++;
                        }
                        else
                        {
                            logger.Information(
                                "[ИСПРАВЛЕНИЕ] Стиль {Name}: умножение визуальных свойств на Dimscale={Dimscale}...",
                                style.Name, style.Dimscale);

                            NormalizeStyleVisualScale(style, style.Dimscale);
                            dimscaleNormalizedCount++;

                            logger.Information(
                                "[ИСПРАВЛЕН] Стиль {Name} нормализован. Новый Dimtxt: {Dimtxt}, Dimasz: {Dimasz}, Dimscale сброшен в 1.0",
                                style.Name, style.Dimtxt, style.Dimasz);
                        }
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

    // Когда Dimscale равен imperial-фактору (304.8), AutoCAD умножает все визуальные размеры
    // на него при отображении. Чтобы установить Dimscale=1.0 без визуального изменения чертежа,
    // нужно предварительно «запечь» этот множитель в каждое визуальное свойство.
    //
    // Масштабируются ТОЛЬКО свойства, которые AutoCAD официально умножает на Dimscale
    // (см. https://help.autodesk.com/view/ACD/2025/ENU/?guid=GUID-AEA309F0-B831-4432-8085-FB6CD49CDC78):
    //   — геометрические размеры: высота текста, стрелки, отступы, зазоры, длины.
    //
    // Намеренно НЕ масштабируются:
    //   — Dimtfac  : коэффициент высоты текста допусков (ratio, не абсолютный размер)
    //   — Dimlfac  : масштабный множитель измеряемых длин (область измерений, не геометрии)
    //   — Dimaltf  : множитель альтернативных единиц (область измерений)
    //   — Dimrnd   : округление измеряемых расстояний (область измерений)
    //   — Dimtp/Dimtm: допуски ± (значения в единицах измерения, не геометрии)
    //   — Dimlwd/Dimlwe: веса линий (целые коды AutoCAD, не миллиметры)
    //
    // Каждое свойство после умножения округляется до ближайшего кратного baseValue
    // (в текущей конфигурации вызывается с baseValue = 5, см. ScaleVisualValue).
    private static void NormalizeStyleVisualScale(DimStyleTableRecord style, double scale)
    {
        style.Dimtxt = ScaleVisualValue(style.Dimtxt, scale, 5); // высота текста размера
        style.Dimasz = ScaleVisualValue(style.Dimasz, scale, 5); // размер стрелки / засечки
        style.Dimtsz = ScaleVisualValue(style.Dimtsz, scale, 5); // размер косой засечки (tick)
        style.Dimexo = ScaleVisualValue(style.Dimexo, scale, 5); // отступ выносной линии от объекта
        style.Dimexe = ScaleVisualValue(style.Dimexe, scale, 5); // выступ выносной линии за размерную
        style.Dimgap = ScaleVisualValue(style.Dimgap, scale, 5); // зазор между текстом и размерной линией
        style.Dimdli = ScaleVisualValue(style.Dimdli, scale, 5); // шаг параллельных размерных линий (baseline)
        style.Dimdle = ScaleVisualValue(style.Dimdle, scale, 5); // выступ размерной линии за выносную
        style.Dimcen = ScaleVisualValue(style.Dimcen, scale, 5); // размер маркера центра окружности
        style.Dimtvp = ScaleVisualValue(style.Dimtvp, scale, 5); // вертикальное смещение текста от линии
        style.Dimfxlen = ScaleVisualValue(style.Dimfxlen, scale, 5); // фиксированная длина выносной линии
        style.Dimscale = 1.0;  // общий масштаб сброшен — все «запечено» выше
    }

    // Умножает визуальное значение на масштаб и округляет результат до ближайшего кратного baseValue.
    // Выбор baseValue позволяет контролировать дискретность значений и избегать визуального шума
    // после умножения на imperial-фактор (304.8).
    private static double ScaleVisualValue(double value, double scale, int baseValue)
    {
        if (double.IsFinite(value) && value != 0.0)
        {
            double scaledValue = value * scale;
            return Math.Round(scaledValue / baseValue) * baseValue;
        }

        return value;
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
