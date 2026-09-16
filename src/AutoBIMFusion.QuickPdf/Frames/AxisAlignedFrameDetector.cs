namespace AutoBIMFusion.QuickPdf.Frames;

internal readonly struct FramePoint
{
    internal FramePoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    internal double X { get; }

    internal double Y { get; }
}

internal sealed class FramePath
{
    internal FramePath(int sourceId, bool closed, IReadOnlyList<FramePoint> points)
    {
        SourceId = sourceId;
        Closed = closed;
        Points = points;
    }

    internal int SourceId { get; }

    internal bool Closed { get; }

    internal IReadOnlyList<FramePoint> Points { get; }
}

internal sealed class DetectedFrame
{
    internal DetectedFrame(double minX, double minY, double maxX, double maxY, IReadOnlyList<int> sourceIds)
    {
        MinX = minX;
        MinY = minY;
        MaxX = maxX;
        MaxY = maxY;
        SourceIds = sourceIds;
    }

    internal double MinX { get; }

    internal double MinY { get; }

    internal double MaxX { get; }

    internal double MaxY { get; }

    internal IReadOnlyList<int> SourceIds { get; }
}

internal static class AxisAlignedFrameDetector
{
    private const double ModelUnitsPerMm = 100;
    private const double A4ShortSide = 210 * ModelUnitsPerMm;
    private const double A4LongSide = 297 * ModelUnitsPerMm;
    private const double A0x3ShortSide = 1189 * ModelUnitsPerMm;
    private const double A0x3LongSide = 2523 * ModelUnitsPerMm;

    internal static IReadOnlyList<DetectedFrame> Find(IReadOnlyList<FramePath> paths, double tolerance)
    {
        List<DetectedFrame> frames = [];
        foreach (FramePath path in paths)
        {
            if (!path.Closed && path.Points.Count == 2)
            {
                continue;
            }

            if (path.Points.Count < 4)
            {
                continue;
            }

            FramePoint first = path.Points[0];
            FramePoint last = path.Points[path.Points.Count - 1];
            if (!path.Closed && (Abs(first.X - last.X) > tolerance || Abs(first.Y - last.Y) > tolerance))
            {
                continue;
            }

            double minX = path.Points.Min(point => point.X);
            double minY = path.Points.Min(point => point.Y);
            double maxX = path.Points.Max(point => point.X);
            double maxY = path.Points.Max(point => point.Y);
            double width = maxX - minX;
            double height = maxY - minY;
            if (!FitsPrintableSheet(width, height))
            {
                continue;
            }

            bool axisAligned = true;
            bool hasLeft = false;
            bool hasRight = false;
            bool hasBottom = false;
            bool hasTop = false;
            for (int index = 0; index < path.Points.Count; index++)
            {
                FramePoint start = path.Points[index];
                FramePoint end = path.Points[(index + 1) % path.Points.Count];
                bool vertical = Abs(start.X - end.X) <= tolerance;
                bool horizontal = Abs(start.Y - end.Y) <= tolerance;
                bool onLeft = vertical && Abs(start.X - minX) <= tolerance;
                bool onRight = vertical && Abs(start.X - maxX) <= tolerance;
                bool onBottom = horizontal && Abs(start.Y - minY) <= tolerance;
                bool onTop = horizontal && Abs(start.Y - maxY) <= tolerance;
                bool onVerticalBoundary = onLeft || onRight;
                bool onHorizontalBoundary = onBottom || onTop;
                if (!onVerticalBoundary && !onHorizontalBoundary)
                {
                    axisAligned = false;
                    break;
                }

                if (Abs(start.Y - end.Y) > tolerance)
                {
                    hasLeft |= onLeft;
                    hasRight |= onRight;
                }

                if (Abs(start.X - end.X) > tolerance)
                {
                    hasBottom |= onBottom;
                    hasTop |= onTop;
                }
            }

            if (!axisAligned || !hasLeft || !hasRight || !hasBottom || !hasTop)
            {
                continue;
            }

            frames.Add(new DetectedFrame(minX, minY, maxX, maxY, [path.SourceId]));
        }

        AddLineFrames(paths, tolerance, frames);
        return SelectOutermost(frames, tolerance);
    }

