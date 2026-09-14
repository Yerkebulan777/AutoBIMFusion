namespace AutoBIMFusion.QuickPdf.Frames;

/// <summary>
///     Порядок листов: справа налево внутри ряда, ряды сверху вниз.
///     Ряд задаётся верхней кромкой рамки с допуском 25% медианы высоты.
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

        List<DetectedFrame> remaining =
        [
            .. frames
                .OrderByDescending(frame => frame.MaxY)
                .ThenByDescending(frame => frame.MaxX)
        ];
        List<DetectedFrame> ordered = [];
        while (remaining.Count > 0)
        {
            double rowY = remaining[0].MaxY;
            List<DetectedFrame> row = [];
            List<DetectedFrame> rest = [];
            foreach (DetectedFrame frame in remaining)
            {
                if (Abs(frame.MaxY - rowY) <= rowTolerance)
                {
                    row.Add(frame);
                }
                else
                {
                    rest.Add(frame);
                }
            }

            ordered.AddRange(row.OrderByDescending(frame => frame.MaxX).ThenByDescending(frame => frame.MaxY));
            remaining = rest;
        }

        return ordered;
    }
}
