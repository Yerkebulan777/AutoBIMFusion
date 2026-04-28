using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Копирует файлы растровых изображений в папку с целевым DWG
/// и обновляет пути RasterImageDef на относительные.
/// </summary>
internal static class RasterImagePathFixer
{
    public static void CopyImagesToTargetFolder(Database db, string targetFilePath, OperationLogger log)
    {
        string? targetDir = Path.GetDirectoryName(targetFilePath);
        if (string.IsNullOrEmpty(targetDir))
        {
            log.Warn("RasterImagePathFixer: не удалось определить папку целевого файла");
            return;
        }

        Dictionary<string, string> copiedBySourcePath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedDestinationPaths = new(StringComparer.OrdinalIgnoreCase);

        using Transaction tr = db.TransactionManager.StartTransaction();
        ObjectId dictId = RasterImageDef.GetImageDictionary(db);

        if (dictId.IsNull)
        {
            tr.Commit();
            return;
        }

        DBDictionary dict = (DBDictionary)tr.GetObject(dictId, OpenMode.ForRead);
        int fixedCount = 0;

        foreach (DBDictionaryEntry entry in dict)
        {
            try
            {
                if (tr.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def)
                {
                    continue;
                }

                string? path = def.SourceFileName;
                if (string.IsNullOrWhiteSpace(path))
                {
                    log.Warn($"RasterImageDef '{entry.Key}': путь не задан");
                    continue;
                }

                if (!TryResolveImagePath(db, path, targetDir, out string resolvedPath, out System.Exception? resolveError))
                {
                    if (resolveError is not null)
                    {
                        log.Warn(resolveError, $"RasterImageDef '{entry.Key}': ошибка разрешения пути: {path}");
                    }
                    else
                    {
                        log.Warn($"RasterImageDef '{entry.Key}': файл не найден: {path}");
                    }

                    continue;
                }

                if (copiedBySourcePath.TryGetValue(resolvedPath, out string? existingRelativePath)
                    && !string.IsNullOrEmpty(existingRelativePath))
                {
                    def.SourceFileName = existingRelativePath;
                    def.Load();
                    fixedCount++;
                    log.Debug($"RasterImage повторно использует файл: {existingRelativePath}");
                    continue;
                }

                (string uniqueDestPath, string uniqueFileName) = BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

                if (!string.Equals(Path.GetFullPath(resolvedPath), Path.GetFullPath(uniqueDestPath), StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(resolvedPath, uniqueDestPath, overwrite: true);
                }

                _ = reservedDestinationPaths.Add(uniqueDestPath);
                copiedBySourcePath[resolvedPath] = uniqueFileName;
                def.SourceFileName = uniqueFileName; // относительный путь к папке DWG
                def.Load(); // Правило 2: загружаем определение после смены пути
                fixedCount++;
                log.Info($"RasterImage скопирован: {resolvedPath} -> {uniqueFileName}");
            }
            catch (System.Exception ex)
            {
                log.Warn(ex, $"RasterImageDef '{entry.Key}': не удалось обработать изображение");
            }
        }

        tr.Commit();

        if (fixedCount > 0)
        {
            log.Info($"RasterImagePathFixer: обработано {fixedCount} изображений");
        }
    }

    private static (string DestinationPath, string FileName) BuildUniqueDestination(
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

    private static bool TryResolveImagePath(
        Database db, string path, string? searchDir,
        out string resolvedPath, out System.Exception? resolveError)
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

        // 3. Только имя файла в папке целевого DWG
        if (!string.IsNullOrEmpty(searchDir))
        {
            string candidate = Path.Combine(searchDir, Path.GetFileName(path));
            if (File.Exists(candidate))
            {
                resolvedPath = candidate;
                return true;
            }
        }

        // 4. AutoCAD FindFile
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
        catch (System.Exception ex)
        {
            resolveError = ex;
            return false;
        }
    }
}
