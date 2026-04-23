

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Результат слияния одного DWG-файла.
/// </summary>
public sealed record MergeResult(
    bool Success,
    string FileName,
    string? BlockName = null,
    bool IsSkipped = false,
    string? Message = null)
{
    private const int DefaultMaxLength = 125;

    private static string Short(string? message, string fallback, int maxLength = DefaultMaxLength)
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

        return span.Length <= maxLength
            ? span.ToString()
            : span[..maxLength].ToString();
    }

    public static MergeResult Ok(string fileName, string? blockName = null)
    {
        return new(true, fileName, blockName);
    }

    public static MergeResult Fail(string fileName, string? message, string fallback = "Ошибка")
    {
        string msg = Short(message, fallback);
        return new(false, fileName, Message: msg);
    }

    public static MergeResult Warn(string fileName, string? message, string fallback = "Пропущено")
    {
        string msg = Short(message, fallback);
        return new(false, fileName, IsSkipped: true, Message: msg);
    }
}
