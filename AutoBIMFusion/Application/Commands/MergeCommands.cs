using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Merge;
using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Application.Merge.Layouts.Transforms;
using AutoBIMFusion.Application.Merge.Models;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Diagnostics;
using System.Runtime.Versioning;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class MergeCommands
{
    private const string DiagnosticTestFolder = @"C:\Users\y.zhumabayev\Desktop\TEST";

    private readonly SemaphoreSlim _mergeGate = new(1, 1);

    [CommandMethod("MERGEDWG", CommandFlags.Modal | CommandFlags.Session)]
    public async void MergeDwgFolderCommand()
    {
        try
        {
            await MergeDwgFolderCommandAsync();
        }
        catch (System.Exception ex)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc is not null)
            {
                new AILog(doc.Editor).Error(ex, "Критическая ошибка запуска MERGEDWG");
            }
        }
    }

    [CommandMethod("MERGEDWG_DIAG_TEST", CommandFlags.Modal | CommandFlags.Session)]
    public async void MergeDwgDiagnosticTestCommand()
    {
        try
        {
            await MergeDwgFolderCommandAsync(DiagnosticTestFolder, showDialogs: false, commandName: "MERGEDWG_DIAG_TEST");
        }
        catch (System.Exception ex)
        {
            Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc is not null)
            {
                new AILog(doc.Editor).Error(ex, "Критическая ошибка запуска MERGEDWG_DIAG_TEST");
            }
        }
    }

    private async Task MergeDwgFolderCommandAsync()
    {
        await MergeDwgFolderCommandAsync(folderPath: null, showDialogs: true, commandName: "MERGEDWG");
    }

    private async Task MergeDwgFolderCommandAsync(string? folderPath, bool showDialogs, string commandName)
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        AILog log = new(doc.Editor);
        log.Info($"Запуск команды {commandName}...");
        bool dimensionDiagnostics = string.Equals(commandName, "MERGEDWG_DIAG_TEST", StringComparison.Ordinal);

        if (!await _mergeGate.WaitAsync(0))
        {
            log.Warn($"{commandName}: операция уже запущена.");
            log.Info($"Завершение команды {commandName}.");
            return;
        }

        try
        {
            if (dimensionDiagnostics)
            {
                DimensionTransformUtils.BeginDiagnosticRun();
            }

            await ExecuteMerge(doc, log, folderPath, showDialogs, dimensionDiagnostics);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка выполнения {commandName}");
        }
        finally
        {
            if (dimensionDiagnostics)
            {
                DimensionTransformUtils.LogDiagnosticSummary(log);
            }

            _ = _mergeGate.Release();
            log.Info($"Завершение команды {commandName}.");
        }
    }

    private static async Task ExecuteMerge(Document doc, AILog log, string? folderPath, bool showDialogs, bool dimensionDiagnostics)
    {
        string? sourceFolder = folderPath;

        if (string.IsNullOrWhiteSpace(sourceFolder) && !FolderSelector.TrySelectFolder(out sourceFolder))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(sourceFolder))
        {
            log.Warn("Исходная папка не задана.");
            return;
        }

        log.Info($"Исходная папка: {sourceFolder}");
        log.Info($"Файл лога: {LoggerFactory.GetCurrentLogFilePath()}");

        string savePath = BuildSavePath(sourceFolder);
        log.Info($"Путь сохранения: {savePath}");

        string[] dwgFiles = FileEnumerator.GetFiles(sourceFolder, log: log);

        if (dwgFiles.Length == 0)
        {
            log.Warn("DWG файлы не найдены.");

            if (showDialogs)
            {
                UiDialogService.ShowMessage("DWG-файлов нет!", "MERGEDWG");
            }

            return;
        }

        const double gapPercent = 0.1;
        MergeStatistics stats = new();
        Stopwatch sw = Stopwatch.StartNew();

        BlockInserter inserter = new(gapPercent, log);

        if (dimensionDiagnostics)
        {
            DimensionStyleDiagnosticUtils.LogDimensionStyleSnapshot(doc.Database, log, "before-merge");
        }

        await MergeFiles(dwgFiles, inserter, doc, stats, log, dimensionDiagnostics);

        using (doc.LockDocument())
        {
            RasterImagePathFixer.CopyImagesToTargetFolder(doc.Database, savePath, log);
            DwgOptimizer.Optimize(doc.Database, log);

            if (dimensionDiagnostics)
            {
                DimensionStyleDiagnosticUtils.LogDimensionStyleSnapshot(doc.Database, log, "before-save");
            }

            SaveMerged(doc.Database, savePath, log);

            doc.SendStringToExecute("._REGENALL ", true, false, false);
            doc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);
        }

        sw.Stop();
        log.Prefix = string.Empty;
        log.Info($"Завершено: {stats}");

        if (showDialogs)
        {
            ShowSummary(stats, sw.Elapsed, savePath);
        }
    }

    private static async Task MergeFiles(
        string[] files,
        BlockInserter inserter,
        Document doc,
        MergeStatistics stats,
        AILog log,
        bool dimensionDiagnostics)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        try
        {
            for (int idx = 0; idx < files.Length; idx++)
            {
                log.Prefix = $"[{idx + 1}/{files.Length}]";

                stats.RecordTotal();

                MergeResult result = await MergeCoordinator.MergeSingleFile(files[idx], inserter, doc, log);

                log.Info($"[{(result.Success ? "OK" : result.IsSkipped ? "SKIP" : "FAIL")}] {result.Message}");

                if (dimensionDiagnostics)
                {
                    DimensionStyleDiagnosticUtils.LogDimensionStyleSnapshot(doc.Database, log, $"after-file-{idx + 1}");
                }

                if (result.Success)
                {
                    stats.RecordSuccess();
                }
                else if (result.IsSkipped)
                {
                    stats.RecordSkipped();
                }
                else
                {
                    stats.RecordFailed();
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
        ArgumentException.ThrowIfNullOrEmpty(rootPath);
        DirectoryInfo dir = new(rootPath);
        DirectoryInfo? parent = dir.Parent;

        if (parent is null)
        {
            string name = string.IsNullOrEmpty(dir.Name) ? "MergedDrawings" : dir.Name;
            return Path.Combine(dir.FullName, $"{name}.dwg");
        }

        return Path.Combine(parent.FullName, $"{dir.Name}.dwg");
    }

    private static void SaveMerged(Database db, string savePath, AILog log)
    {
        try
        {
            if (File.Exists(savePath))
            {
                File.Delete(savePath);
            }

            using (new AcadWarningSuppressScope())
            {
                db.SaveAs(savePath, DwgVersion.AC1032);
            }

            log.Info($"Сохранено: {savePath}");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Сбой сохранения: {savePath}");
            throw;
        }
    }

    private static void ShowSummary(MergeStatistics stats, TimeSpan elapsed, string savePath)
    {
        string summary = stats.Failed == 0
            ? $"Завершено успешно.\nОбработано файлов: {stats.Successful}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}"
            : $"Завершено с ошибками.\nУспешно: {stats.Successful}\nПропущено: {stats.Skipped}\nОшибок: {stats.Failed}\nВремя: {elapsed:mm\\:ss\\.fff}\nСохранено в: {savePath}";

        UiDialogService.ShowMessage(summary, "MERGEDWG");
    }
}
