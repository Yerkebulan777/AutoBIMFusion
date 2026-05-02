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
internal static class MergeOrchestrator
{
    public static async Task<MergeResult> MergeSingleFile(string filePath, BlockInserter inserter, Document targetDoc, AILog log)
    {
        string fileName = Path.GetFileName(filePath);
        string layoutName = Path.GetFileNameWithoutExtension(filePath);

        if (!FileUtil.TryValidateDwg(filePath, out string warn))
        {
            return MergeResult.Warn(fileName, warn);
        }

        try
        {
            log.Info($"Файл: {fileName}");

            using Database? sourceDb = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log);

            if (sourceDb == null)
            {
                return MergeResult.Warn(fileName, "Листы не найдены");
            }

            Extents3d? bounds = ExtentsUtils.GetDatabaseExtents(sourceDb);

            if (!bounds.HasValue)
            {
                return MergeResult.Warn(fileName, "Пустой файл");
            }

            Extents3d? worldBounds;
            using (targetDoc.LockDocument())
            using (new AcadUnitScalingOverrideScope())
            {
                worldBounds = inserter.InsertNativeObjects(targetDoc.Database, sourceDb, layoutName, bounds.Value);
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
    }
}
