using System.Globalization;

namespace AutoBIMFusion.QuickPdf;

/// <summary>
///     Параметры печати QuickPDF: масштаб, цветность и начальный номер листа.
///     Выбираются пользователем в одном окне перед печатью.
/// </summary>
public readonly struct QuickPdfOptions
{
    /// <summary>
    ///     Все стандартные уменьшения AutoCAD (1:1–1:100) плюс 1:200 и 1:500.
    ///     1:200 и 1:500 отсутствуют в <c>StdScaleType</c>, поэтому печатаются
    ///     произвольным масштабом 1:N.
    /// </summary>
    public static readonly int[] SupportedScales = [1, 2, 4, 5, 8, 10, 16, 20, 30, 40, 50, 100, 200, 500];

    public static readonly QuickPdfOptions Default = new(DefaultScaleDenominator, false);

    public const int DefaultScaleDenominator = 100;

    public const int MinStartSheet = 0;

    public const int MaxStartSheet = 999;

    public QuickPdfOptions(int scaleDenominator, bool monochrome, int startSheet = MinStartSheet)
    {
        if (Array.IndexOf(SupportedScales, scaleDenominator) < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(scaleDenominator),
                scaleDenominator,
                "Поддерживаются масштабы 1:1–1:100, 1:200 и 1:500.");
        }

        if (startSheet < MinStartSheet || startSheet > MaxStartSheet)
        {
            throw new ArgumentOutOfRangeException(
                nameof(startSheet),
                startSheet,
                "Начальный номер листа: от 000 до 999.");
        }

        ScaleDenominator = scaleDenominator;
        Monochrome = monochrome;
        StartSheet = startSheet;
    }

    public int ScaleDenominator { get; }

    public bool Monochrome { get; }

    /// <summary>
    ///     Номер, с которого начинается нумерация листов (формат 000).
    /// </summary>
    public int StartSheet { get; }

    public string StartSheetLabel => StartSheet.ToString("D3", CultureInfo.InvariantCulture);

    public double ScaleRatio => 1.0 / ScaleDenominator;

    public string ScaleLabel => "1:" + ScaleDenominator.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    ///     «Стандартный масштаб 1:100» либо «Произвольный масштаб 1:200»
    ///     (1:200/1:500 отсутствуют в <c>StdScaleType</c>).
    /// </summary>
    public string ScaleModeLabel =>
        (StdScaleOrNull is null ? "Произвольный масштаб " : "Стандартный масштаб ") + ScaleLabel;

    public string ColorLabel => Monochrome ? "Ч/Б" : "Цветной";

    /// <summary>
    ///     Стандартный масштаб AutoCAD или <c>null</c> для 1:200/1:500 (произвольный масштаб).
    /// </summary>
    internal StdScaleType? StdScaleOrNull => ScaleDenominator switch
    {
        1 => StdScaleType.StdScale1To1,
        2 => StdScaleType.StdScale1To2,
        4 => StdScaleType.StdScale1To4,
        5 => StdScaleType.StdScale1To5,
        8 => StdScaleType.StdScale1To8,
        10 => StdScaleType.StdScale1To10,
        16 => StdScaleType.StdScale1To16,
        20 => StdScaleType.StdScale1To20,
        30 => StdScaleType.StdScale1To30,
        40 => StdScaleType.StdScale1To40,
        50 => StdScaleType.StdScale1To50,
        100 => StdScaleType.StdScale1To100,
        _ => null
    };
}
