using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Вставляет содержимое временных DWG как нативные объекты в Model Space целевого чертежа,
/// располагая их вдоль оси X с заданным зазором. Использует DuplicateRecordCloning.Ignore
/// для предотвращения перезаписи стилей и слоёв.
///
/// ВАЖНО: объекты вставляются как нативные сущности (не в блоке), чтобы сохранить
/// исходный вид и структуру объектов такими же, как в исходном файле.
/// </summary>
internal sealed class BlockInserter(double gapPercent, OperationLogger log)
{
    private readonly double _gapPercent = gapPercent;
    private readonly OperationLogger _log = log;
    private double _rightMax;
    private bool _hasPlacedObjects;

    /// <summary>
    /// Открывает временный DWG, клонирует все объекты из его Model Space
    /// в целевой чертёж как нативные сущности с учётом смещения.
    /// Возвращает мировые границы вставленных объектов или null при ошибке.
    /// </summary>
    public Extents3d? InsertNativeObjects(Database targetDb, string sourceFilePath, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            using Database sourceDb = new(false, true);
            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            ObjectIdCollection sourceIds = [];
            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);

            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIds.Add(id);
                    }
                }

                tr.Commit();
            }

            if (sourceIds.Count == 0)
            {
                _log.Warn($"{sourceName}: пустой Model Space");
                return null;
            }

            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            IdMapping map = [];
            sourceDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

            Extents3d? worldBounds = null;
            int clonedCount = 0;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary)
                    {
                        continue;
                    }

                    if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                    {
                        ent.TransformBy(displacement);
                        clonedCount++;

                        Extents3d? ext = GeometryUtils.TryGetExtents(ent);
                        if (ext.HasValue)
                        {
                            worldBounds = worldBounds.HasValue
                                ? GeometryUtils.Union(worldBounds.Value, ext.Value)
                                : ext.Value;
                        }
                    }
                }

                tr.Commit();
            }

            if (clonedCount == 0)
            {
                _log.Warn($"{sourceName}: не удалось клонировать объекты");
                return null;
            }

            if (!worldBounds.HasValue)
            {
                worldBounds = new Extents3d(
                    new Point3d(insertPt.X + sourceBounds.MinPoint.X, insertPt.Y + sourceBounds.MinPoint.Y, 0),
                    new Point3d(insertPt.X + sourceBounds.MaxPoint.X, insertPt.Y + sourceBounds.MaxPoint.Y, 0)
                );
            }

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;
            _log.Info($"{sourceName}: вставлено {clonedCount} нативных объектов");
            return worldBounds;
        }
        catch (System.Exception ex)
        {
            _log.Error(ex, $"Ошибка вставки: {sourceName}");
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = Math.Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        double height = Math.Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        double maxDimension = Math.Max(width, height);
        double gap = Math.Max(1.0, Math.Round(maxDimension * _gapPercent, 0));

        double insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;
        Point3d insertPt = new(insertX, -bounds.MinPoint.Y, 0);

        _log.Debug($"Позиция вставки: X={insertPt.X:F2}, Y={insertPt.Y:F2}, gap={gap:F0}");
        return insertPt;
    }
}
