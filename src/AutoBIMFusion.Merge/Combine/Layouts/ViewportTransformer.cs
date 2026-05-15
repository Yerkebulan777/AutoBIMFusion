using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using Serilog.Core;
using Exception = System.Exception;

namespace AutoBIMFusion.Merge.Combine.Layouts;

/// <summary>
///     Переводит содержимое вспомогательного vpt'а (узла) в модельные координаты
///     главного vpt'а и выполняет операции над наборами объектов Model Space.
///     Математика: MainModelFromAuxModel = MainModelFromPaper ∘ PaperFromAuxModel.
///     PaperFromAuxModel(p) = CenterPaper_aux + Rot(-twist_aux) * (p - ViewCenter_aux) * scale_aux
///     MainModelFromPaper(p) = ViewCenter_main + Rot(+twist_main) * (p - CenterPaper_main) / scale_main
/// </summary>
internal static class ViewportTransformer
{
    /// <summary>
    ///     Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(ViewportInfo main, ViewportInfo aux, Logger log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        // PaperFromAuxModel: tAux -> rAux -> sAux
        var tAux = Matrix3d.Displacement(origin - aux.ViewCenter);
        var rAux = Matrix3d.Rotation(-aux.ViewTwist, z, origin);
        var sAux = Matrix3d.Scaling(aux.CustomScale, origin);

        // MainModelFromPaper: tPaper -> sMain -> rMain -> tMain
        var tPaper = Matrix3d.Displacement(aux.CenterPaper - main.CenterPaper);
        var sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        var rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        var tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper * sAux * rAux * tAux;

        log.Debug(
            $"BuildMatrix aux#{aux.Number} -> main#{main.Number}: " +
            $"auxScale={aux.CustomScale:F6}, mainScale={main.CustomScale:F6}, " +
            $"auxTwist={aux.ViewTwist:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"auxWindow={ExtentsUtils.FormatExtents(aux.ModelWindow)}");

        return result;
    }

