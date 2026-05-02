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

            using Database sourceDb = new(false, true);
            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);
            ExtentsUtils.SyncUnits(sourceDb);
            sourceDb.CloseInput(true);

            // Запоминаем исходные единицы целевого чертежа
            UnitsValue originalTargetUnits = targetDb.Insunits;

            // ПРИНУДИТЕЛЬНО отключаем единицы в ОБЕИХ базах, чтобы WblockCloneObjects
            // не пытался конвертировать размеры (304.8)
            sourceDb.Insunits = UnitsValue.Undefined;
            targetDb.Insunits = UnitsValue.Undefined;

            ObjectIdCollection sourceIdsCollection = new ObjectIdCollection();
            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);

            // Настраиваем исходный ModelSpace
            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                // 1. Открываем таблицу блоков и жестко фиксируем метрические единицы для ВСЕХ блоков.
                // Это необходимо, чтобы анонимные блоки размеров (*D) не сохранили исходные футы
                // и WblockCloneObjects не применил к ним коэффициент масштабирования 304.8.
                BlockTable bt = (BlockTable)tr.GetObject(sourceDb.BlockTableId, OpenMode.ForRead);
                foreach (ObjectId btrId in bt)
                {
                    BlockTableRecord btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForWrite);
                    btr.Units = targetDb.Insunits;
                }

                // 2. Собираем ID объектов из Model Space для последующего клонирования
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(sourceMsId, OpenMode.ForRead);
                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIdsCollection.Add(id);
                    }
                }

                tr.Commit();
            }

            if (sourceIdsCollection.Count == 0)
            {
                log.Warn($"{sourceName}: пустой Model Space");
                targetDb.Insunits = originalTargetUnits; // Восстанавливаем
                return null;
            }

            IdMapping map = new IdMapping();
            Extents3d? worldBounds = null;
            int clonedCount = 0;

            // Клонирование и настройка целевого ModelSpace
            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord targetMs = (BlockTableRecord)tr.GetObject(targetMsId, OpenMode.ForWrite);
                UnitsValue originalTargetMsUnits = targetMs.Units;
                targetMs.Units = UnitsValue.Undefined; // Временно отключаем

                // Теперь клонирование пройдет без конвертации размерных стилей 1 к 304.8
                targetDb.WblockCloneObjects(sourceIdsCollection, targetMsId, map, DuplicateRecordCloning.Ignore, false);

                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary) continue;

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

                // Восстанавливаем единицы ModelSpace
                targetMs.Units = originalTargetMsUnits;
                tr.Commit();
            }

            // Восстанавливаем глобальные единицы целевой базы
            targetDb.Insunits = originalTargetUnits;

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
