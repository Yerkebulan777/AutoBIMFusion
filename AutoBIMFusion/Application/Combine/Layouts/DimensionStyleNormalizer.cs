using System.Globalization;

namespace AutoBIMFusion.Application.Combine.Layouts;

internal static class DimensionStyleNormalizer
{
    private const double Tolerance = 1e-9;

    internal static ObjectId NormalizeDimensionStyleForViewport(
        ObjectId currentStyleId,
        Database db,
        double viewportMultiplier,
        double styleScaleMultiplier,
        Transaction trx)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(trx);

        if (!TryResolveStyleRecord(currentStyleId, db, trx, out DimStyleTableRecord sourceStyle))
        {
            return ObjectId.Null;
        }

        string baseName = NormalizeBaseName(sourceStyle.Name);
        double effectiveViewportMultiplier = NormalizeScale(viewportMultiplier) * NormalizeScale(styleScaleMultiplier);
        string normalizedStyleName = $"{baseName}_{FormatScale(effectiveViewportMultiplier)}";

        DimStyleTable dimStyleTable = (DimStyleTable)trx.GetObject(db.DimStyleTableId, OpenMode.ForRead);
        if (dimStyleTable.Has(normalizedStyleName))
        {
            return dimStyleTable[normalizedStyleName];
        }

        if (!dimStyleTable.IsWriteEnabled)
        {
            dimStyleTable.UpgradeOpen();
        }

        DimStyleTableRecord normalizedStyle = new();
        normalizedStyle.CopyFrom(sourceStyle);
        normalizedStyle.Name = normalizedStyleName;

        ApplyViewportMetricSettings(normalizedStyle, ResolveVisualMultiplier(sourceStyle, viewportMultiplier, styleScaleMultiplier));

        ObjectId normalizedStyleId = dimStyleTable.Add(normalizedStyle);
        trx.AddNewlyCreatedDBObject(normalizedStyle, true);
        return normalizedStyleId;
    }

    private static bool TryResolveStyleRecord(ObjectId styleId, Database db, Transaction trx, out DimStyleTableRecord style)
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

    private static void ApplyViewportMetricSettings(DimStyleTableRecord style, double visualMultiplier)
    {
        style.Annotative = AnnotativeStates.False;
        style.Dimtxt = ScaleVisualValue(style.Dimtxt, visualMultiplier);
        style.Dimasz = ScaleVisualValue(style.Dimasz, visualMultiplier);
        style.Dimtsz = ScaleVisualValue(style.Dimtsz, visualMultiplier);
        style.Dimexo = ScaleVisualValue(style.Dimexo, visualMultiplier);
        style.Dimexe = ScaleVisualValue(style.Dimexe, visualMultiplier);
        style.Dimgap = ScaleVisualValue(style.Dimgap, visualMultiplier);
        style.Dimdli = ScaleVisualValue(style.Dimdli, visualMultiplier);
        style.Dimdle = ScaleVisualValue(style.Dimdle, visualMultiplier);
        style.Dimcen = ScaleVisualValue(style.Dimcen, visualMultiplier);
        style.Dimtvp = ScaleVisualValue(style.Dimtvp, visualMultiplier);
        style.Dimfxlen = ScaleVisualValue(style.Dimfxlen, visualMultiplier);
        style.Dimscale = 1.0;
    }

    private static double ResolveVisualMultiplier(DimStyleTableRecord sourceStyle, double viewportMultiplier, double styleScaleMultiplier)
    {
        double baseMultiplier = IsUsableScale(sourceStyle.Dimscale) ? sourceStyle.Dimscale : NormalizeScale(viewportMultiplier);
        return baseMultiplier * NormalizeScale(styleScaleMultiplier);
    }

    private static double NormalizeScale(double scale)
    {
        return IsUsableScale(scale) ? scale : 1.0;
    }

    private static double ScaleVisualValue(double value, double multiplier)
    {
        if (double.IsFinite(value) && Math.Abs(value) > Tolerance)
        {
            return Math.Round(value * multiplier, 4, MidpointRounding.AwayFromZero);
        }

        return value;
    }

    private static string NormalizeBaseName(string styleName)
    {
        string trimmed = styleName.Trim();
        int lastLetterIndex = trimmed.Length - 1;

        while (lastLetterIndex >= 0 && !char.IsLetter(trimmed[lastLetterIndex]))
        {
            lastLetterIndex--;
        }

        string normalized = lastLetterIndex >= 0 ? trimmed[..(lastLetterIndex + 1)] : string.Empty;
        return string.IsNullOrWhiteSpace(normalized) ? "DimStyle" : normalized;
    }

    private static string FormatScale(double scale)
    {
        double normalized = NormalizeScale(scale);
        double rounded = Math.Round(normalized);

        return Math.Abs(normalized - rounded) < Tolerance
            ? rounded.ToString("0", CultureInfo.InvariantCulture)
            : normalized.ToString("0.######", CultureInfo.InvariantCulture).TrimEnd('0').TrimEnd('.');
    }

    private static bool IsUsableScale(double scale)
    {
        return double.IsFinite(scale) && scale > Tolerance;
    }
}
