using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using Serilog.Events;
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
        string? sourceSearchDir = null, string? stage = null)
    {
        var rasterLog = RasterLogger(log);
        var phase = string.IsNullOrWhiteSpace(stage) ? "copy" : stage;

        var targetDir = Path.GetDirectoryName(targetFilePath);
        if (string.IsNullOrEmpty(targetDir))
        {
            rasterLog.Warning(
                "Raster copy [{Stage}]: не удалось определить папку целевого файла, targetFilePath=\"{TargetFilePath}\"",
                phase, targetFilePath);
            return;
        }

        _ = Directory.CreateDirectory(targetDir);

        string?[] searchDirs = [targetDir, sourceSearchDir, TryGetDatabaseDirectory(db)];
        var searchDesc = DescribeSearchDirs(searchDirs);
        rasterLog.Information(
            "Raster copy [{Stage}]: start targetDir=\"{TargetDir}\" sourceSearchDir=\"{SourceSearchDir}\" dbFilename=\"{DbFilename}\" search={SearchDirs}",
            phase, targetDir, NullPath(sourceSearchDir), NullPath(TryReadFilename(db)), searchDesc);

        Dictionary<string, string> copiedBySourcePath = new(StringComparer.OrdinalIgnoreCase);
        HashSet<string> reservedDestinationPaths = new(StringComparer.OrdinalIgnoreCase);
        var total = 0;
        var copied = 0;
        var alreadyInFolder = 0;
        var reused = 0;
        var missing = 0;
        var failed = 0;

        ForEachImageDef(db, rasterLog, phase, "не удалось обработать изображение", (def, key) =>
        {
            total++;
            var stored = NullPath(def.SourceFileName);
            if (!TryResolveExisting(db, def, searchDirs, searchDesc, rasterLog, key, out var resolvedPath, out var via))
            {
                missing++;
                return;
            }

            string destPath;
            string action;
            if (copiedBySourcePath.TryGetValue(resolvedPath, out var existingDestPath))
            {
                destPath = existingDestPath;
                action = "reused";
                reused++;
            }
            else if (!TryCopyBesideDrawing(targetDir, resolvedPath, reservedDestinationPaths, rasterLog, key, phase,
                         out destPath, out var alreadyThere))
            {
                failed++;
                return;
            }
            else
            {
                action = alreadyThere ? "already-in-folder" : "copied";
                if (alreadyThere) alreadyInFolder++;
                else copied++;
                copiedBySourcePath[resolvedPath] = destPath;
            }

            Relink(def, destPath, rasterLog, key, action);
            rasterLog.Debug(
                "RasterImageDef '{Key}' [{Stage}]: stored=\"{Stored}\" resolved=\"{Resolved}\" via={Via} dest=\"{Dest}\" action={Action} loaded={Loaded}",
                key, phase, stored, resolvedPath, via, destPath, action, def.IsLoaded);
        });

        rasterLog.Write(missing + failed > 0 ? LogEventLevel.Warning : LogEventLevel.Information,
            "Raster copy [{Stage}]: итог defs={Total} copied={Copied} alreadyInFolder={AlreadyInFolder} reused={Reused} missing={Missing} failed={Failed}",
            phase, total, copied, alreadyInFolder, reused, missing, failed);
    }

    /// <summary>
    ///     Переводит SourceFileName в относительный путь. Вызывать после SaveAs.
    /// </summary>
    public static void ConvertPathsToRelative(Database db, string targetFilePath, Logger log, string? stage = null)
    {
        var rasterLog = RasterLogger(log);
        var phase = string.IsNullOrWhiteSpace(stage) ? "relative" : stage;

        var saveDir = Path.GetDirectoryName(targetFilePath);
        var dbFilename = TryReadFilename(db);
        // SaveAs may leave Filename pointing at the original DWT (AutoCAD 2019).
        // Persist image paths relative to the file we actually save.
        var drawingDir = saveDir;
        if (string.IsNullOrEmpty(drawingDir))
        {
            rasterLog.Warning(
                "Raster relative [{Stage}]: не удалось определить папку сохранённого DWG, dbFilename=\"{DbFilename}\" savePath=\"{SavePath}\"",
                phase, NullPath(dbFilename), targetFilePath);
            return;
        }

        rasterLog.Information(
            "Raster relative [{Stage}]: start drawingDir=\"{DrawingDir}\" dbFilename=\"{DbFilename}\" savePath=\"{SavePath}\"",
            phase, drawingDir, NullPath(dbFilename), targetFilePath);

        var searchDesc = DescribeSearchDirs([drawingDir]);
        var total = 0;
        var relinked = 0;
        var alreadyRelative = 0;
        var outsideFolder = 0;
        var missing = 0;

        ForEachImageDef(db, rasterLog, phase, "не удалось задать относительный путь", (def, key) =>
        {
            total++;
            if (!TryResolveExisting(db, def, [drawingDir], searchDesc, rasterLog, key, out var resolvedPath, out var via))
            {
                missing++;
                return;
            }

            if (!TryMakeRelativePath(drawingDir, resolvedPath, out var relativePath))
            {
                outsideFolder++;
                rasterLog.Warning(
                    "RasterImageDef '{Key}' [{Stage}]: файл вне папки DWG, относительный путь не задан. stored=\"{Stored}\" active=\"{Active}\" resolved=\"{Resolved}\" via={Via} drawingDir=\"{DrawingDir}\" sameNameInDwgFolder={SameNameInDwgFolder} entities={Entities} loaded={Loaded}",
                    key, phase, NullPath(def.SourceFileName), NullPath(TryGetActiveFileName(def)), resolvedPath, via,
                    drawingDir, SameNameExistsIn(drawingDir, resolvedPath), TryGetEntityCount(def), def.IsLoaded);
                return;
            }

            if (string.Equals(def.SourceFileName, relativePath, StringComparison.OrdinalIgnoreCase))
            {
                Relink(def, relativePath, rasterLog, key, "already-relative", resolvedPath);
                alreadyRelative++;
                rasterLog.Debug(
                    "RasterImageDef '{Key}' [{Stage}]: уже относительный \"{Relative}\" resolved=\"{Resolved}\" via={Via} loaded={Loaded}",
                    key, phase, relativePath, resolvedPath, via, def.IsLoaded);
                return;
            }

            Relink(def, relativePath, rasterLog, key, "relative", resolvedPath);
            relinked++;
            rasterLog.Debug(
                "RasterImageDef '{Key}' [{Stage}]: {From} → {To} via={Via} loaded={Loaded}",
                key, phase, resolvedPath, relativePath, via, def.IsLoaded);
        });

        rasterLog.Write(missing + outsideFolder > 0 ? LogEventLevel.Warning : LogEventLevel.Information,
            "Raster relative [{Stage}]: итог defs={Total} relinked={Relinked} alreadyRelative={AlreadyRelative} outsideFolder={OutsideFolder} missing={Missing}",
            phase, total, relinked, alreadyRelative, outsideFolder, missing);
    }

    /// <summary>
    ///     Пишет Filename базы и savePath — чтобы поймать расхождение папок вокруг SaveAs.
    /// </summary>
    public static void LogDatabaseSaveState(Database db, string savePath, Logger log, string step)
    {
        var dbDir = TryGetDatabaseDirectory(db);
        var saveDir = Path.GetDirectoryName(savePath);
        var sameDir = dbDir is not null && saveDir is not null && PathsEqual(dbDir, saveDir);
        RasterLogger(log).Information(
            "Raster save [{Step}]: dbFilename=\"{DbFilename}\" savePath=\"{SavePath}\" sameDir={SameDir}",
            step, NullPath(TryReadFilename(db)), savePath, sameDir);
    }

    /// <summary>
    ///     Ищет файл на диске по сохранённому пути или по имени рядом с исходным DWG.
    ///     Нужен, когда RasterImageDef указывает на уже удалённый %TEMP%\RBF-* кэш.
    /// </summary>
    internal static bool TryResolveOnDisk(string path, IEnumerable<string?> searchDirs, out string resolvedPath)
        => TryResolveOnDisk(path, searchDirs, out resolvedPath, out _);

    internal static bool TryResolveOnDisk(string path, IEnumerable<string?> searchDirs, out string resolvedPath,
        out string via)
    {
        resolvedPath = string.Empty;
        via = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
            return false;

        if (Path.IsPathRooted(path) && File.Exists(path))
        {
            resolvedPath = path;
            via = "rooted-exists";
            return true;
        }

        if (TryRemapUserProfilePath(path, out resolvedPath))
        {
            via = "user-profile-remap";
            return true;
        }

        var fileName = Path.GetFileName(path);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        foreach (var dir in DistinctExistingDirectories(searchDirs))
        {
            var direct = Path.Combine(dir, fileName);
            if (File.Exists(direct))
            {
                resolvedPath = direct;
                via = $"search-dir:{dir}";
                return true;
            }

            try
            {
                foreach (var subDir in Directory.EnumerateDirectories(dir))
                {
                    var nested = Path.Combine(subDir, fileName);
                    if (!File.Exists(nested)) continue;
                    resolvedPath = nested;
                    via = $"search-subdir:{subDir}";
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

    private static void ForEachImageDef(Database db, Logger log, string stage, string failureMessage,
        Action<RasterImageDef, string> body)
    {
        using var trx = db.TransactionManager.StartTransaction();
        var dict = GetImageDictionary(db, trx);
        if (dict is null)
        {
            log.Information("Raster [{Stage}]: словаря ACAD_IMAGE_DICT нет", stage);
            trx.Commit();
            return;
        }

        log.Information("Raster [{Stage}]: defs={DefCount}", stage, dict.Count);

        foreach (var entry in dict)
            try
            {
                if (trx.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def)
                    continue;

                body(def, entry.Key);
            }
            catch (Exception ex)
            {
                log.Warning(ex, "RasterImageDef '{Key}' [{Stage}]: {Reason}", entry.Key, stage, failureMessage);
            }

        trx.Commit();
    }

    private static bool TryResolveExisting(Database db, RasterImageDef def, IEnumerable<string?> searchDirs,
        string searchDesc, Logger log, string key, out string resolvedPath, out string via)
    {
        resolvedPath = string.Empty;
        via = string.Empty;
        var storedPath = def.SourceFileName;
        if (string.IsNullOrWhiteSpace(storedPath))
        {
            log.Warning(
                "RasterImageDef '{Key}': путь не задан, active=\"{Active}\" entities={Entities} loaded={Loaded}",
                key, NullPath(TryGetActiveFileName(def)), TryGetEntityCount(def), def.IsLoaded);
            return false;
        }

        if (TryResolve(db, def, searchDirs, out resolvedPath, out via))
            return true;

        log.Warning(
            "RasterImageDef '{Key}': файл не найден. stored=\"{Stored}\" active=\"{Active}\" search={SearchDirs} entities={Entities} loaded={Loaded}",
            key, storedPath, NullPath(TryGetActiveFileName(def)), searchDesc, TryGetEntityCount(def), def.IsLoaded);
        return false;
    }

    private static bool TryCopyBesideDrawing(string targetDir, string resolvedPath,
        HashSet<string> reservedDestinationPaths, Logger log, string key, string stage, out string destPath,
        out bool alreadyInFolder)
    {
        destPath = string.Empty;
        alreadyInFolder = false;
        var (uniqueDestPath, _) =
            FileUtil.BuildUniqueDestination(targetDir, resolvedPath, reservedDestinationPaths);

        if (!PathsEqual(resolvedPath, uniqueDestPath))
        {
            try
            {
                File.Copy(resolvedPath, uniqueDestPath, true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warning(ex,
                    "RasterImageDef '{Key}' [{Stage}]: копирование не удалось, from=\"{From}\" to=\"{To}\"",
                    key, stage, resolvedPath, uniqueDestPath);
                return false;
            }
        }
        else
        {
            alreadyInFolder = true;
        }

        if (!File.Exists(uniqueDestPath))
        {
            log.Warning("RasterImageDef '{Key}' [{Stage}]: копия недоступна: {Path}",
                key, stage, uniqueDestPath);
            return false;
        }

        _ = reservedDestinationPaths.Add(uniqueDestPath);
        destPath = uniqueDestPath;
        return true;
    }

    private static bool TryResolve(Database db, RasterImageDef def, IEnumerable<string?> searchDirs,
        out string resolvedPath, out string via)
    {
        via = string.Empty;
        string? fileName = null;
        foreach (var candidate in CandidatePaths(def))
        {
            fileName ??= Path.GetFileName(candidate);
            if (TryResolveOnDisk(candidate, searchDirs, out resolvedPath, out via))
                return true;
        }

        resolvedPath = string.Empty;
        if (fileName is string name
            && !string.IsNullOrWhiteSpace(name)
            && TryFindWithAcad(db, name, out resolvedPath))
        {
            via = "acad-findfile";
            return true;
        }

        return false;
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

    private static void Relink(RasterImageDef def, string path, Logger log, string key, string action,
        string? resolvedPath = null)
    {
        if (def.IsLoaded)
            def.Unload(false);
        try
        {
            def.SourceFileName = path;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex) when (
            ex.ErrorStatus == ErrorStatus.FileAccessErr
            && resolvedPath is not null && File.Exists(resolvedPath)
            && string.Equals(def.SourceFileName, path, StringComparison.OrdinalIgnoreCase))
        {
            // A19 stores the relative name, then fails to resolve it against the DWT.
            // Supply the verified active filename below; all other setter failures propagate.
        }
        // The active path serves this session even when Filename still names a template.
        // SourceFileName is the portable path persisted in the resulting DWG.
        def.ActiveFileName = resolvedPath ?? path;
        def.Load();
        if (string.Equals(def.SourceFileName, path, StringComparison.OrdinalIgnoreCase))
            return;

        log.Warning(
            "RasterImageDef '{Key}': Relink {Action} не закрепился, requested=\"{Requested}\" storedAfter=\"{StoredAfter}\" activeAfter=\"{ActiveAfter}\" loaded={Loaded}",
            key, action, path, NullPath(def.SourceFileName), NullPath(TryGetActiveFileName(def)), def.IsLoaded);
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

    private static string? TryReadFilename(Database db)
    {
        try
        {
            var fileName = db.Filename;
            return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static string? TryGetDatabaseDirectory(Database db)
    {
        var fileName = TryReadFilename(db);
        return fileName is null ? null : Path.GetDirectoryName(fileName);
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

    private static Logger RasterLogger(Logger log) =>
        (Logger)log.ForContext("SourceContext", LoggerFactory.RasterImagesContext);

    private static string NullPath(string? path) => string.IsNullOrWhiteSpace(path) ? "(empty)" : path;

    private static int TryGetEntityCount(RasterImageDef def)
    {
        try
        {
            return def.GetEntityCount(out _);
        }
        catch (Exception)
        {
            return -1;
        }
    }

    private static bool SameNameExistsIn(string drawingDir, string resolvedPath)
    {
        try
        {
            var name = Path.GetFileName(resolvedPath);
            return !string.IsNullOrWhiteSpace(name) && File.Exists(Path.Combine(drawingDir, name));
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static string DescribeSearchDirs(IEnumerable<string?> dirs)
    {
        List<string> parts = [];
        foreach (var dir in dirs)
        {
            if (string.IsNullOrWhiteSpace(dir))
            {
                parts.Add("(empty)");
                continue;
            }

            try
            {
                var full = Path.GetFullPath(dir);
                parts.Add(Directory.Exists(full) ? full : $"{full} (missing)");
            }
            catch (Exception)
            {
                parts.Add($"{dir} (invalid)");
            }
        }

        return string.Join(" | ", parts);
    }
}
