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
            ExtentsUtils.SyncUnits(targetDb);

            // 1. Читаем исходную базу (БЕЗ попыток изменить ее единицы)
            using Database sourceDb = new(false, true);
            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            sourceDb.CloseInput(true);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            ObjectIdCollection sourceIds = [];

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
            UnitsValue originalTargetMsUnits;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord targetMs = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForRead);
                originalTargetMsUnits = targetMs.Units;
                tr.Commit();
            }

            IdMapping map = [];
            Extents3d? worldBounds = null;
            int clonedCount = 0;

            try
            {
                // 4. ВРЕМЕННО приравниваем единицы целевой базы к исходной.
                // Это полностью отключает встроенную логику WblockCloneObjects по авто-масштабированию (304.8).
                targetDb.Insunits = sourceDb.Insunits;
                targetDb.Measurement = sourceDb.Measurement;
                using (Transaction tr = targetDb.TransactionManager.StartTransaction())
                {
                    BlockTableRecord targetMs = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForWrite);
                    targetMs.Units = targetDb.Insunits;
                    tr.Commit();
                }

                // 5. Выполняем клонирование 1:1
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
            }
            finally
            {
                // 6. ОБЯЗАТЕЛЬНО возвращаем целевой базе ее правильные метрические единицы
                targetDb.Insunits = originalTargetDbUnits;
                targetDb.Measurement = originalTargetDbMeasurement;
                using (Transaction tr = targetDb.TransactionManager.StartTransaction())
                {
                    BlockTableRecord targetMs = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForWrite);
                    targetMs.Units = originalTargetMsUnits;
                    tr.Commit();
                }
                ExtentsUtils.SyncUnits(targetDb);
            }

            int healedCount = DimensionHealer.Heal(targetDb);

            if (clonedCount == 0)
            {
                log.Warn($"{sourceName}: не удалось клонировать объекты");
                return null;
            }

            worldBounds ??= ExtentsUtils.Transform(sourceBounds, displacement);

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;

            log.Info($"{sourceName}: вставлено {clonedCount} объектов");
            log.Debug($"{sourceName}: исправлено размеров после клонирования: {healedCount}");
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