    /// <summary>
    ///     Матрица переноса «бумага → модель main-VP». Используется для содержимого Paper Space
    ///     (рамка, штамп, тексты) когда его пересаживают в Model Space через главный VP.
    /// </summary>
    internal static Matrix3d BuildPaperToMainMatrix(ViewportInfo main, Logger log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        // PaperToMain: tPaper -> sMain -> rMain -> tMain
        var tPaper = Matrix3d.Displacement(origin - main.CenterPaper);
        var sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        var rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        var tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper;

        log.Debug(
            $"BuildPaperToMainMatrix main#{main.Number}: " +
            $"mainScale={main.CustomScale:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"centerPaper={ExtentsUtils.FormatPoint(main.CenterPaper)}, viewCenter={ExtentsUtils.FormatPoint(main.ViewCenter)}");

        return result;
    }

    /// <summary>
    ///     Применяет матрицу трансформации ко всем объектам модели в базе данных.
    ///     Viewport'ы пропускаются; особенности конкретных типов сущностей обрабатывает
    ///     <see cref="EntityTransformUtils" />.
    /// </summary>
    internal static void ScaleModelSpaceObjects(Database db, Matrix3d matrix, double ratio, Logger log)
    {
        Dictionary<string, int> errorTypes = [];

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction trx = db.TransactionManager.StartTransaction();
        var modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (trx.GetObject(id, OpenMode.ForWrite) is Entity ent)
            {
                if (ent is Viewport)
                {
                    continue;
                }

                var entType = ent.GetType().Name;
                var handle = ent.Handle.ToString();

                try
                {
                    Extents3d? oldExt = ExtentsUtils.TryGetExtents(ent);

                    EntityTransformUtils.TransformResult transformResult = EntityTransformUtils.TransformEntity(ent, matrix, trx);

                    if (transformResult.SkippedAssociativeHatch)
                    {
                        continue;
                    }

                    Extents3d? newExt = ExtentsUtils.TryGetExtents(ent);

                    if (ExtentsUtils.TryGetScaleRatio(oldExt, newExt, out var oldDig, out var newDig,
                            out var digRatio) && digRatio > ratio * 5.0)
                    {
                        log.Warning(
                            $"[АНОМАЛИЯ МАСШТАБА] Тип: {entType}, Handle: {handle}. Диагональ ДО: {oldDig:F2}, ПОСЛЕ: {newDig:F2}");
                    }
                }
                catch (Exception ex)
                {
                    log.Error(ex, $"[ОШИБКА ТРАНСФОРМАЦИИ] Тип: {entType}, Handle: {handle}. Сообщение: {ex.Message}");

                    if (!errorTypes.TryGetValue(entType, out var value))
                    {
                        value = 0;
                        errorTypes[entType] = value;
                    }

                    errorTypes[entType] = ++value;
                }
            }
        }

        trx.Commit();

        if (errorTypes.Count > 0)
        {
            var errorStr = string.Join(", ", errorTypes.Select(kv => $"{kv.Key}({kv.Value})"));
            log.Warning($"Ошибочные типы (Scale): {errorStr}");
        }
    }

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId,
        Logger log)
    {
        var total = 0;

        List<ModelEntitySnapshot> result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();
        var modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            total++;

            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (ent is Viewport)
            {
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
            if (ext is null)
            {
                continue;
            }

            result.Add(new ModelEntitySnapshot(id, ext.Value));
        }

        trx.Commit();
        log.Debug($"CollectModelEntitiesWithExtents total={total}, cached={result.Count}");
        return result;
    }

    /// <summary>
    ///     Глубоко клонирует набор объектов и применяет матрицу трансформации к каждому клону.
    /// </summary>
    /// <remarks>
    ///     Type-specific transform compensation is delegated to <see cref="EntityTransformUtils" />.
    /// </remarks>
    internal static CloneTransformResult DeepCloneAndTransform(
        Database db,
        ObjectIdCollection sourceIds,
        ObjectId sourceOwnerId,
        ObjectId ownerId,
        Matrix3d matrix,
        Logger log)
    {
        IReadOnlyList<ObjectId> sourceOrder = DrawOrderPreserver.Capture(db, sourceOwnerId, sourceIds, log);

        using ObjectIdCollection validIds = CollectValidIds(sourceIds);

        if (validIds.Count == 0)
        {
            return new CloneTransformResult();
        }

        using IdMapping map = [];
        CloneTransformResult result = new();

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            db.DeepCloneObjects(validIds, ownerId, map, false);
            TransformClonedEntities(trx, map, matrix, result, log);

            trx.Commit();
        }

        DrawOrderPreserver.Restore(db, ownerId, sourceOrder, map, log);
        return result;
    }

    private static ObjectIdCollection CollectValidIds(ObjectIdCollection sourceIds)
    {
        ObjectIdCollection validIds = [];

        foreach (ObjectId id in sourceIds)
        {
            if (id.IsValidForOperation())
            {
                _ = validIds.Add(id);
            }
        }

        return validIds;
    }

    private static void TransformClonedEntities(
        Transaction trx,
        IdMapping map,
        Matrix3d matrix,
        CloneTransformResult result,
        Logger log)
    {
        foreach (IdPair pair in map)
        {
            if (!pair.IsCloned || !pair.IsPrimary)
            {
                continue;
            }

            if (trx.GetObject(pair.Value, OpenMode.ForWrite) is not Entity entity)
            {
                continue;
            }

            try
            {
                _ = EntityTransformUtils.TransformEntity(entity, matrix, trx);
                _ = result.ClonedIds.Add(pair.Value);
            }
            catch (Exception ex)
            {
                log.Warning($"[ОШИБКА КЛОНА] {entity.GetType().Name} {entity.Handle}: {ex.Message}");
            }
        }
    }

    /// <param name="mainWindow">
    ///     Модельное окно главного VP. Используется в эвристике: пропускать сущность,
    ///     только если она огромна И при этом принадлежит главному VP (пересекает mainWindow).
    ///     Большие сущности, не пересекающие mainWindow, — легитимный контент вспомогательного VP.
    /// </param>
    internal static ObjectIdCollection SelectModelInside(IReadOnlyList<ModelEntitySnapshot> modelEntities,
        Extents3d window, Extents3d mainWindow, Logger log)
    {
        const double HugeEntityDiagonalRatio = 3.0;

        var outsideWindow = 0;
        var skippedHugeObjects = 0;
        ObjectIdCollection result = [];

        double windowDiagonal = window.MinPoint.DistanceTo(window.MaxPoint);

        foreach (ModelEntitySnapshot entity in modelEntities)
        {
            if (!ExtentsUtils.AabbIntersect(window, entity.Extents))
            {
                outsideWindow++;
                continue;
            }

            // Эвристика: пропустить только если сущность огромна (фон main VP) И находится в main window.
            // Большой объект, которого нет в main window, — легитимный контент aux VP, не трогаем.
            double entityDiagonal = entity.Extents.MinPoint.DistanceTo(entity.Extents.MaxPoint);
            if (windowDiagonal > 0
                && entityDiagonal > windowDiagonal * HugeEntityDiagonalRatio
                && ExtentsUtils.AabbIntersect(mainWindow, entity.Extents))
            {
                skippedHugeObjects++;
                continue;
            }

            _ = result.Add(entity.Id);
        }

        log.Debug(
            $"SelectModelInside cached={modelEntities.Count}, selected={result.Count}, " +
            $"skippedHuge={skippedHugeObjects}, outsideWindow={outsideWindow}, " +
            $"window={ExtentsUtils.FormatExtents(window)}");
        return result;
    }

    /// <summary>
    ///     Удаляет из модели оригинальные объекты вспомогательного VP, которые НЕ входят в окно
    ///     главного VP. Вызывается после DeepCloneAndTransform для каждого aux VP.
    ///     Логика: если объект виден в главном VP — оставляем (нужен для его плоского представления).
    ///     Если объект только в aux VP — удаляем, так как его клон уже создан на правильной позиции.
    ///     Без этого шага объекты aux VP, чьи модельные координаты попадают в пределы листа
    ///     (frameBounds), не удаляются TrimOutside и остаются как «мусор» в результирующем файле.
    /// </summary>
    internal static void EraseEntitiesOutsideMainWindow(Database db, ObjectIdCollection auxEntities, IReadOnlyList<ModelEntitySnapshot> modelSnapshots, Extents3d mainWindow)
    {
        HashSet<ObjectId> inMain = [];

        foreach (ModelEntitySnapshot s in modelSnapshots)
        {
            if (ExtentsUtils.AabbIntersect(mainWindow, s.Extents))
            {
                _ = inMain.Add(s.Id);
            }
        }

        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in auxEntities)
        {
            if (inMain.Contains(id))
            {
                continue;
            }

            if (id.IsErased)
            {
                continue;
            }

            if (trx.GetObject(id, OpenMode.ForWrite) is Entity e && !e.IsErased)
            {
                e.Erase();
            }
        }

        trx.Commit();
    }

    /// <summary>
    ///     Снимок состояния сущности модели, включая её идентификатор и границы.
    /// </summary>
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents);

    /// <summary>
    ///     Результат клонирования и трансформации сущностей модели.
    /// </summary>
    internal sealed class CloneTransformResult : IDisposable
    {
        internal ObjectIdCollection ClonedIds { get; } = [];

        public void Dispose()
        {
            ClonedIds.Dispose();
        }
    }
}
