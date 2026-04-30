using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Application.Merge.Models;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
/// вычисляет границы, вставляет как блок со смещением.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class MergeCoordinator
{
    public static async Task<MergeResult> MergeSingleFile(string filePath, BlockInserter inserter, Document targetDoc, AILog log)
    {
        string fileName = Path.GetFileName(filePath);
        string layoutName = Path.GetFileNameWithoutExtension(filePath);

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

            Extents3d? worldBounds;
            using (targetDoc.LockDocument())
            using (new AcadUnitScalingOverrideScope())
            {
                worldBounds = inserter.InsertNativeObjects(targetDoc.Database, tempPath, layoutName, bounds.Value);
                if (worldBounds.HasValue)
                {
                    DimensionStyleDiagnosticUtils.ClearDimensionOverrides(targetDoc.Database, log);
                }
            }

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

    private static Extents3d? ReadBounds(string tempPath, AILog log)
    {
        try
        {
            using Database db = new(false, true);
            db.ReadDwgFile(tempPath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            db.CloseInput(true);
            return ExtentsUtils.GetDatabaseExtents(db);
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Warn(ex, $"AutoCAD API не смог прочитать границы временного файла: {tempPath}");
            return null;
        }
        catch (System.Exception ex)
        {
            log.Warn(ex, $"Не удалось прочитать границы временного файла: {tempPath}");
            return null;
        }
    }
}
