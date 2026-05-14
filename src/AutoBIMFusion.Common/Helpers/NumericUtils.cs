using System.Globalization;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Числовые утилиты: форматирование, нормализация, сравнение.
/// </summary>
public static class NumericUtils
{
    /// <summary>
    ///     Форматирует значение double как F6 в инвариантной культуре.
    ///     Возвращает "n/a", если значение не является конечным (NaN, Infinity).
    /// </summary>
    public static string FormatF6(double value)
    {
        return double.IsFinite(value) ? value.ToString("F6", CultureInfo.InvariantCulture) : "n/a";
    }

    /// <summary>
    ///     Округляет значение до указанной точности (tolerance).
    ///     Эквивалент: Round(value / tolerance) * tolerance.
    /// </summary>
    public static double NormalizeToTolerance(double value, double tolerance = 0.0001)
    {
        return Round(value / tolerance) * tolerance;
    }
}
