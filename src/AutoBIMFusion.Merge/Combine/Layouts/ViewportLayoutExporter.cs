using AutoBIMFusion.Common;
using Serilog.Core;
using System.Runtime.Versioning;

using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;

namespace AutoBIMFusion.Merge.Layouts;

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
/// Экспортирует первый макет исходного файла DWG во временный файл DWG, содержащий только пространство модели.
/// Данный метод сохраняет за собой управление общим процессом и делегирует обработку проекции окна просмотра
/// и нормализацию масштаба объекту <see cref="LayoutProjectionProcessor"/>.
/// </summary>
[SupportedOSPlatform("Windows")]
internal static class ViewportLayoutExporter
{
    public static PreparedSourceDatabase? PrepareDatabaseForMerge(string sourceFilePath, string fileName, Logger log)
    {
        ArgumentNullException.ThrowIfNull(sourceFilePath);

        Database db = new(false, true);

        try
        {
            db.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            db.CloseInput(true);

            ExtentsUtils.SyncUnits(db);

            using (Transaction trx = db.TransactionManager.StartTransaction())
            {
                BlockTable bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);

                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForWrite);

                    if (!btr.IsFromExternalReference)
                    {
                        btr.Units = UnitsValue.Millimeters;
                    }
                }
                trx.Commit();
            }

            if (!LayoutUtil.TryFindFirstLayout(db, out string layoutName))
            {
                log.Warning($"{fileName}: листы не найдены");
                db.Dispose();
                return null;
            }

            List<ViewportInfo> vps = ViewportCollector.Collect(db, layoutName);

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "source-before-normalize");

            LayoutProjectionProcessor.LayoutProjectionResult projection = LayoutProjectionProcessor.ProjectLayoutToModelSpace(db, layoutName, vps, log);

            if (projection.FrameBounds.HasValue)
            {
                _ = ModelSpaceTrimmer.TrimOutside(db, projection.FrameBounds.Value, log);
            }

            DimensionStyleDiagnosticUtils.LogStyleSnapshot(db, log, "source-after-normalize-before-clone");

            return new PreparedSourceDatabase(db, projection.TargetVisualScale, projection.LinearScaleMultiplier);
        }
        catch (System.Exception)
        {
            db.Dispose();
            throw;
        }
    }
}


