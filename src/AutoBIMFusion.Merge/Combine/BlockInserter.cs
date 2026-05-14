using AutoBIMFusion.Common.AcadSupport;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Combine.Layouts;
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
        double linearScaleMultiplier)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        var displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            ExtentsUtils.SyncUnits(targetDb);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);

            using ObjectIdCollection sourceIds = [];

            using (Transaction srcTrx = sourceDb.TransactionManager.StartTransaction())
            {
                StyleUnificationService.NormalizeTextStyleNames(sourceDb, srcTrx);
                StyleUnificationService.ApplyGostToAllStyles(sourceDb, srcTrx);

                var ms = (BlockTableRecord)srcTrx.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (id.IsValidForOperation())
                    {
                        _ = sourceIds.Add(id);
                    }
                }

                srcTrx.Commit();
            }

            if (sourceIds.Count == 0)
            {
                return null;
            }

            Extents3d? worldBounds = null;
            var clonedCount = 0;

            using Transaction targetTr = targetDb.TransactionManager.StartTransaction();

            using IdMapping map = new();
            using (new DatabaseUnitSyncScope(sourceDb, targetDb))
            {
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);
            }

            foreach (IdPair pair in map)
            {
                if (!pair.IsCloned || !pair.IsPrimary)
                {
                    continue;
                }

                if (targetTr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                {
                    ent.TransformBy(displacement);
                    clonedCount++;

                    Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
                    if (ext.HasValue)
                    {
                        worldBounds = worldBounds.HasValue
                            ? ExtentsUtils.Union(worldBounds.Value, ext.Value)
                            : ext.Value;
                    }
                }
            }

            DimensionStyleNormalizer.NormalizeClonedStyles(map, targetTr, targetVisualScale, linearScaleMultiplier);

            targetTr.Commit();

            if (clonedCount == 0)
            {
                log.Warning($"{sourceName}: не удалось клонировать объекты");
                return null;
            }

            worldBounds ??= ExtentsUtils.Transform(sourceBounds, displacement);

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;

            return worldBounds;
        }
        catch (Exception ex)
        {
            log.Error(ex, $"Ошибка вставки: {sourceName}");
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        var width = Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        var height = Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        var gap = Max(1.0, Round(Max(width, height) * gapPercent, 0));

        var insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;

        return new Point3d(insertX, -bounds.MinPoint.Y, 0);
    }
}
