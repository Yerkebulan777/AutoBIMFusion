using System.Globalization;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Числовые утилиты: форматирование, нормализация, сравнение, округление.
/// </summary>
public static class NumericUtils
{
    /// <summary>
    ///     Форматирует значение double как F6 в инвариантной культуре.
    ///     Возвращает "n/a", если значение не является конечным (NaN, Infinity).
    /// </summary>
    public static string FormatF6(double value)
    {
        return !double.IsNaN(value) && !double.IsInfinity(value)
            ? value.ToString("F6", CultureInfo.InvariantCulture)
            : "n/a";
    }

    /// <summary>
    ///     Вычисляет промежуточное значение по проценту между a и b.
    /// </summary>
    public static double IntermediatePercentage(this double a, double b, double percentage)
    {
        return a + (percentage * (b - a) / 100);
    }
}
