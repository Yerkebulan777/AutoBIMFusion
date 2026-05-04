using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Combine;
using AutoBIMFusion.Application.Combine.Layouts;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using Serilog.Core;
using System.Diagnostics;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class CombineCommands
{
    private readonly SemaphoreSlim _mergeGate = new(1, 1);

    [CommandMethod("MERGEDWG", CommandFlags.Modal | CommandFlags.Session)]
    public async void MergeDwgFolderCommand()
    {
        await ExecuteMergeAsync(folderPath: null, showDialogs: true, commandName: "MERGEDWG");
    }

    private async Task ExecuteMergeAsync(string? folderPath, bool showDialogs, string commandName)
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        if (doc?.Database == null)
        {
            return;
        }

        Logger log = LoggerFactory.GetSharedLogger();
        log.Information($"Запуск {commandName}...");

        if (!await _mergeGate.WaitAsync(0))
        {
            log.Warning($"{commandName}: операция уже запущена.");
            return;
        }

        try
        {
            string? sourceFolder = folderPath;
            if (string.IsNullOrWhiteSpace(sourceFolder) && !UiDialogService.TrySelectFolder("Выберите папку с файлами DWG для объединения", out sourceFolder))
            {
                return;
            }

            log.Information($"Исходная папка: {sourceFolder}");
            log.Information($"Файл лога: {LoggerFactory.GetCurrentLogFilePath()}");

            string savePath = BuildSavePath(sourceFolder!);
            log.Information($"Путь сохранения: {savePath}");

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

            const double gapPercent = 0.1;
            CombineStatistics stats = new();
            Stopwatch sw = Stopwatch.StartNew();

            BlockInserter inserter = new(gapPercent, log);
            await MergeFiles(dwgFiles, inserter, doc, stats, log);

            using (doc.LockDocument())
            {
                RasterImagePathFixer.CopyImagesToTargetFolder(doc.Database, savePath, log);

                DimensionStyleDiagnosticUtils.LogStyleSnapshot(doc.Database, log, "after-merge");

                DwgOptimizer.Optimize(doc.Database, log);

                SaveMerged(doc.Database, savePath, log);

                doc.SendStringToExecute("._REGENALL ", true, false, false);
                doc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);
            }

            sw.Stop();

            log.Information($"Завершено: {stats}");

            if (showDialogs)
            {
                ShowSummary(stats, sw.Elapsed, savePath, commandName);
            }
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка {commandName}");
        }
        finally
        {
            _ = _mergeGate.Release();
            log.Information($"Завершение {commandName}.");
        }
    }

    private static async Task MergeFiles(string[] files, BlockInserter inserter, Document doc, CombineStatistics stats, Logger log)
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

        if (parent is null)
        {
            return Path.Combine(dir.FullName, $"{dir.Name}.dwg");
        }

        return Path.Combine(parent.FullName, $"{dir.Name}.dwg");
    }

    private static void SaveMerged(Database db, string savePath, Logger log)
    {
        try
        {
            string? dir = Path.GetDirectoryName(savePath);

            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            using (new AcadWarningSuppressScope())
            {
                db.SaveAs(savePath, DwgVersion.AC1032);
            }

            log.Information($"Сохранено: {savePath}");
        }
        catch (System.Exception ex)
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
