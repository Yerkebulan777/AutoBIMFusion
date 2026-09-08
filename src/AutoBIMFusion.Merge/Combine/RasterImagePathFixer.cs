using AutoBIMFusion.Common.Helpers;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Копирует файлы растровых изображений в папку с целевым DWG
///     и обновляет пути RasterImageDef на относительные.
/// </summary>
public static class RasterImagePathFixer
{
    internal static void MoveRasterImagesToBack(Database db, Transaction trx)
    {
        var blockTable = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

        foreach (ObjectId btrId in blockTable)
        {
            var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);
            if (btr.IsFromExternalReference || btr.DrawOrderTableId.IsNull) continue;

            using ObjectIdCollection imageIds = [];
            foreach (ObjectId id in btr)
                if (trx.GetObject(id, OpenMode.ForRead) is RasterImage)
                    _ = imageIds.Add(id);

            if (imageIds.Count == 0) continue;

            var drawOrder = (DrawOrderTable)trx.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);
            drawOrder.MoveToBottom(imageIds);
        }
    }

    public static void CopyImagesToTargetFolder(Database db, string targetFilePath, Logger log,
        string? sourceSearchDir = null)
    {
        var targetDir = Path.GetDirectoryName(targetFilePath);
        if (string.IsNullOrEmpty(targetDir))
        {
            log.Warning("RasterImagePathFixer: не удалось определить папку целевого файла");
            return;
        }

        _ = Directory.CreateDirectory(targetDir);

        var searchDirs = new[] { targetDir, sourceSearchDir, TryGetDatabaseDirectory(db) };
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

                var storedPath = def.SourceFileName;
                if (string.IsNullOrWhiteSpace(storedPath))
                {
                    log.Warning("RasterImageDef '{Key}': путь не задан", entry.Key);
                    continue;
                }

                if (!TryResolve(db, def, searchDirs, out var resolvedPath))
                {
                    log.Warning("RasterImageDef '{Key}': файл не найден: {Path}", entry.Key, storedPath);
                    continue;
                }

                if (copiedBySourcePath.TryGetValue(resolvedPath, out var existingRelativePath)
                    && !string.IsNullOrEmpty(existingRelativePath))
                {
                    Relink(def, existingRelativePath);
                    continue;
                }

                var (uniqueDestPath, uniqueFileName) =
                    FileUtil.BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

                if (!string.Equals(Path.GetFullPath(resolvedPath), Path.GetFullPath(uniqueDestPath),
                        StringComparison.OrdinalIgnoreCase))
                    File.Copy(resolvedPath, uniqueDestPath, true);

                _ = reservedDestinationPaths.Add(uniqueDestPath);
                copiedBySourcePath[resolvedPath] = uniqueFileName;
                Relink(def, uniqueFileName);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "RasterImageDef '{Key}': не удалось обработать изображение", entry.Key);
            }

        trx.Commit();
    }

    /// <summary>
    ///     Ищет файл на диске по сохранённому пути или по имени рядом с исходным DWG.
    ///     Нужен, когда RasterImageDef указывает на уже удалённый %TEMP%\RBF-* кэш.
    /// </summary>
    internal static bool TryResolveOnDisk(string path, IEnumerable<string?> searchDirs, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            resolvedPath = path;
            return true;
        }

        if (TryRemapUserProfilePath(path, out resolvedPath))
            return true;

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var dir in DistinctExistingDirectories(searchDirs))
        {
            var direct = Path.Combine(dir, fileName);
            if (File.Exists(direct))
            {
                resolvedPath = direct;
                return true;
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    var nested = Path.Combine(subDir, fileName);
                    if (!File.Exists(nested)) continue;
                    resolvedPath = nested;
                    return true;
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        return false;
    }

    private static bool TryResolve(Database db, RasterImageDef def, IEnumerable<string?> searchDirs,
        out string resolvedPath)
    {
        string? fileName = null;
        foreach (var candidate in CandidatePaths(def))
        {
            fileName ??= Path.GetFileName(candidate);
            if (TryResolveOnDisk(candidate, searchDirs, out resolvedPath))
                return true;
        }

        resolvedPath = string.Empty;
        return fileName is string name
            && !string.IsNullOrWhiteSpace(name)
            && TryFindWithAcad(db, name, out resolvedPath);
    }

    private static IEnumerable<string> CandidatePaths(RasterImageDef def)
    {
        if (!string.IsNullOrWhiteSpace(def.SourceFileName))
            yield return def.SourceFileName;

        if (TryGetActiveFileName(def) is string active
            && !string.IsNullOrWhiteSpace(active)
            && !string.Equals(active, def.SourceFileName, StringComparison.OrdinalIgnoreCase))
            yield return active;
    }

    private static IEnumerable<string> DistinctExistingDirectories(IEnumerable<string?> dirs)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir)) continue;

            string fullDir;
            try
            {
                fullDir = Path.GetFullPath(dir);
            }
            catch (Exception)
            {
                continue;
            }

            if (seen.Add(fullDir) && Directory.Exists(fullDir))
                yield return fullDir;
        }
    }

    private static void Relink(RasterImageDef def, string relativePath)
    {
        if (def.IsLoaded)
            def.Unload(false);
        def.SourceFileName = relativePath;
        def.Load();
    }

    private static bool TryRemapUserProfilePath(string path, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        if (!Path.IsPathRooted(path))
            return false;

        var userProfileParent = Path.GetDirectoryName(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)) ?? string.Empty;
        if (string.IsNullOrEmpty(userProfileParent)
            || !path.StartsWith(userProfileParent, StringComparison.OrdinalIgnoreCase))
            return false;

        var afterUsersDir = path[(userProfileParent.Length + 1)..];
        var slashIdx = afterUsersDir.IndexOf(Path.DirectorySeparatorChar);
        if (slashIdx <= 0)
            return false;

        var candidate = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            afterUsersDir[(slashIdx + 1)..]);
        if (!File.Exists(candidate))
            return false;

        resolvedPath = candidate;
        return true;
    }

    private static string? TryGetDatabaseDirectory(Database db)
    {
        try
        {
            var fileName = db.Filename;
            return string.IsNullOrWhiteSpace(fileName) ? null : Path.GetDirectoryName(fileName);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetActiveFileName(RasterImageDef def)
    {
        try
        {
            var active = def.ActiveFileName;
            return string.IsNullOrWhiteSpace(active) ? null : active;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool TryFindWithAcad(Database db, string fileName, out string foundPath)
    {
        foundPath = string.Empty;
        FindFileHint[] hints = [FindFileHint.EmbeddedImageFile, FindFileHint.Default];
        foreach (var hint in hints)
        {
            try
            {
                var result = HostApplicationServices.Current.FindFile(fileName, db, hint);
                if (string.IsNullOrEmpty(result) || !File.Exists(result))
                    continue;

                foundPath = result;
                return true;
            }
            catch (Exception ex) when (IsMissingRaster(ex))
            {
            }
        }

        return false;
    }

    private static bool IsMissingRaster(Exception ex) =>
        ex is Autodesk.AutoCAD.Runtime.Exception acad
        && acad.ErrorStatus is ErrorStatus.FilerError
            or ErrorStatus.FileNotFound
            or ErrorStatus.FileAccessErr;
}
