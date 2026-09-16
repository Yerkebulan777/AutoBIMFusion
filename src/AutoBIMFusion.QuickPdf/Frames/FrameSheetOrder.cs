namespace AutoBIMFusion.QuickPdf.Frames;

/// <summary>
///     Порядок листов: слева направо внутри ряда, ряды сверху вниз.
///     Сортируются только XY-координаты центров уже распознанных рамок:
///     Y по убыванию, при одинаковом Y — X по возрастанию.
/// </summary>
internal static class FrameSheetOrder
{
    internal static IReadOnlyList<DetectedFrame> Sort(IReadOnlyList<DetectedFrame> frames)
    {
        if (frames.Count <= 1)
        {
            return frames;
        }

        return frames
            .OrderByDescending(frame => (frame.MinY + frame.MaxY) / 2)
            .ThenBy(frame => (frame.MinX + frame.MaxX) / 2)
            .ToArray();
    }
}