    private static IReadOnlyList<DetectedFrame> SelectOutermost(
        IReadOnlyList<DetectedFrame> candidates,
        double tolerance)
    {
        List<DetectedFrame> distinct = [];
        foreach (DetectedFrame candidate in candidates)
        {
            int matchIndex = distinct.FindIndex(frame => SameBounds(frame, candidate, tolerance));
            if (matchIndex < 0)
            {
                distinct.Add(candidate);
                continue;
            }

            DetectedFrame match = distinct[matchIndex];
            int[] sourceIds = match.SourceIds.Concat(candidate.SourceIds).Distinct().OrderBy(id => id).ToArray();
            distinct[matchIndex] = new DetectedFrame(
                Min(match.MinX, candidate.MinX),
                Min(match.MinY, candidate.MinY),
                Max(match.MaxX, candidate.MaxX),
                Max(match.MaxY, candidate.MaxY),
                sourceIds);
        }

        List<DetectedFrame> selected = [];
        foreach (DetectedFrame candidate in distinct.OrderByDescending(Area))
        {
            if (!selected.Any(outer => StrictlyContains(outer, candidate, tolerance)))
            {
                selected.Add(candidate);
            }
        }

        return selected;
    }

    private static bool SameBounds(DetectedFrame first, DetectedFrame second, double tolerance)
    {
        return Abs(first.MinX - second.MinX) <= tolerance &&
               Abs(first.MinY - second.MinY) <= tolerance &&
               Abs(first.MaxX - second.MaxX) <= tolerance &&
               Abs(first.MaxY - second.MaxY) <= tolerance;
    }

    private static bool StrictlyContains(DetectedFrame outer, DetectedFrame inner, double tolerance)
    {
        bool contains = outer.MinX <= inner.MinX + tolerance &&
                        outer.MinY <= inner.MinY + tolerance &&
                        outer.MaxX >= inner.MaxX - tolerance &&
                        outer.MaxY >= inner.MaxY - tolerance;
        return contains && Area(outer) > Area(inner);
    }

    private static double Area(DetectedFrame frame)
    {
        return (frame.MaxX - frame.MinX) * (frame.MaxY - frame.MinY);
    }

    private static bool FitsPrintableSheet(double width, double height)
    {
        double shortSide = Min(width, height);
        double longSide = Max(width, height);
        return shortSide >= A4ShortSide && longSide >= A4LongSide &&
               shortSide <= A0x3ShortSide && longSide <= A0x3LongSide;
    }

    private static void AddLineFrames(
        IReadOnlyList<FramePath> paths,
        double tolerance,
        ICollection<DetectedFrame> frames)
    {
        List<LineSpan> horizontal = [];
        List<LineSpan> vertical = [];
        foreach (FramePath path in paths)
        {
            if (path.Closed || path.Points.Count != 2)
            {
                continue;
            }

            FramePoint first = path.Points[0];
            FramePoint second = path.Points[1];
            double deltaX = Abs(first.X - second.X);
            double deltaY = Abs(first.Y - second.Y);
            if (deltaY <= tolerance && deltaX > tolerance)
            {
                horizontal.Add(LineSpan.Horizontal(path.SourceId, first, second));
            }
            else if (deltaX <= tolerance && deltaY > tolerance)
            {
                vertical.Add(LineSpan.Vertical(path.SourceId, first, second));
            }
        }

        if (horizontal.Count < 2 || vertical.Count < 2)
        {
            return;
        }

        EndpointIndex verticalEnds = new(vertical, tolerance);
        EndpointIndex horizontalEnds = new(horizontal, tolerance);
        foreach (LineSpan baseSide in horizontal)
        {
            foreach (LineSpan leftSide in verticalEnds.Find(baseSide.First))
            {
                if (!leftSide.TryGetOtherEnd(baseSide.First, tolerance, out FramePoint oppositeLeft))
                {
                    continue;
                }

                foreach (LineSpan rightSide in verticalEnds.Find(baseSide.Second))
                {
                    if (rightSide.SourceId == leftSide.SourceId ||
                        !rightSide.TryGetOtherEnd(baseSide.Second, tolerance, out FramePoint oppositeRight))
                    {
                        continue;
                    }

                    foreach (LineSpan oppositeSide in horizontalEnds.Find(oppositeLeft))
                    {
                        if (oppositeSide.SourceId == baseSide.SourceId ||
                            !oppositeSide.HasEndNear(oppositeLeft, tolerance) ||
                            !oppositeSide.HasEndNear(oppositeRight, tolerance))
                        {
                            continue;
                        }

                        AddLineFrame(baseSide, leftSide, rightSide, oppositeSide, frames);
                    }
                }
            }
        }
    }

