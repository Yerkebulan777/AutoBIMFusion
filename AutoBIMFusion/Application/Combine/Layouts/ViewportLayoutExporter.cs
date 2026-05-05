using AutoBIMFusion.Application.Utils;
using Serilog.Core;
using System.Runtime.Versioning;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Экспортирует первый макет исходного файла DWG во временный файл DWG, содержащий только пространство модели.
/// Данный метод сохраняет за собой управление общим процессом и делегирует обработку проекции окна просмотра
/// и нормализацию масштаба объекту <see cref="LayoutProjectionProcessor"/>.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class ViewportLayoutExporter
{
    public static Database? PrepareDatabaseForMerge(string sourceFilePath, string fileName, Logger log)
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
                log.Warning($"{fileName}: листы не найдены");
                return null;
            }

            List<ViewportInfo> vps = ViewportCollector.Collect(db, layoutName);

            LayoutProjectionProcessor.LayoutProjectionResult projection = LayoutProjectionProcessor.ProjectLayoutToModelSpace(db, layoutName, vps, log);

            if (projection.FrameBounds.HasValue)
            {
                _ = ModelSpaceTrimmer.TrimOutside(db, projection.FrameBounds.Value, log);
            }

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "before-normalize");

            DimensionStyleNormalizer.NormalizeModelSpaceDimensions(
                db,
                projection.DimensionScales,
                projection.FallbackMultiplier,
                projection.ClampRatio,
                log);

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "after-normalize");

            return db;
        }
        catch (System.Exception)
        {
            db.Dispose();
            throw;
        }
    }
}
