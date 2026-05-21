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
            return LogFileFailedAndReturnWarn(diagnosticContext, fileName, warn, true);
        }

        MergeDiagnostics.WriteEvent(diagnosticContext, "file.validated", new Dictionary<string, object?>
        {
            ["fileName"] = fileName
        });

        try
        {
            return ProcessPreparedDatabase(filePath, fileName, layoutName, inserter, targetDoc, log, targetSavePath, diagnosticContext);
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

    private static CombineResult ProcessPreparedDatabase(string filePath, string fileName, string layoutName,
        BlockInserter inserter, Document targetDoc, Logger log, string targetSavePath,
        MergeDiagnosticContext diagnosticContext)
    {
        using var prepared = ViewportLayoutExporter.PrepareDatabaseForMerge(filePath, fileName, log, diagnosticContext);

        if (prepared == null)
        {
            return LogFileFailedAndReturnWarn(diagnosticContext, fileName, "Листы не найдены", true);
        }

        // Очистка мелких объектов за рамкой выполняется внутри PrepareDatabaseForMerge до нормализации базовых точек.
        BlockBasePointEditor.NormalizeAllBlocksBasePoints(prepared.Db);

        // ComputeModelSpaceBounds: прямой scan сущностей, не зависит от кэша db.Extmin/Extmax.
        var bounds = ExtentsUtils.ComputeModelSpaceBounds(prepared.Db);

        if (!bounds.HasValue)
        {
            return LogFileFailedAndReturnWarn(diagnosticContext, fileName, "Пустой файл", true);
        }

        log.Debug("{FileName}: source bounds before insert {Bounds}", fileName, ExtentsUtils.FormatExtents(bounds.Value));

        RasterImagePathFixer.CopyImagesToTargetFolder(prepared.Db, targetSavePath, log);

        var worldBounds = InsertIntoTarget(inserter, targetDoc, prepared, layoutName, bounds.Value, log, diagnosticContext);

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

    private static Extents3d? InsertIntoTarget(BlockInserter inserter, Document targetDoc,
        PreparedSourceDatabase prepared, string layoutName, Extents3d bounds, Logger log,
        MergeDiagnosticContext diagnosticContext)
    {
        Extents3d? worldBounds;

        using (targetDoc.LockDocument())
        {
            DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-before-clone");

            worldBounds = inserter.InsertNativeObjects(
                targetDoc.Database,
                prepared.Db,
                layoutName,
                bounds,
                prepared.TargetVisualScale,
                prepared.LinearScaleMultiplier,
                diagnosticContext);

            if (worldBounds is not null)
            {
                DimensionStyleDiagnosticUtils.LogStyleSnapshot(targetDoc.Database, log, "target-after-clone");
            }
        }

        return worldBounds;
    }

    private static CombineResult LogFileFailedAndReturnWarn(MergeDiagnosticContext diagnosticContext, string fileName, string reason, bool isSkipped)
    {
        MergeDiagnostics.WriteEvent(diagnosticContext, "file.failed", new Dictionary<string, object?>
        {
            ["fileName"] = fileName,
            ["reason"] = reason,
            ["isSkipped"] = isSkipped
        });
        return CombineResult.Warn(fileName, reason);
    }
}
