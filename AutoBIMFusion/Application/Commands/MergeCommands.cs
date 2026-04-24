using AutoBIMFusion.Application.AcadSupport;
using Autodesk.AutoCAD.ApplicationServices;
using AutoBIMFusion.Application.Merge;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;

namespace AutoBIMFusion.Application.Commands;

[SupportedOSPlatform("Windows")]
public sealed class MergeCommands
{
    private readonly SemaphoreSlim _mergeGate = new(1, 1);

    [CommandMethod("MERGEDWG", CommandFlags.Session)]
    public void MergeDwgFolderCommand()
    {
        _ = MergeDwgFolderCommandAsync();
    }

    private async Task MergeDwgFolderCommandAsync()
    {
        Document? doc = AcadApp.DocumentManager.MdiActiveDocument;
        ArgumentNullException.ThrowIfNull(doc, nameof(doc));

        OperationLogger log = new(doc.Editor);
        log.Info("Запуск команды MERGEDWG...");

        if (!await _mergeGate.WaitAsync(0))
        {
            log.Warn("MERGEDWG: операция уже запущена.");
            log.Info("Завершение команды MERGEDWG.");
            return;
        }

        try
        {
            await ExecuteMerge(doc, log);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения MERGEDWG");
        }
        finally
        {
            _ = _mergeGate.Release();
            log.Info("Завершение команды MERGEDWG.");
        }
    }

    private static async Task ExecuteMerge(Document doc, OperationLogger log)
    {
        if (FolderSelector.TrySelectFolder(out string? folderPath))
        {
            log.Info($"Исходная папка: {folderPath}");
            string[] dwgFiles = FileEnumerator.GetFiles(folderPath, log: log);

            if (dwgFiles.Length == 0)
            {
                log.Warn("DWG файлы не найдены.");
                _ = MessageBox.Show("DWG-файлов нет!", "MERGEDWG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            const double gapPercent = 0.1;
            MergeStatistics stats = new();
            Stopwatch sw = Stopwatch.StartNew();

            string savePath = BuildSavePath(folderPath);
            BlockInserter inserter = new(gapPercent, log);

            using (doc.LockDocument())
            {
                await MergeFiles(dwgFiles, inserter, doc.Database, stats, log);

                RasterImagePathFixer.CopyImagesToTargetFolder(doc.Database, savePath, log);
                DwgOptimizer.Optimize(doc.Database, log);
                SaveMerged(doc.Database, savePath, log);

                sw.Stop();
                log.Prefix = string.Empty;
                log.Info($"Завершено: {stats}");

                doc.SendStringToExecute("._REGENALL ", true, false, false);
                doc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);

                ShowSummary(stats, sw.Elapsed, savePath);
            }
        }
    }

    private static async Task MergeFiles(string[] files, BlockInserter inserter, Database db, MergeStatistics stats, OperationLogger log)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        for (int idx = 0; idx < files.Length; idx++)
        {
            log.Prefix = $"[{idx + 1}/{files.Length}]";

            stats.RecordTotal();

            MergeResult result = await DwgMerger.MergeSingleFile(files[idx], inserter, db, log);

            log.Info($"[{(result.Success ? "OK" : result.IsSkipped ? "SKIP" : "FAIL")}] {result.Message}");

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

        pm.Stop();
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

    private static void SaveMerged(Database db, string savePath, OperationLogger log)
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

        MessageBoxIcon icon = stats.Failed > 0 ? MessageBoxIcon.Warning : MessageBoxIcon.Information;
        _ = MessageBox.Show(summary, "MERGEDWG", MessageBoxButtons.OK, icon);
    }
}
