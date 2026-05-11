namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты для работы со строками.
/// </summary>
public static class StringUtils
{
    /// <summary>
    ///     Возвращает первую непустую строку <paramref name="message" />, обрезанную до <paramref name="maxLength" />
    ///     символов.
    ///     Если строка пустая или состоит только из пробелов — возвращает <paramref name="fallback" />.
    ///     Многострочный текст обрезается по первой строке.
    /// </summary>
    public static string Truncate(string? message, string fallback, int maxLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fallback);

        if (string.IsNullOrWhiteSpace(message)) return fallback;

        var span = message.AsSpan().Trim();
        var lineBreakIdx = span.IndexOfAny('\r', '\n');

        if (lineBreakIdx >= 0)
        {
            span = span[..lineBreakIdx].TrimEnd();

            if (span.Length == 0) return fallback;
        }

        return span.Length <= maxLength ? span.ToString() : span[..maxLength].ToString();
    }
}
