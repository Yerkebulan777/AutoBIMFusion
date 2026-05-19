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

        if (string.IsNullOrWhiteSpace(message))
        {
            return fallback;
        }

        ReadOnlySpan<char> span = message.AsSpan().Trim();
        int lineBreakIdx = span.IndexOfAny('\r', '\n');

        if (lineBreakIdx >= 0)
        {
            span = span[..lineBreakIdx].TrimEnd();

            if (span.Length == 0)
            {
                return fallback;
            }
        }

        return span.Length <= maxLength ? span.ToString() : span[..maxLength].ToString();
    }

    /// <summary>
    ///     Экранирует спецсимволы в строке для безопасного включения в закавыченный контекст
    ///     (логирование, сериализация).
    ///     Обратный слэш → \\, двойная кавычка → \", CR → \r, LF → \n.
    /// </summary>
    public static string EscapeForQuotedContext(string? value)
    {
        return (value ?? string.Empty)
            .Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("\"", "\\\"", StringComparison.Ordinal)
            .Replace("\r", "\\r", StringComparison.Ordinal)
            .Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
