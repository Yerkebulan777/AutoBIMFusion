namespace AutoBIMFusion.QuickPdf.Frames;

/// <summary>
///     Порядок листов: слева направо внутри ряда, ряды сверху вниз.
///     Ряд — префикс списка, отсортированного по верхней кромке, с допуском 25% медианы высоты.
/// </summary>
internal static class FrameSheetOrder
{
    private const double RowBandFraction = 0.25;

    internal static IReadOnlyList<DetectedFrame> Sort(IReadOnlyList<DetectedFrame> frames)
    {
        if (frames.Count <= 1)
        {
            return frames;
        }

        double medianHeight = frames
            .Select(frame => frame.MaxY - frame.MinY)
            .OrderBy(height => height)
            .ElementAt(frames.Count / 2);
        double rowTolerance = Max(medianHeight * RowBandFraction, 1);

        List<DetectedFrame> byTop = [.. frames.OrderByDescending(frame => frame.MaxY)];
        List<DetectedFrame> ordered = [];
        int index = 0;
        while (index < byTop.Count)
        {
            double rowY = byTop[index].MaxY;
            List<DetectedFrame> row = [];
            while (index < byTop.Count && Abs(byTop[index].MaxY - rowY) <= rowTolerance)
            {
                row.Add(byTop[index]);
                index++;
            }

            ordered.AddRange(row.OrderBy(frame => frame.MinX));
        }

        return ordered;
    }
}
