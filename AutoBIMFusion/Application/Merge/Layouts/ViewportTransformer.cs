using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge.Layouts;

/// <summary>
/// Переводит содержимое вспомогательного viewport'а (узла) в модельные координаты
/// главного viewport'а и клонирует объекты на новом месте.
///
/// Математика: MainModelFromAuxModel = MainModelFromPaper ∘ PaperFromAuxModel.
///
/// PaperFromAuxModel(p) = CenterPaper_aux + Rot(-twist_aux) * (p - ViewCenter_aux) * scale_aux
/// MainModelFromPaper(p) = ViewCenter_main + Rot(+twist_main) * (p - CenterPaper_main) / scale_main
/// </summary>
/// <remarks>
/// ИЗВЕСТНЫЕ ОСОБЕННОСТИ AutoCAD API:
///
/// [Hatch + DeepCloneObjects] После глубокого клонирования (<see cref="Database.DeepCloneObjects"/>)
/// ассоциативные штриховки (<see cref="Hatch"/>) теряют привязку к своим граничным контурам.
/// Вызов <see cref="Hatch.EvaluateHatch"/> ОБЯЗАТЕЛЬЕН сразу после <see cref="Entity.TransformBy"/>
/// — иначе штриховка остаётся на старых координатах или отображается некорректно ("каша" линий).
///
/// АНТИ-ПАТТЕРН (не вводить повторно!):
///   if (ent is Hatch) continue;  // ← пропускает трансформацию штриховок → рассинхронизация
///
/// ПРАВИЛЬНЫЙ ПАТТЕРН:
///   ent.TransformBy(matrix);
///   if (ent is Hatch h) { try { h.EvaluateHatch(true); } catch { /* сломанная геометрия */ } }
/// </remarks>
internal static class ViewportTransformer
{
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents);

    /// <summary>
    /// Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(LayoutViewportInfo main, LayoutViewportInfo aux, OperationLogger log)
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
    internal static Matrix3d BuildPaperToMainMatrix(LayoutViewportInfo main, OperationLogger log)
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
    /// Viewport'ы пропускаются. Штриховки трансформируются и немедленно переоцениваются
    /// через <see cref="Hatch.EvaluateHatch"/>, чтобы избежать рассинхронизации контуров.
    /// </summary>
    internal static void ScaleModelSpaceObjects(
        Database db,
        Matrix3d matrix,
        double ratio,
        OperationLogger log)
    {
        int scaled = 0;
        int total = 0;
        int skippedViewport = 0;
        int skippedNonEntity = 0;
        int skippedAssociative = 0;
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        Dictionary<string, int> successTypes = [];
        Dictionary<string, int> errorTypes = [];

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            total++;

            if (tr.GetObject(id, OpenMode.ForWrite) is not Entity ent)
            {
                skippedNonEntity++;
                continue;
            }

            if (ent is Viewport)
            {
                skippedViewport++;
                continue;
            }

            // Associative hatches self-update when their boundary entities are transformed.
            // Applying TransformBy to the hatch itself would cause double transformation (scale²).
            if (ent is Hatch hatch && hatch.Associative)
            {
                skippedAssociative++;
                continue;
            }

            string entType = ent.GetType().Name;
            string handle = ent.Handle.ToString();

            try
            {
                Extents3d? oldExt = ExtentsUtils.TryGetExtents(ent);
                ent.TransformBy(matrix);

                double scaleFactor = Vector3d.XAxis.TransformBy(matrix).Length;

                if (ent is Dimension dimScale)
                {
                    double currentDimscale = dimScale.Dimscale == 0.0 ? 1.0 : dimScale.Dimscale;
                    dimScale.Dimscale = currentDimscale * scaleFactor;

                    if (scaleFactor > 0.0001)
                    {
                        dimScale.Dimlfac = dimScale.Dimlfac / scaleFactor;
                    }

                    dimScale.RecomputeDimensionBlock(true);
                }
                else if (ent is MLeader mleaderScale)
                {
                    double currentScale = mleaderScale.Scale == 0.0 ? 1.0 : mleaderScale.Scale;
                    mleaderScale.Scale = currentScale * scaleFactor;
                }
                else if (ent is Hatch hatchScale)
                {
                    try { hatchScale.EvaluateHatch(true); }
                    catch { }
                }

                Extents3d? newExt = ExtentsUtils.TryGetExtents(ent);

                if (oldExt.HasValue && newExt.HasValue)
                {
                    double oldDiag = oldExt.Value.MaxPoint.DistanceTo(oldExt.Value.MinPoint);
                    double newDiag = newExt.Value.MaxPoint.DistanceTo(newExt.Value.MinPoint);

                    if (oldDiag > 0.001 && (newDiag / oldDiag) > (ratio * 5.0))
                    {
                        log.Warn($"[АНОМАЛИЯ МАСШТАБА] Тип: {entType}, Handle: {handle}. Диагональ ДО: {oldDiag:F2}, ПОСЛЕ: {newDiag:F2}");
                    }
                }

                scaled++;
                if (!successTypes.ContainsKey(entType))
                {
                    successTypes[entType] = 0;
                }

                successTypes[entType]++;
            }
            catch (System.Exception ex)
            {
                log.Error(ex, $"[ОШИБКА ТРАНСФОРМАЦИИ] Тип: {entType}, Handle: {handle}. Сообщение: {ex.Message}");
                if (!errorTypes.ContainsKey(entType))
                {
                    errorTypes[entType] = 0;
                }

                errorTypes[entType]++;
            }
        }

        tr.Commit();

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

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId, OperationLogger log)
    {
        List<ModelEntitySnapshot> result = [];
        int total = 0;
        int skippedViewport = 0;
        int skippedNoExtents = 0;

        using Transaction tr = db.TransactionManager.StartTransaction();
        BlockTableRecord ms = (BlockTableRecord)tr.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in ms)
        {
            total++;

            if (tr.GetObject(id, OpenMode.ForRead) is not Entity ent)
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

        tr.Commit();
        log.Debug($"CollectModelEntitiesWithExtents total={total}, cached={result.Count}, skippedViewport={skippedViewport}, skippedNoExtents={skippedNoExtents}");
        return result;
    }

    /// <summary>
    /// Глубоко клонирует набор объектов и применяет матрицу трансформации к каждому клону.
    /// </summary>
    /// <remarks>
    /// Штриховки (<see cref="Hatch"/>) требуют особой обработки: после
    /// <see cref="Database.DeepCloneObjects"/> они теряют привязку к граничным контурам.
    /// Вызов <see cref="Hatch.EvaluateHatch"/> сразу после <see cref="Entity.TransformBy"/>
    /// восстанавливает корректное отображение заливки на новых координатах.
    /// </remarks>
    internal static ObjectIdCollection DeepCloneAndTransform(
        Database db, ObjectIdCollection sourceIds, ObjectId sourceOwnerId, ObjectId ownerId,
        Matrix3d matrix, OperationLogger log, string sourceName, double? dimensionScale = null)
    {
        IReadOnlyList<ObjectId> sourceOrder = DrawOrderPreserver.Capture(db, sourceOwnerId, sourceIds, log);

        ObjectIdCollection validIds = [];
        int skippedErased = 0;
        foreach (ObjectId id in sourceIds)
        {
            if (id.IsErased)
            {
                skippedErased++;
            }
            else
            {
                _ = validIds.Add(id);
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
        ObjectIdCollection cloned = [];
        int mappedPrimary = 0;
        int dimensionOverrides = 0;

        // Извлекаем коэффициент масштабирования из матрицы для корректного расчета Dimscale
        double scaleFactor = Vector3d.XAxis.TransformBy(matrix).Length;

        using (Transaction tr = db.TransactionManager.StartTransaction())
        {
            db.DeepCloneObjects(validIds, ownerId, map, false);
            foreach (IdPair pair in map)
            {
                if (!pair.IsCloned || !pair.IsPrimary)
                {
                    continue;
                }

                mappedPrimary++;

                if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity e)
                {
                    // Associative hatches self-update when their boundary entities are transformed.
                    // Applying TransformBy to the hatch itself would cause double transformation (scale²).
                    if (e is Hatch clonedHatch && clonedHatch.Associative)
                    {
                        _ = cloned.Add(pair.Value);
                        continue;
                    }

                    string entType = e.GetType().Name;
                    string handle = e.Handle.ToString();

                    try
                    {
                        Extents3d? oldExt = ExtentsUtils.TryGetExtents(e);

                        // 1. Сначала трансформируем геометрию (точки привязки изменят координаты)
                        e.TransformBy(matrix);

                        // 2. Если это размер, пропорционально меняем его масштаб и пересчитываем
                        if (e is Dimension transformedDimension)
                        {
                            double originalDimscale = transformedDimension.Dimscale;

                            double safeDimscale = originalDimscale == 0.0 ? 1.0 : originalDimscale;

                            transformedDimension.Dimscale = safeDimscale * scaleFactor;

                            if (scaleFactor > 0.0001)
                            {
                                transformedDimension.Dimlfac = transformedDimension.Dimlfac / scaleFactor;
                            }

                            transformedDimension.RecomputeDimensionBlock(true);

                            log.Debug(
                                $"[DBG] DimscaleOverride clone source={sourceName}: CloneHandle={transformedDimension.Handle}, " +
                                $"scaleFactor={scaleFactor:F6}, originalDimscale={originalDimscale:F6}, " +
                                $"newDimscale={transformedDimension.Dimscale:F6}");
                            dimensionOverrides++;
                        }
                        else if (e is MLeader clonedMLeader)
                        {
                            double safeScale = clonedMLeader.Scale == 0.0 ? 1.0 : clonedMLeader.Scale;
                            clonedMLeader.Scale = safeScale * scaleFactor;
                        }

                        // DeepCloneObjects разрывает ассоциацию Hatch ↔ контур —
                        // EvaluateHatch принудительно пересчитывает геометрию заливки
                        // по актуальным (уже трансформированным) координатам.
                        if (e is Hatch hatchClone)
                        {
                            try { hatchClone.EvaluateHatch(true); }
                            catch { /* Игнорируем ошибки EvaluateHatch на сложной/сломанной геометрии */ }
                        }

                        Extents3d? newExt = ExtentsUtils.TryGetExtents(e);

                        if (oldExt.HasValue && newExt.HasValue)
                        {
                            double oldDiag = oldExt.Value.MaxPoint.DistanceTo(oldExt.Value.MinPoint);
                            double newDiag = newExt.Value.MaxPoint.DistanceTo(newExt.Value.MinPoint);

                            if (oldDiag > 0.001 && (newDiag / oldDiag) > 1000.0)
                            {
                                log.Warn($"[АНОМАЛИЯ КЛОНА] Тип: {entType}, Handle: {handle}. " +
                                         $"Диагональ ДО: {oldDiag:F2}, ПОСЛЕ: {newDiag:F2}");
                            }
                        }

                        _ = cloned.Add(pair.Value);
                    }
                    catch (System.Exception ex)
                    {
                        log.Warn($"[ОШИБКА КЛОНА] Тип: {entType}, Handle: {handle}. Ошибка: {ex.Message}");
                    }
                }
            }

            tr.Commit();
        }

        DrawOrderPreserver.Restore(db, ownerId, sourceOrder, map, log);

        log.Debug(
            $"DeepCloneAndTransform source={sourceName}, input={sourceIds.Count}, " +
            $"mappedPrimary={mappedPrimary}, transformed={cloned.Count}, dimensionOverrides={dimensionOverrides}, " +
            $"scaleFactor={scaleFactor:F6}");
        return cloned;
    }

    internal static ObjectIdCollection SelectModelInside(
        IReadOnlyList<ModelEntitySnapshot> modelEntities,
        Extents3d window,
        OperationLogger log)
    {
        ObjectIdCollection result = [];
        int outsideWindow = 0;

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
    internal static int EraseEntitiesOutsideMainWindow(
        Database db,
        ObjectIdCollection auxEntities,
        IReadOnlyList<ModelEntitySnapshot> modelSnapshots,
        Extents3d mainWindow,
        OperationLogger log)
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
    /// Если высота задана жёстко (TextSize > 0), AutoCAD игнорирует Dimscale размерных стилей —
    /// обнуление «отвязывает» высоту и позволяет масштабированию работать корректно.
    /// Вызывается перед масштабированием объектов Model Space.
    /// </summary>
    internal static void UnlockTextStylesHeight(Database db, OperationLogger log)
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
