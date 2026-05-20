using System.Runtime.Versioning;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Diagnostics;
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
        MergeDiagnosticContext diagnosticContext = MergeDiagnostics.CreateFileContext(filePath);
        var fileName = Path.GetFileName(filePath);

        var layoutName = Path.GetFileNameWithoutExtension(filePath);

        MergeDiagnostics.WriteEvent(diagnosticContext, "file.start", new Dictionary<string, object?>
        {
            ["fileName"] = fileName,
            ["layoutName"] = layoutName
        });

        if (!FileUtil.TryValidateDwg(filePath, out var warn))
        {
            MergeDiagnostics.WriteEvent(diagnosticContext, "file.failed", new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["reason"] = warn,
                ["isSkipped"] = true
            });
            return CombineResult.Warn(fileName, warn);
        }

        MergeDiagnostics.WriteEvent(diagnosticContext, "file.validated", new Dictionary<string, object?>
        {
            ["fileName"] = fileName
        });

        try
        {
            using var prepared = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log, diagnosticContext);

            if (prepared == null)
            {
                MergeDiagnostics.WriteEvent(diagnosticContext, "file.failed", new Dictionary<string, object?>
                {
                    ["fileName"] = fileName,
                    ["reason"] = "Листы не найдены",
                    ["isSkipped"] = true
                });
                return CombineResult.Warn(fileName, "Листы не найдены");
            }

            // Очистка мелких объектов за рамкой выполняется внутри PrepareDatabaseForMerge до нормализации базовых точек.
            BlockBasePointEditor.NormalizeAllBlocksBasePoints(prepared.Db);

            // ComputeModelSpaceBounds: прямой scan сущностей, не зависит от кэша db.Extmin/Extmax.
            var bounds = ExtentsUtils.ComputeModelSpaceBounds(prepared.Db);

            if (!bounds.HasValue)
            {
                MergeDiagnostics.WriteEvent(diagnosticContext, "file.failed", new Dictionary<string, object?>
                {
                    ["fileName"] = fileName,
                    ["reason"] = "Пустой файл",
                    ["isSkipped"] = true
                });
                return CombineResult.Warn(fileName, "Пустой файл");
            }

            log.Debug("{FileName}: source bounds before insert {Bounds}", fileName, ExtentsUtils.FormatExtents(bounds.Value));

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
                    prepared.LinearScaleMultiplier,
                    diagnosticContext);

                if (worldBounds is not null)
                    DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-after-clone");
            }

            MergeDiagnostics.WriteEvent(diagnosticContext, "file.done", new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["sourceBounds"] = MergeDiagnostics.FormatExtents(bounds.Value),
                ["worldBounds"] = MergeDiagnostics.FormatExtents(worldBounds),
                ["targetVisualScale"] = prepared.TargetVisualScale,
                ["linearScaleMultiplier"] = prepared.LinearScaleMultiplier
            });

            return CombineResult.Ok(fileName);
        }
        catch (Exception ex)
        {
            MergeDiagnostics.WriteEvent(diagnosticContext, "file.failed", new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["reason"] = ex.Message,
                ["exceptionType"] = ex.GetType().FullName,
                ["isSkipped"] = false
            });
            log.Error(ex, "Ошибка: {FileName}", fileName);
            return CombineResult.Fail(fileName, ex.Message, "Ошибка обработки");
        }
    }
}
