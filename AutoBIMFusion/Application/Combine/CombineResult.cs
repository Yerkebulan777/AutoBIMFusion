using AutoBIMFusion.Application.Utils;

namespace AutoBIMFusion.Application.Combine;

/// <summary>
/// Результат слияния одного DWG-файла.
/// </summary>
internal sealed record CombineResult(
    bool Success,
    string FileName,
    string? BlockName = null,
    bool IsSkipped = false,
    string? Message = null)
{
    private const int DefaultMaxLength = 125;

    public static CombineResult Ok(string fileName, string? blockName = null)
    {
        return new(true, fileName, blockName);
    }

    public static CombineResult Fail(string fileName, string? message, string fallback = "Ошибка")
    {
        return new(false, fileName, Message: StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }

    public static CombineResult Warn(string fileName, string? message, string fallback = "Пропущено")
    {
        return new(false, fileName, IsSkipped: true, Message: StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }
}
