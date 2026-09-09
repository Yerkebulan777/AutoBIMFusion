namespace AutoBIMFusion.QuickPdf.Media;

/// <summary>
///     Проверка, что драйвер не подменил размер, поля и масштаб 1:100.
/// </summary>
public static class ExactPaper
{
    public const double SizeToleranceMm = 0.001;
    public const double MarginToleranceMm = 0.001;
    public const double ScaleRatioTolerance = 1e-9;
    public const double OriginTolerance = 1e-8;
    public const double WantedScale = 0.01;

    public static void Validate(
        double wantedWidth,
        double wantedHeight,
        double actualWidth,
        double actualHeight,
        IReadOnlyList<double> margins,
        double wantedScale,
        double actualScale)
    {
        if (!IsFinitePositive(wantedWidth) || !IsFinitePositive(wantedHeight) ||
            !IsFinitePositive(actualWidth) || !IsFinitePositive(actualHeight) ||
            !IsFinitePositive(wantedScale) || !IsFinitePositive(actualScale))
        {
            throw new QuickPdfException("Некорректный размер бумаги или масштаб.");
        }

        if (Abs(actualWidth - wantedWidth) > SizeToleranceMm || Abs(actualHeight - wantedHeight) > SizeToleranceMm)
        {
            throw new QuickPdfException(
                $"Драйвер подменил формат {actualWidth:F3} x {actualHeight:F3} мм; запрошено {wantedWidth:F3} x {wantedHeight:F3} мм. Печать отменена.");
        }

        if (margins.Count != 4 || margins.Any(margin => !IsFinite(margin) || Abs(margin) > MarginToleranceMm))
        {
            throw new QuickPdfException("Драйвер не принял нулевые поля бумаги. Печать отменена.");
        }

        if (Abs(actualScale / wantedScale - 1.0) > ScaleRatioTolerance)
        {
            throw new QuickPdfException("Драйвер изменил физический масштаб 1:100. Печать отменена.");
        }
    }

    private static bool IsFinitePositive(double value)
    {
        return IsFinite(value) && value > 0;
    }

    private static bool IsFinite(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value);
    }
}
