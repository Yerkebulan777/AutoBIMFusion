namespace AutoBIMFusion.QuickPdf.Frames;

internal readonly struct FrameWindow
{
    internal FrameWindow(double minX, double minY, double maxX, double maxY)
    {
        MinX = Min(minX, maxX);
        MinY = Min(minY, maxY);
        MaxX = Max(minX, maxX);
        MaxY = Max(minY, maxY);
    }

    internal double MinX { get; }

    internal double MinY { get; }

    internal double MaxX { get; }

    internal double MaxY { get; }

    internal bool Intersects(FrameWindow other)
    {
        return MinX <= other.MaxX && MaxX >= other.MinX &&
               MinY <= other.MaxY && MaxY >= other.MinY;
    }
}

/// <summary>
///     Отбор рамок, пересекающих указанный участок модели.
/// </summary>
internal static class FrameAreaFilter
{
    internal static FrameWindow Of(DetectedFrame frame)
    {
        return new FrameWindow(frame.MinX, frame.MinY, frame.MaxX, frame.MaxY);
    }

    internal static IReadOnlyList<DetectedFrame> Intersecting(
        IReadOnlyList<DetectedFrame> frames,
        FrameWindow window)
    {
        List<DetectedFrame> selected = [];
        foreach (DetectedFrame frame in frames)
        {
            if (window.Intersects(Of(frame)))
            {
                selected.Add(frame);
            }
        }

        return selected;
    }
}
