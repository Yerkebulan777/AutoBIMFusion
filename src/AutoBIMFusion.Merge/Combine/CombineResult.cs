using AutoBIMFusion.AutoCAD.Helpers;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
/// Результат слияния одного DWG-файла.
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
        return new(true, fileName);
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


