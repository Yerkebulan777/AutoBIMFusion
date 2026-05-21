using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Merge.Diagnostics;
using Serilog.Core;

namespace AutoBIMFusion.Merge.Combine.Layouts;

internal static class LayoutProjectionProcessor
{
    internal static LayoutProjectionResult ProjectLayoutToModelSpace(Database db, string layoutName,
        IReadOnlyList<ViewportInfo> viewports, Logger log, MergeDiagnosticContext? diagnosticContext = null)
    {
        return viewports.Count == 0
            ? ProjectNoViewport(db, layoutName, log, diagnosticContext)
            : ProjectWithViewports(db, layoutName, viewports, log, diagnosticContext);
    }

    private static LayoutProjectionResult ProjectWithViewports(Database db, string layoutName,
        IReadOnlyList<ViewportInfo> viewports, Logger log, MergeDiagnosticContext? diagnosticContext)
    {
        var mainOriginal = ViewportInfo.PickMainViewport(viewports);
        var scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale, log);
        var mainNormalized = mainOriginal with { CustomScale = scale.WorkingCustomScale };

        MergeDiagnostics.WriteEvent(diagnosticContext, "scale.normalized", new Dictionary<string, object?>
        {
            ["layoutName"] = layoutName,
            ["mainViewportNumber"] = mainOriginal.Number,
            ["originalCustomScale"] = mainOriginal.CustomScale,
            ["workingCustomScale"] = scale.WorkingCustomScale,
            ["geometryScale"] = scale.GeometryScale,
            ["targetVisualScale"] = scale.TargetVisualScale,
            ["linearScaleMultiplier"] = scale.LinearScaleMultiplier,
            ["mainModelWindow"] = MergeDiagnostics.FormatExtents(mainOriginal.ModelWindow)
        });

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using ObjectIdCollection allClonedIds = [];

        if (viewports.Count > 1)
        {
            var originalModelSnapshot = ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);
            log.Debug("[AUX-VP] ModelSpace snapshot: {Count} entities captured before aux processing",
                originalModelSnapshot.Count);

