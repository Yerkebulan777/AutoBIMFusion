using Serilog.Core;

namespace AutoBIMFusion.Application.Combine.Layouts;

/// <summary>
/// Переводит содержимое вспомогательного vpt'а (узла) в модельные координаты
/// главного vpt'а и выполняет операции над наборами объектов Model Space.
///
/// Математика: MainModelFromAuxModel = MainModelFromPaper ∘ PaperFromAuxModel.
///
/// PaperFromAuxModel(p) = CenterPaper_aux + Rot(-twist_aux) * (p - ViewCenter_aux) * scale_aux
/// MainModelFromPaper(p) = ViewCenter_main + Rot(+twist_main) * (p - CenterPaper_main) / scale_main
/// </summary>
internal static class ViewportTransformer
{
    /// <summary>
    /// Снимок состояния сущности модели, включая её идентификатор и границы.
    /// </summary>
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents);

    /// <summary>
    /// Результат клонирования и трансформации сущностей модели.
    /// </summary>
    internal sealed class CloneTransformResult : IDisposable
    {
        internal ObjectIdCollection ClonedIds { get; } = [];

        public void Dispose()
        {
            ClonedIds.Dispose();
        }
    }

    /// <summary>
    /// Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(ViewportInfo main, ViewportInfo aux, Logger log)
    {
        Vector3d z = Vector3d.ZAxis;
        Point3d origin = Point3d.Origin;

        Matrix3d tAux = Matrix3d.Displacement(origin - aux.ViewCenter);
        Matrix3d rAux = Matrix3d.Rotation(-aux.ViewTwist, z, origin);
        Matrix3d sAux = Matrix3d.Scaling(aux.CustomScale, origin);
        Matrix3d rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        Matrix3d tMain = Matrix3d.Displacement(main.ViewCenter - origin);
        Matrix3d sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        Matrix3d tPaper = Matrix3d.Displacement(aux.CenterPaper - main.CenterPaper);

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
    internal static Matrix3d BuildPaperToMainMatrix(ViewportInfo main, Logger log)
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
    internal static void ScaleModelSpaceObjects(Database db, Matrix3d matrix, double ratio, Logger log)
    {
        int total = 0;
        int scaled = 0;

        Dictionary<string, int> errorTypes = [];

        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (trx.GetObject(id, OpenMode.ForWrite) is Entity ent)
            {
                total++;

                if (ent is Viewport)
                {
                    continue;
                }

                string entType = ent.GetType().Name;
                string handle = ent.Handle.ToString();

                try
                {
                    Extents3d? oldExt = ExtentsUtils.TryGetExtents(ent);

                    EntityTransformUtils.TransformResult transformResult = EntityTransformUtils.TransformEntity(ent, matrix);

                    if (transformResult.SkippedAssociativeHatch)
                    {
                        continue;
                    }

                    Extents3d? newExt = ExtentsUtils.TryGetExtents(ent);

                    if (ExtentsUtils.TryGetScaleRatio(oldExt, newExt, out double oldDig, out double newDig, out double digRatio) && digRatio > (ratio * 5.0))
                    {
                        log.Warning($"[АНОМАЛИЯ МАСШТАБА] Тип: {entType}, Handle: {handle}. Диагональ ДО: {oldDig:F2}, ПОСЛЕ: {newDig:F2}");
                    }

                    scaled++;
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
        }

        trx.Commit();

        log.Information($"ScaleModelSpaceObjects завершен: ratio={ratio:F6}, total={total}, scaled={scaled}");

        if (errorTypes.Count > 0)
        {
            string errorStr = string.Join(", ", errorTypes.Select(kv => $"{kv.Key}({kv.Value})"));
            log.Warning($"Ошибочные типы (Scale): {errorStr}");
        }
    }

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId, Logger log)
    {
        int total = 0;

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
    /// Глубоко клонирует набор объектов и применяет матрицу трансформации к каждому клону.
    /// </summary>
    /// <remarks>
    /// Type-specific transform compensation is delegated to <see cref="EntityTransformUtils"/>.
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

        using ObjectIdCollection validIds = [];
        foreach (ObjectId id in sourceIds)
        {
            if (!id.IsNull && !id.IsErased)
            {
                _ = validIds.Add(id);
            }
        }

        if (validIds.Count == 0)
        {
            return new CloneTransformResult();
        }

        using IdMapping map = [];
        CloneTransformResult result = new();

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            db.DeepCloneObjects(validIds, ownerId, map, false);

            foreach (IdPair pair in map)
            {
                if (pair.IsCloned && pair.IsPrimary)
                {
                    if (trx.GetObject(pair.Value, OpenMode.ForWrite) is Entity e)
                    {
                        try
                        {
                            _ = EntityTransformUtils.TransformEntity(e, matrix);
                            _ = result.ClonedIds.Add(pair.Value);
                        }
                        catch (System.Exception ex)
                        {
                            log.Warning($"[ОШИБКА КЛОНА] {e.GetType().Name} {e.Handle}: {ex.Message}");
                        }
                    }
                }
            }
            trx.Commit();
        }

        DrawOrderPreserver.Restore(db, ownerId, sourceOrder, map, log);
        return result;
    }

    internal static ObjectIdCollection SelectModelInside(IReadOnlyList<ModelEntitySnapshot> modelEntities, Extents3d window, Logger log)
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

    internal static int NormalizeDimensionsInsideViewport(
        Database db,
        IReadOnlyList<ModelEntitySnapshot> modelEntities,
        ViewportInfo viewport,
        double styleScaleMultiplier,
        Logger log)
    {
        double vpScale = ResolveViewportScale(viewport);
        int normalized = 0;
        int overridesCleared = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        foreach (ModelEntitySnapshot snapshot in modelEntities)
        {
            if (snapshot.Id.IsNull
                || snapshot.Id.IsErased
                || !ExtentsUtils.AabbIntersect(viewport.ModelWindow, snapshot.Extents)
                || trx.GetObject(snapshot.Id, OpenMode.ForWrite, false) is not Dimension dimension)
            {
                continue;
            }

            if (DimensionUtils.TryRemoveDimensionStyleOverrides(dimension))
            {
                overridesCleared++;
            }

            ObjectId normalizedStyleId = DimensionStyleNormalizer.NormalizeDimensionStyleForViewport(
                dimension.DimensionStyle,
                db,
                vpScale,
                styleScaleMultiplier,
                trx);
            if (normalizedStyleId.IsNull)
            {
                continue;
            }

            dimension.DimensionStyle = normalizedStyleId;
            dimension.RecomputeDimensionBlock(true);
            dimension.RecordGraphicsModified(true);
            normalized++;
        }

        trx.Commit();

        log.Debug(
            "VP #{Number}: normalized {Count} dimensions with vpScale={Scale:F6}, styleScaleMultiplier={StyleScaleMultiplier:F6}, overridesCleared={OverridesCleared}",
            viewport.Number,
            normalized,
            vpScale,
            styleScaleMultiplier,
            overridesCleared);

        return normalized;
    }

    internal static int FinalizeModelSpaceDimensionLinearScales(Database db, Logger log)
    {
        int finalized = 0;
        int stylesFinalized = 0;
        int overridesCleared = 0;
        HashSet<ObjectId> finalizedStyleIds = [];
        ObjectId msId = SymbolUtilityServices.GetBlockModelSpaceId(db);

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (ObjectId id in modelSpace)
        {
            if (id.IsNull
                || id.IsErased
                || trx.GetObject(id, OpenMode.ForWrite, false) is not Dimension dimension)
            {
                continue;
            }

            if (DimensionUtils.TryRemoveDimensionStyleOverrides(dimension))
            {
                overridesCleared++;
            }

            ObjectId styleId = ResolveDimensionStyleId(db, dimension.DimensionStyle);
            if (!styleId.IsNull
                && !styleId.IsErased
                && finalizedStyleIds.Add(styleId)
                && trx.GetObject(styleId, OpenMode.ForWrite, false) is DimStyleTableRecord style
                && !style.IsErased)
            {
                style.Dimlfac = 1.0;
                stylesFinalized++;
            }

            dimension.Dimlfac = 1.0;
            dimension.RecomputeDimensionBlock(true);
            dimension.RecordGraphicsModified(true);
            finalized++;
        }

        trx.Commit();

        log.Information(
            "FinalizeModelSpaceDimensionLinearScales: dimensions={Dimensions}, styles={Styles}, overridesCleared={OverridesCleared}",
            finalized,
            stylesFinalized,
            overridesCleared);

        return finalized;
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
    internal static int EraseEntitiesOutsideMainWindow(Database db, ObjectIdCollection auxEntities, IReadOnlyList<ModelEntitySnapshot> modelSnapshots, Extents3d mainWindow, Logger log)
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
                erased++;
            }
        }

        trx.Commit();
        log.Information($"EraseEntitiesOutsideMainWindow: erased={erased} of {auxEntities.Count}, inMain={inMain.Count}");
        return erased;
    }

    /// <summary>
    /// Обнуляет фиксированную высоту текста во всех текстовых стилях базы данных.
    /// Если высота задана жёстко (TextSize > 0), AutoCAD не применяет масштаб
    /// размерных стилей ожидаемым образом. Обнуление «отвязывает» высоту.
    /// Вызывается перед масштабированием объектов Model Space.
    /// </summary>
    internal static void UnlockTextStylesHeight(Database db, Logger log)
    {
        int unlocked = 0;

        using Transaction trx = db.TransactionManager.StartTransaction();

        TextStyleTable tt = (TextStyleTable)trx.GetObject(db.TextStyleTableId, OpenMode.ForRead);

        foreach (ObjectId tsId in tt)
        {
            TextStyleTableRecord ts = (TextStyleTableRecord)trx.GetObject(tsId, OpenMode.ForRead);

            if (ts.TextSize > 0.0)
            {
                ts.UpgradeOpen();
                ts.TextSize = 0.0;
                unlocked++;
            }
        }

        trx.Commit();
        log.Information($"UnlockTextStylesHeight: разблокировано {unlocked} текстовых стилей");
    }

    private static ObjectId ResolveDimensionStyleId(Database db, ObjectId styleId)
    {
        return !styleId.IsNull && !styleId.IsErased ? styleId : db.Dimstyle;
    }

    private static double ResolveViewportScale(ViewportInfo viewport)
    {
        return viewport.CustomScale > 0.0 ? 1.0 / viewport.CustomScale : 1.0;
    }
}
