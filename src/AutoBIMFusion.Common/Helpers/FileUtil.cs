namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///     Утилиты для работы с файлами и проверками.
/// </summary>
public static class FileUtil
{
    private const long MaxFileSizeBytes = 15L * 1024 * 1024;
    private static readonly WindowsNaturalComparer NaturalComparer = new();

    /// <summary>
    ///     Возвращает отсортированный список DWG-файлов из директории.
    /// </summary>
    public static string[] GetFiles(string rootPath, string excludePrefix = "#")
    {
        EnumerationOptions opts = new()
        {
            MaxRecursionDepth = 1, // Ограничиваем глубину рекурсии, так как RecurseSubdirectories = false
            IgnoreInaccessible = true, // Игнорируем папки, к которым нет доступа (например, из-за прав доступа)
            RecurseSubdirectories = false, // Включаем поиск в поддиректориях
            MatchCasing = MatchCasing.PlatformDefault // Игнорируем регистр при фильтрации по шаблону "*.dwg"
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

        files.Sort((x, y) =>
            NaturalComparer.Compare(Path.GetRelativePath(rootPath, x), Path.GetRelativePath(rootPath, y)));

        return [.. files];
    }

    /// <summary>
    ///     Проверяет доступность файла и его ненулевой размер.
    ///     Структурная валидация DWG выполняется позже в PrepareDatabaseForMerge,
    ///     что исключает двойное открытие файла.
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
        catch (Exception ex)
        {
            warn = ex.Message;
            return false;
        }
    }

    /// <summary>
    ///     Генерирует уникальный путь файла назначения, добавляя суффиксы _1, _2 и т.д.
    ///     при конфликте имён. Если исходный файл уже находится в целевой папке, возвращает его путь как есть.
    /// </summary>
    public static (string DestinationPath, string FileName) BuildUniqueDestination(
        string targetDir,
        string sourcePath,
        HashSet<string> reservedDestinationPaths)
    {
        string sourceFullPath = Path.GetFullPath(sourcePath);
        string sourceDir = Path.GetDirectoryName(sourceFullPath) ?? string.Empty;
        if (string.Equals(sourceDir, Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
        {
            return (sourceFullPath, Path.GetFileName(sourceFullPath));
        }

        string fileName = Path.GetFileName(sourcePath);
        string destinationPath = Path.Combine(targetDir, fileName);

        int counter = 1;
        while (reservedDestinationPaths.Contains(destinationPath) || File.Exists(destinationPath))
        {
            string name = Path.GetFileNameWithoutExtension(fileName);
            string ext = Path.GetExtension(fileName);
            string candidateName = $"{name}_{counter}{ext}";
            destinationPath = Path.Combine(targetDir, candidateName);
            counter++;
        }

        return (destinationPath, Path.GetFileName(destinationPath));
    }
}
