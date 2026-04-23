using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Merge;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using Autodesk.AutoCAD.ApplicationServices;
using System.Diagnostics;
using System.Runtime.Versioning;
using System.Windows.Forms;

using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

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

        if (!await _mergeGate.WaitAsync(0))
        {
            log.Warn("Операция MERGEDWG уже выполняется.");
            return;
        }

        try
        {
            await ExecuteMerge(doc, log);
        }
        catch (System.Exception ex)
        {
            log.Error(ex, "Ошибка выполнения команды");
        }
        finally
        {
            _ = _mergeGate.Release();
        }
    }

    private static async Task ExecuteMerge(Document doc, OperationLogger log)
    {
        if (FolderSelector.TrySelectFolder(out string? folderPath))
        {
            log.Info($"Выбрана папка: {folderPath}");
            string[] dwgFiles = FileEnumerator.GetFiles(folderPath, log: log);

            if (dwgFiles.Length == 0)
            {
                log.Warn("В выбранной папке и подпапках нет DWG файлов.");
                _ = MessageBox.Show("DWG-файлов нет!", "MERGEDWG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using (doc.LockDocument())
            {
                const double gapPercent = 0.1;
                MergeStatistics stats = new();
                Stopwatch sw = Stopwatch.StartNew();

                string savePath = BuildSavePath(folderPath);

                DwgMerger merger = new(gapPercent, log);

                await MergeFiles(dwgFiles, merger, doc.Database, stats, log);

                RasterImagePathFixer.CopyImagesToTargetFolder(doc.Database, savePath, log);
                SaveMerged(doc.Database, savePath, log);

                sw.Stop();
                log.Prefix = string.Empty;
                log.Info($"Готово: {stats}");

                // Обновляем чертёж и вид после сохранения
                doc.SendStringToExecute("._REGENALL ", true, false, false);
                doc.SendStringToExecute("._ZOOM _EXTENTS ", true, false, false);

                ShowSummary(stats, sw.Elapsed, savePath);
            }
        }
    }

    private static async Task MergeFiles(string[] files, DwgMerger merger, Database db, MergeStatistics stats, OperationLogger log)
    {
        using ProgressMeter pm = new();
        pm.Start("Объединение файлов DWG...");
        pm.SetLimit(files.Length);

        for (int idx = 0; idx < files.Length; idx++)
        {
            log.Prefix = $"[{idx + 1}/{files.Length}]";

            stats.RecordTotal();

            MergeResult result = await merger.MergeSingleFile(files[idx], db);

            log.Info($"Результат: {(result.Success ? "OK" : result.IsSkipped ? "SKIP" : "FAIL")} - {result.Message}");

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

            log.Info($"Файл сохранен: {savePath}");
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка сохранения результата: {savePath}");
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
