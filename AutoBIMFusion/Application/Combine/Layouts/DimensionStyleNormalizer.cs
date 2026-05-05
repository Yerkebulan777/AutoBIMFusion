using Serilog.Core;
using System.Globalization;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Normalizes model-space dimensions by assigning projection-scale-specific dimension styles
/// before the source database is cloned into the target drawing.
/// </summary>
internal static class DimensionStyleNormalizer
{
    private const double Tolerance = 1e-9;
    private const double ModelSizedDimtxtThreshold = 20.0;
    private const double MinScaleMultiplier = 0.01;
    private const double MaxScaleMultiplier = 100.0;

    /// <summary>
    /// Creates or reuses effective projection-scale-specific dimension styles for every model-space dimension,
    /// clears local DSTYLE XData overrides, assigns the normalized style, and rebuilds the dimension.
    /// </summary>
    /// <param name="db">Prepared source database whose Model Space dimensions should be normalized.</param>
    /// <param name="scaleByDimensionId">Effective clamped main viewport multiplier by source dimension id.</param>
    /// <param name="fallbackMultiplier">Effective clamped main viewport multiplier used when a dimension has no explicit match.</param>
    /// <param name="log">Logger for normalization diagnostics.</param>
    internal static void NormalizeModelSpaceDimensions(
        Database db,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallbackMultiplier,
        Logger log)
    {
        int scanned = 0;
        int normalized = 0;
        int overridesCleared = 0;
        int stylesCreated = 0;
        int fallbackUsed = 0;
        HashSet<ObjectId> replacedStyleIds = [];

        using Transaction tr = db.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        Dictionary<string, ObjectId> styleCache = BuildStyleCache(dimStyleTable, tr);

        BlockTableRecord modelSpace = (BlockTableRecord)tr.GetObject(
            SymbolUtilityServices.GetBlockModelSpaceId(db),
            OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsNull || id.IsErased || tr.GetObject(id, OpenMode.ForRead, false) is not Dimension dimension)
            {
                continue;
            }

            scanned++;

            double multiplier = ResolveMultiplier(id, scaleByDimensionId, fallbackMultiplier, out bool usedFallback);
            if (usedFallback)
            {
                fallbackUsed++;
            }

            if (!TryResolveStyleRecord(db, dimension.DimensionStyle, tr, out DimStyleTableRecord sourceStyle))
            {
                log.Warning(
                    "[DIM-NORMALIZE] handle={Handle}: skipped because source dimension style is not available.",
                    dimension.Handle);
                continue;
            }

            string newStyleName = BuildScaledStyleName(sourceStyle.Name, multiplier);

            if (!styleCache.TryGetValue(newStyleName, out ObjectId normalizedStyleId))
            {
                normalizedStyleId = CreateScaledStyle(dimStyleTable, sourceStyle, newStyleName, multiplier, tr, log);
                styleCache[newStyleName] = normalizedStyleId;
                stylesCreated++;

                log.Information(
                    "[DIM-NORMALIZE] created style \"{StyleName}\" from \"{BaseStyle}\" with scale={Scale}.",
                    newStyleName,
                    sourceStyle.Name,
                    FormatScale(multiplier));
            }

            dimension.UpgradeOpen();

            if (DimensionUtils.TryRemoveDimensionStyleOverrides(dimension))
            {
                overridesCleared++;
            }

            dimension.DimensionStyle = normalizedStyleId;
            dimension.Dimlfac = 1.0;
            dimension.TextRotation = 0.0;
            dimension.RecomputeDimensionBlock(true);
            dimension.RecordGraphicsModified(true);

            if (sourceStyle.ObjectId != normalizedStyleId)
            {
                _ = replacedStyleIds.Add(sourceStyle.ObjectId);
            }

            normalized++;
        }

        tr.Commit();

        int purgedOldStyles = PurgeUnusedReplacedStyles(db, replacedStyleIds, log);

