using AutoBIMFusion.Common;
using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.Merge.Combine;
using AutoBIMFusion.Merge.Combine.Layouts;
using AutoBIMFusion.Merge.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.Json;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AutoBIMFusion.Plugin.Commands;

[SupportedOSPlatform("Windows")]
public sealed class CombineCommands
{
    private const double gapPercent = 0.1; // Зазор 10%
    private static readonly SemaphoreSlim _mergeGate = new(1, 1);
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    [CommandMethod("MERGEDWG", CommandFlags.Modal | CommandFlags.Session)]
    public static void MergeDwgFolderCommand()
    {
        Logger log = LoggerFactory.GetSharedLogger();
        ExecutionResult result = ExecuteMerge(null, true, "MERGEDWG");

        if (!result.Success)
        {
            log.Warning("MERGEDWG: {Message}", result.Message);
        }
    }

    [CommandMethod("MERGEDWG_BATCH", CommandFlags.Modal | CommandFlags.Session)]
    public static void MergeDwgBatchCommand()
    {
        string? statusPath = null;
        string? sourceFolder = null;

        DateTimeOffset startedAt = DateTimeOffset.Now;

        ExecutionResult result = default;

        Logger log = LoggerFactory.GetSharedLogger();

        try
        {
            Editor? editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;

            sourceFolder = PromptRequiredString(editor, "Папка DWG");
            statusPath = PromptRequiredString(editor, "Файл статуса");

            if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(statusPath))
            {
                result = ExecutionResult.Fail(null, "Не переданы обязательные параметры пакетной команды.");
                return;
            }

            result = ExecuteMerge(sourceFolder, false, "MERGEDWG_BATCH");
        }
        catch (Exception ex)
        {
            log.Error(ex, "MERGEDWG_BATCH");
            result = ExecutionResult.Fail(null, ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(statusPath))
            {
                try
                {
                    WriteBatchStatus(statusPath, sourceFolder ?? string.Empty, result, startedAt, DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    log.Error(ex, "MERGEDWG_BATCH: не удалось записать статус в {StatusPath}", statusPath);
                }
            }
        }
    }

    private static ExecutionResult ExecuteMerge(string? folderPath, bool showDialogs, string commandName)
    {
        Logger log = LoggerFactory.GetSharedLogger();

        if (!_mergeGate.Wait(0))
        {
            const string busyMessage = "Операция объединения уже выполняется.";
            log.Warning("{Command}: {Message}", commandName, busyMessage);
            return ExecutionResult.Fail(null, busyMessage);
        }

        using AcadWarningSuppressScope warningSuppress = new();

        try
        {
            string? sourceFolder = folderPath ?? (UiDialogService.TrySelectFolder("Выберите папку с файлами DWG", out string? selectedFolder) ? selectedFolder : null);

            if (sourceFolder is null)
            {
                return ExecutionResult.Fail(null, "Выбор папки отменён.");
            }

            string savePath = BuildSavePath(sourceFolder);
            string[] dwgFiles = FileUtil.GetFiles(sourceFolder);

            if (dwgFiles.Length == 0)
            {
                if (showDialogs)
                {
                    UiDialogService.ShowMessage("DWG-файлов нет!", commandName);
                }

                return ExecutionResult.Fail(savePath, "DWG файлы не найдены.");
            }

            CombineStatistics stats = new();
            Stopwatch sw = Stopwatch.StartNew();

            MergeDocumentSelection target = SelectMergeDocument(AcadApp.DocumentManager, log);
            Document mergeDoc = target.Document;

            BlockInserter inserter = new(gapPercent, log);
            MergeFiles(dwgFiles, inserter, mergeDoc, stats, savePath, log);

            using (mergeDoc.LockDocument())
            {
                RasterImagePathFixer.CopyImagesToTargetFolder(mergeDoc.Database, savePath, log);
                DimensionStyleDiagnosticUtils.LogStyleSnapshot(mergeDoc.Database, log, "target-after-merge");
                DrawingPurger.Optimize(mergeDoc.Database, log);
                SaveMerged(mergeDoc.Database, savePath, log);
                TryRunPostMergeViewCommands(mergeDoc, log);
            }

            sw.Stop();

            log.Information("{Command}: завершено, {Stats}, save=\"{SavePath}\", elapsed={Elapsed}", commandName, stats, savePath, sw.Elapsed);

            if (showDialogs)
            {
                ShowSummary(stats, sw.Elapsed, savePath, commandName);
            }

            return new ExecutionResult(stats.Failed == 0, savePath,
                stats.Failed == 0 ? "Завершено успешно." : $"Завершено с ошибками. {stats}");
        }
        catch (Exception ex)
        {
            log.Error(ex, "Ошибка {Command}", commandName);
            return ExecutionResult.Fail(null, ex.Message);
        }
        finally
        {
            _ = _mergeGate.Release();
        }
    }

    private static string? PromptRequiredString(Editor? editor, string promptName)
    {
        if (editor is null)
        {
            return null;
        }

        PromptStringOptions options = new($"\n{promptName}: ")
        {
            AllowSpaces = true
        };

        PromptResult result = editor.GetString(options);
        return result.Status == PromptStatus.OK
            ? result.StringResult.Trim().Trim('"')
            : null;
    }

