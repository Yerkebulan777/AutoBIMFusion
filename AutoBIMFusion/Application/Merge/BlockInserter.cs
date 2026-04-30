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
internal sealed class BlockInserter(double gapPercent, AILog log)
{
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
            ObjectIdCollection sourceIds = [];

            using Database sourceDb = new(false, true);

            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            UnitsValue sourceInsunitsBefore = sourceDb.Insunits;
            UnitsValue targetInsunits = targetDb.Insunits;
            bool insunitsChanged = sourceInsunitsBefore != targetInsunits;
            if (insunitsChanged)
            {
                sourceDb.Insunits = targetInsunits;
            }

            MeasurementValue sourceMeasurementBefore = sourceDb.Measurement;
            MeasurementValue targetMeasurement = targetDb.Measurement;
            bool measurementChanged = sourceMeasurementBefore != targetMeasurement;
            if (measurementChanged)
            {
                sourceDb.Measurement = targetMeasurement;
            }

            log.Info(
                $"[INSUNITS] source={sourceName}, sourceBefore={sourceInsunitsBefore}, " +
                $"target={targetInsunits}, sourceAfter={sourceDb.Insunits}, synced={insunitsChanged}");
            log.Info(
                $"[MEASUREMENT] source={sourceName}, sourceBefore={sourceMeasurementBefore}, " +
                $"target={targetMeasurement}, sourceAfter={sourceDb.Measurement}, synced={measurementChanged}");

            sourceDb.CloseInput(true);

            DimensionStyleDiagnosticUtils.LogNewStylesBeforeMerge(sourceDb, targetDb, log);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            int sourceDimensionCount = 0;

            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIds.Add(id);
                        if (id.ObjectClass.DxfName == "DIMENSION")
                        {
                            sourceDimensionCount++;
                        }
                    }
                }

                tr.Commit();
            }

            if (sourceIds.Count == 0)
            {
                log.Warn($"{sourceName}: пустой Model Space");
                return null;
            }

            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            IdMapping map = [];
            Extents3d? worldBounds = null;
            int clonedCount = 0;
            int targetDimensionCount = 0;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                // Клонируем объекты из временной базы в целевую
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary)
                    {
                        continue;
                    }

                    if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                    {
                        if (ent is Dimension)
                        {
                            targetDimensionCount++;
                        }

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

                tr.Commit();
            }

            if (clonedCount == 0)
            {
                log.Warn($"{sourceName}: не удалось клонировать объекты");
                return null;
            }

            if (!worldBounds.HasValue)
            {
                worldBounds = ExtentsUtils.Transform(sourceBounds, displacement);
            }

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;
            log.Info(
                $"{sourceName}: вставлено {clonedCount} нативных объектов, " +
                $"sourceDimensions={sourceDimensionCount}, targetDimensions={targetDimensionCount}");
            return worldBounds;
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            log.Error(ex, $"Ошибка AutoCAD API при вставке: {sourceName}");
            return null;
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка вставки: {sourceName}");
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        double height = Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        double maxDimension = Max(width, height);
        double gap = Max(1.0, Round(maxDimension * gapPercent, 0));

        double insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;
        Point3d insertPt = new(insertX, -bounds.MinPoint.Y, 0);

        log.Debug($"Позиция вставки: X={insertPt.X:F2}, Y={insertPt.Y:F2}, gap={gap:F0}");
        return insertPt;
    }
}
