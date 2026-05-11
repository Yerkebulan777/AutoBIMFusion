using AutoBIMFusion.Merge.Layouts;
using AutoBIMFusion.Common;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Runtime.Versioning;

using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;
using AutoBIMFusion.Merge.Combine;

namespace AutoBIMFusion.Merge;

/// <summary>
/// Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
/// вычисляет границы, вставляет как блок со смещением.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CombineOrchestrator
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
            using PreparedSourceDatabase? prepared = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log);

            if (prepared == null)
            {
                return CombineResult.Warn(fileName, "Листы не найдены");
            }

            Extents3d? bounds = ExtentsUtils.GetDatabaseExtents(prepared.Db);

            if (!bounds.HasValue)
            {
                return CombineResult.Warn(fileName, "Пустой файл");
            }

            Extents3d? worldBounds;
            using (targetDoc.LockDocument())
            {
                DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-before-clone");

                worldBounds = inserter.InsertNativeObjects(
                    targetDoc.Database,
                    prepared.Db,
                    layoutName,
                    bounds.Value,
                    prepared.TargetVisualScale,
                    prepared.LinearScaleMultiplier);

                if (worldBounds is not null)
                {
                    DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-after-clone");
                }
            }

            return worldBounds is null ? CombineResult.Fail(fileName, "Не удалось вставить объекты") : CombineResult.Ok(fileName);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка: {fileName}");
            return CombineResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
    }
}


