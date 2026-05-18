using AutoBIMFusion.Common;
using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.Merge.Combine;
using AutoBIMFusion.Merge.Combine.Layouts;
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
    private readonly SemaphoreSlim _mergeGate = new(1, 1);

    [CommandMethod("MERGEDWG", CommandFlags.Modal | CommandFlags.Session)]
    public async void MergeDwgFolderCommand()
    {
        _ = await ExecuteMergeAsync(null, true, "MERGEDWG");
    }

    [CommandMethod("MERGEDWG_BATCH", CommandFlags.Modal | CommandFlags.Session)]
    public void MergeDwgBatchCommand()
    {
        string? statusPath = null;
        string? sourceFolder = null;

        DateTimeOffset startedAt = DateTimeOffset.Now;

        MergeExecutionResult result = MergeExecutionResult.Fail(null, "Пакетная команда не была выполнена.");

        Logger log = LoggerFactory.GetSharedLogger();

        try
        {
            Editor? editor = AcadApp.DocumentManager.MdiActiveDocument?.Editor;
            sourceFolder = PromptRequiredString(editor, "Папка DWG");
            statusPath = PromptRequiredString(editor, "Файл статуса");

            if (string.IsNullOrWhiteSpace(sourceFolder) || string.IsNullOrWhiteSpace(statusPath))
            {
                result = MergeExecutionResult.Fail(null, "Не переданы обязательные параметры пакетной команды.");
                return;
            }

            result = ExecuteMergeAsync(sourceFolder, false, "MERGEDWG_BATCH").GetAwaiter().GetResult();
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Error(ex, "Ошибка MERGEDWG_BATCH");
            result = MergeExecutionResult.Fail(null, ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            log.Error(ex, "Ошибка MERGEDWG_BATCH");
            result = MergeExecutionResult.Fail(null, ex.Message);
        }
        catch (IOException ex)
        {
            log.Error(ex, "Ошибка MERGEDWG_BATCH");
            result = MergeExecutionResult.Fail(null, ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            log.Error(ex, "Ошибка MERGEDWG_BATCH");
            result = MergeExecutionResult.Fail(null, ex.Message);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(statusPath))
            {
                WriteBatchStatus(statusPath, sourceFolder ?? string.Empty, result, startedAt, DateTimeOffset.Now);
            }
        }
    }

    private async Task<MergeExecutionResult> ExecuteMergeAsync(string? folderPath, bool showDialogs, string commandName)
    {
        Logger log = LoggerFactory.GetSharedLogger();

        if (!await _mergeGate.WaitAsync(0))
        {
            const string busyMessage = "Операция объединения уже выполняется.";
            log.Warning("{Command}: {Message}", commandName, busyMessage);
            return MergeExecutionResult.Fail(null, busyMessage);
        }

        try
        {
            string? sourceFolder = folderPath;

            if (string.IsNullOrWhiteSpace(sourceFolder) && !UiDialogService.TrySelectFolder("Выберите папку с файлами DWG для объединения", out sourceFolder))
            {
                const string cancelMessage = "Выбор папки отменён.";
                return MergeExecutionResult.Fail(null, cancelMessage);
            }

            string savePath = BuildSavePath(sourceFolder!);

            string[] dwgFiles = FileUtil.GetFiles(sourceFolder!, log: log);

            if (dwgFiles.Length == 0)
            {
                const string emptyFolderMessage = "DWG файлы не найдены.";
                log.Warning(emptyFolderMessage);

                if (showDialogs)
                {
                    UiDialogService.ShowMessage("DWG-файлов нет!", commandName);
                }

                return MergeExecutionResult.Fail(savePath, emptyFolderMessage);
            }

            const double gapPercent = 0.1;
            CombineStatistics stats = new();
            Stopwatch sw = Stopwatch.StartNew();

            DocumentCollection docMgr = AcadApp.DocumentManager;
            MergeDocumentSelection target = SelectMergeDocument(docMgr, log);
            Document mergeDoc = target.Document;

            BlockInserter inserter = new(gapPercent, log);
            await MergeFiles(dwgFiles, inserter, mergeDoc, stats, savePath, log);

            using (mergeDoc.LockDocument())
            {
                RasterImagePathFixer.CopyImagesToTargetFolder(mergeDoc.Database, savePath, log);

                DimensionStyleDiagnosticUtils.LogStyleSnapshot(mergeDoc.Database, log, "target-after-merge");

                DrawingPurger.Optimize(mergeDoc.Database, log);

                SaveMerged(mergeDoc.Database, savePath, log);
            }

            mergeDoc.SendStringToExecute("._REGENALL ", true, false, false);
            mergeDoc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);

            sw.Stop();
            log.Information(
                "{Command}: завершено, {Stats}, save=\"{SavePath}\", elapsed={Elapsed}",
                commandName,
                stats,
                savePath,
                sw.Elapsed);

            if (showDialogs)
            {
                ShowSummary(stats, sw.Elapsed, savePath, commandName);
            }

            string message = stats.Failed == 0
                ? "Завершено успешно."
                : $"Завершено с ошибками. Успешно: {stats.Successful}, пропущено: {stats.Skipped}, ошибок: {stats.Failed}.";

            return new MergeExecutionResult(stats.Failed == 0, savePath, message);
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Ошибка {commandName}");
            return MergeExecutionResult.Fail(null, ex.Message);
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

    private static void WriteBatchStatus(string statusPath, string folderPath, MergeExecutionResult result, DateTimeOffset startedAt, DateTimeOffset finishedAt)
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
            startedAt,
            finishedAt
        };

        JsonSerializerOptions options = new()
        {
            WriteIndented = true
        };

        File.WriteAllText(statusPath, JsonSerializer.Serialize(payload, options));
    }

    private static MergeDocumentSelection SelectMergeDocument(DocumentCollection docMgr, Logger log)
    {
        Document? activeDoc = docMgr.MdiActiveDocument;

        if (activeDoc is not null && CanUseActiveDocument(activeDoc, log))
        {
            return new MergeDocumentSelection(activeDoc, false);
        }

        Document mergeDoc = docMgr.Add(string.Empty);
        docMgr.MdiActiveDocument = mergeDoc;
        return new MergeDocumentSelection(mergeDoc, true);
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

                if (!isEmpty)
                {
                    return false;
                }

                return true;
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Warning(ex, "MERGEDWG: не удалось проверить текущий документ \"{DocumentName}\", результат будет собран во временном документе.", doc.Name);
            return false;
        }
    }

    private static bool IsDrawingContentEmpty(Database db)
    {
        using Transaction tr = db.TransactionManager.StartOpenCloseTransaction();
        BlockTable blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

        if (!IsBlockRecordEmpty(blockTable[BlockTableRecord.ModelSpace], tr))
        {
            tr.Commit();
            return false;
        }

        DBDictionary layoutDictionary = (DBDictionary)tr.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        foreach (DBDictionaryEntry entry in layoutDictionary)
        {
            Layout layout = (Layout)tr.GetObject(entry.Value, OpenMode.ForRead);

            if (!layout.ModelType && !IsBlockRecordEmpty(layout.BlockTableRecordId, tr))
            {
                tr.Commit();
                return false;
            }
        }

        tr.Commit();
        return true;
    }

    private static bool IsBlockRecordEmpty(ObjectId blockRecordId, Transaction tr)
    {
        BlockTableRecord blockRecord = (BlockTableRecord)tr.GetObject(blockRecordId, OpenMode.ForRead);

        foreach (ObjectId entityId in blockRecord)
        {
            Entity entity = (Entity)tr.GetObject(entityId, OpenMode.ForRead);

            if (entity is not Viewport)
            {
                return false;
            }
        }

        return true;
    }

    private static async Task MergeFiles(string[] files, BlockInserter inserter, Document doc, CombineStatistics stats, string savePath, Logger log)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        try
        {
            for (int idx = 0; idx < files.Length; idx++)
            {
                stats.AddTotal();

                CombineResult result = await CombineOrchestrator.MergeSingleFile(files[idx], inserter, doc, log, savePath);

                if (result.Success)
                {
                    stats.AddSuccess();
                }
                else if (result.IsSkipped)
                {
                    stats.AddSkipped();
                }
                else
                {
                    stats.AddFailed();
                }

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
        try
        {
            string? dir = Path.GetDirectoryName(savePath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
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
        catch (Exception ex)
        {
            log.Error(ex, $"Сбой сохранения: {savePath}");
            throw;
        }
    }

    private static void ShowSummary(CombineStatistics stats, TimeSpan elapsed, string savePath, string commandName)
    {
        string summary = stats.Failed == 0
            ? $"Завершено успешно.\nОбработано файлов: {stats.Successful}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}"
            : $"Завершено с ошибками.\nУспешно: {stats.Successful}\nПропущено: {stats.Skipped}\nОшибок: {stats.Failed}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}";

        UiDialogService.ShowMessage(summary, commandName);
    }

    private readonly record struct MergeDocumentSelection(Document Document, bool CloseAfterSave);

    private readonly record struct MergeExecutionResult(bool Success, string? SavePath, string Message)
    {
        public static MergeExecutionResult Fail(string? savePath, string message)
        {
            return new MergeExecutionResult(false, savePath, message);
        }
    }


}
