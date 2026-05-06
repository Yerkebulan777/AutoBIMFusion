using Serilog.Core;
using System.Globalization;

namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class DimensionStyleNormalizer
{
    private const double Tolerance = 1e-7;
    private const double MinScaleMultiplier = 0.01;
    private const double ModelSizedDimtxtThreshold = 20.0;
    private static readonly HashSet<string> StandardDimensionStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISO-25",
        "Standard",
        "Annotative"
    };

    /// <summary>
    /// Пересоздает используемые размерные стили в уже переведенной в metric source DB до WblockCloneObjects.
    /// </summary>
    internal static void RecreateMetricDimStyles(
        Database sourceDb,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallbackMultiplier,
        double clampRatio,
        Logger log)
    {
        int scannedDimensions = 0;
        int scannedLeaders = 0;
        int scannedMLeaders = 0;
        int erasedLayoutObjects = 0;
        int stylesCreated = 0;
        int stylesReused = 0;
        int overridesCleared = 0;
        int remappedDimensions = 0;
        int remappedLeaders = 0;
        int standardStylesSkipped = 0;
        int annotativeStylesSkipped = 0;
        int fallbackUsed = 0;
        int invalidMultiplierFallbackUsed = 0;
        int minMultiplierClamped = 0;

        HashSet<ObjectId> replacedStyleIds = [];
        HashSet<ObjectId> skippedAnnotativeStyleIds = [];
        Dictionary<StyleScaleKey, ObjectId> remapCache = [];

        using Transaction trx = sourceDb.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)trx.GetObject(sourceDb.DimStyleTableId, OpenMode.ForRead);
        Dictionary<string, ObjectId> styleNameCache = BuildStyleCache(dimStyleTable, trx);
        AddAnnotativeStyleIds(dimStyleTable, trx, skippedAnnotativeStyleIds);
        ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);

        BlockTable blockTable = (BlockTable)trx.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            if (trx.GetObject(blockId, OpenMode.ForRead, false) is not BlockTableRecord block || block.IsErased)
            {
                continue;
            }

            bool isModelSpace = blockId == modelSpaceId;
            bool isLayoutSpace = block.IsLayout && !isModelSpace;
            foreach (ObjectId entityId in block)
            {
                if (entityId.IsNull || entityId.IsErased)
                {
                    continue;
                }

                DBObject? obj = trx.GetObject(entityId, OpenMode.ForRead, false);
                switch (obj)
                {
                    case Dimension dimension:
                        scannedDimensions++;
                        if (isLayoutSpace)
                        {
                            EraseLayoutAnnotation(dimension);
                            erasedLayoutObjects++;
                            continue;
                        }

                        if (!isModelSpace)
                        {
                            continue;
                        }

                        if (TryGetMetricStyleForDimension(
                            sourceDb,
                            dimStyleTable,
                            styleNameCache,
                            remapCache,
                            dimension,
                            scaleByDimensionId,
                            fallbackMultiplier,
                            clampRatio,
                            trx,
                            log,
                            out ObjectId metricStyleId,
                            out ObjectId sourceStyleId,
                            out MetricStyleStatus styleStatus,
                            out bool usedFallback,
                            out bool usedInvalidMultiplierFallback,
                            out bool usedMinMultiplierClamp))
                        {
                            ApplyMetricStyle(dimension, metricStyleId, ref overridesCleared);
                            remappedDimensions++;
                            AddReplacedStyle(sourceStyleId, metricStyleId, replacedStyleIds);
                        }
                        else
                        {
                            CountSkippedStyle(styleStatus, skippedAnnotativeStyleIds, sourceStyleId, ref standardStylesSkipped, ref annotativeStylesSkipped);
                        }

                        CountMultiplierFallbacks(
                            usedFallback,
                            usedInvalidMultiplierFallback,
                            usedMinMultiplierClamp,
                            ref fallbackUsed,
                            ref invalidMultiplierFallbackUsed,
                            ref minMultiplierClamped);
                        CountStyleCreation(styleStatus, ref stylesCreated, ref stylesReused);
                        break;

                    case Leader leader:
                        scannedLeaders++;
                        if (isLayoutSpace)
                        {
                            EraseLayoutAnnotation(leader);
                            erasedLayoutObjects++;
                            continue;
                        }

                        if (!isModelSpace)
                        {
                            continue;
                        }

                        if (TryGetMetricStyleForLeader(
                            sourceDb,
                            dimStyleTable,
                            styleNameCache,
                            remapCache,
                            leader,
                            fallbackMultiplier,
                            clampRatio,
                            trx,
                            log,
                            out DimStyleTableRecord metricStyle,
                            out ObjectId sourceLeaderStyleId,
                            out MetricStyleStatus leaderStyleStatus,
                            out bool leaderUsedFallback,
                            out bool leaderUsedInvalidMultiplierFallback,
                            out bool leaderUsedMinMultiplierClamp))
                        {
                            ApplyMetricStyle(leader, metricStyle);
                            remappedLeaders++;
                            AddReplacedStyle(sourceLeaderStyleId, metricStyle.ObjectId, replacedStyleIds);
                        }
                        else
                        {
                            CountSkippedStyle(leaderStyleStatus, skippedAnnotativeStyleIds, sourceLeaderStyleId, ref standardStylesSkipped, ref annotativeStylesSkipped);
                        }

                        CountMultiplierFallbacks(
                            leaderUsedFallback,
                            leaderUsedInvalidMultiplierFallback,
                            leaderUsedMinMultiplierClamp,
                            ref fallbackUsed,
                            ref invalidMultiplierFallbackUsed,
                            ref minMultiplierClamped);
                        CountStyleCreation(leaderStyleStatus, ref stylesCreated, ref stylesReused);
                        break;

                    case MLeader mLeader:
                        scannedMLeaders++;
                        log.Debug(
                            "[DIM-METRIC] mleader handle={Handle}: MLeaderStyle={MLeaderStyle} left unchanged.",
                            mLeader.Handle,
                            FormatObjectId(mLeader.MLeaderStyle));
                        break;
                }
            }
        }

        trx.Commit();

        int purgedOldStyles = PurgeUnusedStyles(sourceDb, replacedStyleIds, log, "[DIM-METRIC] failed to purge replaced dimension styles.");
        int purgedAnnotativeStyles = PurgeUnusedStyles(sourceDb, skippedAnnotativeStyleIds, log, "[DIM-METRIC] failed to purge skipped annotative dimension styles.");
        int annotativeStylesStillReferenced = CountStillReferencedStyles(sourceDb, skippedAnnotativeStyleIds);

        if (invalidMultiplierFallbackUsed > 0)
        {
            log.Warning(
                "[DIM-METRIC] invalid scale multiplier fallback used for {Count} object(s); fallbackMultiplier={FallbackMultiplier}.",
                invalidMultiplierFallbackUsed,
                FormatValue(fallbackMultiplier));
        }

        log.Information(
            "[DIM-METRIC] scannedDimensions={ScannedDimensions}, scannedLeaders={ScannedLeaders}, scannedMLeaders={ScannedMLeaders}, layoutObjectsErased={LayoutObjectsErased}, dimensionsRemapped={DimensionsRemapped}, leadersRemapped={LeadersRemapped}, stylesCreated={StylesCreated}, stylesReused={StylesReused}, overridesCleared={OverridesCleared}, standardStylesSkipped={StandardStylesSkipped}, annotativeStylesSkipped={AnnotativeStylesSkipped}, annotativeStylesPurged={AnnotativeStylesPurged}, annotativeStylesStillReferenced={AnnotativeStylesStillReferenced}, oldStylesPurged={OldStylesPurged}, fallbackUsed={FallbackUsed}, invalidMultiplierFallbackUsed={InvalidMultiplierFallbackUsed}, minMultiplierClamped={MinMultiplierClamped}.",
            scannedDimensions,
            scannedLeaders,
            scannedMLeaders,
            erasedLayoutObjects,
            remappedDimensions,
            remappedLeaders,
            stylesCreated,
            stylesReused,
            overridesCleared,
            standardStylesSkipped,
            annotativeStylesSkipped,
            purgedAnnotativeStyles,
            annotativeStylesStillReferenced,
            purgedOldStyles,
            fallbackUsed,
            invalidMultiplierFallbackUsed,
            minMultiplierClamped);
    }

    private static bool TryGetMetricStyleForDimension(
        Database db,
        DimStyleTable dimStyleTable,
        Dictionary<string, ObjectId> styleNameCache,
        Dictionary<StyleScaleKey, ObjectId> remapCache,
        Dimension dimension,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallbackMultiplier,
        double clampRatio,
        Transaction trx,
        Logger log,
        out ObjectId metricStyleId,
        out ObjectId sourceStyleId,
        out MetricStyleStatus styleStatus,
        out bool usedFallback,
        out bool usedInvalidMultiplierFallback,
        out bool usedMinMultiplierClamp)
    {
        metricStyleId = ObjectId.Null;
        sourceStyleId = ObjectId.Null;

        double multiplier = ResolveMultiplier(
            dimension.ObjectId,
            scaleByDimensionId,
            fallbackMultiplier,
            out usedFallback,
            out usedInvalidMultiplierFallback,
            out usedMinMultiplierClamp);

        if (!TryResolveStyleRecord(db, dimension.DimensionStyle, trx, out DimStyleTableRecord sourceStyle))
        {
            styleStatus = MetricStyleStatus.Unavailable;
            log.Warning(
                "[DIM-METRIC] dimension handle={Handle}: skipped because source dimension style is not available.",
                dimension.Handle);
            return false;
        }

        sourceStyleId = sourceStyle.ObjectId;
        return TryGetOrCreateMetricStyle(
            dimStyleTable,
            styleNameCache,
            remapCache,
            sourceStyle,
            multiplier,
            clampRatio,
            trx,
            log,
            out metricStyleId,
            out styleStatus);
    }

    private static bool TryGetMetricStyleForLeader(
        Database db,
        DimStyleTable dimStyleTable,
        Dictionary<string, ObjectId> styleNameCache,
        Dictionary<StyleScaleKey, ObjectId> remapCache,
        Leader leader,
        double fallbackMultiplier,
        double clampRatio,
        Transaction trx,
        Logger log,
        out DimStyleTableRecord metricStyle,
        out ObjectId sourceStyleId,
        out MetricStyleStatus styleStatus,
        out bool usedFallback,
        out bool usedInvalidMultiplierFallback,
        out bool usedMinMultiplierClamp)
    {
        metricStyle = null!;
        sourceStyleId = ObjectId.Null;

        double multiplier = ResolveMultiplier(
            ObjectId.Null,
            new Dictionary<ObjectId, double>(),
            fallbackMultiplier,
            out usedFallback,
            out usedInvalidMultiplierFallback,
            out usedMinMultiplierClamp);

        ObjectId resolvedStyleId = leader.DimensionStyle;

        if (!TryResolveStyleRecord(db, resolvedStyleId, trx, out DimStyleTableRecord sourceStyle))
        {
            styleStatus = MetricStyleStatus.Unavailable;
            log.Warning(
                "[DIM-METRIC] leader handle={Handle}: skipped because source dimension style is not available.",
                leader.Handle);
            return false;
        }

        sourceStyleId = sourceStyle.ObjectId;
        if (!TryGetOrCreateMetricStyle(
            dimStyleTable,
            styleNameCache,
            remapCache,
            sourceStyle,
            multiplier,
            clampRatio,
            trx,
            log,
            out ObjectId metricStyleId,
            out styleStatus))
        {
            return false;
        }

        metricStyle = (DimStyleTableRecord)trx.GetObject(metricStyleId, OpenMode.ForRead);
        return true;
    }

    private static bool TryGetOrCreateMetricStyle(
        DimStyleTable dimStyleTable,
        Dictionary<string, ObjectId> styleNameCache,
        Dictionary<StyleScaleKey, ObjectId> remapCache,
        DimStyleTableRecord sourceStyle,
        double scaleMultiplier,
        double clampRatio,
        Transaction trx,
        Logger log,
        out ObjectId metricStyleId,
        out MetricStyleStatus status)
    {
        metricStyleId = ObjectId.Null;

        if (IsStandardStyle(sourceStyle))
        {
            status = MetricStyleStatus.StandardSkipped;
            return false;
        }

        if (IsAnnotativeStyle(sourceStyle))
        {
            status = MetricStyleStatus.AnnotativeSkipped;
            return false;
        }

        StyleScaleKey cacheKey = new(sourceStyle.ObjectId, FormatScale(scaleMultiplier));
        if (remapCache.TryGetValue(cacheKey, out metricStyleId))
        {
            status = MetricStyleStatus.Reused;
            return true;
        }

        string metricStyleName = BuildMetricStyleName(sourceStyle.Name, scaleMultiplier);
        if (styleNameCache.TryGetValue(metricStyleName, out metricStyleId))
        {
            remapCache[cacheKey] = metricStyleId;
            status = MetricStyleStatus.Reused;
            return true;
        }

        metricStyleId = CreateMetricStyle(dimStyleTable, sourceStyle, metricStyleName, scaleMultiplier, clampRatio, trx, out double visualMultiplier);
        remapCache[cacheKey] = metricStyleId;
        styleNameCache[metricStyleName] = metricStyleId;
        status = MetricStyleStatus.Created;

        log.Information(
            "[DIM-METRIC] created style: baseStyle=\"{BaseStyle}\", newStyle=\"{NewStyle}\", vpScale={VpScale}, visualScale={VisualScale}, clampRatio={ClampRatio}, changes={Changes}.",
            sourceStyle.Name,
            metricStyleName,
            FormatScale(scaleMultiplier),
            FormatScale(visualMultiplier),
            FormatValue(clampRatio),
            FormatStyleVisualChanges(sourceStyle, metricStyleId, trx));

        return true;
    }

    private static ObjectId CreateMetricStyle(
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

        DimStyleTableRecord metricStyle = new();
        metricStyle.CopyFrom(sourceStyle);
        metricStyle.Name = styleName;

        visualMultiplier = ResolveVisualBakeMultiplier(sourceStyle, scaleMultiplier, clampRatio);
        NormalizeStyleVisualScale(metricStyle, visualMultiplier);
        metricStyle.Dimscale = 1.0;
        metricStyle.Dimlfac = 1.0;

        ObjectId styleId = dimStyleTable.Add(metricStyle);
        trx.AddNewlyCreatedDBObject(metricStyle, true);
        return styleId;
    }

    private static void ApplyMetricStyle(Dimension dimension, ObjectId metricStyleId, ref int overridesCleared)
    {
        if (!dimension.IsWriteEnabled)
        {
            dimension.UpgradeOpen();
        }

        if (DimensionUtils.TryRemoveDimensionStyleOverrides(dimension))
        {
            overridesCleared++;
        }

        dimension.DimensionStyle = metricStyleId;
        dimension.Dimlfac = 1.0;
        dimension.RecomputeDimensionBlock(true);
        dimension.RecordGraphicsModified(true);
    }

    private static void ApplyMetricStyle(Leader leader, DimStyleTableRecord metricStyle)
    {
        if (!leader.IsWriteEnabled)
        {
            leader.UpgradeOpen();
        }

        leader.SetDimstyleData(metricStyle);
        leader.EvaluateLeader();
        leader.RecordGraphicsModified(true);
    }

    private static void EraseLayoutAnnotation(Entity entity)
    {
        if (!entity.IsWriteEnabled)
        {
            entity.UpgradeOpen();
        }

        entity.Erase();
    }

    private static void AddReplacedStyle(ObjectId sourceStyleId, ObjectId metricStyleId, HashSet<ObjectId> replacedStyleIds)
    {
        if (!sourceStyleId.IsNull && !sourceStyleId.IsErased && sourceStyleId != metricStyleId)
        {
            _ = replacedStyleIds.Add(sourceStyleId);
        }
    }

    private static void CountSkippedStyle(
        MetricStyleStatus status,
        HashSet<ObjectId> skippedAnnotativeStyleIds,
        ObjectId sourceStyleId,
        ref int standardStylesSkipped,
        ref int annotativeStylesSkipped)
    {
        if (status == MetricStyleStatus.StandardSkipped)
        {
            standardStylesSkipped++;
        }
        else if (status == MetricStyleStatus.AnnotativeSkipped)
        {
            annotativeStylesSkipped++;
            if (!sourceStyleId.IsNull && !sourceStyleId.IsErased)
            {
                _ = skippedAnnotativeStyleIds.Add(sourceStyleId);
            }
        }
    }

    private static void CountStyleCreation(MetricStyleStatus status, ref int stylesCreated, ref int stylesReused)
    {
        if (status == MetricStyleStatus.Created)
        {
            stylesCreated++;
        }
        else if (status == MetricStyleStatus.Reused)
        {
            stylesReused++;
        }
    }

    private static void CountMultiplierFallbacks(
        bool usedFallback,
        bool usedInvalidMultiplierFallback,
        bool usedMinMultiplierClamp,
        ref int fallbackUsed,
        ref int invalidMultiplierFallbackUsed,
        ref int minMultiplierClamped)
    {
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
    }

    private static int PurgeUnusedStyles(Database db, IReadOnlyCollection<ObjectId> styleIds, Logger log, string warningMessage)
    {
        if (styleIds.Count == 0)
        {
            return 0;
        }

        using ObjectIdCollection candidates = [];
        ObjectId currentDimStyleId = db.Dimstyle;

        foreach (ObjectId styleId in styleIds)
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
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Warning(ex, warningMessage);
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

    private static int CountStillReferencedStyles(Database db, IReadOnlyCollection<ObjectId> styleIds)
    {
        if (styleIds.Count == 0)
        {
            return 0;
        }

        int stillReferenced = 0;
        using Transaction trx = db.TransactionManager.StartTransaction();
        foreach (ObjectId styleId in styleIds)
        {
            if (!styleId.IsNull
                && !styleId.IsErased
                && trx.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style
                && !style.IsErased)
            {
                stillReferenced++;
            }
        }

        trx.Commit();
        return stillReferenced;
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

    private static void AddAnnotativeStyleIds(DimStyleTable dimStyleTable, Transaction trx, HashSet<ObjectId> annotativeStyleIds)
    {
        foreach (ObjectId styleId in dimStyleTable)
        {
            if (trx.GetObject(styleId, OpenMode.ForRead, false) is DimStyleTableRecord style
                && !style.IsErased
                && IsAnnotativeStyle(style))
            {
                _ = annotativeStyleIds.Add(styleId);
            }
        }
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

    private static string FormatStyleVisualChanges(DimStyleTableRecord sourceStyle, ObjectId normalizedStyleId, Transaction trx)
    {
        DimStyleTableRecord normalizedStyle = (DimStyleTableRecord)trx.GetObject(normalizedStyleId, OpenMode.ForRead);

        return string.Join(", ",
        [
            FormatChange("Dimtxt", sourceStyle.Dimtxt, normalizedStyle.Dimtxt),
            FormatChange("Dimasz", sourceStyle.Dimasz, normalizedStyle.Dimasz),
            FormatChange("Dimtsz", sourceStyle.Dimtsz, normalizedStyle.Dimtsz),
            FormatChange("Dimexo", sourceStyle.Dimexo, normalizedStyle.Dimexo),
            FormatChange("Dimexe", sourceStyle.Dimexe, normalizedStyle.Dimexe),
            FormatChange("Dimgap", sourceStyle.Dimgap, normalizedStyle.Dimgap),
            FormatChange("Dimdli", sourceStyle.Dimdli, normalizedStyle.Dimdli),
            FormatChange("Dimdle", sourceStyle.Dimdle, normalizedStyle.Dimdle),
            FormatChange("Dimcen", sourceStyle.Dimcen, normalizedStyle.Dimcen),
            FormatChange("Dimtvp", sourceStyle.Dimtvp, normalizedStyle.Dimtvp),
            FormatChange("Dimfxlen", sourceStyle.Dimfxlen, normalizedStyle.Dimfxlen),
            FormatChange("Dimscale", sourceStyle.Dimscale, normalizedStyle.Dimscale),
            FormatChange("Dimlfac", sourceStyle.Dimlfac, normalizedStyle.Dimlfac),
            FormatChange("Dimtfill", sourceStyle.Dimtfill, normalizedStyle.Dimtfill)
        ]);
    }

    private static double ResolveMultiplier(
        ObjectId dimensionId,
        IReadOnlyDictionary<ObjectId, double> scaleByDimensionId,
        double fallback,
        out bool fallbackUsed,
        out bool invalidMultiplierUsed,
        out bool minMultiplierClamped)
    {
        double multiplier = fallback;
        fallbackUsed = dimensionId.IsNull || !scaleByDimensionId.TryGetValue(dimensionId, out multiplier);
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

        // Прозрачная заливка исключает лишний фон после пересоздания стиля.
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

    private static string BuildMetricStyleName(string baseStyleName, double multiplier)
    {
        return $"{baseStyleName}_VP-{FormatScale(multiplier)}_Metric";
    }

    private static string FormatChange(string propertyName, double before, double after)
    {
        return $"{propertyName}:{FormatValue(before)}->{FormatValue(after)}";
    }

    private static string FormatChange(string propertyName, short before, short after)
    {
        return $"{propertyName}:{before.ToString(CultureInfo.InvariantCulture)}->{after.ToString(CultureInfo.InvariantCulture)}";
    }

    private static string FormatScale(double multiplier)
    {
        if (double.IsFinite(multiplier))
        {
            double rounded = Math.Round(multiplier);
            return Math.Abs(multiplier - rounded) < Tolerance
                ? rounded.ToString("0", CultureInfo.InvariantCulture)
                : multiplier.ToString("0.######", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
        }

        return string.Empty;
    }

    private static string FormatValue(double value)
    {
        return double.IsFinite(value) ? value.ToString("0.######", CultureInfo.InvariantCulture) : "n/a";
    }

    private static string FormatObjectId(ObjectId id)
    {
        if (id.IsNull)
        {
            return "Null";
        }

        try
        {
            return id.Handle.ToString();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
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
                return clampRatio;
            }

            return scaleMultiplier;
        }

        return 1.0;
    }

    private static bool IsStandardStyle(DimStyleTableRecord style)
    {
        return StandardDimensionStyleNames.Contains(style.Name);
    }

    private static bool IsAnnotativeStyle(DimStyleTableRecord style)
    {
        return style.Annotative == AnnotativeStates.True;
    }

    private readonly record struct StyleScaleKey(ObjectId SourceStyleId, string Scale);

    private enum MetricStyleStatus
    {
        Unavailable,
        Created,
        Reused,
        StandardSkipped,
        AnnotativeSkipped
    }
}
