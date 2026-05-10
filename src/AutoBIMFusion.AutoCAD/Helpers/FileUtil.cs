using Serilog.Core;

namespace AutoBIMFusion.AutoCAD.Helpers;

/// <summary>
/// Утилиты для работы с файлами и проверками.
/// </summary>
public static class FileUtil
{
    private const long MaxFileSizeBytes = 15L * 1024 * 1024;
    private static readonly WindowsNaturalComparer NaturalComparer = new();

    /// <summary>
    /// Возвращает отсортированный список DWG-файлов из директории.
    /// </summary>
    public static string[] GetFiles(string rootPath, string excludePrefix = "#", Logger? log = null)
    {
        EnumerationOptions opts = new()
        {
            MaxRecursionDepth = 3,
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            MatchCasing = MatchCasing.CaseInsensitive
        };

        List<string> files = [];

        foreach (string path in Directory.EnumerateFiles(rootPath, "*.dwg", opts))
        {
            string fileName = Path.GetFileName(path);
            if (fileName.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (new FileInfo(path).Length > MaxFileSizeBytes)
            {
                continue;
            }

            files.Add(path);
        }

        files.Sort((x, y) => NaturalComparer.Compare(Path.GetRelativePath(rootPath, x), Path.GetRelativePath(rootPath, y)));

        return [.. files];
    }

    /// <summary>
    /// Проверяет доступность файла и его ненулевой размер.
    /// Структурная валидация DWG выполняется позже в PrepareDatabaseForMerge,
    /// что исключает двойное открытие файла.
    /// </summary>
    public static bool TryValidateDwg(string path, out string warn)
    {
        warn = string.Empty;
        if (!File.Exists(path))
        {
            warn = "Файл не найден";
            return false;
        }

        try
        {
            if (new FileInfo(path).Length == 0)
            {
                warn = "Пустой файл";
                return false;
            }

            return true;
        }
        catch (System.Exception ex)
        {
            warn = ex.Message;
            return false;
        }
    }
}