    private static void WriteBatchStatus(string statusPath, string folderPath, ExecutionResult result, DateTimeOffset startedAt, DateTimeOffset finishedAt)
    {
        string? dir = Path.GetDirectoryName(statusPath);

        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        var payload = new
        {
            folderPath,
            success = result.Success,
            savePath = result.SavePath,
            message = result.Message,
            logPath = LoggerFactory.GetCurrentLogFilePath(),
            diagnosticPath = MergeDiagnostics.GetCurrentDiagnosticFilePath(),
            startedAt,
            finishedAt
        };

        File.WriteAllText(statusPath, JsonSerializer.Serialize(payload, _jsonOptions));
    }

    private static MergeDocumentSelection SelectMergeDocument(DocumentCollection docMgr, Logger log)
    {
        Document? activeDoc = docMgr.MdiActiveDocument;

        if (activeDoc is not null && CanUseActiveDocument(activeDoc, log))
        {
            return new MergeDocumentSelection(activeDoc);
        }

        Document mergeDoc = docMgr.Add(string.Empty);
        docMgr.MdiActiveDocument = mergeDoc;
        return new MergeDocumentSelection(mergeDoc);
    }

    private static bool CanUseActiveDocument(Document doc, Logger log)
    {
        if (doc.IsNamedDrawing)
        {
            return false;
        }

        try
        {
            using (doc.LockDocument())
            {
                bool isEmpty = IsDrawingContentEmpty(doc.Database);

                return isEmpty;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Warning(ex,
                "MERGEDWG: не удалось проверить текущий документ \"{DocumentName}\", результат будет собран во временном документе.",
                doc.Name);
            return false;
        }
    }

    private static bool IsDrawingContentEmpty(Database db)
    {
        using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();

        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (!IsBlockRecordEmpty(blockTable[BlockTableRecord.ModelSpace], tr))
        {
            return false;
        }

        DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        foreach (DBDictionaryEntry entry in layoutDictionary)
        {
            Layout layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);

            if (!layout.ModelType && !IsBlockRecordEmpty(layout.BlockTableRecordId, tr))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsBlockRecordEmpty(ObjectId blockRecordId, Transaction tr)
    {
        BlockTableRecord blockRecord = (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead);

        foreach (ObjectId entityId in blockRecord)
        {
            if (tr.GetObject(entityId, OpenMode.ForRead) is not Viewport)
            {
                return false;
            }
        }

        return true;
    }

    private static void TryRunPostMergeViewCommands(Document mergeDoc, Logger log)
    {
        try
        {
            mergeDoc.Editor.Command("._REGENALL");
            mergeDoc.Editor.Command("._ZOOM", "_EXTENTS");
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Warning(ex, "Post-merge view command skipped: {Message}", ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            log.Warning(ex, "Post-merge view command skipped: {Message}", ex.Message);
        }
    }

    private static void MergeFiles(string[] files, BlockInserter inserter, Document doc, CombineStatistics stats, string savePath, Logger log)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        try
        {
            for (int idx = 0; idx < files.Length; idx++)
            {
                CombineResult result = CombineOrchestrator.MergeSingleFile(files[idx], inserter, doc, log, savePath);
                stats.Update(result);
                pm.MeterProgress();
            }
        }
        finally
        {
            pm.Stop();
        }
    }

    private static string BuildSavePath(string rootPath)
    {
        DirectoryInfo dir = new(rootPath);
        DirectoryInfo? parent = dir.Parent;

        const string buildSuffix = "-сборка";
        string outputFolderName = $"{dir.Name}{buildSuffix}";
        string outputFileName = $"{dir.Name}.dwg";

        return parent is null
            ? Path.Combine(dir.FullName, outputFolderName, outputFileName)
            : Path.Combine(parent.FullName, outputFolderName, outputFileName);
    }

    private static void SaveMerged(Database db, string savePath, Logger log)
    {
        string? dir = Path.GetDirectoryName(savePath);
        if (!string.IsNullOrEmpty(dir))
        {
            _ = Directory.CreateDirectory(dir);
        }

        if (File.Exists(savePath))
        {
            File.Delete(savePath);
        }

        using (new AcadWarningSuppressScope())
        {
            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "target-before-save");
            db.SaveAs(savePath, DwgVersion.AC1032);
        }
    }

    private static void ShowSummary(CombineStatistics stats, TimeSpan elapsed, string savePath, string commandName)
    {
        string summary = stats.Failed == 0
            ? $"Завершено успешно.\nОбработано файлов: {stats.Successful}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}"
            : $"Завершено с ошибками.\nУспешно: {stats.Successful}\nПропущено: {stats.Skipped}\nОшибок: {stats.Failed}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}";

        UiDialogService.ShowMessage(summary, commandName);
    }

    private readonly record struct MergeDocumentSelection(Document Document);

    private readonly record struct ExecutionResult(bool Success, string? SavePath, string Message)
    {
        public static ExecutionResult Fail(string? savePath, string message)
        {
            return new ExecutionResult(false, savePath, message);
        }
    }



}
