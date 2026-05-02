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
    public static Database? PrepareDatabaseForMerge(string sourceFilePath, string fileName, AILog log)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        Database db = new(false, true);

        try
        {
            db.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            db.CloseInput(true);

            db.Insunits = UnitsValue.Millimeters;
            db.Measurement = MeasurementValue.Metric;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);

                    if (!btr.IsFromExternalReference)
                    {
                        btr.Units = UnitsValue.Millimeters;
                    }
                }
                tr.Commit();
            }

            if (!LayoutUtil.TryFindFirstLayout(db, out string layoutName))
            {
                log.Warn($"{fileName}: листы не найдены");
                return null;
            }

            List<LayoutViewportInfo> vps = ViewportCollector.Collect(db, layoutName);

            Extents3d? frameBounds = LayoutProjectionProcessor.ProjectLayoutToModelSpace(db, layoutName, vps, log);

            if (frameBounds.HasValue)
            {
                _ = ModelSpaceTrimmer.TrimOutside(db, frameBounds.Value, log);
            }

            return db;
        }
        catch (System.Exception)
        {
            db.Dispose();
            throw;
        }
    }
}