        log.Information(
            "[DIM-NORMALIZE] scanned={Scanned}, normalized={Normalized}, stylesCreated={StylesCreated}, overridesCleared={OverridesCleared}, fallbackUsed={FallbackUsed}, purgedOldStyles={PurgedOldStyles}.",
            scanned,
            normalized,
            stylesCreated,
            overridesCleared,
            fallbackUsed,
            purgedOldStyles);
    }

    /// <summary>
    /// Deletes only replaced source dimension styles that AutoCAD reports as purgeable after
    /// every model-space dimension has been reassigned to a normalized style.
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

        int purged = 0;
        using Transaction tr = db.TransactionManager.StartTransaction();

        foreach (ObjectId styleId in candidates)
        {
            if (styleId.IsNull || styleId.IsErased || styleId == currentDimStyleId)
            {
                continue;
            }

            try
            {
                if (tr.GetObject(styleId, OpenMode.ForWrite, false) is DimStyleTableRecord style
                    && !style.IsErased)
                {
                    string styleName = style.Name;
                    style.Erase();
                    purged++;
                    log.Debug("[DIM-NORMALIZE] purged unused replaced dimension style \"{StyleName}\".", styleName);
                }
            }
            catch (System.Exception ex)
            {
                log.Debug("[DIM-NORMALIZE] skipped replaced dimension style {Handle}: {Reason}", styleId.Handle, ex.Message);
            }
        }

        tr.Commit();
        return purged;
    }

    private static Dictionary<string, ObjectId> BuildStyleCache(DimStyleTable dimStyleTable, Transaction tr)
    {
        Dictionary<string, ObjectId> cache = new(StringComparer.OrdinalIgnoreCase);

        foreach (ObjectId styleId in dimStyleTable)
        {
            if (tr.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style && !style.IsErased)
            {
                cache[style.Name] = styleId;
            }
        }

        return cache;
    }

    private static bool TryResolveStyleRecord(
        Database db,
        ObjectId styleId,
        Transaction tr,
        out DimStyleTableRecord style)
    {
        style = null!;

        ObjectId resolvedStyleId = !styleId.IsNull && !styleId.IsErased
            ? styleId
            : db.Dimstyle;

        if (resolvedStyleId.IsNull || resolvedStyleId.IsErased)
        {
            return false;
        }

        if (tr.GetObject(resolvedStyleId, OpenMode.ForRead, false) is not DimStyleTableRecord resolvedStyle
            || resolvedStyle.IsErased)
        {
            return false;
        }

        style = resolvedStyle;
        return true;
    }

    private static double ResolveMultiplier(
        ObjectId dimensionId,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallbackMultiplier,
        out bool fallbackUsed)
    {
        fallbackUsed = !scaleByDimensionId.TryGetValue(dimensionId, out double multiplier);

        if (fallbackUsed)
        {
            multiplier = fallbackMultiplier;
        }

        return !IsUsableMultiplier(multiplier) ? 1.0 : Math.Clamp(multiplier, MinScaleMultiplier, MaxScaleMultiplier);
    }

    private static ObjectId CreateScaledStyle(
        DimStyleTable dimStyleTable,
        DimStyleTableRecord sourceStyle,
        string styleName,
        double scaleMultiplier,
        Transaction tr,
        Logger log)
    {
        if (!dimStyleTable.IsWriteEnabled)
        {
            dimStyleTable.UpgradeOpen();
        }

        DimStyleTableRecord scaledStyle = (DimStyleTableRecord)sourceStyle.Clone();
        scaledStyle.Name = styleName;

        double visualMultiplier = ResolveVisualBakeMultiplier(sourceStyle, scaleMultiplier, log);
        NormalizeStyleVisualScale(scaledStyle, visualMultiplier);
        scaledStyle.Dimscale = 1.0;
        scaledStyle.Dimlfac = 1.0;

        ObjectId styleId = dimStyleTable.Add(scaledStyle);
        tr.AddNewlyCreatedDBObject(scaledStyle, true);
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

    private static double ScaleVisualValue(double value, double multiplier, int baseValue = 5)
    {
        if (double.IsFinite(value) && Math.Abs(value) > Tolerance)
        {
            double scaledValue = value * multiplier;
            return Math.Round(scaledValue / baseValue) * baseValue;
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

    private static bool IsUsableMultiplier(double multiplier)
    {
        return double.IsFinite(multiplier) && multiplier > Tolerance;
    }

    private static double ResolveVisualBakeMultiplier(DimStyleTableRecord sourceStyle, double scaleMultiplier, Logger log)
    {
        if (IsUsableMultiplier(scaleMultiplier))
        {
            if (double.IsFinite(sourceStyle.Dimtxt) && sourceStyle.Dimtxt > ModelSizedDimtxtThreshold)
            {
                log.Information(
                    "[DIM-NORMALIZE] style \"{StyleName}\" already has model-sized Dimtxt={Dimtxt}; Dimscale will be reset without multiplying visual values by effective scale={Scale}.",
                    sourceStyle.Name,
                    sourceStyle.Dimtxt,
                    FormatScale(scaleMultiplier));

                return 1.0;
            }

            return scaleMultiplier;
        }
        
        return 1.0;
    }
}
