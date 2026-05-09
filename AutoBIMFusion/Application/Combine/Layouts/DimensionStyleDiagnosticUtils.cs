using Autodesk.AutoCAD.Colors;
using Serilog.Core;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Диагностические утилиты для логирования состояния размерных и текстовых стилей базы данных.
/// Используется для отладки: снимок до/после слияния позволяет отследить
/// аномалии масштабирования и коллизии стилей.
/// </summary>
internal static class DimensionStyleDiagnosticUtils
{
    private const int DimensionHandleSampleLimit = 5;

    private static readonly object SnapshotSync = new();

    private static readonly Dictionary<string, DimensionStyleSnapshot> SnapshotsByStage = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> PreviousStageByStage = new(StringComparer.Ordinal)
    {
        ["source-after-normalize-before-clone"] = "source-before-normalize",
        ["target-after-clone"] = "target-before-clone",
        ["target-before-save"] = "target-after-merge"
    };

    private static readonly HashSet<string> StandardDimensionStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISO-25",
        "Standard",
        "Annotative"
    };

    private static readonly PropertyInfo[] DimStyleProperties = typeof(DimStyleTableRecord)
        .GetProperties(BindingFlags.Instance | BindingFlags.Public)
        .Where(p => p.GetIndexParameters().Length == 0)
        .OrderBy(p => p.Name, StringComparer.Ordinal)
        .ToArray();

    private static readonly string[] SummaryPropertyNames =
    [
        nameof(DimStyleTableRecord.Dimscale),
        nameof(DimStyleTableRecord.Dimlfac),
        nameof(DimStyleTableRecord.Dimtxt),
        nameof(DimStyleTableRecord.Dimasz),
        nameof(DimStyleTableRecord.Dimtsz),
        nameof(DimStyleTableRecord.Dimexo),
        nameof(DimStyleTableRecord.Dimexe),
        nameof(DimStyleTableRecord.Dimgap),
        nameof(DimStyleTableRecord.Dimdli),
        nameof(DimStyleTableRecord.Dimdle),
        nameof(DimStyleTableRecord.Dimcen),
        nameof(DimStyleTableRecord.Dimtvp),
        nameof(DimStyleTableRecord.Dimfxlen),
        nameof(DimStyleTableRecord.Annotative)
    ];

    /// <summary>
    /// Записывает в лог снимок всех пользовательских размерных и текстовых стилей базы данных.
    /// </summary>
    /// <param name="db">База данных AutoCAD.</param>
    /// <param name="log">Экземпляр логгера.</param>
    /// <param name="stage">Метка этапа (например: "after-merge").</param>
    internal static void LogStyleSnapshot(Database db, Logger log, string stage)
    {
        DimensionStyleSnapshot snapshot = BuildSnapshot(db, stage);

        log.Information($"[STYLE-SNAPSHOT] stage={stage}, dimStyles={snapshot.DimensionStyles.Count}, textStyles={snapshot.TextStyles.Count}");

        foreach (DimensionStyleSnapshotEntry style in snapshot.DimensionStyles.Values.OrderBy(s => s.StyleName, StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[DIM-STYLE-SUMMARY] stage={stage}, {FormatDimensionStyleSummary(style)}");
        }

        foreach (DimensionStyleSnapshotEntry style in snapshot.DimensionStyles.Values.OrderBy(s => s.FullLogLine, StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[DIM-STYLE] stage={stage}, {style.FullLogLine}");
        }

        foreach (string style in snapshot.TextStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[TEXT-STYLE] stage={stage}, {style}");
        }

        LogSnapshotDiff(snapshot, log);
        StoreSnapshot(snapshot);
    }

    private static DimensionStyleSnapshot BuildSnapshot(Database db, string stage)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)trx.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        TextStyleTable textStyleTable = (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        Dictionary<ObjectId, DimensionStyleUsage> dimensionStyleUsage = CollectDimensionStyleUsage(db, trx);

        Dictionary<string, DimensionStyleSnapshotEntry> dimStyles = new(StringComparer.Ordinal);

        foreach (ObjectId id in dimStyleTable)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (style.IsErased || !ShouldLogDimensionStyle(style, dimensionStyleUsage.Keys))
            {
                continue;
            }

            DimensionStyleUsage usage = dimensionStyleUsage.GetValueOrDefault(style.ObjectId) ?? new DimensionStyleUsage();
            DimensionStyleSnapshotEntry entry = BuildDimensionStyleEntry(style, usage);
            dimStyles[entry.ComparisonKey] = entry;
        }

        List<string> textStyles = [];

        foreach (ObjectId id in textStyleTable)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)trx.GetObject(id, OpenMode.ForRead);
            if (!style.IsDependent && !style.IsErased)
            {
                textStyles.Add(FormatTextStyle(style));
            }
        }

        trx.Commit();

        return new DimensionStyleSnapshot(stage, dimStyles, textStyles);
    }

    private static Dictionary<ObjectId, DimensionStyleUsage> CollectDimensionStyleUsage(Database db, Transaction trx)
    {
        Dictionary<ObjectId, DimensionStyleUsage> usageByStyleId = [];
        AddDimensionStyleUsage(db.Dimstyle, null, usageByStyleId);

        BlockTable blockTable = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId blockId in blockTable)
        {
            if (trx.GetObject(blockId, OpenMode.ForRead, false) is not BlockTableRecord block || block.IsErased)
            {
                continue;
            }

            foreach (ObjectId entityId in block)
            {
                if (entityId.IsNull || entityId.IsErased)
                {
                    continue;
                }

                if (trx.GetObject(entityId, OpenMode.ForRead, false) is Dimension dimension)
                {
                    AddDimensionStyleUsage(dimension.DimensionStyle, dimension.Handle.ToString(), usageByStyleId);
                }
            }
        }

        return usageByStyleId;
    }

    private static void AddDimensionStyleUsage(ObjectId styleId, string? dimensionHandle, Dictionary<ObjectId, DimensionStyleUsage> usageByStyleId)
    {
        if (styleId.IsNull || styleId.IsErased)
        {
            return;
        }

        if (!usageByStyleId.TryGetValue(styleId, out DimensionStyleUsage? usage))
        {
            usage = new DimensionStyleUsage();
            usageByStyleId[styleId] = usage;
        }

        if (dimensionHandle is null)
        {
            return;
        }

        usage.Count++;
        if (usage.DimensionHandles.Count < DimensionHandleSampleLimit)
        {
            usage.DimensionHandles.Add(dimensionHandle);
        }
    }

    private static DimensionStyleSnapshotEntry BuildDimensionStyleEntry(DimStyleTableRecord style, DimensionStyleUsage usage)
    {
        Dictionary<string, string> summaryProperties = BuildSummaryProperties(style);
        string styleHandle = style.Handle.ToString();

        return new DimensionStyleSnapshotEntry(
            BuildComparisonKey(styleHandle, style.Name),
            style.Name,
            styleHandle,
            FormatObjectId(style.ObjectId),
            style.IsDependent,
            ReadOptionalBool(style, "IsResolved"),
            usage.Count,
            [.. usage.DimensionHandles],
            summaryProperties,
            FormatDimensionStyle(style));
    }

    private static Dictionary<string, string> BuildSummaryProperties(DimStyleTableRecord style)
    {
        Dictionary<string, string> properties = new(StringComparer.Ordinal);
        foreach (string propertyName in SummaryPropertyNames)
        {
            PropertyInfo? property = typeof(DimStyleTableRecord).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property is null || property.GetIndexParameters().Length > 0)
            {
                properties[propertyName] = "<missing>";
                continue;
            }

            properties[propertyName] = FormatPropertyValue(style, property);
        }

        return properties;
    }

    private static string BuildComparisonKey(string styleHandle, string styleName)
    {
        return !string.IsNullOrWhiteSpace(styleHandle)
            ? $"handle:{styleHandle}"
            : $"name:{styleName}";
    }

    private static bool ShouldLogDimensionStyle(DimStyleTableRecord style, ICollection<ObjectId> usedDimensionStyleIds)
    {
        return !StandardDimensionStyleNames.Contains(style.Name) || usedDimensionStyleIds.Contains(style.ObjectId);
    }

    private static string FormatDimensionStyle(DimStyleTableRecord style)
    {
        string properties = string.Join(", ", DimStyleProperties.Select(p => $"{p.Name}={FormatPropertyValue(style, p)}"));

        return
            $"styleName=\"{Escape(style.Name)}\", styleHandle={style.Handle},  objectId={FormatObjectId(style.ObjectId)}, " +
            $"isDependent={style.IsDependent}, isResolved={ReadOptionalBool(style, "IsResolved")}, " +
            $"properties={{ {properties} }}";
    }

    private static string FormatDimensionStyleSummary(DimensionStyleSnapshotEntry style)
    {
        StringBuilder builder = new();
        _ = builder
            .Append("styleName=\"").Append(Escape(style.StyleName)).Append("\", ")
            .Append("styleHandle=").Append(style.StyleHandle).Append(", ")
            .Append("objectId=").Append(style.ObjectIdText).Append(", ")
            .Append("isDependent=").Append(style.IsDependent).Append(", ")
            .Append("isResolved=").Append(style.IsResolved).Append(", ")
            .Append("usedByDimensions=").Append(style.UsedByDimensions).Append(", ")
            .Append("dimensionHandleSamples=").Append(FormatDimensionHandles(style.DimensionHandleSamples)).Append(", ")
            .Append("properties={ ");

        AppendProperties(builder, style.SummaryProperties);
        _ = builder.Append(" }");

        return builder.ToString();
    }

    private static string FormatDimensionHandles(IReadOnlyList<string> handles)
    {
        if (handles.Count == 0)
        {
            return "[]";
        }

        StringBuilder builder = new("[");
        for (int i = 0; i < handles.Count; i++)
        {
            if (i > 0)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(handles[i]);
        }

        _ = builder.Append(']');
        return builder.ToString();
    }

    private static void AppendProperties(StringBuilder builder, IReadOnlyDictionary<string, string> properties)
    {
        bool hasPrevious = false;
        foreach (KeyValuePair<string, string> property in properties.OrderBy(p => p.Key, StringComparer.Ordinal))
        {
            if (hasPrevious)
            {
                _ = builder.Append(", ");
            }

            _ = builder.Append(property.Key).Append('=').Append(property.Value);
            hasPrevious = true;
        }
    }

    private static void LogSnapshotDiff(DimensionStyleSnapshot snapshot, Logger log)
    {
        if (!PreviousStageByStage.TryGetValue(snapshot.Stage, out string? previousStage))
        {
            return;
        }

        DimensionStyleSnapshot? previousSnapshot;
        lock (SnapshotSync)
        {
            _ = SnapshotsByStage.TryGetValue(previousStage, out previousSnapshot);
        }

        if (previousSnapshot is null)
        {
            log.Information($"[DIM-STYLE-DIFF] from={previousStage}, to={snapshot.Stage}, status=missing-baseline");
            return;
        }

        LogRemovedStyles(previousSnapshot, snapshot, log);
        LogAddedStyles(previousSnapshot, snapshot, log);
        LogChangedStyles(previousSnapshot, snapshot, log);
    }

    private static void LogRemovedStyles(DimensionStyleSnapshot previousSnapshot, DimensionStyleSnapshot snapshot, Logger log)
    {
        foreach (DimensionStyleSnapshotEntry previousStyle in previousSnapshot.DimensionStyles.Values.OrderBy(s => s.StyleName, StringComparer.OrdinalIgnoreCase))
        {
            if (snapshot.DimensionStyles.ContainsKey(previousStyle.ComparisonKey))
            {
                continue;
            }

            log.Information(
                $"[DIM-STYLE-DIFF] from={previousSnapshot.Stage}, to={snapshot.Stage}, change=removed, " +
                FormatDiffStyleIdentity(previousStyle));
        }
    }

    private static void LogAddedStyles(DimensionStyleSnapshot previousSnapshot, DimensionStyleSnapshot snapshot, Logger log)
    {
        foreach (DimensionStyleSnapshotEntry currentStyle in snapshot.DimensionStyles.Values.OrderBy(s => s.StyleName, StringComparer.OrdinalIgnoreCase))
        {
            if (previousSnapshot.DimensionStyles.ContainsKey(currentStyle.ComparisonKey))
            {
                continue;
            }

            log.Information(
                $"[DIM-STYLE-DIFF] from={previousSnapshot.Stage}, to={snapshot.Stage}, change=added, " +
                $"{FormatDiffStyleIdentity(currentStyle)}, values={{ {FormatProperties(currentStyle.SummaryProperties)} }}");
        }
    }

    private static void LogChangedStyles(DimensionStyleSnapshot previousSnapshot, DimensionStyleSnapshot snapshot, Logger log)
    {
        foreach (KeyValuePair<string, DimensionStyleSnapshotEntry> currentPair in snapshot.DimensionStyles.OrderBy(p => p.Value.StyleName, StringComparer.OrdinalIgnoreCase))
        {
            if (!previousSnapshot.DimensionStyles.TryGetValue(currentPair.Key, out DimensionStyleSnapshotEntry? previousStyle))
            {
                continue;
            }

            DimensionStyleSnapshotEntry currentStyle = currentPair.Value;
            string changedProperties = FormatChangedProperties(previousStyle, currentStyle);
            bool usageChanged = previousStyle.UsedByDimensions != currentStyle.UsedByDimensions;

            if (changedProperties.Length == 0 && !usageChanged)
            {
                continue;
            }

            string usageChange = usageChanged
                ? $", usedByDimensions={previousStyle.UsedByDimensions}->{currentStyle.UsedByDimensions}"
                : string.Empty;

            log.Information(
                $"[DIM-STYLE-DIFF] from={previousSnapshot.Stage}, to={snapshot.Stage}, change=changed, " +
                $"{FormatDiffStyleIdentity(currentStyle)}{usageChange}, properties={{ {changedProperties} }}");
        }
    }

    private static string FormatDiffStyleIdentity(DimensionStyleSnapshotEntry style)
    {
        return $"key={style.ComparisonKey}, styleName=\"{Escape(style.StyleName)}\", styleHandle={style.StyleHandle}";
    }

    private static string FormatProperties(IReadOnlyDictionary<string, string> properties)
    {
        StringBuilder builder = new();
        AppendProperties(builder, properties);
        return builder.ToString();
    }

    private static string FormatChangedProperties(DimensionStyleSnapshotEntry previousStyle, DimensionStyleSnapshotEntry currentStyle)
    {
        StringBuilder builder = new();
        bool hasPrevious = false;

        foreach (string propertyName in SummaryPropertyNames)
        {
            _ = previousStyle.SummaryProperties.TryGetValue(propertyName, out string? previousValue);
            _ = currentStyle.SummaryProperties.TryGetValue(propertyName, out string? currentValue);

            if (StringComparer.Ordinal.Equals(previousValue, currentValue))
            {
                continue;
            }

            if (hasPrevious)
            {
                _ = builder.Append(", ");
            }

            _ = builder
                .Append(propertyName)
                .Append(':')
                .Append(previousValue ?? "<missing>")
                .Append("->")
                .Append(currentValue ?? "<missing>");
            hasPrevious = true;
        }

        return builder.ToString();
    }

    private static void StoreSnapshot(DimensionStyleSnapshot snapshot)
    {
        lock (SnapshotSync)
        {
            SnapshotsByStage[snapshot.Stage] = snapshot;
        }
    }

    private static string FormatTextStyle(TextStyleTableRecord style)
    {
        Autodesk.AutoCAD.GraphicsInterface.FontDescriptor font = style.Font;
        return
            $"styleName=\"{Escape(style.Name)}\", styleHandle={style.Handle}, " +
            $"styleFile=\"{Escape(style.FileName)}\", styleBigFont=\"{Escape(style.BigFontFileName)}\", " +
            $"styleTypeface=\"{Escape(font.TypeFace)}\", styleBold={font.Bold}, styleItalic={font.Italic}, " +
            $"styleCharacterSet={font.CharacterSet}, stylePitchAndFamily={font.PitchAndFamily}, " +
            $"styleIsShapeFile={style.IsShapeFile}, styleIsVertical={style.IsVertical}, " +
            $"styleTextSize={F(style.TextSize)}, styleXScale={F(style.XScale)}, " +
            $"styleObliquingAngle={F(style.ObliquingAngle)}";
    }

    private static string FormatColor(Color color)
    {
        return $"{color.ColorMethod}:{color.ColorIndex}";
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
        catch (System.Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }

    private static string FormatPropertyValue(DimStyleTableRecord style, PropertyInfo property)
    {
        try
        {
            return FormatValue(property.GetValue(style));
        }
        catch (TargetInvocationException ex) when (ex.InnerException is not null)
        {
            return $"<error: {ex.InnerException.GetType().Name}>";
        }
        catch (System.Exception ex)
        {
            return $"<error: {ex.GetType().Name}>";
        }
    }

    private static string FormatValue(object? value)
    {
        return value switch
        {
            null => "null",
            string text => $"\"{Escape(text)}\"",
            double d => F(d),
            float f => F(f),
            decimal d => d.ToString("0.######", CultureInfo.InvariantCulture),
            bool b => b ? "true" : "false",
            Color color => FormatColor(color),
            ObjectId id => FormatObjectId(id),
            Enum e => e.ToString(),
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => $"\"{Escape(value.ToString())}\""
        };
    }

    private static string ReadOptionalBool(DimStyleTableRecord style, string propertyName)
    {
        PropertyInfo? property = typeof(DimStyleTableRecord).GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
        if (property is null || property.PropertyType != typeof(bool) || property.GetIndexParameters().Length > 0)
        {
            return "n/a";
        }

        return FormatPropertyValue(style, property);
    }

    private static string F(double value)
    {
        return double.IsFinite(value) ? value.ToString("F6", CultureInfo.InvariantCulture) : "n/a";
    }

    /// <summary>
    /// Escapes special characters in a string for safe inclusion in a quoted context.
    /// </summary>
    /// <remarks>This method replaces backslashes (\), double quotes (") with their escaped forms, and
    /// converts carriage return and line feed characters to \r and \n, respectively. This is useful when preparing
    /// strings for serialization or display in formats that require escaping of these characters.</remarks>
    /// <param name="value">The string to escape. If null, an empty string is used.</param>
    /// <returns>A string with backslashes, double quotes, carriage returns, and line feeds replaced by their escaped
    /// representations.</returns>
    private static string Escape(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    private sealed class DimensionStyleUsage
    {
        internal int Count { get; set; }

        internal List<string> DimensionHandles { get; } = [];
    }

    private sealed record DimensionStyleSnapshot(
        string Stage,
        IReadOnlyDictionary<string, DimensionStyleSnapshotEntry> DimensionStyles,
        IReadOnlyList<string> TextStyles);

    private sealed record DimensionStyleSnapshotEntry(
        string ComparisonKey,
        string StyleName,
        string StyleHandle,
        string ObjectIdText,
        bool IsDependent,
        string IsResolved,
        int UsedByDimensions,
        IReadOnlyList<string> DimensionHandleSamples,
        IReadOnlyDictionary<string, string> SummaryProperties,
        string FullLogLine);
}