            ProjectAuxViewports(db, msId, mainOriginal, scale.GeometryScale, viewports, originalModelSnapshot,
                allClonedIds, log, diagnosticContext);
        }

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log, diagnosticContext, allClonedIds);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var layoutData = CollectAndFilterLayoutData(db, layoutName);

        using (layoutData.FilteredPaperIds)
        {
            var matrix = ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log);
            var frameBounds = TransformFrameBounds(layoutData.FrameBounds, matrix);
            MovePaperToModelSpace(db, layoutData.BtrId, layoutData.FilteredPaperIds, matrix, log, diagnosticContext,
                frameBounds);

            return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
        }
    }

    /// <summary>
    ///     Обрабатывает все вспомогательные Viewport'ы, проецируя содержимое их окон в Model Space
    ///     относительно главного Viewport'а.
    ///     <para>
    ///         КРИТИЧНО: Слепок сущностей модели (modelEntities) снимается ОДИН РАЗ до начала цикла.
    ///         Это предотвращает «feedback loop» — ситуацию, когда клоны, созданные на первой итерации,
    ///         попадают в выборку второй итерации и подвергаются повторной трансформации (масштаб × масштаб).
    ///         Все итерации работают с одним и тем же снимком исходных объектов.
    ///     </para>
    ///     <para>
    ///         allClonedIds накапливает ID всех созданных клонов и передаётся в финальный
    ///         ScaleModelSpaceObjects, чтобы главный масштаб не применился к уже трансформированным клонам.
    ///     </para>
    /// </summary>
    private static void ProjectAuxViewports(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        double geometryScale,
        IReadOnlyList<ViewportInfo> viewports,
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> originalModelSnapshot,
        ObjectIdCollection allClonedIds,
        Logger log,
        MergeDiagnosticContext? diagnosticContext)
    {
        if (viewports.Count <= 1) return;

        // НАКОПИТЕЛЬНЫЙ ФИЛЬТР: единый набор для всех итераций.
        // claimedSourceIds — исходные объекты, которые уже были клонированы (защита от двойного клонирования).
        // allClonedIds — ID всех созданных клонов (защита от финального масштабирования).
        HashSet<ObjectId> claimedSourceIds = [];
        using var mainWindowEntityIds = CollectEntityIdsInsideWindow(originalModelSnapshot, mainOriginal.ModelWindow);

        foreach (var aux in viewports)
        {
            if (aux.VpId == mainOriginal.VpId) continue;

            ProjectAuxViewport(db, msId, mainOriginal, geometryScale, aux, originalModelSnapshot, mainWindowEntityIds,
                claimedSourceIds, allClonedIds, log, diagnosticContext);
        }

        using var countTrx = db.TransactionManager.StartTransaction();
        var msForCount = (BlockTableRecord)countTrx.GetObject(msId, OpenMode.ForRead);
        var countAfter = msForCount.Cast<ObjectId>().Count();
        countTrx.Commit();

        log.Debug(
            "[AUX-VP] Processing complete: sources={Sources}, clones={Clones}, ModelSpace total after={TotalAfter}",
            claimedSourceIds.Count, allClonedIds.Count, countAfter);
    }

    private static void ProjectAuxViewport(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        double geometryScale,
        ViewportInfo aux,
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities,
        ObjectIdCollection mainWindowEntityIds,
        HashSet<ObjectId> claimedSourceIds,
        ObjectIdCollection allClonedIds,
        Logger log,
        MergeDiagnosticContext? diagnosticContext)
    {
        // Компонуем Aux→Main матрицу с матрицей нормализации масштаба.
        var auxToMain = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);
        var scaleMatrix = Matrix3d.Scaling(geometryScale, mainOriginal.ViewCenter);
        var matrix = scaleMatrix * auxToMain;

        log.Debug(
            "[AUX-VP-MATRIX] Aux#{AuxNumber}: geometryScale={GeometryScale:F6}, scaleCenter={ScaleCenter}, composedMatrix=true",
            aux.Number, geometryScale, ExtentsUtils.FormatPoint(mainOriginal.ViewCenter));

        using var selection =
            ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, mainOriginal.ModelWindow, log);

        using var toClone = CollectUnclaimedObjectIds(selection.SelectedIds, claimedSourceIds, out var duplicateSkipped);

        MergeDiagnostics.WriteEvent(diagnosticContext, "aux.selected", new Dictionary<string, object?>
        {
            ["auxViewportNumber"] = aux.Number,
            ["cached"] = modelEntities.Count,
            ["selected"] = selection.SelectedIds.Count,
            ["toClone"] = toClone.Count,
            ["duplicateSkipped"] = duplicateSkipped,
            ["outsideWindow"] = selection.OutsideWindow,
            ["smallPartialOutsideWindow"] = selection.SmallPartialOutsideWindow,
            ["skippedHuge"] = selection.SkippedHugeObjects,
            ["centerOutsideWindow"] = selection.CenterOutsideWindow,
            ["selectedHandleSamples"] = selection.SelectedHandleSamples,
            ["outsideHandleSamples"] = selection.OutsideHandleSamples,
            ["smallPartialHandleSamples"] = selection.SmallPartialHandleSamples,
            ["hugeHandleSamples"] = selection.HugeHandleSamples,
            ["centerOutsideHandleSamples"] = selection.CenterOutsideHandleSamples,
            ["window"] = MergeDiagnostics.FormatExtents(aux.ModelWindow)
        });

        if (toClone.Count == 0)
        {
            log.Debug(
                $"Aux#{aux.Number}: candidates={selection.SelectedIds.Count}, duplicateSkipped={duplicateSkipped}, nothing to clone");
            return;
        }

        log.Debug(
            $"Aux#{aux.Number}: candidates={selection.SelectedIds.Count}, toClone={toClone.Count}, duplicateSkipped={duplicateSkipped}");

        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, toClone, msId, msId, matrix, log);

        foreach (ObjectId clonedId in cloneResult.ClonedIds)
            _ = allClonedIds.Add(clonedId);

        ViewportTransformer.EraseEntitiesOutsideMainWindow(db, toClone, mainWindowEntityIds);
    }

    /// <summary>
    ///     Собирает ID сущностей из снимка модели, чьи extents пересекаются с окном главного VP.
    ///     Возвращает ObjectIdCollection — нативную коллекцию AutoCAD, которая корректно работает
    ///     с API удаления (Erase) и гарантирует правильную обработку ObjectId в транзакциях.
    /// </summary>
    private static ObjectIdCollection CollectEntityIdsInsideWindow(
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities,
        Extents3d window)
    {
        ObjectIdCollection result = [];

        foreach (var entity in modelEntities)
            if (ExtentsUtils.AabbIntersect(window, entity.Extents))
                _ = result.Add(entity.Id);

        return result;
    }

    private static ObjectIdCollection CollectUnclaimedObjectIds(
        ObjectIdCollection candidates,
        HashSet<ObjectId> claimedSourceIds,
        out int duplicateSkipped)
    {
        ObjectIdCollection result = [];
        duplicateSkipped = 0;

        foreach (ObjectId id in candidates)
            if (claimedSourceIds.Add(id))
                _ = result.Add(id);
            else
                duplicateSkipped++;

        return result;
    }

    private static LayoutProjectionResult ProjectNoViewport(
        Database db,
        string layoutName,
        Logger log,
        MergeDiagnosticContext? diagnosticContext)
    {
        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var layoutData = CollectAndFilterLayoutData(db, layoutName);

        using (layoutData.FilteredPaperIds)
        {
            if (layoutData.FilteredPaperIds.Count == 0 || layoutData.BtrId.IsNull)
            {
                MergeDiagnostics.WriteEvent(diagnosticContext, "paper.moved", new Dictionary<string, object?>
                {
                    ["layoutName"] = layoutName,
                    ["paperEntityCount"] = layoutData.FilteredPaperIds.Count,
                    ["frameBounds"] = null,
                    ["resultBounds"] = null
                });
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            var paperBounds = ComputeBounds(db, layoutData.FilteredPaperIds, log);

            if (!paperBounds.HasValue)
            {
                MergeDiagnostics.WriteEvent(diagnosticContext, "paper.moved", new Dictionary<string, object?>
                {
                    ["layoutName"] = layoutName,
                    ["paperEntityCount"] = layoutData.FilteredPaperIds.Count,
                    ["frameBounds"] = null,
                    ["resultBounds"] = null
                });
                return new LayoutProjectionResult(null, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
            }

            var minPt = paperBounds.Value.MinPoint;

            var matrix = Matrix3d.Scaling(ViewportScaleNormalizer.WorkingScaleMultiplier, Point3d.Origin) *
                         Matrix3d.Displacement(Point3d.Origin - minPt);

            var frameBounds = TransformFrameBounds(layoutData.FrameBounds, matrix);
            MovePaperToModelSpace(db, layoutData.BtrId, layoutData.FilteredPaperIds, matrix, log, diagnosticContext,
                frameBounds);

            return new LayoutProjectionResult(frameBounds, ViewportScaleNormalizer.WorkingScaleMultiplier, 1.0);
        }
    }

    /// <summary>
    ///     Собирает идентификаторы Paper Space, ищет рамку-штамп и фильтрует объекты
    ///     в рамках единственной транзакции, заменяя цепочку из 5–8 отдельных транзакций.
    /// </summary>
    /// <returns>BTR-идентификатор листа и отфильтрованная коллекция объектов.</returns>
    private static LayoutData CollectAndFilterLayoutData(Database db, string layoutName)
    {
        using var trx = db.TransactionManager.StartTransaction();

        var layoutDict = (DBDictionary)trx.GetObject(db.LayoutDictionaryId, OpenMode.ForRead);

        if (!layoutDict.Contains(layoutName))
        {
            trx.Commit();
            return new LayoutData(ObjectId.Null, [], null);
        }

        var layoutId = layoutDict.GetAt(layoutName);
        var layout = (Layout)trx.GetObject(layoutId, OpenMode.ForRead);
        var btrId = layout.BlockTableRecordId;
        var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);

        var viewportClass = RXObject.GetClass(typeof(Viewport));

        var clipEntityIds = CollectViewportClipEntityIds(trx, btr, viewportClass);
        var paperIdsList = CollectPaperEntityIds(btr, viewportClass, clipEntityIds);

        // Поиск рамки-штампа и фильтрация в той же транзакции
        var titleBlockBounds = BlockReferences.FindLargestBlockReferenceBoundsByArea(trx, paperIdsList.Cast<ObjectId>());
        var filteredIds = FilterEntitiesByBounds(trx, paperIdsList, titleBlockBounds);

        trx.Commit();
        return new LayoutData(btrId, filteredIds, titleBlockBounds);
    }

    private static ObjectIdCollection CollectViewportClipEntityIds(
        Transaction trx,
        BlockTableRecord btr,
        RXClass viewportClass)
    {
        ObjectIdCollection clipEntityIds = new();

        foreach (ObjectId id in btr)
        {
            if (!id.ObjectClass.IsDerivedFrom(viewportClass)) continue;

            var viewport = (Viewport)trx.GetObject(id, OpenMode.ForRead);
            if (!viewport.NonRectClipEntityId.IsNull) _ = clipEntityIds.Add(viewport.NonRectClipEntityId);
        }

        return clipEntityIds;
    }

    private static ObjectIdCollection CollectPaperEntityIds(
        BlockTableRecord btr,
        RXClass viewportClass,
        ObjectIdCollection clipEntityIds)
    {
        ObjectIdCollection paperIds = new();

        foreach (ObjectId id in btr)
            if (!id.ObjectClass.IsDerivedFrom(viewportClass) && !clipEntityIds.Contains(id))
                paperIds.Add(id);

        return paperIds;
    }

    /// <summary>
    ///     Фильтрует объекты листа, оставляя только те, что пересекаются с рамкой-штампом.
    ///     Работает в рамках переданной транзакции, не открывая новых.
    ///     Всегда возвращает новую коллекцию — владение передаётся вызывающей стороне.
    /// </summary>
    private static ObjectIdCollection FilterEntitiesByBounds(Transaction trx, ObjectIdCollection paperIds,
        Extents3d? boundingBox)
    {
        ObjectIdCollection filtered = new();

        if (!boundingBox.HasValue)
        {
            // Рамка не найдена — включаем все объекты
            foreach (ObjectId id in paperIds) _ = filtered.Add(id);

            return filtered;
        }

        var bounds = boundingBox.Value;

        foreach (ObjectId id in paperIds)
        {
            if (trx.GetObject(id, OpenMode.ForRead) is not Entity ent) continue;

            // Сама рамка-штамп (BlockReference с совпадающими габаритами) включается всегда
            if (ent is BlockReference br)
            {
                var ext = ExtentsUtils.TryGetExtents(br);
                if (ext.HasValue && ExtentsUtils.ExtentsApproxEqual(ext.Value, bounds))
                {
                    _ = filtered.Add(id);
                    continue;
                }
            }

            var entityExt = ExtentsUtils.TryGetExtents(ent);
            if (entityExt.HasValue && ExtentsUtils.AabbIntersect(bounds, entityExt.Value))
            {
                _ = filtered.Add(id);
                continue;
            }

            if (ExtentsUtils.IsEntityPointIn(ent, bounds)) _ = filtered.Add(id);
        }

        return filtered;
    }

    /// <summary>
    ///     Масштабирует объекты Model Space вокруг указанного центра до рабочего масштаба 1:100.
    /// </summary>
    private static void NormalizeModelSpaceScale(
        Database db,
        double geometryScale,
        Point3d center,
        Logger log,
        MergeDiagnosticContext? diagnosticContext,
        ObjectIdCollection? clonedIdsToSkip = null)
    {
        if (Abs(geometryScale - 1.0) > 1e-9)
        {
            var scaleMatrix = Matrix3d.Scaling(geometryScale, center);
            ViewportTransformer.ScaleModelSpaceObjects(db, scaleMatrix, geometryScale, log, diagnosticContext, clonedIdsToSkip);
            return;
        }

        MergeDiagnostics.WriteEvent(diagnosticContext, "model.scaled", new Dictionary<string, object?>
        {
            ["ratio"] = geometryScale,
            ["skipped"] = true,
            ["reason"] = "geometryScale-is-one"
        });
    }

    /// <summary>
    ///     Клонирует отфильтрованные Paper Space объекты в Model Space и стирает исходный BTR.
    ///     Принимает заранее вычисленные данные — не открывает дополнительных транзакций для
    ///     получения списка сущностей.
    /// </summary>
    private static void MovePaperToModelSpace(
        Database db,
        ObjectId paperBtrId,
        ObjectIdCollection filteredIds,
        Matrix3d matrix,
        Logger log,
        MergeDiagnosticContext? diagnosticContext,
        Extents3d? frameBounds)
    {
        if (filteredIds.Count == 0 || paperBtrId.IsNull)
        {
            MergeDiagnostics.WriteEvent(diagnosticContext, "paper.moved", new Dictionary<string, object?>
            {
                ["paperEntityCount"] = filteredIds.Count,
                ["frameBounds"] = MergeDiagnostics.FormatExtents(frameBounds),
                ["resultBounds"] = null
            });
            return;
        }

        var msId = SymbolUtilityServices.GetBlockModelSpaceId(db);
        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, filteredIds, paperBtrId, msId, matrix, log);

        BlockReferences.EraseBlockContents(db, paperBtrId);

        var resultBounds = ComputeBounds(db, cloneResult.ClonedIds, log);
        MergeDiagnostics.WriteEvent(diagnosticContext, "paper.moved", new Dictionary<string, object?>
        {
            ["paperEntityCount"] = filteredIds.Count,
            ["clonedEntityCount"] = cloneResult.ClonedIds.Count,
            ["frameBounds"] = MergeDiagnostics.FormatExtents(frameBounds),
            ["resultBounds"] = MergeDiagnostics.FormatExtents(resultBounds)
        });
    }

    private static Extents3d? TransformFrameBounds(Extents3d? frameBounds, Matrix3d matrix)
    {
        return frameBounds.HasValue ? ExtentsUtils.Transform(frameBounds.Value, matrix) : null;
    }

    private static Extents3d? ComputeBounds(Database db, ObjectIdCollection entityIds, Logger log)
    {
        var result = ExtentsUtils.ComputeBounds(db, entityIds);

        if (result.HasValue)
            log.Debug("ComputeBounds: entities={Count}, bounds={Bounds}", entityIds.Count, ExtentsUtils.FormatExtents(result.Value));

        return result;
    }

    private sealed record LayoutData(ObjectId BtrId, ObjectIdCollection FilteredPaperIds, Extents3d? FrameBounds);

    internal sealed record LayoutProjectionResult(
        Extents3d? FrameBounds,
        double TargetVisualScale,
        double LinearScaleMultiplier);
}
