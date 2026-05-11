using AutoBIMFusion.Common.Helpers;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Результат слияния одного DWG-файла.
/// </summary>
public sealed record CombineResult(
    bool Success,
    string FileName,
    bool IsSkipped = false,
    string? Message = null)
{
    private const int DefaultMaxLength = 125;

    public static CombineResult Ok(string fileName)
    {
        return new CombineResult(true, fileName);
    }

    public static CombineResult Fail(string fileName, string? message, string fallback = "Ошибка")
    {
        return new CombineResult(false, fileName, Message: StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }

    public static CombineResult Warn(string fileName, string? message, string fallback = "Пропущено")
    {
        return new CombineResult(false, fileName, true, StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }
}