    private static void AddLineFrame(
        LineSpan first,
        LineSpan second,
        LineSpan third,
        LineSpan fourth,
        ICollection<DetectedFrame> frames)
    {
        FramePoint[] points =
        [
            first.First, first.Second,
            second.First, second.Second,
            third.First, third.Second,
            fourth.First, fourth.Second
        ];
        double minX = points.Min(point => point.X);
        double minY = points.Min(point => point.Y);
        double maxX = points.Max(point => point.X);
        double maxY = points.Max(point => point.Y);
        double width = maxX - minX;
        double height = maxY - minY;
        if (!FitsPrintableSheet(width, height))
        {
            return;
        }

        int[] sourceIds = [first.SourceId, second.SourceId, third.SourceId, fourth.SourceId];
        Array.Sort(sourceIds);
        if (frames.Any(frame => frame.SourceIds.SequenceEqual(sourceIds)))
        {
            return;
        }

        frames.Add(new DetectedFrame(minX, minY, maxX, maxY, sourceIds));
    }

    private sealed class LineSpan
    {
        private LineSpan(int sourceId, FramePoint first, FramePoint second)
        {
            SourceId = sourceId;
            First = first;
            Second = second;
        }

        internal int SourceId { get; }

        internal FramePoint First { get; }

        internal FramePoint Second { get; }

        internal static LineSpan Horizontal(int sourceId, FramePoint first, FramePoint second)
        {
            double y = (first.Y + second.Y) / 2;
            return first.X <= second.X
                ? new LineSpan(sourceId, new FramePoint(first.X, y), new FramePoint(second.X, y))
                : new LineSpan(sourceId, new FramePoint(second.X, y), new FramePoint(first.X, y));
        }

        internal static LineSpan Vertical(int sourceId, FramePoint first, FramePoint second)
        {
            double x = (first.X + second.X) / 2;
            return first.Y <= second.Y
                ? new LineSpan(sourceId, new FramePoint(x, first.Y), new FramePoint(x, second.Y))
                : new LineSpan(sourceId, new FramePoint(x, second.Y), new FramePoint(x, first.Y));
        }

        internal bool HasEndNear(FramePoint point, double tolerance)
        {
            return IsNear(First, point, tolerance) || IsNear(Second, point, tolerance);
        }

        internal bool TryGetOtherEnd(FramePoint point, double tolerance, out FramePoint other)
        {
            if (IsNear(First, point, tolerance))
            {
                other = Second;
                return true;
            }

            if (IsNear(Second, point, tolerance))
            {
                other = First;
                return true;
            }

            other = default;
            return false;
        }
    }

    private sealed class EndpointIndex
    {
        private readonly double _cellSize;
        private readonly Dictionary<GridKey, List<LineSpan>> _buckets = [];

        internal EndpointIndex(IEnumerable<LineSpan> spans, double tolerance)
        {
            _cellSize = tolerance > 0 ? tolerance : 0.001;
            foreach (LineSpan span in spans)
            {
                Add(span.First, span);
                Add(span.Second, span);
            }
        }

        internal IEnumerable<LineSpan> Find(FramePoint point)
        {
            GridKey center = KeyFor(point);
            HashSet<LineSpan> found = [];
            for (long offsetX = -1; offsetX <= 1; offsetX++)
            {
                for (long offsetY = -1; offsetY <= 1; offsetY++)
                {
                    GridKey key = new(center.X + offsetX, center.Y + offsetY);
                    if (!_buckets.TryGetValue(key, out List<LineSpan>? bucket))
                    {
                        continue;
                    }

                    foreach (LineSpan span in bucket)
                    {
                        _ = found.Add(span);
                    }
                }
            }

            return found;
        }

        private void Add(FramePoint point, LineSpan span)
        {
            GridKey key = KeyFor(point);
            if (!_buckets.TryGetValue(key, out List<LineSpan>? bucket))
            {
                bucket = [];
                _buckets.Add(key, bucket);
            }

            bucket.Add(span);
        }

        private GridKey KeyFor(FramePoint point)
        {
            return new GridKey((long)Floor(point.X / _cellSize), (long)Floor(point.Y / _cellSize));
        }
    }

    private readonly struct GridKey : IEquatable<GridKey>
    {
        internal GridKey(long x, long y)
        {
            X = x;
            Y = y;
        }

        internal long X { get; }

        internal long Y { get; }

        public bool Equals(GridKey other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object? obj)
        {
            return obj is GridKey other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }
    }

    private static bool IsNear(FramePoint first, FramePoint second, double tolerance)
    {
        return Abs(first.X - second.X) <= tolerance && Abs(first.Y - second.Y) <= tolerance;
    }
}
