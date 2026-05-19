using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Helpers;

/// <summary>
///  Утилиты для работы с файлами и проверками.
/// </summary>
public static class FileUtil
{
    private const long MaxFileSizeBytes = 15L * 1024 * 1024;
    private static readonly WindowsNaturalComparer NaturalComparer = new();

    /// <summary>
    /// Возвращает отсортированный список DWG-файлов из директории.
    /// </summary>
    public static string[] GetFiles(string rootPath, string excludePrefix = "#")
    {
        EnumerationOptions opts = new()
        {
            MaxRecursionDepth = 1,   // Ограничиваем глубину рекурсии, так как RecurseSubdirectories = false
            IgnoreInaccessible = true,   // Игнорируем папки, к которым нет доступа (например, из-за прав доступа)
            RecurseSubdirectories = false,   // Включаем поиск в поддиректориях
            MatchCasing = MatchCasing.PlatformDefault   // Игнорируем регистр при фильтрации по шаблону "*.dwg"
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

    /// <summary>
    ///     Форматирует размер файла из байтов в читаемую строку (KB, MB, GB и т.д.).
    /// </summary>
    public static string FormatFileSizeFromByte(long ovalue, int odecimalPlaces = 1)
    {
        string[] SizeSuffixes =
            { "bytes", "KB", "MB", "GB", "TB", "PB", "EB", "ZB", "YB" };

        string SizeSuffix(long value, int decimalPlaces = 1)
        {
            if (value < 0)
            {
                return "-" + SizeSuffix(-value, decimalPlaces);
            }

            int i = 0;
            decimal dValue = value;
            while (Round(dValue, decimalPlaces) >= 1000)
            {
                dValue /= 1024;
                i++;
            }

            return string.Format("{0:n" + decimalPlaces + "} {1}", dValue, SizeSuffixes[i]);
        }

        return SizeSuffix(ovalue, odecimalPlaces);
    }

    /// <summary>
    ///     Проверяет, заблокирован ли файл для записи или доступен только для чтения.
    /// </summary>
    public static bool IsFileLockedOrReadOnly(string path)
    {
        return IsFileLockedOrReadOnly(new FileInfo(path));
    }

    /// <summary>
    ///     Проверяет, заблокирован ли файл для записи или доступен только для чтения.
    /// </summary>
    public static bool IsFileLockedOrReadOnly(FileInfo fi)
    {
        if (!fi.Exists)
        {
            return false;
        }

        FileStream fs = null;
        try
        {
            fs = fi.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex)
        {
            if (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }
            throw;
        }
        finally
        {
            fs?.Close();
        }

        return false;
    }


    /// <summary>
    ///     Разрешает путь к файлу изображения через несколько стратегий:
    ///     1. Абсолютный путь на текущей машине
    ///     2. Подстановка текущего пользователя (cross-machine C:\Users\OtherUser\...)
    ///     3. Поиск по имени файла в searchDir
    ///     4. AutoCAD FindFile
    /// </summary>
    public static bool TryResolveImagePath(Database db, string path, string? searchDir, out string resolvedPath, out Exception? resolveError)
    {
        resolvedPath = string.Empty;
        resolveError = null;

        // Абсолютный путь на текущей машине
        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        // Подстановка текущего пользователя (cross-machine C:\Users\OtherUser\...)
        if (Path.IsPathRooted(path))
        {
            string userProfileParent = Path.GetDirectoryName(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? string.Empty;
            if (!string.IsNullOrEmpty(userProfileParent) &&
                path.StartsWith(userProfileParent, StringComparison.OrdinalIgnoreCase))
            {
                string afterUsersDir = path[(userProfileParent.Length + 1)..];
                int slashIdx = afterUsersDir.IndexOf(Path.DirectorySeparatorChar);
                if (slashIdx > 0)
                {
                    string relativePart = afterUsersDir[(slashIdx + 1)..];
                    string candidate = Path.Combine(
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

        // Только имя файла в папке целевого DWG
        if (!string.IsNullOrEmpty(searchDir))
        {
            string candidate = Path.Combine(searchDir, Path.GetFileName(path));
            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        // AutoCAD FindFile
        try
        {
            string foundPath = HostApplicationServices.Current.FindFile(path, db, FindFileHint.EmbeddedImageFile);

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
