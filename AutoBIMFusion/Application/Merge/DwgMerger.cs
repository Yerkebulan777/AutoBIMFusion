using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
/// вычисляет границы, вставляет как блок со смещением.
/// </summary>
internal static class DwgMerger
{
    public static async Task<MergeResult> MergeSingleFile(string filePath, BlockInserter inserter, Database targetDb, OperationLogger log)
    {
        string layoutName = Path.GetFileNameWithoutExtension(filePath);
        string fileName = Path.GetFileName(filePath);

        if (!FileHelper.TryValidateFile(filePath, FileShare.ReadWrite, out string warn))
        {
            return MergeResult.Warn(fileName, warn);
        }

        if (!FileHelper.TryValidateDwgStructure(filePath, out warn))
        {
            return MergeResult.Warn(fileName, warn);
        }

        string tempPath = string.Empty;

        try
        {
            log.Info($"Файл: {fileName}");

            tempPath = await ViewportLayoutExporter.ExportToTempAsync(filePath, fileName, log);

            if (string.IsNullOrEmpty(tempPath))
            {
                return MergeResult.Warn(fileName, "Листы не найдены");
            }

            Extents3d? bounds = ReadBounds(tempPath, log);

            if (!bounds.HasValue)
            {
                return MergeResult.Warn(fileName, "Пустой файл");
            }

            Extents3d? worldBounds = inserter.InsertNativeObjects(targetDb, tempPath, layoutName, bounds.Value);

            if (worldBounds is null)
            {
                return MergeResult.Fail(fileName, "Не удалось вставить объекты");
            }

            log.Info($"Вставлен лист '{layoutName}'");
            return MergeResult.Ok(fileName, layoutName);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка: {fileName}");
            return MergeResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                try
                {
                    File.Delete(tempPath);
                }
                catch (System.Exception deleteEx)
                {
                    log.Warn(deleteEx, $"Сбой удаления temp-файла: {tempPath}");
                }
            }
        }
    }

    private static Extents3d? ReadBounds(string tempPath, OperationLogger log)
    {
        try
        {
            using Database db = new(false, true);
            db.ReadDwgFile(tempPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            using Transaction tr = db.TransactionManager.StartTransaction();
            db.UpdateExt(true); // true для максимальной точности пересчета всех объектов

            Point3d min = db.Extmin;
            Point3d max = db.Extmax;
            tr.Commit();

            bool isEmpty = min.X > max.X || min.Y > max.Y;
            return isEmpty ? null : new Extents3d(min, max);
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"Не удалось прочитать границы временного файла: {tempPath}");
            return null;
        }
    }
}
