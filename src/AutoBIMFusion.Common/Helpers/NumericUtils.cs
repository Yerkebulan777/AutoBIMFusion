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

    /// <summary>
    ///     Ограничивает значение в диапазоне [minValue, maxValue].
    /// </summary>
    public static double Clamp(this double value, double minValue, double maxValue)
    {
        return Max(Min(value, maxValue), minValue);
    }

    /// <summary>
    ///     Округляет значение вниз до ближайшего кратного.
    /// </summary>
    public static double RoundToNearestMultiple(this double value, int multiple)
    {
        return Floor(value / multiple) * multiple;
    }

    /// <summary>
    ///     Проверяет, находится ли значение в диапазоне [min, max] включительно.
    /// </summary>
    public static bool IsBetween(this double value, double min, double max)
    {
        return value >= min && value <= max;
    }

    /// <summary>
    ///     Вычисляет промежуточное значение по проценту между a и b.
    /// </summary>
    public static double IntermediatePercentage(this double a, double b, double percentage)
    {
        return a + percentage * (b - a) / 100;
    }
}
