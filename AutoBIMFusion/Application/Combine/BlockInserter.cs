using AutoBIMFusion.Application.AcadSupport;
using AutoBIMFusion.Application.Combine.Layouts;
using Serilog.Core;

namespace AutoBIMFusion.Application.Combine;

/// <summary>
/// Вставляет содержимое DWG как нативные объекты в Model Space целевого чертежа,
/// располагая их вдоль оси X с заданным зазором.
/// </summary>
internal sealed class BlockInserter(double gapPercent, Logger log)
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
    public Extents3d? InsertNativeObjects(Database targetDb, Database sourceDb, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            ExtentsUtils.SyncUnits(targetDb);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            // Цель цикла: собрать уже подготовленные объекты для клонирования в targetDb.
            using ObjectIdCollection sourceIds = [];

            using (Transaction trx = sourceDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)trx.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIds.Add(id);
                    }
                }

                trx.Commit();
            }

            if (sourceIds.Count == 0)
            {
                return null;
            }

            Extents3d? worldBounds = null;
            int clonedCount = 0;

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
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка вставки: {sourceName}");
            return null;
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
