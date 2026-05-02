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
    public Extents3d? InsertNativeObjects(Database targetDb, Database sourceDb, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            ExtentsUtils.SyncUnits(targetDb);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            using ObjectIdCollection sourceIds = [];

            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                // 1. ПОЛНЫЙ ОБХОД ВСЕХ БЛОКОВ (включая скрытые анонимные блоки размеров *D)
                // Это гарантирует, что WblockCloneObjects не найдет ни одного футового блока
                // и не применит коэффициент 304.8.
                BlockTable bt = (BlockTable)tr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                    // Пропускаем XREF, чтобы не вызвать ошибку доступа
                    if (!btr.IsFromExternalReference)
                    {
                        btr.Units = targetDb.Insunits;
                    }
                }

                // 2. Сбор объектов для клонирования из Model Space
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

            // 3. Сохраняем исходные метрические настройки целевой базы
            UnitsValue originalTargetDbUnits = targetDb.Insunits;
            MeasurementValue originalTargetDbMeasurement = targetDb.Measurement;
            IdMapping map = [];
            Extents3d? worldBounds = null;
            int clonedCount = 0;

            try
            {
                using Transaction targetTr = targetDb.TransactionManager.StartTransaction();
                
                // 3. Получаем исходные единицы Model Space и ВРЕМЕННО приравниваем
                BlockTableRecord targetMs = (BlockTableRecord)targetTr.GetObject(targetMsId, OpenMode.ForWrite);
                UnitsValue originalTargetMsUnits = targetMs.Units;

                targetDb.Insunits = sourceDb.Insunits;
                targetDb.Measurement = sourceDb.Measurement;
                targetMs.Units = targetDb.Insunits;

                // 4. Выполняем клонирование 1:1
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary)
                    {
                        continue;
                    }

                    try
                    {
                        if (targetTr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                        {
                            // Оптимизация Phase 2: Объединяем трансформацию и очистку
                            ent.TransformBy(displacement);
                            clonedCount++;

                            if (ent is Dimension dim)
                            {
                                DimensionUtils.Heal(dim);
                            }

                            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
                            if (ext.HasValue)
                            {
                                worldBounds = worldBounds.HasValue
                                    ? ExtentsUtils.Union(worldBounds.Value, ext.Value)
                                    : ext.Value;
                            }
                        }
                    }
                    catch (System.Exception ex)
                    {
                        log.Warn(ex, $"Ошибка обработки клонированного объекта {pair.Value}");
                    }
                }

                // Лечим стили размеров в той же транзакции
                _ = DimensionHealer.HealDimensionStyles(targetDb, targetTr, []);

                // 5. ОБЯЗАТЕЛЬНО возвращаем целевой базе ее правильные метрические единицы
                targetDb.Insunits = originalTargetDbUnits;
                targetDb.Measurement = originalTargetDbMeasurement;
                targetMs.Units = originalTargetMsUnits;

                targetTr.Commit();
            }
            finally
            {
                // На случай падения транзакции, гарантируем восстановление уровня базы
                targetDb.Insunits = originalTargetDbUnits;
                targetDb.Measurement = originalTargetDbMeasurement;
                ExtentsUtils.SyncUnits(targetDb);
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
