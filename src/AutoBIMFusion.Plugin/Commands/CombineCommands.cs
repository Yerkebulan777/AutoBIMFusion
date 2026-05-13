using AutoBIMFusion.Common;
using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Logging;
using AutoBIMFusion.Merge;
using AutoBIMFusion.Merge.Combine;
using AutoBIMFusion.Merge.Combine.Layouts;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Diagnostics;
using System.Runtime.Versioning;
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
        await ExecuteMergeAsync(null, true, "MERGEDWG");
    }

    private async Task ExecuteMergeAsync(string? folderPath, bool showDialogs, string commandName)
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;

        if (doc?.Database == null)
        {
            return;
        }

        Logger log = LoggerFactory.GetSharedLogger();

        if (!await _mergeGate.WaitAsync(0))
        {
            log.Warning($"{commandName}: операция уже запущена.");
            return;
        }

        try
        {
            string? sourceFolder = folderPath;

            if (string.IsNullOrWhiteSpace(sourceFolder) &&
                !UiDialogService.TrySelectFolder("Выберите папку с файлами DWG для объединения", out sourceFolder))
            {
                return;
            }

            string savePath = BuildSavePath(sourceFolder!);

            string[] dwgFiles = FileUtil.GetFiles(sourceFolder!, log: log);

            if (dwgFiles.Length == 0)
            {
                log.Warning("DWG файлы не найдены.");
                if (showDialogs)
                {
                    UiDialogService.ShowMessage("DWG-файлов нет!", commandName);
                }

                return;
            }

            log.Information(
                "{Command}: старт, files={FileCount}, source=\"{SourceFolder}\", save=\"{SavePath}\", log=\"{LogPath}\"",
                commandName,
                dwgFiles.Length,
                sourceFolder,
                savePath,
                LoggerFactory.GetCurrentLogFilePath());

            const double gapPercent = 0.1;
            CombineStatistics stats = new();
            Stopwatch sw = Stopwatch.StartNew();

            var docMgr = AcadApp.DocumentManager;
            Document mergeDoc = docMgr.Add(string.Empty);
            docMgr.MdiActiveDocument = mergeDoc;

            BlockInserter inserter = new(gapPercent, log);
            await MergeFiles(dwgFiles, inserter, mergeDoc, stats, log);

            using (mergeDoc.LockDocument())
            {
                RasterImagePathFixer.CopyImagesToTargetFolder(mergeDoc.Database, savePath, log);

                DimensionStyleDiagnosticUtils.LogStyleSnapshot(mergeDoc.Database, log, "target-after-merge");

                DwgOptimizer.Optimize(mergeDoc.Database, log);

                SaveMerged(mergeDoc.Database, savePath, log);

                mergeDoc.SendStringToExecute("._REGENALL ", true, false, false);
                mergeDoc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);
            }

            sw.Stop();

            log.Information($"Завершено: {stats}");

            if (showDialogs)
            {
                ShowSummary(stats, sw.Elapsed, savePath, commandName);
            }
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Ошибка {commandName}");
        }
        finally
        {
            _ = _mergeGate.Release();
        }
    }

    private static async Task MergeFiles(string[] files, BlockInserter inserter, Document doc, CombineStatistics stats,
        Logger log)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        try
        {
            for (int idx = 0; idx < files.Length; idx++)
            {
                stats.AddTotal();

                CombineResult result = await CombineOrchestrator.MergeSingleFile(files[idx], inserter, doc, log);

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

            log.Information($"Сохранено: {savePath}");
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
}
