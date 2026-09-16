namespace AutoBIMFusion.QuickPdf.Frames;

/// <summary>
///     Порядок листов: слева направо внутри ряда, ряды сверху вниз.
///     Рамки попадают в один ряд, когда их полосы по вертикали пересекаются
///     не меньше чем наполовину меньшей рамки. Поэтому разновысокие рамки
///     на одной полке (общий низ, верх или центр) остаются одним рядом,
///     дрожание центров до половины высоты ряд не разрывает,
///     а рамки без общего участка по Y всегда идут в разные ряды.
/// </summary>
internal static class FrameSheetOrder
{
    // Доля высоты меньшей рамки, на которую полосы обязаны пересекаться.
    private const double RequiredOverlapFraction = 0.5;
    private const double FloatTolerance = 1e-9;

    internal static IReadOnlyList<DetectedFrame> Sort(IReadOnlyList<DetectedFrame> frames)
    {
        if (frames.Count <= 1)
        {
            return frames;
        }

        List<DetectedFrame> ordered = new(frames.Count);
        foreach (SheetRow row in GroupIntoRowsTopToBottom(frames))
        {
            ordered.AddRange(row.LeftToRight());
        }

        return ordered;
    }

    private static List<SheetRow> GroupIntoRowsTopToBottom(IReadOnlyList<DetectedFrame> frames)
    {
        DetectedFrame[] topToBottom =
        [
            .. frames
                .OrderByDescending(CenterY)
                .ThenBy(CenterX)
        ];

        List<SheetRow> rows = [];
        foreach (DetectedFrame frame in topToBottom)
        {
            SheetRow? row = FindRowFor(rows, frame);
            if (row is null)
            {
                rows.Add(new SheetRow(frame));
            }
            else
            {
                row.Add(frame);
            }
        }

        return rows;
    }

    private static SheetRow? FindRowFor(List<SheetRow> rows, DetectedFrame frame)
    {
        foreach (SheetRow row in rows)
        {
            if (BelongsToRow(row.Seed, frame))
            {
                return row;
            }
        }

        return null;
    }

    private static bool BelongsToRow(DetectedFrame seed, DetectedFrame candidate)
    {
        double overlap = VerticalOverlap(seed, candidate);
        if (overlap <= 0)
        {
            return false;
        }

        double shorter = Min(Height(seed), Height(candidate));
        return shorter > 0 && overlap + FloatTolerance >= RequiredOverlapFraction * shorter;
    }

    private static double VerticalOverlap(DetectedFrame first, DetectedFrame second)
    {
        return Min(first.MaxY, second.MaxY) - Max(first.MinY, second.MinY);
    }

    private static double CenterX(DetectedFrame frame) => (frame.MinX + frame.MaxX) / 2;

    private static double CenterY(DetectedFrame frame) => (frame.MinY + frame.MaxY) / 2;

    private static double Height(DetectedFrame frame) => frame.MaxY - frame.MinY;

    /// <summary>
    ///     Один ряд печати. Первая рамка задаёт полосу ряда, остальные
    ///     к ней привязываются. Полоса не расширяется: иначе высокая рамка,
    ///     доставшая до соседнего ряда, склеила бы оба ряда в один.
    /// </summary>
    private sealed class SheetRow
    {
        private readonly List<DetectedFrame> _members = [];

        internal SheetRow(DetectedFrame seed)
        {
            Seed = seed;
            _members.Add(seed);
        }

        internal DetectedFrame Seed { get; }

        internal void Add(DetectedFrame frame) => _members.Add(frame);

        internal IEnumerable<DetectedFrame> LeftToRight()
        {
            // OrderBy стабилен: равные центры X сохраняют порядок сверху вниз
            // (при полном совпадении центров — порядок обнаружения).
            return _members.OrderBy(CenterX);
        }
    }
}
