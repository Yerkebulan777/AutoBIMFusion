using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;

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

        HashSet<string> copiedFiles = new(StringComparer.OrdinalIgnoreCase);

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
            if (tr.GetObject(entry.Value, OpenMode.ForWrite) is not RasterImageDef def)
                continue;

            string? path = def.SourceFileName;
            if (string.IsNullOrWhiteSpace(path))
            {
                log.Warn($"RasterImageDef '{entry.Key}': путь не задан");
                continue;
            }

            // Правило 1: используем FindFile для стабильного разрешения путей
            string resolvedPath = HostApplicationServices.Current.FindFile(path, db, FindFileHint.Image);
            if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
            {
                log.Warn($"RasterImageDef '{entry.Key}': файл не найден: {path}");
                continue;
            }

            string fileName = Path.GetFileName(resolvedPath);
            string destPath = Path.Combine(targetDir, fileName);

            // Обработка коллизий имён
            int counter = 1;
            string uniqueDestPath = destPath;
            string uniqueFileName = fileName;
            while (copiedFiles.Contains(uniqueDestPath) || File.Exists(uniqueDestPath))
            {
                string name = Path.GetFileNameWithoutExtension(fileName);
                string ext = Path.GetExtension(fileName);
                uniqueFileName = $"{name}_{counter}{ext}";
                uniqueDestPath = Path.Combine(targetDir, uniqueFileName);
                counter++;
            }

            try
            {
                File.Copy(resolvedPath, uniqueDestPath, overwrite: true);
                copiedFiles.Add(uniqueDestPath);
                def.SourceFileName = uniqueFileName; // относительный путь к папке DWG
                def.Load(); // Правило 2: загружаем определение после смены пути
                fixedCount++;
                log.Info($"RasterImage скопирован: {resolvedPath} -> {uniqueFileName}");
            }
            catch (System.Exception ex)
            {
                log.Warn(ex, $"Не удалось скопировать растр: {path}");
            }
        }

        tr.Commit();

        if (fixedCount > 0)
        {
            log.Info($"RasterImagePathFixer: обработано {fixedCount} изображений");
        }
    }
}
