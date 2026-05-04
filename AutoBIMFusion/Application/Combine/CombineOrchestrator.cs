using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Combine.Layouts;
using AutoBIMFusion.Application.Utils;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Application.Combine;

/// <summary>
/// Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
/// вычисляет границы, вставляет как блок со смещением.
/// </summary>
[SupportedOSPlatform("windows")]
internal static class CombineOrchestrator
{
    public static async Task<CombineResult> MergeSingleFile(string filePath, BlockInserter inserter, Document targetDoc, Logger log)
    {
        string fileName = Path.GetFileName(filePath);
        string layoutName = Path.GetFileNameWithoutExtension(filePath);

        if (!FileUtil.TryValidateDwg(filePath, out string warn))
        {
            return CombineResult.Warn(fileName, warn);
        }

        try
        {
            log.Information($"Файл: {fileName}");

            using Database? sourceDb = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log);

            if (sourceDb == null)
            {
                return CombineResult.Warn(fileName, "Листы не найдены");
            }

            Extents3d? bounds = ExtentsUtils.GetDatabaseExtents(sourceDb);

            if (!bounds.HasValue)
            {
                return CombineResult.Warn(fileName, "Пустой файл");
            }

            Extents3d? worldBounds;
            using (targetDoc.LockDocument())
            using (new AcadUnitScalingOverrideScope())
            {
                worldBounds = inserter.InsertNativeObjects(targetDoc.Database, sourceDb, layoutName, bounds.Value);
            }

            if (worldBounds is null)
            {
                return CombineResult.Fail(fileName, "Не удалось вставить объекты");
            }

            log.Information($"Вставлен лист '{layoutName}'");
            return CombineResult.Ok(fileName, layoutName);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка: {fileName}");
            return CombineResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
    }
}
