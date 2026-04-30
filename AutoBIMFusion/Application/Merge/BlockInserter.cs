using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Вставляет содержимое DWG как нативные объекты в Model Space целевого чертежа,
/// располагая их вдоль оси X с заданным зазором.
/// </summary>
internal sealed class BlockInserter(double gapPercent, AILog log)
{
    private double _rightMax;
    private bool _hasPlacedObjects;

    /// <summary>
    /// Клонирует все сущности из Model Space исходного DWG в Model Space целевой базы,
    /// затем смещает их в рассчитанную позицию для последовательной раскладки по оси X.
    /// </summary>
    /// <param name="targetDb">Целевая база данных чертежа, в которую выполняется вставка.</param>
    /// <param name="sourceFilePath">Полный путь к исходному DWG-файлу.</param>
    /// <param name="sourceName">Человекочитаемое имя источника для логирования.</param>
    /// <param name="sourceBounds">Границы исходного содержимого, используемые для расчёта смещения.</param>
    /// <returns>
    /// Границы вставленных объектов в мировой системе координат,
    /// либо <see langword="null"/>, если вставка не выполнена.
    /// </returns>
    public Extents3d? InsertNativeObjects(Database targetDb, string sourceFilePath, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            SyncUnits(targetDb);

            using Database sourceDb = new(false, true);
            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            SyncUnits(sourceDb);
            sourceDb.CloseInput(true);

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
                log.Warn($"{sourceName}: пустой Model Space");
                return null;
            }

            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            IdMapping map = [];
            Extents3d? worldBounds = null;
            int clonedCount = 0;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

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

            worldBounds ??= ExtentsUtils.Transform(sourceBounds, displacement);

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;

            log.Info($"{sourceName}: вставлено {clonedCount} объектов");
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

    /// <summary>
    /// Нормализует единицы измерения базы данных к миллиметрам и метрической системе.
    /// </summary>
    /// <param name="db">База данных AutoCAD, для которой синхронизируются единицы.</param>
    internal static void SyncUnits(Database db)
    {
        if (db.Insunits != UnitsValue.Millimeters)
        {
            db.Insunits = UnitsValue.Millimeters;
        }

        if (db.Measurement != MeasurementValue.Metric)
        {
            db.Measurement = MeasurementValue.Metric;
        }
    }

    /// <summary>
    /// Вычисляет точку вставки следующего блока с учётом уже размещённых объектов и заданного зазора.
    /// </summary>
    /// <param name="bounds">Границы вставляемого содержимого в локальных координатах.</param>
    /// <returns>Точка смещения для приведения содержимого в целевые координаты.</returns>
    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        double height = Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        double gap = Max(1.0, Round(Max(width, height) * gapPercent, 0));

        double insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;

        return new Point3d(insertX, -bounds.MinPoint.Y, 0);
    }
}
