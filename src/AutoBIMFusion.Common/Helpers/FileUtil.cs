using Serilog.Core;
using Exception = System.Exception;

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
    ///     Создаёт корневую папку назначения, очищает временную папку и удаляет существующий ZIP-файл.
    /// </summary>
    public static void PrepareOutputFolders(string destinationRoot, string tempFolder, string zipFilePath)
    {
        _ = Directory.CreateDirectory(destinationRoot);

        if (Directory.Exists(tempFolder))
        {
            Directory.Delete(tempFolder, true);
        }

        _ = Directory.CreateDirectory(tempFolder);

        if (File.Exists(zipFilePath))
        {
            File.Delete(zipFilePath);
        }
    }

    /// <summary>
    ///     Безопасно удаляет временную директорию с обработкой IOException и UnauthorizedAccessException.
    /// </summary>
    public static void TryDeleteDirectory(string tempFolder, Logger log)
    {
        try
        {
            if (Directory.Exists(tempFolder))
            {
                Directory.Delete(tempFolder, true);
            }
        }
        catch (IOException ex)
        {
            log.Warning(ex, $"Не удалось удалить временную папку: {tempFolder}");
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Warning(ex, $"Нет прав на удаление временной папки: {tempFolder}");
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
        var sourceFullPath = Path.GetFullPath(sourcePath);
        var sourceDir = Path.GetDirectoryName(sourceFullPath) ?? string.Empty;
        if (string.Equals(sourceDir, Path.GetFullPath(targetDir), StringComparison.OrdinalIgnoreCase))
            return (sourceFullPath, Path.GetFileName(sourceFullPath));

        var fileName = Path.GetFileName(sourcePath);
        var destinationPath = Path.Combine(targetDir, fileName);

        var counter = 1;
        while (reservedDestinationPaths.Contains(destinationPath) || File.Exists(destinationPath))
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            var ext = Path.GetExtension(fileName);
            var candidateName = $"{name}_{counter}{ext}";
            destinationPath = Path.Combine(targetDir, candidateName);
            counter++;
        }

        return (destinationPath, Path.GetFileName(destinationPath));
    }

    /// <summary>
    ///     Разрешает путь к файлу изображения через несколько стратегий:
    ///     1. Абсолютный путь на текущей машине
    ///     2. Подстановка текущего пользователя (cross-machine C:\Users\OtherUser\...)
    ///     3. Поиск по имени файла в searchDir
    ///     4. AutoCAD FindFile
    /// </summary>
    public static bool TryResolveImagePath(
        Database db, string path, string? searchDir,
        out string resolvedPath, out Exception? resolveError)
    {
        resolvedPath = string.Empty;
        resolveError = null;

        // 1. Абсолютный путь на текущей машине
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        // 2. Подстановка текущего пользователя (cross-machine C:\Users\OtherUser\...)
        if (Path.IsPathRooted(path))
        {
            var userProfileParent = Path.GetDirectoryName(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? string.Empty;
            if (!string.IsNullOrEmpty(userProfileParent) &&
                path.StartsWith(userProfileParent, StringComparison.OrdinalIgnoreCase))
            {
                var afterUsersDir = path[(userProfileParent.Length + 1)..];
                var slashIdx = afterUsersDir.IndexOf(Path.DirectorySeparatorChar);
                if (slashIdx > 0)
                {
                    var relativePart = afterUsersDir[(slashIdx + 1)..];
                    var candidate = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        relativePart);
                    if (File.Exists(candidate))
                    {
                        resolvedPath = candidate;
                        return true;
                    }
                }
            }
        }

        // 3. Только имя файла в папке целевого DWG
        if (!string.IsNullOrEmpty(searchDir))
        {
            var candidate = Path.Combine(searchDir, Path.GetFileName(path));
            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        // 4. AutoCAD FindFile
        try
        {
            var foundPath = HostApplicationServices.Current.FindFile(path, db, FindFileHint.EmbeddedImageFile);
            if (!string.IsNullOrEmpty(foundPath) && File.Exists(foundPath))
            {
                resolvedPath = foundPath;
                return true;
            }

            return false;
        }
        catch (Exception ex)
        {
            resolveError = ex;
            return false;
        }
    }
}
