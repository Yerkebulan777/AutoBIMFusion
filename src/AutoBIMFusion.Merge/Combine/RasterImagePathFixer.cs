using AutoBIMFusion.Common.Helpers;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Копирует растры в папку целевого DWG и после SaveAs переводит
///     RasterImageDef.SourceFileName на относительные пути.
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

    /// <summary>
    ///     Копирует растры рядом с итоговым DWG и ставит абсолютный путь копии.
    ///     Относительные пути нельзя задавать до SaveAs: у чертежа ещё нет Filename.
    /// </summary>
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

        string?[] searchDirs = [targetDir, sourceSearchDir, TryGetDatabaseDirectory(db)];
        Dictionary<string, string> copiedBySourcePath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedDestinationPaths = new(StringComparer.OrdinalIgnoreCase);

        ForEachImageDef(db, log, "не удалось обработать изображение", (def, key) =>
        {
            if (!TryResolveExisting(db, def, searchDirs, log, key, out var resolvedPath))
                return;

            if (copiedBySourcePath.TryGetValue(resolvedPath, out var existingDestPath))
            {
                Relink(def, existingDestPath);
                return;
            }

            if (!TryCopyBesideDrawing(targetDir, resolvedPath, reservedDestinationPaths, log, key, out var destPath))
                return;

            copiedBySourcePath[resolvedPath] = destPath;
            Relink(def, destPath);
        });
    }

    /// <summary>
    ///     Переводит SourceFileName в относительный путь. Вызывать после SaveAs.
    /// </summary>
    public static void ConvertPathsToRelative(Database db, string targetFilePath, Logger log)
    {
        var drawingPath = string.IsNullOrWhiteSpace(db.Filename) ? targetFilePath : db.Filename;
        var drawingDir = Path.GetDirectoryName(drawingPath);
        if (string.IsNullOrEmpty(drawingDir))
        {
            log.Warning("RasterImagePathFixer: не удалось определить папку сохранённого DWG");
            return;
        }

        ForEachImageDef(db, log, "не удалось задать относительный путь", (def, key) =>
        {
            if (!TryResolveExisting(db, def, [drawingDir], log, key, out var resolvedPath))
                return;

            if (!TryMakeRelativePath(drawingDir, resolvedPath, out var relativePath))
            {
                log.Warning("RasterImageDef '{Key}': файл вне папки DWG, относительный путь не задан: {Path}",
                    key, resolvedPath);
                return;
            }

            if (string.Equals(def.SourceFileName, relativePath, StringComparison.OrdinalIgnoreCase))
                return;

            Relink(def, relativePath);
            log.Debug("RasterImageDef '{Key}': {From} → {To}", key, resolvedPath, relativePath);
        });
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

    /// <summary>
    ///     Относительный путь только внутри папки DWG (без '..'): иначе сборка не самодостаточна.
    /// </summary>
    internal static bool TryMakeRelativePath(string drawingDir, string imageFullPath, out string relativePath)
    {
        relativePath = string.Empty;
        if (string.IsNullOrWhiteSpace(drawingDir) || string.IsNullOrWhiteSpace(imageFullPath))
            return false;

        string fromFull;
        string toFull;
        try
        {
            fromFull = Path.GetFullPath(drawingDir);
            toFull = Path.GetFullPath(imageFullPath);
        }
        catch (Exception)
        {
            return false;
        }

        var fromPrefix = fromFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                         + Path.DirectorySeparatorChar;
        if (!toFull.StartsWith(fromPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var relative = toFull[fromPrefix.Length..];
        if (string.IsNullOrEmpty(relative))
            return false;

        relativePath = relative;
        return true;
    }

    private static void ForEachImageDef(Database db, Logger log, string failureMessage,
        Action<RasterImageDef, string> body)
    {
        using var trx = db.TransactionManager.StartTransaction();
        var dict = GetImageDictionary(db, trx);
        if (dict is null)
        {
            trx.Commit();
            return;
        }

        UnloadAll(dict, trx);

        foreach (var entry in dict)
            try
            {
                if (trx.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def)
                    continue;

                body(def, entry.Key);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "RasterImageDef '{Key}': {Reason}", entry.Key, failureMessage);
            }

        trx.Commit();
    }

    private static bool TryResolveExisting(Database db, RasterImageDef def, IEnumerable<string?> searchDirs,
        Logger log, string key, out string resolvedPath)
    {
        resolvedPath = string.Empty;
        var storedPath = def.SourceFileName;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            log.Warning("RasterImageDef '{Key}': путь не задан", key);
            return false;
        }

        if (TryResolve(db, def, searchDirs, out resolvedPath))
            return true;

        log.Warning("RasterImageDef '{Key}': файл не найден: {Path}", key, storedPath);
        return false;
    }

    private static bool TryCopyBesideDrawing(string targetDir, string resolvedPath,
        HashSet<string> reservedDestinationPaths, Logger log, string key, out string destPath)
    {
        var (uniqueDestPath, _) =
            FileUtil.BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

        if (!PathsEqual(resolvedPath, uniqueDestPath))
            File.Copy(resolvedPath, uniqueDestPath, true);

        if (!File.Exists(uniqueDestPath))
        {
            log.Warning("RasterImageDef '{Key}': копия недоступна: {Path}", key, uniqueDestPath);
            destPath = string.Empty;
            return false;
        }

        _ = reservedDestinationPaths.Add(uniqueDestPath);
        destPath = uniqueDestPath;
        return true;
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

    private static DBDictionary? GetImageDictionary(Database db, Transaction trx)
    {
        var dictId = RasterImageDef.GetImageDictionary(db);
        return dictId.IsNull ? null : (DBDictionary)trx.GetObject(dictId, OpenMode.ForRead);
    }

    private static void UnloadAll(DBDictionary dict, Transaction trx)
    {
        foreach (var entry in dict)
        {
            if (trx.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def || !def.IsLoaded)
                continue;

            try
            {
                def.Unload(false);
            }
            catch (Exception)
            {
            }
        }
    }

    private static void Relink(RasterImageDef def, string path)
    {
        if (def.IsLoaded)
            def.Unload(false);
        def.SourceFileName = path;
    }

    private static bool PathsEqual(string left, string right)
    {
        try
        {
            return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception)
        {
            return string.Equals(left, right, StringComparison.OrdinalIgnoreCase);
        }
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
