using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Копирует файлы растровых изображений в папку с целевым DWG
///     и обновляет пути RasterImageDef на относительные.
/// </summary>
public static class RasterImagePathFixer
{
    public static void CopyImagesToTargetFolder(Database db, string targetFilePath, Logger log)
    {
        var targetDir = Path.GetDirectoryName(targetFilePath);
        if (string.IsNullOrEmpty(targetDir))
        {
            log.Warning("RasterImagePathFixer: не удалось определить папку целевого файла");
            return;
        }

        Directory.CreateDirectory(targetDir);

        Dictionary<string, string> copiedBySourcePath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedDestinationPaths = new(StringComparer.OrdinalIgnoreCase);

        using var trx = db.TransactionManager.StartTransaction();
        var dictId = RasterImageDef.GetImageDictionary(db);

        if (dictId.IsNull)
        {
            trx.Commit();
            return;
        }

        var dict = (DBDictionary)trx.GetObject(dictId, OpenMode.ForRead);

        foreach (var entry in dict)
            try
            {
                if (trx.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def) continue;

                var path = def.SourceFileName;
                if (string.IsNullOrWhiteSpace(path))
                {
                    log.Warning($"RasterImageDef '{entry.Key}': путь не задан");
                    continue;
                }

                if (!TryResolveImagePath(db, path, targetDir, out var resolvedPath, out var resolveError))
                {
                    if (resolveError is not null)
                        log.Warning(resolveError, $"RasterImageDef '{entry.Key}': ошибка разрешения пути: {path}");
                    else
                        log.Warning($"RasterImageDef '{entry.Key}': файл не найден: {path}");

                    continue;
                }

                if (copiedBySourcePath.TryGetValue(resolvedPath, out var existingRelativePath)
                    && !string.IsNullOrEmpty(existingRelativePath))
                {
                    def.SourceFileName = existingRelativePath;
                    def.Load();
                    continue;
                }

                var (uniqueDestPath, uniqueFileName) =
                    BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

                if (!string.Equals(Path.GetFullPath(resolvedPath), Path.GetFullPath(uniqueDestPath),
                        StringComparison.OrdinalIgnoreCase)) File.Copy(resolvedPath, uniqueDestPath, true);

                _ = reservedDestinationPaths.Add(uniqueDestPath);
                copiedBySourcePath[resolvedPath] = uniqueFileName;
                def.SourceFileName = uniqueFileName; // относительный путь к папке DWG
                def.Load(); // Правило 2: загружаем определение после смены пути
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"RasterImageDef '{entry.Key}': не удалось обработать изображение");
            }

        trx.Commit();
    }

    private static (string DestinationPath, string FileName) BuildUniqueDestination(
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

    private static bool TryResolveImagePath(
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
