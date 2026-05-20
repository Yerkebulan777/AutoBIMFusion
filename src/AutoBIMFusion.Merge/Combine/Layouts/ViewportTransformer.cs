using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Diagnostics;
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
    private const double HugeEntityDiagonalRatio = 3.0;
    private const double SmallEntityDiagonalThreshold = 100.0;

    /// <summary>
    ///     Матрица переноса «модель aux-VP → модель main-VP».
    /// </summary>
    internal static Matrix3d BuildMatrix(ViewportInfo main, ViewportInfo aux, Logger log)
    {
        var z = Vector3d.ZAxis;
        var origin = Point3d.Origin;

        // PaperFromAuxModel: tAux -> rAux -> sAux
        var tAux = Matrix3d.Displacement(origin - aux.ViewCenter);
        var rAux = Matrix3d.Rotation(-aux.ViewTwist, z, origin);
        var sAux = Matrix3d.Scaling(aux.CustomScale, origin);

        // MainModelFromPaper: tPaper -> sMain -> rMain -> tMain
        var tPaper = Matrix3d.Displacement(aux.CenterPaper - main.CenterPaper);
        var sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        var rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        var tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        var result = tMain * rMain * sMain * tPaper * sAux * rAux * tAux;

        log.Debug(
            "BuildMatrix aux#{AuxNumber} -> main#{MainNumber}: auxScale={AuxScale:F6}, mainScale={MainScale:F6}, auxTwist={AuxTwist:F6}, mainTwist={MainTwist:F6}, auxWindow={AuxWindow}",
            aux.Number, main.Number, aux.CustomScale, main.CustomScale, aux.ViewTwist, main.ViewTwist, ExtentsUtils.FormatExtents(aux.ModelWindow));

        return result;
    }

    /// <summary>
    ///     Матрица переноса «бумага → модель main-VP». Используется для содержимого Paper Space
    ///     (рамка, штамп, тексты) когда его пересаживают в Model Space через главный VP.
    /// </summary>
    internal static Matrix3d BuildPaperToMainMatrix(ViewportInfo main, Logger log)
    {
        var z = Vector3d.ZAxis;
        var origin = Point3d.Origin;

        // PaperToMain: tPaper -> sMain -> rMain -> tMain
        var tPaper = Matrix3d.Displacement(origin - main.CenterPaper);
        var sMain = Matrix3d.Scaling(1.0 / main.CustomScale, origin);
        var rMain = Matrix3d.Rotation(main.ViewTwist, z, origin);
        var tMain = Matrix3d.Displacement(main.ViewCenter - origin);

        var result = tMain * rMain * sMain * tPaper;

        log.Debug(
            "BuildPaperToMainMatrix main#{MainNumber}: mainScale={MainScale:F6}, mainTwist={MainTwist:F6}, centerPaper={CenterPaper}, viewCenter={ViewCenter}",
            main.Number, main.CustomScale, main.ViewTwist, ExtentsUtils.FormatPoint(main.CenterPaper), ExtentsUtils.FormatPoint(main.ViewCenter));

        return result;
    }

    /// <summary>
    ///     Применяет матрицу трансформации ко всем объектам модели в базе данных.
    ///     Viewport'ы пропускаются; особенности конкретных типов сущностей обрабатывает
    ///     <see cref="EntityTransformUtils" />.
    /// </summary>
    internal static void ScaleModelSpaceObjects(
        Database db,
        Matrix3d matrix,
        double ratio,
        Logger log,
        MergeDiagnosticContext? diagnosticContext = null,
        HashSet<ObjectId>? clonedIds = null)
    {
        Dictionary<string, int> errorTypes = [];
        List<Dictionary<string, object?>> anomalySamples = [];

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        var total = 0;
        var transformed = 0;
        var viewportSkipped = 0;
        var associativeHatchSkipped = 0;

        using var trx = db.TransactionManager.StartTransaction();
        var modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (var id in modelSpace)
        {
            // Открываем в ForRead — большинство объектов будут пропущены (Viewport, клоны).
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

            total++;

            if (ent is Viewport)
            {
                viewportSkipped++;
                continue;
            }

            if (clonedIds != null && clonedIds.Contains(id))
            {
                if (log.IsEnabled(Serilog.Events.LogEventLevel.Debug))
                {
                    log.Debug(
                        "[МАСШТАБ-ПРОПУСК] Clone Handle={Handle}, Type={EntityType} — уже масштабирован через BuildMatrix",
                        ent.Handle.ToString(), ent.GetType().Name);
                }

                continue;
            }

            // Переводим в ForWrite только для объектов, которые будем трансформировать.
            ent.UpgradeOpen();

            try
            {
                var oldExt = ExtentsUtils.TryGetExtents(ent);

                var transformResult = EntityTransformUtils.TransformEntity(ent, matrix, trx);

                if (transformResult.SkippedAssociativeHatch)
                {
                    associativeHatchSkipped++;
                    continue;
                }

                transformed++;

                var newExt = ExtentsUtils.TryGetExtents(ent);

                if (ExtentsUtils.TryGetScaleRatio(oldExt, newExt, out var oldDig, out var newDig,
                        out var digRatio) && digRatio > ratio * 5.0)
                {
                    var entType = ent.GetType().Name;
                    var handle = ent.Handle.ToString();
                    log.Warning(
                        "[АНОМАЛИЯ МАСШТАБА] Тип: {EntityType}, Handle: {Handle}. Диагональ ДО: {OldDiag:F2}, ПОСЛЕ: {NewDiag:F2}",
                        entType, handle, oldDig, newDig);

                    _ = MergeDiagnostics.TryAddSample(anomalySamples, new Dictionary<string, object?>
                    {
                        ["entityType"] = entType,
                        ["handle"] = handle,
                        ["oldDiagonal"] = oldDig,
                        ["newDiagonal"] = newDig,
                        ["diagonalRatio"] = digRatio
                    });
                }
            }
            catch (Exception ex)
            {
                var entType = ent.GetType().Name;
                var handle = ent.Handle.ToString();
                log.Error(ex, "[ОШИБКА ТРАНСФОРМАЦИИ] Тип: {EntityType}, Handle: {Handle}. Сообщение: {Message}",
                    entType, handle, ex.Message);

                errorTypes[entType] = errorTypes.GetValueOrDefault(entType) + 1;
            }
        }

        trx.Commit();

        log.Debug(
            "[МАСШТАБ] Итого: {Total}, transformed={Transformed}, viewportSkipped={ViewportSkipped}, hatchSkipped={HatchSkipped}",
            total, transformed, viewportSkipped, associativeHatchSkipped);

        MergeDiagnostics.WriteEvent(diagnosticContext, "model.scaled", new Dictionary<string, object?>
        {
            ["ratio"] = ratio,
            ["total"] = total,
            ["transformed"] = transformed,
            ["viewportSkipped"] = viewportSkipped,
            ["associativeHatchSkipped"] = associativeHatchSkipped,
            ["errorTypes"] = errorTypes,
            ["anomalySamples"] = anomalySamples
        });

        if (errorTypes.Count > 0)
        {
            var errorStr = string.Join(", ", errorTypes.Select(kv => $"{kv.Key}({kv.Value})"));
            log.Warning("Ошибочные типы (Scale): {ErrorTypes}", errorStr);
        }
    }

    internal static IReadOnlyList<ModelEntitySnapshot> CollectModelEntitiesWithExtents(Database db, ObjectId msId,
        Logger log)
    {
        var total = 0;

        List<ModelEntitySnapshot> result = [];

        using var trx = db.TransactionManager.StartTransaction();
        var modelSpace = (BlockTableRecord)trx.GetObject(msId, OpenMode.ForRead);

        foreach (var id in modelSpace)
        {
            total++;

            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

            if (ent is Viewport) continue;

            var ext = ExtentsUtils.TryGetExtents(ent);
            if (ext is null) continue;

            result.Add(new ModelEntitySnapshot(id, ext.Value, ent.Handle.ToString(), ent.GetType().Name));
        }

        trx.Commit();
        log.Debug("CollectModelEntitiesWithExtents total={Total}, cached={Cached}", total, result.Count);
        return result;
    }

    /// <summary>
    ///     Глубоко клонирует набор объектов и применяет матрицу трансформации к каждому клону.
    /// </summary>
    /// <remarks>
    ///     Компенсация трансформации для конкретных типов делегируется <see cref="EntityTransformUtils" />.
    /// </remarks>
    internal static CloneTransformResult DeepCloneAndTransform(
        Database db,
        ObjectIdCollection sourceIds,
        ObjectId sourceOwnerId,
        ObjectId ownerId,
        Matrix3d matrix,
        Logger log)
    {
        var sourceOrder = DrawOrderPreserver.Capture(db, sourceOwnerId, sourceIds, log);

        using var validIds = CollectValidIds(sourceIds);

        if (validIds.Count == 0) return new CloneTransformResult();

        using IdMapping map = [];
        CloneTransformResult result = new();

        using (var trx = db.TransactionManager.StartTransaction())
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
            if (id.IsValidForOperation())
                _ = validIds.Add(id);

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
            if (!pair.IsCloned || !pair.IsPrimary) continue;

            if (trx.GetObject(pair.Value, OpenMode.ForWrite) is not Entity entity) continue;

            try
            {
                var oldExt = ExtentsUtils.TryGetExtents(entity);

                _ = EntityTransformUtils.TransformEntity(entity, matrix, trx);

                var newExt = ExtentsUtils.TryGetExtents(entity);

                log.Debug(
                    "[КЛОН] SourceHandle={SourceHandle} -> ClonedHandle={ClonedHandle}, Type={EntityType}, ExtentsBefore={Before}, ExtentsAfter={After}",
                    pair.Key, entity.Handle, entity.GetType().Name,
                    FormatExtentsNullable(oldExt), FormatExtentsNullable(newExt));

                _ = result.ClonedIds.Add(pair.Value);
            }
            catch (Exception ex)
            {
                log.Warning("[ОШИБКА КЛОНА] {EntityType} {Handle}: {Message}", entity.GetType().Name, entity.Handle, ex.Message);
            }
        }
    }

    private static string FormatExtentsNullable(Extents3d? ext)
    {
        return ext.HasValue ? ExtentsUtils.FormatExtents(ext.Value) : "<null>";
    }

    /// <param name="mainWindow">
    ///     Модельное окно главного VP. Используется в эвристике: пропускать сущность,
    ///     только если она огромна И при этом принадлежит главному VP (пересекает mainWindow).
    ///     Большие сущности, не пересекающие mainWindow, — легитимный контент вспомогательного VP.
    /// </param>
    internal static ModelSelectionResult SelectModelInside(IReadOnlyList<ModelEntitySnapshot> modelEntities,
        Extents3d window, Extents3d mainWindow, Logger log)
    {
        var outsideWindow = 0;
        var smallPartialOutsideWindow = 0;
        var skippedHugeObjects = 0;
        ModelSelectionResult result = new();

        foreach (var entity in modelEntities)
        {
            var decision = ClassifyModelEntityForViewport(window, mainWindow, entity.Extents);

            if (decision == ModelEntitySelection.OutsideWindow)
            {
                outsideWindow++;
                _ = MergeDiagnostics.TryAddSample(result.OutsideHandleSamples, entity.Handle);
                continue;
            }

            if (decision == ModelEntitySelection.SmallPartialOutsideWindow)
            {
                smallPartialOutsideWindow++;
                _ = MergeDiagnostics.TryAddSample(result.SmallPartialHandleSamples, entity.Handle);
                continue;
            }

            if (decision == ModelEntitySelection.HugeInMainWindow)
            {
                skippedHugeObjects++;
                _ = MergeDiagnostics.TryAddSample(result.HugeHandleSamples, entity.Handle);
                continue;
            }

            if (decision == ModelEntitySelection.CenterOutsideWindow)
            {
                result.CenterOutsideWindow++;
                _ = MergeDiagnostics.TryAddSample(result.CenterOutsideHandleSamples, entity.Handle);
                continue;
            }

            _ = result.SelectedIds.Add(entity.Id);
            _ = MergeDiagnostics.TryAddSample(result.SelectedHandleSamples, entity.Handle);
        }

        result.OutsideWindow = outsideWindow;
        result.SmallPartialOutsideWindow = smallPartialOutsideWindow;
        result.SkippedHugeObjects = skippedHugeObjects;

        log.Debug(
            "SelectModelInside cached={Cached}, selected={Selected}, skippedHuge={SkippedHuge}, smallPartialOutside={SmallPartialOutside}, centerOutside={CenterOutside}, outsideWindow={OutsideWindow}, window={Window}",
            modelEntities.Count, result.SelectedIds.Count, skippedHugeObjects, smallPartialOutsideWindow, result.CenterOutsideWindow, outsideWindow, ExtentsUtils.FormatExtents(window));
        return result;
    }

    internal static ModelEntitySelection ClassifyModelEntityForViewport(
        Extents3d window,
        Extents3d mainWindow,
        Extents3d entityExtents)
    {
        if (!ExtentsUtils.AabbIntersect(window, entityExtents))
        {
            return ModelEntitySelection.OutsideWindow;
        }

        var entityDiagonal = entityExtents.MinPoint.DistanceTo(entityExtents.MaxPoint);

        if (entityDiagonal <= SmallEntityDiagonalThreshold
            && !AabbContainsXY(window, entityExtents))
        {
            return ModelEntitySelection.SmallPartialOutsideWindow;
        }

        var windowDiagonal = window.MinPoint.DistanceTo(window.MaxPoint);

        // Большой объект главного VP не должен становиться контентом aux VP.
        if (windowDiagonal > 0
            && entityDiagonal > windowDiagonal * HugeEntityDiagonalRatio
            && ExtentsUtils.AabbIntersect(mainWindow, entityExtents))
        {
            return ModelEntitySelection.HugeInMainWindow;
        }

        // Объект пересекает окно краем, но его геометрический центр снаружи —
        // значит меньше половины объекта попадает в VP, он принадлежит другому VP.
        // Без этой проверки большие объекты (длинные линии, штриховки, выноски)
        // клонируются целиком и после scaleMatrix * auxToMain выходят за рамку листа.
        var centerX = (entityExtents.MinPoint.X + entityExtents.MaxPoint.X) * 0.5;
        var centerY = (entityExtents.MinPoint.Y + entityExtents.MaxPoint.Y) * 0.5;

        if (centerX < window.MinPoint.X || centerX > window.MaxPoint.X ||
            centerY < window.MinPoint.Y || centerY > window.MaxPoint.Y)
        {
            return ModelEntitySelection.CenterOutsideWindow;
        }

        return ModelEntitySelection.Selected;
    }

    private static bool AabbContainsXY(Extents3d outer, Extents3d inner, double tolerance = 1e-6)
    {
        return inner.MinPoint.X >= outer.MinPoint.X - tolerance
               && inner.MaxPoint.X <= outer.MaxPoint.X + tolerance
               && inner.MinPoint.Y >= outer.MinPoint.Y - tolerance
               && inner.MaxPoint.Y <= outer.MaxPoint.Y + tolerance;
    }

    /// <summary>
    ///     Удаляет из модели оригинальные объекты вспомогательного VP, которые НЕ входят в окно
    ///     главного VP. Вызывается после DeepCloneAndTransform для каждого aux VP.
    ///     Логика: если объект виден в главном VP — оставляем (нужен для его плоского представления).
    ///     Если объект только в aux VP — удаляем, так как его клон уже создан на правильной позиции.
    ///     Без этого шага объекты aux VP, чьи модельные координаты попадают в пределы листа,
    ///     не удаляются очисткой малых объектов за рамкой и остаются как «мусор» в результирующем файле.
    /// </summary>
    internal static void EraseEntitiesOutsideMainWindow(Database db, ObjectIdCollection auxEntities,
        IReadOnlySet<ObjectId> mainWindowEntityIds)
    {
        using var trx = db.TransactionManager.StartTransaction();

        foreach (ObjectId id in auxEntities)
        {
            if (mainWindowEntityIds.Contains(id)) continue;

            if (id.IsErased) continue;

            if (trx.GetObject(id, OpenMode.ForWrite) is Entity e && !e.IsErased) e.Erase();
        }

        trx.Commit();
    }

    /// <summary>
    ///     Снимок состояния сущности модели, включая её идентификатор и границы.
    /// </summary>
    internal sealed record ModelEntitySnapshot(ObjectId Id, Extents3d Extents, string Handle, string EntityType);

    internal sealed class ModelSelectionResult : IDisposable
    {
        internal ObjectIdCollection SelectedIds { get; } = [];

        internal int OutsideWindow { get; set; }

        internal int SmallPartialOutsideWindow { get; set; }

        internal int SkippedHugeObjects { get; set; }

        internal List<string> SelectedHandleSamples { get; } = [];

        internal List<string> OutsideHandleSamples { get; } = [];

        internal List<string> SmallPartialHandleSamples { get; } = [];

        internal List<string> HugeHandleSamples { get; } = [];

        internal int CenterOutsideWindow { get; set; }

        internal List<string> CenterOutsideHandleSamples { get; } = [];

        public void Dispose()
        {
            SelectedIds.Dispose();
        }
    }

    internal enum ModelEntitySelection
    {
        Selected,
        OutsideWindow,
        SmallPartialOutsideWindow,
        HugeInMainWindow,
        CenterOutsideWindow
    }

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
