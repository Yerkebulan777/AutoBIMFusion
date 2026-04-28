using AutoBIMFusion.Application.Utils;

namespace AutoBIMFusion.Application.Merge.Models;

/// <summary>
/// Результат слияния одного DWG-файла.
/// </summary>
internal sealed record MergeResult(
    bool Success,
    string FileName,
    string? BlockName = null,
    bool IsSkipped = false,
    string? Message = null)
{
    private const int DefaultMaxLength = 125;

    public static MergeResult Ok(string fileName, string? blockName = null)
    {
        return new(true, fileName, blockName);
    }

    public static MergeResult Fail(string fileName, string? message, string fallback = "Ошибка")
    {
        return new(false, fileName, Message: StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }

    public static MergeResult Warn(string fileName, string? message, string fallback = "Пропущено")
    {
        return new(false, fileName, IsSkipped: true, Message: StringUtils.Truncate(message, fallback, DefaultMaxLength));
    }
}
