using System.Globalization;

namespace AutoBIMFusion.QuickPdf.Media;

public enum IsoMediaKind
{
    FullBleed = 0,
    Expand = 1,
    Other = 2
}

public static class IsoMedia
{
    public const double FitToleranceMm = 0.05;
    internal const double MaxExcessMm = 5.0;

    public static bool IsIsoName(string? name)
    {
        return name is { Length: > 0 } &&
               name.StartsWith("ISO_", StringComparison.OrdinalIgnoreCase);
    }

    public static bool FitsMm(double available, double required)
    {
        return available + FitToleranceMm >= required;
    }

    internal static bool PaperMatches(double paperWidth, double paperHeight, double needWidth, double needHeight)
    {
        return OrientationMatches(paperWidth, paperHeight, needWidth, needHeight) ||
               OrientationMatches(paperWidth, paperHeight, needHeight, needWidth);
    }

    internal static bool TryGetMatchingArea(
        string? name,
        double needWidth,
        double needHeight,
        out double paperArea)
    {
        paperArea = 0;
        if (!IsIsoName(name) ||
            !TryParseSize(name, out double paperWidth, out double paperHeight) ||
            !PaperMatches(paperWidth, paperHeight, needWidth, needHeight))
        {
            return false;
        }

        paperArea = paperWidth * paperHeight;
        return true;
    }

    public static IsoMediaKind Kind(string name)
    {
        string upper = name.ToUpperInvariant();
        if (upper.Contains("FULL_BLEED"))
        {
            return IsoMediaKind.FullBleed;
        }

        return upper.Contains("EXPAND") ? IsoMediaKind.Expand : IsoMediaKind.Other;
    }

    public static bool TryParseSize(string? name, out double width, out double height)
    {
        width = 0;
        height = 0;
        if (name is not { Length: > 0 })
        {
            return false;
        }

        int open = name.IndexOf('(');
        if (open < 0 || open >= name.Length - 1)
        {
            return false;
        }

        string inner = name[(open + 1)..];
        int separator = inner.IndexOf("_X_", StringComparison.OrdinalIgnoreCase);
        if (separator < 0)
        {
            return false;
        }

        return TryParseLeadingNumber(inner[..separator], out width) &&
               TryParseLeadingNumber(inner[(separator + 3)..], out height) &&
               width > 0 && height > 0;
    }

    private static bool TryParseLeadingNumber(string text, out double value)
    {
        value = 0;
        ReadOnlySpan<char> span = text.AsSpan().TrimStart();
        int length = 0;
        while (length < span.Length && (char.IsDigit(span[length]) || span[length] == '.'))
        {
            length++;
        }

        return length > 0 &&
               double.TryParse(span[..length], NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool OrientationMatches(double paperWidth, double paperHeight, double needWidth, double needHeight)
    {
        return paperWidth >= needWidth &&
               paperHeight >= needHeight &&
               paperWidth - needWidth <= MaxExcessMm &&
               paperHeight - needHeight <= MaxExcessMm;
    }
}
