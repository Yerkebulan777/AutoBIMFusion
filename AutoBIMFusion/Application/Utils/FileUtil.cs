using Serilog.Core;

namespace AutoBIMFusion.Application.Utils;

/// <summary>
/// Утилиты для работы с файлами и проверками.
/// </summary>
internal static class FileUtil
{
    private const long MaxFileSizeBytes = 15L * 1024 * 1024;
    private static readonly WindowsNaturalComparer NaturalComparer = new();

    /// <summary>
    /// Возвращает отсортированный список DWG-файлов из директории.
    /// </summary>
    internal static string[] GetFiles(string rootPath, string excludePrefix = "#", Logger? log = null)
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
            if (fileName.StartsWith(excludePrefix, StringComparison.OrdinalIgnoreCase)) continue;

            if (new FileInfo(path).Length > MaxFileSizeBytes) continue;

            files.Add(path);
        }

        files.Sort((x, y) => NaturalComparer.Compare(Path.GetRelativePath(rootPath, x), Path.GetRelativePath(rootPath, y)));

        log?.Information($"Найдено DWG: {files.Count}");
        return [.. files];
    }

    /// <summary>
    /// Проверяет доступность и структуру DWG.
    /// </summary>
    internal static bool TryValidateDwg(string path, out string warn)
    {
        warn = string.Empty;
        if (!File.Exists(path))
        {
            warn = "Файл не найден";
            return false;
        }

        try
        {
            using (FileStream fs = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            {
                if (fs.Length == 0)
                {
                    warn = "Пустой файл";
                    return false;
                }
            }

            using Database db = new(false, true);
            db.ReadDwgFile(path, FileOpenMode.OpenForReadAndReadShare, true, string.Empty);
            db.CloseInput(true);
            return true;
        }
        catch (System.Exception ex)
        {
            warn = ex.Message;
            return false;
        }
    }
}
