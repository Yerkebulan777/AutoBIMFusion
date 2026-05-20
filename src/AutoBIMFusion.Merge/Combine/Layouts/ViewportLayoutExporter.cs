using System.Runtime.Versioning;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Diagnostics;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal sealed record PreparedSourceDatabase(
    Database Db,
    double TargetVisualScale,
    double LinearScaleMultiplier) : IDisposable
{
    public void Dispose()
    {
        Db.Dispose();
    }
}

/// <summary>
///     Экспортирует первый макет исходного файла DWG во временный файл DWG, содержащий только пространство модели.
///     Данный метод сохраняет за собой управление общим процессом и делегирует обработку проекции окна просмотра
///     и нормализацию масштаба объекту <see cref="LayoutProjectionProcessor" />.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class ViewportLayoutExporter
{
    public static PreparedSourceDatabase? PrepareDatabaseForMerge(
        string sourceFilePath,
        string fileName,
        Logger log,
        MergeDiagnosticContext? diagnosticContext = null)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        Database db = new(false, true);

        try
        {
            db.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            db.CloseInput(true);

            ExtentsUtils.SyncUnits(db);

            using (var trx = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (var btrId in bt)
                {
                    var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForWrite);

                    if (!btr.IsFromExternalReference) btr.Units = UnitsValue.Millimeters;
                }

                trx.Commit();
            }

            if (!LayoutUtil.TryFindFirstLayout(db, out var layoutName))
            {
                log.Warning("{FileName}: листы не найдены", fileName);
                db.Dispose();
                return null;
            }

            MergeDiagnostics.WriteEvent(diagnosticContext, "layout.selected", new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["layoutName"] = layoutName
            });

            var vps = ViewportCollector.Collect(db, layoutName);

            MergeDiagnostics.WriteEvent(diagnosticContext, "viewport.collected", new Dictionary<string, object?>
            {
                ["fileName"] = fileName,
                ["layoutName"] = layoutName,
                ["viewportCount"] = vps.Count,
                ["viewports"] = vps.Select(vp => new Dictionary<string, object?>(StringComparer.Ordinal)
                {
                    ["number"] = vp.Number,
                    ["customScale"] = vp.CustomScale,
                    ["viewTwist"] = vp.ViewTwist,
                    ["centerPaper"] = MergeDiagnostics.FormatPoint(vp.CenterPaper),
                    ["viewCenter"] = MergeDiagnostics.FormatPoint(vp.ViewCenter),
                    ["modelWindow"] = MergeDiagnostics.FormatExtents(vp.ModelWindow)
                }).ToArray()
            });

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "source-before-normalize");

            var projection = LayoutProjectionProcessor.ProjectLayoutToModelSpace(db, layoutName, vps, log, diagnosticContext);

            if (projection.FrameBounds.HasValue) OutOfFrameEntityCleaner.Clean(db, projection.FrameBounds.Value, log);

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "source-after-normalize-before-clone");

            return new PreparedSourceDatabase(db, projection.TargetVisualScale, projection.LinearScaleMultiplier);
        }
        catch (Exception)
        {
            db.Dispose();
            throw;
        }
    }
}
