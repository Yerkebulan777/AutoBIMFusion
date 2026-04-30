using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Utils;
using AutoBIMFusion.Infrastructure.Logging;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Exports the first layout of a source DWG to a temporary Model Space-only DWG.
/// The method keeps the high-level pipeline here and delegates viewport projection
/// and scale normalization to <see cref="LayoutProjectionProcessor"/>.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class ViewportLayoutExporter
{
    public static Task<string> ExportToTempAsync(string sourceFilePath, string fileName, AILog log)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        string tempPath = BuildTempPath(fileName);

        using (Database db = new(false, true))
        {
            db.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            db.CloseInput(true);

            if (!LayoutUtil.TryFindFirstLayout(db, out string layoutName))
            {
                log.Warn($"{fileName}: листы не найдены");
                return Task.FromResult(string.Empty);
            }

            List<LayoutViewportInfo> vps = ViewportCollector.Collect(db, layoutName);

            Extents3d? frameBounds = LayoutProjectionProcessor.ProjectLayoutToModelSpace(db, layoutName, vps, log);

            log.Info($"VP: найдено {vps.Count}");

            if (frameBounds.HasValue)
            {
                int erased = ModelSpaceTrimmer.TrimOutside(db, frameBounds.Value, log);
                log.Info($"VP: очищено {erased} объектов");
            }

            ExtentsUtils.SyncUnits(db);
            log.Info($"VP: единицы нормализованы ({fileName})");

            using (new AcadWarningSuppressScope())
            {
                db.Insunits = UnitsValue.Millimeters;
                db.Measurement = MeasurementValue.Metric;
                db.SaveAs(tempPath, DwgVersion.AC1032);
            }
        }

        log.Info($"VP: экспорт завершен ({fileName})");
        return Task.FromResult(tempPath);
    }

    private static string BuildTempPath(string fileName)
    {
        string name = Path.GetFileNameWithoutExtension(fileName);
        return Path.Combine(Path.GetTempPath(), $"{name}-{Guid.NewGuid()}.dwg");
    }
}
