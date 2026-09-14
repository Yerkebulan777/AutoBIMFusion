namespace AutoBIMFusion.QuickPdf.Media;

internal readonly record struct IsoMediaProbeResult(bool Rotated, double ActualArea);

internal readonly record struct IsoMediaSelection(string CanonicalName, bool Rotated);

/// <summary>
///     Отделяет чистый выбор ISO-носителя от AutoCAD-пробы его фактических размеров.
/// </summary>
internal static class IsoMediaSelector
{
    public static IsoMediaSelection? Pick(
        IEnumerable<string> canonicalNames,
        double needWidth,
        double needHeight,
        Func<string, IsoMediaProbeResult?> probe)
    {
        IsoMediaSelection? best = null;
        (double ActualArea, IsoMediaKind Kind, double ParsedArea) bestRank =
            (double.PositiveInfinity, IsoMediaKind.Other, double.PositiveInfinity);

        foreach (string name in canonicalNames)
        {
            if (!IsoMedia.TryGetMatchingArea(name, needWidth, needHeight, out double parsedArea))
            {
                continue;
            }

            IsoMediaProbeResult? probed = probe(name);
            if (probed is not { } result)
            {
                continue;
            }

            var rank = (result.ActualArea, IsoMedia.Kind(name), parsedArea);
            if (best is null || rank.CompareTo(bestRank) < 0)
            {
                best = new IsoMediaSelection(name, result.Rotated);
                bestRank = rank;
            }
        }

        return best;
    }
}
