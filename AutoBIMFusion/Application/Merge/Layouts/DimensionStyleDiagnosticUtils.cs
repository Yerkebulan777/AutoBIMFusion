using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.Colors;
using System.Globalization;

namespace AutoBIMFusion.Application.Merge.Layouts;

internal static class DimensionStyleDiagnosticUtils
{
    internal static void LogDimensionStyleSnapshot(Database db, AILog log, string stage)
    {
        using Transaction tr = db.TransactionManager.StartTransaction();
        DimStyleTable table = (DimStyleTable)tr.GetObject(db.DimStyleTableId, OpenMode.ForRead);

        foreach (ObjectId id in table)
        {
            DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(id, OpenMode.ForRead);
            log.Info($"[DIM-STYLE] stage={stage}, {FormatStyle(style)}");
        }

        tr.Commit();
    }

    internal static DimensionStyleSnapshot? TryReadStyleSnapshot(Dimension dimension)
    {
        try
        {
            ObjectId styleId = dimension.DimensionStyle;
            if (styleId.IsNull)
            {
                return null;
            }

            using Transaction tr = styleId.Database.TransactionManager.StartTransaction();
            DimStyleTableRecord style = (DimStyleTableRecord)tr.GetObject(styleId, OpenMode.ForRead);
            DimensionStyleSnapshot snapshot = CreateSnapshot(style);
            tr.Commit();
            return snapshot;
        }
        catch
        {
            return null;
        }
    }

    internal static string FormatStyleSnapshot(DimensionStyleSnapshot? snapshot)
    {
        return !snapshot.HasValue ? "styleSnapshot=none" : FormatSnapshot("style", snapshot.Value);
    }

    internal static bool EntityDiffersFromStyle(Dimension dimension, DimensionStyleSnapshot? style)
    {
        if (!style.HasValue)
        {
            return false;
        }

        DimensionStyleSnapshot value = style.Value;
        return !Near(dimension.Dimtxt, value.Dimtxt)
            || !Near(dimension.Dimasz, value.Dimasz)
            || !Near(dimension.Dimscale, value.Dimscale)
            || !Near(dimension.Dimlfac, value.Dimlfac);
    }

    internal static string FormatStyle(DimStyleTableRecord style)
    {
        return FormatSnapshot("style", CreateSnapshot(style));
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

    private static bool Near(double left, double right)
    {
        return Abs(left - right) <= Max(1e-9, Abs(right) * 1e-6);
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
}
