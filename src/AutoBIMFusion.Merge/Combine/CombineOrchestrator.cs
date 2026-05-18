using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Runtime.Versioning;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
///     вычисляет границы, вставляет как блок со смещением.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CombineOrchestrator
{
    public static Task<CombineResult> MergeSingleFile(string filePath, BlockInserter inserter, Document targetDoc, Logger log, string targetSavePath)
    {
        return Task.FromResult(MergeSingleFileCore(filePath, inserter, targetDoc, log, targetSavePath));
    }

    private static CombineResult MergeSingleFileCore(string filePath, BlockInserter inserter, Document targetDoc, Logger log, string targetSavePath)
    {
        string fileName = Path.GetFileName(filePath);

        string layoutName = Path.GetFileNameWithoutExtension(filePath);

        if (!FileUtil.TryValidateDwg(filePath, out string? warn))
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

            // ВАЖНО: PhantomBlockCleaner должен выполняться ДО NormalizeAllBlocksBasePoints.
            // Иначе: GetBlockDefinitionExtents включает TryGetExtents вложенных phantom-ссылок
            // внутри нормальных блоков → offset нормализации становится огромным →
            // INSERT POINTS нормальных блоков смещаются на миллионы единиц → неверные bounds.
            PhantomBlockCleaner.Clean(prepared.Db, log);
            BlockBasePointEditor.NormalizeAllBlocksBasePoints(prepared.Db);

            // ComputeModelSpaceBounds: прямой scan сущностей, не зависит от кэша db.Extmin/Extmax.
            Extents3d? bounds = ExtentsUtils.ComputeModelSpaceBounds(prepared.Db);

            if (!bounds.HasValue)
            {
                return CombineResult.Warn(fileName, "Пустой файл");
            }

            log.Debug($"{fileName}: source bounds before insert {ExtentsUtils.FormatExtents(bounds.Value)}");

            RasterImagePathFixer.CopyImagesToTargetFolder(prepared.Db, targetSavePath, log);

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

            return CombineResult.Ok(fileName);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Ошибка: {fileName}");
            return CombineResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
    }
}
