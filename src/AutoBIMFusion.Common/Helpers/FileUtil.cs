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
            log.Warning(ex, "Не удалось удалить временную папку: {TempFolder}", tempFolder);
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Warning(ex, "Нет прав на удаление временной папки: {TempFolder}", tempFolder);
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

        try
        {
            using FileStream fs = fi.Open(FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }
        catch (Exception ex)
        {
            if (ex is IOException or UnauthorizedAccessException)
            {
                return true;
            }

            throw;
        }

        return false;
    }


    /// <summary>
    ///     Разрешает путь к файлу изображения через несколько стратегий:
    ///     1. Абсолютный путь на текущей машине
    ///     2. Подстановка текущего пользователя (cross-machine C:\Users\OtherUser\...)
    ///     3. Поиск по имени файла в searchDir, папке DWG и одном уровне подпапок
    ///     4. AutoCAD FindFile по имени файла (не по мёртвому абсолютному пути)
    /// </summary>
    public static bool TryResolveImagePath(Database db, string path, string? searchDir, out string resolvedPath,
        out Exception? resolveError, params string?[] additionalSearchDirs)
    {
        resolvedPath = string.Empty;
        resolveError = null;

        List<string?> searchDirs = [searchDir, TryGetDatabaseDirectory(db)];
        if (additionalSearchDirs is { Length: > 0 })
        {
            searchDirs.AddRange(additionalSearchDirs);
        }

        if (TryResolveImagePathOnDisk(path, searchDirs, out resolvedPath))
        {
            return true;
        }

        string fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        // FindFile по полному отсутствующему пути бросает eFilerError и не ищет в support path.
        // Сначала ищем по имени файла — так AutoCAD смотрит папку чертежа, project paths и embedded images.
        try
        {
            if (TryFindFileWithAcad(db, fileName, FindFileHint.EmbeddedImageFile, out resolvedPath)
                || TryFindFileWithAcad(db, fileName, FindFileHint.Default, out resolvedPath))
            {
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

    /// <summary>
    ///     Ищет файл изображения на диске без AutoCAD FindFile.
    ///     Нужен, когда RasterImageDef указывает на уже удалённый %TEMP%\RBF-* кэш.
    /// </summary>
    internal static bool TryResolveImagePathOnDisk(string path, IEnumerable<string?> searchDirs,
        out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        if (TryRemapUserProfilePath(path, out resolvedPath))
        {
            return true;
        }

        string fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return false;
        }

        HashSet<string> seenDirs = new(StringComparer.OrdinalIgnoreCase);
        foreach (string? searchDir in searchDirs)
        {
            if (string.IsNullOrWhiteSpace(searchDir))
            {
                continue;
            }

            string fullDir;
            try
            {
                fullDir = Path.GetFullPath(searchDir);
            }
            catch (Exception)
            {
                continue;
            }

            if (!seenDirs.Add(fullDir) || !Directory.Exists(fullDir))
            {
                continue;
            }

            if (TryFindFileByName(fullDir, fileName, out resolvedPath))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryRemapUserProfilePath(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (!Path.IsPathRooted(path))
        {
            return false;
        }

        string userProfileParent = Path.GetDirectoryName(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? string.Empty;
        if (string.IsNullOrEmpty(userProfileParent)
            || !path.StartsWith(userProfileParent, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string afterUsersDir = path[(userProfileParent.Length + 1)..];
        int slashIdx = afterUsersDir.IndexOf(Path.DirectorySeparatorChar);
        if (slashIdx <= 0)
        {
            return false;
        }

        string relativePart = afterUsersDir[(slashIdx + 1)..];
        string candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            relativePart);
        if (!File.Exists(candidate))
        {
            return false;
        }

        resolvedPath = candidate;
        return true;
    }

    private static bool TryFindFileByName(string searchDir, string fileName, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        string candidate = Path.Combine(searchDir, fileName);
        if (File.Exists(candidate))
        {
            resolvedPath = candidate;
            return true;
        }

        try
        {
            foreach (string subDir in Directory.EnumerateDirectories(searchDir))
            {
                candidate = Path.Combine(subDir, fileName);
                if (File.Exists(candidate))
                {
                    resolvedPath = candidate;
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }

        return false;
    }

    private static string? TryGetDatabaseDirectory(Database db)
    {
        try
        {
            string? fileName = db.Filename;
            return string.IsNullOrWhiteSpace(fileName) ? null : Path.GetDirectoryName(fileName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryFindFileWithAcad(Database db, string fileSpec, FindFileHint hint, out string foundPath)
    {
        foundPath = string.Empty;
        try
        {
            string result = HostApplicationServices.Current.FindFile(fileSpec, db, hint);
            if (string.IsNullOrEmpty(result) || !File.Exists(result))
            {
                return false;
            }

            foundPath = result;
            return true;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
            when (ex.ErrorStatus is Autodesk.AutoCAD.Runtime.ErrorStatus.FilerError
                or Autodesk.AutoCAD.Runtime.ErrorStatus.FileNotFound
                or Autodesk.AutoCAD.Runtime.ErrorStatus.FileAccessErr)
        {
            return false;
        }
    }
}
