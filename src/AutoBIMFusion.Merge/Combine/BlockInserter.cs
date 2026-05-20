using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;
using AutoBIMFusion.Merge.Diagnostics;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Вставляет содержимое DWG как нативные объекты в Model Space целевого чертежа,
///     располагая их вдоль оси X с заданным зазором.
/// </summary>
public sealed class BlockInserter(double gapPercent, Logger log)
{
    private bool _hasPlacedObjects;
    private double _rightMax;

    /// <summary>
    ///     Клонирует все сущности из Model Space исходного DWG в Model Space целевой базы,
    ///     затем смещает их в рассчитанную позицию для последовательной раскладки по оси X.
    ///     Пост-обработка размеров происходит после смещения, чтобы RecomputeDimensionBlock
    ///     использовал финальные координаты.
    /// </summary>
    public Extents3d? InsertNativeObjects(
        Database targetDb,
        Database sourceDb,
        string sourceName,
        Extents3d sourceBounds,
        double targetVisualScale,
        double linearScaleMultiplier,
        MergeDiagnosticContext? diagnosticContext = null)
    {
        try
        {
            ExtentsUtils.SyncUnits(targetDb);

            var sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            var targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);

            using ObjectIdCollection sourceIds = [];

            using (var srcTrx = sourceDb.TransactionManager.StartTransaction())
            {
                StyleUnificationService.NormalizeTextStyleNames(sourceDb, srcTrx);
                StyleUnificationService.ApplyGostToAllStyles(sourceDb, srcTrx);

                var ms = (BlockTableRecord)srcTrx.GetObject(sourceMsId, OpenMode.ForRead);

                HashSet<string> processedBlocks = [];

                foreach (var id in ms)
                    if (id.IsValidForOperation())
                    {
                        if (srcTrx.GetObject(id, OpenMode.ForWrite) is BlockReference blockRef)
                            BlockScaleApplier.NormalizeBlockScale(sourceDb, srcTrx, blockRef, processedBlocks);

                        _ = sourceIds.Add(id);
                    }

                srcTrx.Commit();
            }

            if (sourceIds.Count == 0) return null;

            var placementBounds = ExtentsUtils.ComputeLiveBounds(sourceDb, sourceIds) ?? sourceBounds;
            log.Debug("{SourceName}: placement bounds {Bounds}", sourceName, ExtentsUtils.FormatExtents(placementBounds));

            var insertPt = CalcInsertionPoint(placementBounds);
            var width = Max(0, placementBounds.MaxPoint.X - placementBounds.MinPoint.X);
            var height = Max(0, placementBounds.MaxPoint.Y - placementBounds.MinPoint.Y);
            var gap = CalcGap(placementBounds);
            var displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

            Extents3d? worldBounds = null;
            var clonedCount = 0;

            using var targetTr = targetDb.TransactionManager.StartTransaction();

            using IdMapping map = new();
            using (new DatabaseUnitSyncScope(sourceDb, targetDb))
            {
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);
            }

            foreach (IdPair pair in map)
            {
                if (!pair.IsCloned || !pair.IsPrimary) continue;

                if (targetTr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                {
                    ent.TransformBy(displacement);
                    clonedCount++;

                    var ext = ExtentsUtils.TryGetLiveExtents(ent, targetTr);
                    if (ext.HasValue)
                        worldBounds = worldBounds.HasValue
                            ? ExtentsUtils.Union(worldBounds.Value, ext.Value)
                            : ext.Value;
                }
            }

            DimensionStyleNormalizer.NormalizeClonedStyles(map, targetTr, targetVisualScale, linearScaleMultiplier);

            targetTr.Commit();

            if (clonedCount == 0)
            {
                log.Warning("{SourceName}: не удалось клонировать объекты", sourceName);
                return null;
            }

            worldBounds ??= ExtentsUtils.Transform(placementBounds, displacement);

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;

            MergeDiagnostics.WriteEvent(diagnosticContext, "insert.cloned", new Dictionary<string, object?>
            {
                ["sourceName"] = sourceName,
                ["sourceBounds"] = MergeDiagnostics.FormatExtents(sourceBounds),
                ["placementBounds"] = MergeDiagnostics.FormatExtents(placementBounds),
                ["insertPoint"] = MergeDiagnostics.FormatPoint(insertPt),
                ["width"] = width,
                ["height"] = height,
                ["gap"] = gap,
                ["clonedCount"] = clonedCount,
                ["worldBounds"] = MergeDiagnostics.FormatExtents(worldBounds),
                ["rightMax"] = _rightMax,
                ["targetVisualScale"] = targetVisualScale,
                ["linearScaleMultiplier"] = linearScaleMultiplier
            });

            return worldBounds;
        }
        catch (Exception ex)
        {
            log.Error(ex, "Ошибка вставки: {SourceName}", sourceName);
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        var gap = CalcGap(bounds);

        var insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;

        return new Point3d(insertX, -bounds.MinPoint.Y, 0);
    }

    private double CalcGap(Extents3d bounds)
    {
        var width = Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        var height = Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);

        return Max(1.0, Round(Max(width, height) * gapPercent, 0));
    }
}
