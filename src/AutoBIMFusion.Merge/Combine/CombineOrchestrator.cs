using System.Runtime.Versioning;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Координирует слияние DWG-файлов: экспортирует первый Paper Space лист,
///     вычисляет границы, вставляет как блок со смещением.
/// </summary>
[SupportedOSPlatform("windows")]
public static class CombineOrchestrator
{
    public static CombineResult MergeSingleFile(string filePath, BlockInserter inserter, Document targetDoc,
        Logger log, string targetSavePath)
    {
        var fileName = Path.GetFileName(filePath);

        var layoutName = Path.GetFileNameWithoutExtension(filePath);

        if (!FileUtil.TryValidateDwg(filePath, out var warn)) return CombineResult.Warn(fileName, warn);

        try
        {
            using var prepared = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log);

            if (prepared == null) return CombineResult.Warn(fileName, "Листы не найдены");

            // Очистка мелких объектов за рамкой выполняется внутри PrepareDatabaseForMerge до нормализации базовых точек.
            BlockBasePointEditor.NormalizeAllBlocksBasePoints(prepared.Db);

            // ComputeModelSpaceBounds: прямой scan сущностей, не зависит от кэша db.Extmin/Extmax.
            var bounds = ExtentsUtils.ComputeModelSpaceBounds(prepared.Db);

            if (!bounds.HasValue) return CombineResult.Warn(fileName, "Пустой файл");

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
                    DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-after-clone");
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
