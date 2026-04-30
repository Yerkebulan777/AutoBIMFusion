using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Utils;

/// <summary>
/// Отвечает за поиск и сортировку DWG-файлов в директории.
/// </summary>
internal static class FileEnumerator
{
    private const long MaxFileSizeBytes = 15L * 1024 * 1024;

    private static readonly WindowsNaturalComparer NaturalComparer = new();

    /// <summary>
    /// Возвращает отсортированный список DWG-файлов из директории и поддиректорий.
    /// Исключает файлы с заданным префиксом и файлы размером больше 15 МБ.
    /// </summary>
    internal static string[] GetFiles(string rootPath, string excludePrefix = "#", AILog? log = null)
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

            FileInfo fileInfo = new(path);
            if (fileInfo.Length > MaxFileSizeBytes)
            {
                continue;
            }

            files.Add(path);
        }

        files.Sort((x, y) => NaturalComparer.Compare(Path.GetRelativePath(rootPath, x), Path.GetRelativePath(rootPath, y)));

        log?.Info($"Найдено DWG: {files.Count}");
        return [.. files];
    }
}
