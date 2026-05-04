using Autodesk.AutoCAD.Colors;
using Serilog.Core;
using System.Globalization;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionStyleDiagnosticUtils
{
    private const string AcadRegAppName = "ACAD";
    private const string DimensionStyleOverrideMarker = "DSTYLE";

    private static readonly HashSet<string> StandardStyleNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ISO-25",
        "Standard",
        "Annotative"
    };

    internal static void LogStyleSnapshot(Database db, Logger log, string stage)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        TextStyleTable textStyleTable = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

        List<string> dimStyles = [];
        foreach (ObjectId id in dimStyleTable)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style))
            {
                dimStyles.Add(FormatDimensionStyle(style));
            }
        }

        List<string> textStyles = [];
        foreach (ObjectId id in textStyleTable)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style))
            {
                textStyles.Add(FormatTextStyle(style));
            }
        }

        tr.Commit();

        log.Information($"[STYLE-SNAPSHOT] stage={stage}, dimStyles={dimStyles.Count}, textStyles={textStyles.Count}");

        foreach (string style in dimStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[DIM-STYLE] stage={stage}, {style}");
        }

        foreach (string style in textStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[TEXT-STYLE] stage={stage}, {style}");
        }
    }

    internal static void LogNewStylesBeforeMerge(Database sourceDb, Database targetDb, Logger log)
    {
        HashSet<string> targetDimStyleNames = CollectDimensionStyleNames(targetDb);
        HashSet<string> targetTextStyleNames = CollectTextStyleNames(targetDb);

        List<string> newDimStyles = [];
        List<string> newTextStyles = [];

        using Transaction tr = sourceDb.TransactionManager.StartTransaction();

        DimStyleTable dimStyleTable = (DimStyleTable)tr.GetObject(sourceDb.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in dimStyleTable)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style) && !targetDimStyleNames.Contains(style.Name))
            {
                newDimStyles.Add(FormatDimensionStyle(style));
            }
        }

        TextStyleTable textStyleTable = (TextStyleTable)tr.GetObject(sourceDb.TextStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in textStyleTable)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            if (IsUserStyle(style) && !targetTextStyleNames.Contains(style.Name))
            {
                newTextStyles.Add(FormatTextStyle(style));
            }
        }

        tr.Commit();

        foreach (string style in newDimStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[DIM-STYLE] stage=before-merge, {style}");
        }

        foreach (string style in newTextStyles.Order(StringComparer.OrdinalIgnoreCase))
        {
            log.Information($"[TEXT-STYLE] stage=before-merge, {style}");
        }
    }

    internal static void ClearDimensionOverrides(Database db, Logger log)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId blockId in blockTable)
        {
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

                try
                {
                    _ = DimensionUtils.TryRemoveDimensionStyleOverrides(dimension);
                }
                catch (System.Exception ex)
                {
                    log.Warning($"[DIM-OVERRIDES] handle={dimension.Handle}: не удалось очистить overrides: {ex.Message}");
                }
            }
        }

        tr.Commit();
    }

    private static bool TryRemoveDimensionStyleOverrideSection(TypedValue[] values, out List<TypedValue> cleanedValues)
    {
        cleanedValues = [];
        bool changed = false;

        for (int i = 0; i < values.Length;)
        {
            if (!IsRegApp(values[i], out string? appName))
            {
                cleanedValues.Add(values[i]);
                i++;
                continue;
            }

            int sectionStart = i;
            i++;

            List<TypedValue> sectionValues = [];
            while (i < values.Length && !IsRegApp(values[i], out _))
            {
                sectionValues.Add(values[i]);
                i++;
            }

            List<TypedValue> cleanedSection = sectionValues;
            bool sectionChanged = appName is not null
                && appName.Equals(AcadRegAppName, StringComparison.OrdinalIgnoreCase)
                && TryRemoveAcadDimensionStyleOverrideSection(sectionValues, out cleanedSection);

            if (sectionChanged)
            {
                changed = true;
                if (cleanedSection.Count == 0)
                {
                    continue;
                }

                cleanedValues.Add(values[sectionStart]);
                cleanedValues.AddRange(cleanedSection);
                continue;
            }

            cleanedValues.Add(values[sectionStart]);
            cleanedValues.AddRange(sectionValues);
        }

        return changed;
    }

    private static bool TryRemoveAcadDimensionStyleOverrideSection(
        List<TypedValue> sectionValues,
        out List<TypedValue> cleanedSection)
    {
        cleanedSection = [];
        bool changed = false;

        for (int i = 0; i < sectionValues.Count; i++)
        {
            TypedValue value = sectionValues[i];
            if (!IsDimensionStyleOverrideMarker(value))
            {
                cleanedSection.Add(value);
                continue;
            }

            changed = true;
            i = SkipOverridePayload(sectionValues, i);
        }

        return changed;
    }

    private static int SkipOverridePayload(List<TypedValue> sectionValues, int markerIndex)
    {
        int i = markerIndex + 1;
        if (i >= sectionValues.Count || !IsControlString(sectionValues[i], "{"))
        {
            return markerIndex;
        }

        int depth = 0;
        for (; i < sectionValues.Count; i++)
        {
            if (IsControlString(sectionValues[i], "{"))
            {
                depth++;
            }
            else if (IsControlString(sectionValues[i], "}"))
            {
                depth--;
                if (depth == 0)
                {
                    return i;
                }
            }
        }

        return sectionValues.Count - 1;
    }

    private static HashSet<string> CollectDimensionStyleNames(Database db)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        using Transaction tr = db.TransactionManager.StartTransaction();
        DimStyleTable table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            _ = names.Add(style.Name);
        }

        tr.Commit();
        return names;
    }

    private static HashSet<string> CollectTextStyleNames(Database db)
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);

        using Transaction tr = db.TransactionManager.StartTransaction();
        TextStyleTable table = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);
        foreach (ObjectId id in table)
        {
            TextStyleTableRecord style = (TextStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            _ = names.Add(style.Name);
        }

        tr.Commit();
        return names;
    }

    private static DimensionStyleSnapshot CreateSnapshot(DimStyleTableRecord style)
    {
        return new DimensionStyleSnapshot(
            style.Name,
            style.Handle.ToString(),
            style.Dimtxt,
            style.Dimasz,
            style.Dimscale,
            style.Dimlfac,
            style.Dimexo,
            style.Dimexe,
            style.Dimtad,
            style.Dimjust,
            style.Dimgap,
            style.Dimdec,
            style.Dimrnd,
            style.Dimpost,
            FormatColor(style.Dimclrd),
            FormatColor(style.Dimclre),
            FormatColor(style.Dimclrt),
            style.Annotative.ToString());
    }

    private static TextStyleSnapshot CreateSnapshot(TextStyleTableRecord style)
    {
        Autodesk.AutoCAD.GraphicsInterface.FontDescriptor font = style.Font;

        return new TextStyleSnapshot(
            style.Name,
            style.Handle.ToString(),
            style.FileName ?? string.Empty,
            style.BigFontFileName ?? string.Empty,
            font.TypeFace ?? string.Empty,
            font.Bold,
            font.Italic,
            font.CharacterSet,
            font.PitchAndFamily,
            style.IsShapeFile,
            style.IsVertical,
            style.TextSize,
            style.XScale,
            style.ObliquingAngle);
    }

    private static string FormatDimensionStyle(DimStyleTableRecord style)
    {
        return FormatSnapshot("style", CreateSnapshot(style));
    }

    private static string FormatTextStyle(TextStyleTableRecord style)
    {
        TextStyleSnapshot snapshot = CreateSnapshot(style);
        return
            $"styleName=\"{Escape(snapshot.Name)}\", styleHandle={snapshot.Handle}, " +
            $"styleFile=\"{Escape(snapshot.FileName)}\", styleBigFont=\"{Escape(snapshot.BigFontFileName)}\", " +
            $"styleTypeface=\"{Escape(snapshot.TypeFace)}\", styleBold={snapshot.Bold}, styleItalic={snapshot.Italic}, " +
            $"styleCharacterSet={snapshot.CharacterSet}, stylePitchAndFamily={snapshot.PitchAndFamily}, " +
            $"styleIsShapeFile={snapshot.IsShapeFile}, styleIsVertical={snapshot.IsVertical}, " +
            $"styleTextSize={FormatDouble(snapshot.TextSize)}, styleXScale={FormatDouble(snapshot.XScale)}, " +
            $"styleObliquingAngle={FormatDouble(snapshot.ObliquingAngle)}";
    }

    private static string FormatSnapshot(string prefix, DimensionStyleSnapshot snapshot)
    {
        return
            $"{prefix}Name=\"{Escape(snapshot.Name)}\", {prefix}Handle={snapshot.Handle}, " +
            $"{prefix}Dimtxt={FormatDouble(snapshot.Dimtxt)}, {prefix}Dimasz={FormatDouble(snapshot.Dimasz)}, " +
            $"{prefix}Dimscale={FormatDouble(snapshot.Dimscale)}, {prefix}Dimlfac={FormatDouble(snapshot.Dimlfac)}, " +
            $"{prefix}Dimexo={FormatDouble(snapshot.Dimexo)}, {prefix}Dimexe={FormatDouble(snapshot.Dimexe)}, " +
            $"{prefix}Dimtad={snapshot.Dimtad}, {prefix}Dimjust={snapshot.Dimjust}, " +
            $"{prefix}Dimgap={FormatDouble(snapshot.Dimgap)}, {prefix}Dimdec={snapshot.Dimdec}, " +
            $"{prefix}Dimrnd={FormatDouble(snapshot.Dimrnd)}, {prefix}Dimpost=\"{Escape(snapshot.Dimpost)}\", " +
            $"{prefix}Dimclrd={snapshot.Dimclrd}, {prefix}Dimclre={snapshot.Dimclre}, " +
            $"{prefix}Dimclrt={snapshot.Dimclrt}, {prefix}Annotative={snapshot.Annotative}, " +
            $"{prefix}VisualText={FormatDouble(snapshot.Dimtxt * snapshot.Dimscale)}, " +
            $"{prefix}VisualArrow={FormatDouble(snapshot.Dimasz * snapshot.Dimscale)}";
    }

    private static bool IsUserStyle(SymbolTableRecord style)
    {
        return !style.IsDependent
            && !style.IsErased
            && !StandardStyleNames.Contains(style.Name);
    }

    private static string FormatColor(Color color)
    {
        return $"{color.ColorMethod}:{color.ColorIndex}";
    }

    private static string FormatDouble(double value)
    {
        return double.IsFinite(value)
            ? value.ToString("F6", CultureInfo.InvariantCulture)
            : "n/a";
    }

    private static bool IsRegApp(TypedValue value, out string? appName)
    {
        appName = value.Value as string;
        return value.TypeCode == (int)DxfCode.ExtendedDataRegAppName
            && appName is not null;
    }

    private static bool IsDimensionStyleOverrideMarker(TypedValue value)
    {
        return value.TypeCode == (int)DxfCode.ExtendedDataAsciiString
            && value.Value is string marker
            && marker.Equals(DimensionStyleOverrideMarker, StringComparison.Ordinal);
    }

    private static bool IsControlString(TypedValue value, string expected)
    {
        return value.TypeCode == (int)DxfCode.ExtendedDataControlString
            && value.Value is string control
            && control.Equals(expected, StringComparison.Ordinal);
    }

    private static string Escape(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }

    internal readonly record struct DimensionStyleSnapshot(
        string Name,
        string Handle,
        double Dimtxt,
        double Dimasz,
        double Dimscale,
        double Dimlfac,
        double Dimexo,
        double Dimexe,
        int Dimtad,
        int Dimjust,
        double Dimgap,
        int Dimdec,
        double Dimrnd,
        string Dimpost,
        string Dimclrd,
        string Dimclre,
        string Dimclrt,
        string Annotative);

    private readonly record struct TextStyleSnapshot(
        string Name,
        string Handle,
        string FileName,
        string BigFontFileName,
        string TypeFace,
        bool Bold,
        bool Italic,
        int CharacterSet,
        int PitchAndFamily,
        bool IsShapeFile,
        bool IsVertical,
        double TextSize,
        double XScale,
        double ObliquingAngle);
}
