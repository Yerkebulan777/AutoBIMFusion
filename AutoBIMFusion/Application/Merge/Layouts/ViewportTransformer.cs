using AutoBIMFusion.Application.Merge.Layouts.Transforms;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Переводит содержимое вспомогательного viewport'а (узла) в модельные координаты
/// главного viewport'а и выполняет операции над наборами объектов Model Space.
///
/// Математика: MainModelFromAuxModel = MainModelFromPaper ∘ PaperFromAuxModel.
///
/// PaperFromAuxModel(p) = CenterPaper_aux + Rot(-twist_aux) * (p - ViewCenter_aux) * scale_aux
/// MainModelFromPaper(p) = ViewCenter_main + Rot(+twist_main) * (p - CenterPaper_main) / scale_main
/// </summary>
/// <remarks>
/// Entity-specific post-processing after transforms is centralized in
/// <see cref="EntityTransformUtils"/>.
/// </remarks>

internal static class ViewportTransformer
{
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents);

    /// <summary>
    /// Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(LayoutViewportInfo main, LayoutViewportInfo aux, AILog log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        Matrix3d tAux = Matrix3d.Displacement(origin - aux.ViewCenter);
        Matrix3d rAux = Matrix3d.Rotation(-aux.ViewTwist, z, origin);
        Matrix3d sAux = Matrix3d.Scaling(aux.CustomScale, origin);
        Matrix3d tPaper = Matrix3d.Displacement(aux.CenterPaper - main.CenterPaper);
        Matrix3d sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        Matrix3d rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        Matrix3d tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper * sAux * rAux * tAux;

        log.Debug(
            $"BuildMatrix aux#{aux.Number} -> main#{main.Number}: " +
            $"auxScale={aux.CustomScale:F6}, mainScale={main.CustomScale:F6}, " +
            $"auxTwist={aux.ViewTwist:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"auxWindow={ExtentsUtils.FormatExtents(aux.ModelWindow)}");

        return result;
    }

    /// <summary>
    /// Матрица переноса «бумага → модель main-VP». Используется для содержимого Paper Space
    /// (рамка, штамп, тексты) когда его пересаживают в Model Space через главный VP.
    /// </summary>
    internal static Matrix3d BuildPaperToMainMatrix(LayoutViewportInfo main, AILog log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        Matrix3d tPaper = Matrix3d.Displacement(origin - main.CenterPaper);
        Matrix3d sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        Matrix3d rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        Matrix3d tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        Matrix3d result = tMain * rMain * sMain * tPaper;

        log.Debug(
            $"BuildPaperToMainMatrix main#{main.Number}: " +
            $"mainScale={main.CustomScale:F6}, mainTwist={main.ViewTwist:F6}, " +
            $"centerPaper={ExtentsUtils.FormatPoint(main.CenterPaper)}, viewCenter={ExtentsUtils.FormatPoint(main.ViewCenter)}");

        return result;
    }

    /// <summary>
    /// Применяет матрицу трансформации ко всем объектам модели в базе данных.
    /// Viewport'ы пропускаются; особенности конкретных типов сущностей обрабатывает
    /// <see cref="EntityTransformUtils"/>.
    /// </summary>
    internal static void ScaleModelSpaceObjects(Database db, Matrix3d matrix, double ratio, AILog log)
    {
        int total = 0;
        int scaled = 0;
        int skippedViewport = 0;
        int skippedNonEntity = 0;
        int skippedAssociative = 0;

        Dictionary<string, int> errorTypes = [];
        Dictionary<string, int> successTypes = [];

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        double scaleFactor = EntityTransformUtils.GetScaleFactor(matrix);

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (trx.GetObject(id, OpenMode.ForWrite) is Entity ent)
            {
                total++;

                if (ent is Viewport)
                {
                    skippedViewport++;
                    continue;
                }

                string entType = ent.GetType().Name;
                string handle = ent.Handle.ToString();

                try
                {
                    Extents3d? oldExt = ExtentsUtils.TryGetExtents(ent);

                    EntityTransformUtils.TransformResult transformResult = EntityTransformUtils.TransformEntity(
                        ent,
                        matrix,
                        scaleFactor,
                        log,
                        "model-clamp");

                    if (transformResult.SkippedAssociativeHatch)
                    {
                        skippedAssociative++;
                        continue;
                    }

                    Extents3d? newExt = ExtentsUtils.TryGetExtents(ent);

                    if (ExtentsUtils.TryGetScaleRatio(oldExt, newExt, out double oldDig, out double newDig, out double digRatio) && digRatio > (ratio * 5.0))
                    {
                        log.Warn($"[АНОМАЛИЯ МАСШТАБА] Тип: {entType}, Handle: {handle}. Диагональ ДО: {oldDig:F2}, ПОСЛЕ: {newDig:F2}");
                    }

                    scaled++;

                    if (!successTypes.TryGetValue(entType, out int value))
                    {
                        value = 0;
                        successTypes[entType] = value;
                    }

                    successTypes[entType] = ++value;
                }
                catch (System.Exception ex)
                {
                    log.Error(ex, $"[ОШИБКА ТРАНСФОРМАЦИИ] Тип: {entType}, Handle: {handle}. Сообщение: {ex.Message}");

                    if (!errorTypes.TryGetValue(entType, out int value))
                    {
                        value = 0;
                        errorTypes[entType] = value;
                    }

                    errorTypes[entType] = ++value;
                }
            }
            else
            {
                skippedNonEntity++;
                continue;
            }
        }

        trx.Commit();

        log.Info($"ScaleModelSpaceObjects завершен: ratio={ratio:F6}, total={total}, scaled={scaled}, " +
                 $"skippedViewport={skippedViewport}, skippedNonEntity={skippedNonEntity}, skippedAssociative={skippedAssociative}");

        if (successTypes.Count > 0)
        {
            string successStr = string.Join(", ", successTypes.Select(kv => $"{kv.Key}({kv.Value})"));
            log.Debug($"Успешные типы (Scale): {successStr}");
        }

        if (errorTypes.Count > 0)
        {
            string errorStr = string.Join(", ", errorTypes.Select(kv => $"{kv.Key}({kv.Value})"));
            log.Warn($"Ошибочные типы (Scale): {errorStr}");
        }
    }

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId, AILog log)
    {
        int total = 0;
        int skippedViewport = 0;
        int skippedNoExtents = 0;

        List<ModelEntitySnapshot> result = [];

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            total++;

            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent)
            {
                continue;
            }

            if (ent is Viewport)
            {
                skippedViewport++;
                continue;
            }

            Extents3d? ext = ExtentsUtils.TryGetExtents(ent);
            if (ext is null)
            {
                skippedNoExtents++;
                continue;
            }

            result.Add(new ModelEntitySnapshot(id, ext.Value));
        }

        trx.Commit();
        log.Debug($"CollectModelEntitiesWithExtents total={total}, cached={result.Count}, skippedViewport={skippedViewport}, skippedNoExtents={skippedNoExtents}");
        return result;
    }

    /// <summary>
    /// Глубоко клонирует набор объектов и применяет матрицу трансформации к каждому клону.
    /// </summary>
    /// <remarks>
    /// Type-specific transform compensation is delegated to <see cref="EntityTransformUtils"/>.
    /// </remarks>
    internal static ObjectIdCollection DeepCloneAndTransform(
        Database db,
        ObjectIdCollection sourceIds,
        ObjectId sourceOwnerId,
        ObjectId ownerId,
        Matrix3d matrix,
        AILog log,
        string sourceName)
    {
        IReadOnlyList<ObjectId> sourceOrder = DrawOrderPreserver.Capture(db, sourceOwnerId, sourceIds, log);

        int skippedErased = 0;

        ObjectIdCollection validIds = [];

        foreach (ObjectId id in sourceIds)
        {
            if (!id.IsErased)
            {
                _ = validIds.Add(id);
            }
            else
            {
                skippedErased++;
            }
        }

        if (skippedErased > 0)
        {
            log.Warn($"DeepCloneAndTransform source={sourceName}: пропущено {skippedErased} стёртых объектов");
        }

        if (validIds.Count == 0)
        {
            log.Debug($"DeepCloneAndTransform source={sourceName}: все объекты стёрты, клонирование пропущено");
            return [];
        }

        IdMapping map = [];

        int mappedPrimary = 0;

        ObjectIdCollection cloned = [];

        double scaleFactor = EntityTransformUtils.GetScaleFactor(matrix);

        string dimensionDiagnosticScenario = GetDimensionDiagnosticScenario(sourceName);

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            db.DeepCloneObjects(validIds, ownerId, map, false);

            foreach (IdPair pair in map)
            {
                // Для исходных объектов значение IdPair.IsPrimary равно true.
                if (pair.IsCloned && pair.IsPrimary)
                {
                    mappedPrimary++;

                    if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity e)
                    {
                        string entType = e.GetType().Name;
                        string handle = e.Handle.ToString();

                        try
                        {
                            Extents3d? oldExt = ExtentsUtils.TryGetExtents(e);

                            EntityTransformUtils.TransformResult transformResult = EntityTransformUtils.TransformEntity(
                                e,
                                matrix,
                                scaleFactor,
                                log,
                                dimensionDiagnosticScenario);

                            if (transformResult.SkippedAssociativeHatch)
                            {
                                _ = cloned.Add(pair.Value);
                                continue;
                            }

                            Extents3d? newExt = ExtentsUtils.TryGetExtents(e);

                            if (ExtentsUtils.TryGetScaleRatio(oldExt, newExt, out double oldDig, out double newDig, out double ratio) && ratio > 1000.0)
                            {
                                log.Warn($"[АНОМАЛИЯ КЛОНА] Тип: {entType}, Handle: {handle}. Диагональ ДО: {oldDig:F2}, ПОСЛЕ: {newDig:F2}");
                            }

                            _ = cloned.Add(pair.Value);
                        }
                        catch (System.Exception ex)
                        {
                            log.Warn($"[ОШИБКА КЛОНА] Тип: {entType}, Handle: {handle}. Ошибка: {ex.Message}");
                        }
                    }
                }
            }

            tr.Commit();
        }

        DrawOrderPreserver.Restore(db, ownerId, sourceOrder, map, log);

        log.Debug(
            $"DeepCloneAndTransform source={sourceName}, input={sourceIds.Count}, " +
            $"mappedPrimary={mappedPrimary}, transformed={cloned.Count}, " +
            $"scaleFactor={scaleFactor:F6}");
        return cloned;
    }

    private static string GetDimensionDiagnosticScenario(string sourceName)
    {
        return sourceName.StartsWith("aux-VP", StringComparison.OrdinalIgnoreCase)
            ? "aux-clone"
            : sourceName.StartsWith("paper", StringComparison.OrdinalIgnoreCase) ? "paper-clone" : sourceName;
    }

    internal static ObjectIdCollection SelectModelInside(IReadOnlyList<ModelEntitySnapshot> modelEntities, Extents3d window, AILog log)
    {
        int outsideWindow = 0;
        ObjectIdCollection result = [];

        foreach (ModelEntitySnapshot entity in modelEntities)
        {
            if (ExtentsUtils.AabbIntersect(window, entity.Extents))
            {
                _ = result.Add(entity.Id);
            }
            else
            {
                outsideWindow++;
            }
        }

        log.Debug(
            $"SelectModelInside cached={modelEntities.Count}, selected={result.Count}, " +
            $"outsideWindow={outsideWindow}, window={ExtentsUtils.FormatExtents(window)}");
        return result;
    }

    /// <summary>
    /// Удаляет из модели оригинальные объекты вспомогательного VP, которые НЕ входят в окно
    /// главного VP. Вызывается после DeepCloneAndTransform для каждого aux VP.
    ///
    /// Логика: если объект виден в главном VP — оставляем (нужен для его плоского представления).
    /// Если объект только в aux VP — удаляем, так как его клон уже создан на правильной позиции.
    ///
    /// Без этого шага объекты aux VP, чьи модельные координаты попадают в пределы листа
    /// (frameBounds), не удаляются TrimOutside и остаются как «мусор» в результирующем файле.
    /// </summary>
    internal static int EraseEntitiesOutsideMainWindow(Database db, ObjectIdCollection auxEntities, IReadOnlyList<ModelEntitySnapshot> modelSnapshots, Extents3d mainWindow, AILog log)
    {
        HashSet<ObjectId> inMain = [];

        foreach (ModelEntitySnapshot s in modelSnapshots)
        {
            if (ExtentsUtils.AabbIntersect(mainWindow, s.Extents))
            {
                _ = inMain.Add(s.Id);
            }
        }

        int erased = 0;
        using Transaction tr = db.TransactionManager.StartTransaction();

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

            if (tr.GetObject(id, OpenMode.ForWrite) is Entity e && !e.IsErased)
            {
                e.Erase();
                erased++;
            }
        }

        tr.Commit();
        log.Info($"EraseEntitiesOutsideMainWindow: erased={erased} of {auxEntities.Count}, inMain={inMain.Count}");
        return erased;
    }

    /// <summary>
    /// Обнуляет фиксированную высоту текста во всех текстовых стилях базы данных.
    /// Если высота задана жёстко (TextSize > 0), AutoCAD не применяет масштаб
    /// размерных стилей ожидаемым образом. Обнуление «отвязывает» высоту.
    /// Вызывается перед масштабированием объектов Model Space.
    /// </summary>
    internal static void UnlockTextStylesHeight(Database db, AILog log)
    {
        int unlocked = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();

        TextStyleTable tt = (TextStyleTable)tr.GetObject(db.TextStyleTableId, OpenMode.ForRead);

        foreach (ObjectId tsId in tt)
        {
            TextStyleTableRecord ts = (TextStyleTableRecord)tr.GetObject(tsId, OpenMode.ForRead);

            if (ts.TextSize > 0.0)
            {
                ts.UpgradeOpen();
                ts.TextSize = 0.0;
                unlocked++;
            }
        }

        tr.Commit();
        log.Info($"UnlockTextStylesHeight: разблокировано {unlocked} текстовых стилей");
    }
}
