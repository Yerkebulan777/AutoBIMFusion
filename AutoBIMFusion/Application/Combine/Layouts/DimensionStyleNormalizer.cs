using Serilog.Core;
using System.Globalization;

namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class DimensionStyleNormalizer
{
    private const double Tolerance = 1e-7;
	private const double MinScaleMultiplier = 0.01;
	private const double ModelSizedDimtxtThreshold = 20.0;

	/// <summary>
	/// Создает или повторно использует действующие стили разметок, специфичные для масштаба проекции, для каждой размерки в пространстве модели,
	/// очищает локальные переопределения DSTYLE XData, назначает нормализованный стиль и пересоздает размерку.
	/// </summary>
	internal static void NormalizeDimensions(
        Database db,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallbackMultiplier,
        double clampRatio,
        Logger log)
    {
        int scanned = 0;
        int normalized = 0;
        int overridesCleared = 0;
        int stylesCreated = 0;
        int fallbackUsed = 0;
        int invalidMultiplierFallbackUsed = 0;
        int minMultiplierClamped = 0;
        int dimlfacAdjusted = 0;

        HashSet<ObjectId> replacedStyleIds = [];

        using Transaction trx = db.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)trx.GetObject(db.DimStyleTableId, OpenMode.ForRead);

        Dictionary<string, ObjectId> styleCache = BuildStyleCache(dimStyleTable, trx);

        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsNull || id.IsErased || trx.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension)
            {
                continue;
            }

            scanned++;

            double multiplier = ResolveMultiplier(
                id,
                scaleByDimensionId,
                fallbackMultiplier,
                out bool usedFallback,
                out bool usedInvalidMultiplierFallback,
                out bool usedMinMultiplierClamp);
            if (usedFallback)
            {
                fallbackUsed++;
            }

            if (usedInvalidMultiplierFallback)
            {
                invalidMultiplierFallbackUsed++;
            }

            if (usedMinMultiplierClamp)
            {
                minMultiplierClamped++;
            }

            if (!TryResolveStyleRecord(db, dimension.DimensionStyle, trx, out DimStyleTableRecord sourceStyle))
            {
                log.Warning(
                    "[DIM-NORMALIZE] handle={Handle}: skipped because source dimension style is not available.",
                    dimension.Handle);
                continue;
            }

            string newStyleName = BuildScaledStyleName(sourceStyle.Name, multiplier);

            if (!styleCache.TryGetValue(newStyleName, out ObjectId normalizedStyleId))
            {
                normalizedStyleId = CreateScaledStyle(dimStyleTable, sourceStyle, newStyleName, multiplier, clampRatio, trx, out double visualMultiplier);
                styleCache[newStyleName] = normalizedStyleId;
                stylesCreated++;

                log.Information(
                    "[DIM-NORMALIZE] created style \"{StyleName}\" from \"{BaseStyle}\": scale={Scale}, visualScale={VisualScale}, Dimtxt={DimtxtBefore}->{DimtxtAfter}, Dimasz={DimaszBefore}->{DimaszAfter}.",
                    newStyleName,
                    sourceStyle.Name,
                    FormatScale(multiplier),
                    FormatScale(visualMultiplier),
                    FormatValue(sourceStyle.Dimtxt),
                    FormatValue(ScaleVisualValue(sourceStyle.Dimtxt, visualMultiplier)),
                    FormatValue(sourceStyle.Dimasz),
                    FormatValue(ScaleVisualValue(sourceStyle.Dimasz, visualMultiplier)));
            }

            dimension.UpgradeOpen();

            if (DimensionUtils.TryRemoveDimensionStyleOverrides(dimension))
            {
                overridesCleared++;
            }

            dimension.DimensionStyle = normalizedStyleId;
            // Геометрия model-space размеров была физически умножена на clampRatio в ScaleModelSpaceWhenClamped.
            // Dimlfac компенсирует это, чтобы показываемое значение соответствовало оригинальной геометрии.
            bool adjustDimlfac = scaleByDimensionId.ContainsKey(id) && clampRatio > 1.0 + Tolerance;
            dimension.Dimlfac = adjustDimlfac ? 1.0 / clampRatio : 1.0;
            if (adjustDimlfac)
            {
                dimlfacAdjusted++;
            }

            dimension.RecomputeDimensionBlock(true);
            dimension.RecordGraphicsModified(true);

            if (sourceStyle.ObjectId != normalizedStyleId)
            {
                _ = replacedStyleIds.Add(sourceStyle.ObjectId);
            }

            normalized++;
        }

        trx.Commit();

        int purgedOldStyles = PurgeUnusedReplacedStyles(db, replacedStyleIds, log);

        if (invalidMultiplierFallbackUsed > 0)
        {
            log.Warning(
                "[DIM-NORMALIZE] invalid scale multiplier fallback used for {Count} dimension(s); fallbackMultiplier={FallbackMultiplier}.",
                invalidMultiplierFallbackUsed,
                FormatValue(fallbackMultiplier));
        }

        log.Information(
            "[DIM-NORMALIZE] scanned={Scanned}, normalized={Normalized}, stylesCreated={StylesCreated}, overridesCleared={OverridesCleared}, fallbackUsed={FallbackUsed}, invalidMultiplierFallbackUsed={InvalidMultiplierFallbackUsed}, minMultiplierClamped={MinMultiplierClamped}, dimlfacAdjusted={DimlfacAdjusted}, purgedOldStyles={PurgedOldStyles}.",
            scanned,
            normalized,
            stylesCreated,
            overridesCleared,
            fallbackUsed,
            invalidMultiplierFallbackUsed,
            minMultiplierClamped,
            dimlfacAdjusted,
            purgedOldStyles);
    }

    /// <summary>
    /// Удаляет только заменённые стили разметок исходного пространства, которые AutoCAD помечает как подлежащие удалению после того,
    /// как все размерки в пространстве модели будут переназначены на нормализованный стиль.
    /// </summary>
    private static int PurgeUnusedReplacedStyles(Database db, IReadOnlyCollection<ObjectId> replacedStyleIds, Logger log)
    {
        if (replacedStyleIds.Count == 0)
        {
            return 0;
        }

        using ObjectIdCollection candidates = [];
        ObjectId currentDimStyleId = db.Dimstyle;

        foreach (ObjectId styleId in replacedStyleIds)
        {
            if (!styleId.IsNull
                && !styleId.IsErased
                && styleId != currentDimStyleId)
            {
                _ = candidates.Add(styleId);
            }
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        try
        {
            db.Purge(candidates);
        }
        catch (System.Exception ex)
        {
            log.Warning(ex, "[DIM-NORMALIZE] failed to purge replaced dimension styles.");
            return 0;
        }

        if (candidates.Count == 0)
        {
            return 0;
        }

        using Transaction trx = db.TransactionManager.StartTransaction();
        int purged = DwgOptimizer.ErasePurgedObjects(trx, candidates, log);

        trx.Commit();
        return purged;
    }

    private static Dictionary<string, ObjectId> BuildStyleCache(DimStyleTable dimStyleTable, Transaction trx)
    {
        Dictionary<string, ObjectId> cache = new(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectId styleId in dimStyleTable)
        {
            if (trx.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style && !style.IsErased)
            {
                cache[style.Name] = styleId;
            }
        }

        return cache;
    }

    private static bool TryResolveStyleRecord(Database db, ObjectId styleId, Transaction trx, out DimStyleTableRecord style)
    {
        style = null!;

        ObjectId resolvedStyleId = !styleId.IsNull && !styleId.IsErased ? styleId : db.Dimstyle;

        if (resolvedStyleId.IsNull || resolvedStyleId.IsErased)
        {
            return false;
        }

        if (trx.GetObject(resolvedStyleId, OpenMode.ForRead, false) is not DimStyleTableRecord resolvedStyle || resolvedStyle.IsErased)
        {
            return false;
        }

        style = resolvedStyle;
        return true;
    }

    /// <summary>
    /// Возвращает мультипликатор для нормализации размерного стиля конкретного размера.
    /// </summary>
    /// <remarks>
    /// Верхнего предела нет намеренно: масштабы крупнее 1:100 (1:200, 1:500 и т.д.) должны давать
    /// корректный мультипликатор без урезания. Нижний предел <see cref="MinScaleMultiplier"/> = 0.01
    /// защищает от деления на ноль и экстремально мелких значений при масштабах крупнее 1:1.
    /// Входящий multiplier поступает из <c>dimensionMultiplier</c> (1.0 / mainOriginal.CustomScale) —
    /// не из зажатого effectiveMultiplier — поэтому урезание до 100 было бы неверным.
    /// </remarks>
    private static double ResolveMultiplier(
        ObjectId dimensionId,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallback,
        out bool fallbackUsed,
        out bool invalidMultiplierUsed,
        out bool minMultiplierClamped)
    {
        fallbackUsed = !scaleByDimensionId.TryGetValue(dimensionId, out double multiplier);
        invalidMultiplierUsed = false;
        minMultiplierClamped = false;

        if (fallbackUsed)
        {
            multiplier = fallback;
        }

        invalidMultiplierUsed = !IsUsableMultiplier(multiplier);
        if (invalidMultiplierUsed)
        {
            return 1.0;
        }

        minMultiplierClamped = multiplier < MinScaleMultiplier;
        return minMultiplierClamped ? MinScaleMultiplier : multiplier;
    }

    private static ObjectId CreateScaledStyle(
        DimStyleTable dimStyleTable,
        DimStyleTableRecord sourceStyle,
        string styleName,
        double scaleMultiplier,
        double clampRatio,
        Transaction trx,
        out double visualMultiplier)
    {
        if (!dimStyleTable.IsWriteEnabled)
        {
            dimStyleTable.UpgradeOpen();
        }

        DimStyleTableRecord scaledStyle = (DimStyleTableRecord)sourceStyle.Clone();
        scaledStyle.Name = styleName;

        visualMultiplier = ResolveVisualBakeMultiplier(sourceStyle, scaleMultiplier, clampRatio);
        NormalizeStyleVisualScale(scaledStyle, visualMultiplier);
        scaledStyle.Dimscale = 1.0;
        scaledStyle.Dimlfac = 1.0;

        ObjectId styleId = dimStyleTable.Add(scaledStyle);
        trx.AddNewlyCreatedDBObject(scaledStyle, true);
        return styleId;
    }

    /// <summary>
    /// Bakes the selected visual multiplier into only the geometric dimension style values
    /// that AutoCAD normally displays in drawing units.
    /// </summary>
    private static void NormalizeStyleVisualScale(DimStyleTableRecord style, double multiplier)
    {
        style.Dimtxt = ScaleVisualValue(style.Dimtxt, multiplier);
        style.Dimasz = ScaleVisualValue(style.Dimasz, multiplier);
        style.Dimtsz = ScaleVisualValue(style.Dimtsz, multiplier);
        style.Dimexo = ScaleVisualValue(style.Dimexo, multiplier);
        style.Dimexe = ScaleVisualValue(style.Dimexe, multiplier);
        style.Dimgap = ScaleVisualValue(style.Dimgap, multiplier);
        style.Dimdli = ScaleVisualValue(style.Dimdli, multiplier);
        style.Dimdle = ScaleVisualValue(style.Dimdle, multiplier);
        style.Dimcen = ScaleVisualValue(style.Dimcen, multiplier);
        style.Dimtvp = ScaleVisualValue(style.Dimtvp, multiplier);
        style.Dimfxlen = ScaleVisualValue(style.Dimfxlen, multiplier);

        // Set text fill to transparent (0 = transparent, 1 = background fill, 2 = fill box)
        style.Dimtfill = 0;
    }

    private static double ScaleVisualValue(double value, double multiplier)
    {
        if (double.IsFinite(value) && Math.Abs(value) > Tolerance)
        {
            return Math.Round(value * multiplier, 4, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    private static string BuildScaledStyleName(string baseStyleName, double multiplier)
    {
        return $"{baseStyleName} - Scale {FormatScale(multiplier)}";
    }

    private static string FormatScale(double multiplier)
    {
        if (double.IsFinite(multiplier))
        {
            double rounded = Math.Round(multiplier);
            return Math.Abs(multiplier - rounded) < Tolerance
                ? rounded.ToString("0", CultureInfo.InvariantCulture) : multiplier
                .ToString("0.######", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        return string.Empty;
    }

    private static string FormatValue(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : "n/a";
    }

    private static bool IsUsableMultiplier(double multiplier)
    {
        return double.IsFinite(multiplier) && multiplier > Tolerance;
    }

    private static double ResolveVisualBakeMultiplier(DimStyleTableRecord sourceStyle, double scaleMultiplier, double clampRatio)
    {
        if (IsUsableMultiplier(scaleMultiplier))
        {
            if (double.IsFinite(sourceStyle.Dimtxt) && sourceStyle.Dimtxt > ModelSizedDimtxtThreshold)
            {
                // Стиль уже имеет model-size текст (калиброван под оригинальный масштаб ВЭ).
                // После clampRatio-масштабирования геометрии визуальные свойства нужно также умножить
                // на clampRatio, чтобы пропорции сохранились на уровне effectiveMultiplier.
                return clampRatio;
            }

            return scaleMultiplier;
        }

        return 1.0;
    }
}

