using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Копирует файлы растровых изображений в папку с целевым DWG
///     и обновляет пути RasterImageDef на относительные.
///     Утилитарные операции делегируются к <see cref="FileUtil"/>.
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

                if (!FileUtil.TryResolveImagePath(db, path, targetDir, out var resolvedPath, out var resolveError))
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
                    FileUtil.BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

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

    /// <summary>
    ///     Разрешает относительные пути RasterImageDef в абсолютные, используя директорию исходного DWG-файла.
    ///     Должен вызываться до WblockCloneObjects, пока исходная база данных (prepared.Db) ещё доступна
    ///     и путь к исходному файлу известен.
    /// </summary>
    public static void ResolveRelativePaths(Database db, string sourceFilePath, Logger log)
    {
        string? sourceDir = Path.GetDirectoryName(sourceFilePath);
        if (string.IsNullOrEmpty(sourceDir)) return;

        var dictId = RasterImageDef.GetImageDictionary(db);
        if (dictId.IsNull) return;

        using var trx = db.TransactionManager.StartTransaction();
        var dict = (DBDictionary)trx.GetObject(dictId, OpenMode.ForRead);

        foreach (var entry in dict)
            try
            {
                if (trx.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def) continue;

                var path = def.SourceFileName;
                if (string.IsNullOrWhiteSpace(path)) continue;
                if (Path.IsPathRooted(path) && File.Exists(path)) continue;

                var candidate = Path.GetFullPath(Path.Combine(sourceDir, path));
                if (File.Exists(candidate))
                {
                    def.SourceFileName = candidate;
                    log.Debug($"RasterImageDef '{entry.Key}': путь разрешён → '{candidate}'");
                }
            }
            catch (Exception ex)
            {
                log.Warning(ex, $"RasterImageDef '{entry.Key}': не удалось разрешить путь изображения");
            }

        trx.Commit();
    }
}
