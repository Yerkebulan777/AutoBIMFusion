using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
/// вычисляет границы, вставляет как блок со смещением.
/// </summary>
internal sealed class DwgMerger(double gapPercent, OperationLogger log)
{
    private readonly OperationLogger _log = log;
    private readonly ViewportLayoutExporter _exporter = new(log);
    private readonly BlockInserter _blockInserter = new(gapPercent, log);

    public async Task<MergeResult> MergeSingleFile(string filePath, Database targetDb)
    {
        string layoutName = Path.GetFileNameWithoutExtension(filePath);
        string fileName = Path.GetFileName(filePath);

        if (!Validate(filePath, out string warn))
        {
            return MergeResult.Warn(fileName, warn);
        }

        string tempPath = string.Empty;

        try
        {
            _log.Info($"Обработка: {fileName}");

            tempPath = await _exporter.ExportToTempAsync(filePath, fileName);

            if (string.IsNullOrEmpty(tempPath))
            {
                return MergeResult.Warn(fileName, "Листы не найдены");
            }

            Extents3d? bounds = ReadBounds(tempPath);

            if (!bounds.HasValue)
            {
                return MergeResult.Warn(fileName, "Пустой файл");
            }

            string blockName = _blockInserter.BuildUniqueName(targetDb, layoutName);
            Extents3d? worldBounds = _blockInserter.InsertAndBindXref(targetDb, tempPath, blockName, bounds.Value);

            if (worldBounds is null)
            {
                return MergeResult.Fail(fileName, "Не удалось вставить блок");
            }

            _log.Info($"Успешно вставлен блок '{blockName}'");
            return MergeResult.Ok(fileName, blockName);
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, fileName);
            return MergeResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
        finally
        {
            if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static bool Validate(string filePath, out string warn)
    {
        if (!FileHelper.TryValidateFile(filePath, FileShare.ReadWrite, out warn))
        {
            return false;
        }

        if (!FileHelper.TryValidateDwgStructure(filePath, out warn))
        {
            return false;
        }

        warn = string.Empty;
        return true;
    }

    private static Extents3d? ReadBounds(string tempPath)
    {
        using Database db = new(false, true);
        db.ReadDwgFile(tempPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
        db.UpdateExt(false);

        Point3d min = db.Extmin;
        Point3d max = db.Extmax;
        bool isEmpty = min.X > max.X || min.Y > max.Y;

        return isEmpty ? null : new Extents3d(min, max);
    }
}
