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
        var scale = ViewportScaleNormalizer.Normalize(mainOriginal.CustomScale);
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

        var allClonedIds = ProjectAuxViewports(db, msId, mainOriginal, viewports, log, diagnosticContext);

        NormalizeModelSpaceScale(db, scale.GeometryScale, mainOriginal.ViewCenter, log, diagnosticContext, allClonedIds);

        // Одна транзакция: сбор paper-сущностей + поиск рамки + фильтрация
        var layoutData = CollectAndFilterLayoutData(db, layoutName);

        MergeDiagnostics.WriteEvent(diagnosticContext, "scale.normalized", new Dictionary<string, object?>
        {
            ["layoutName"] = layoutName,
            ["hasViewport"] = false,
            ["workingCustomScale"] = 1.0 / ViewportScaleNormalizer.WorkingScaleMultiplier,
            ["geometryScale"] = ViewportScaleNormalizer.WorkingScaleMultiplier,
            ["targetVisualScale"] = ViewportScaleNormalizer.WorkingScaleMultiplier,
            ["linearScaleMultiplier"] = 1.0
        });

        using (layoutData.FilteredPaperIds)
        {
            var matrix = ViewportTransformer.BuildPaperToMainMatrix(mainNormalized, log);
            var frameBounds = TransformFrameBounds(layoutData.FrameBounds, matrix);
            MovePaperToModelSpace(db, layoutData.BtrId, layoutData.FilteredPaperIds, matrix, log, diagnosticContext,
                frameBounds);

            return new LayoutProjectionResult(frameBounds, scale.TargetVisualScale, scale.LinearScaleMultiplier);
        }
    }

    private static ObjectIdCollection ProjectAuxViewports(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        IReadOnlyList<ViewportInfo> viewports,
        Logger log,
        MergeDiagnosticContext? diagnosticContext)
    {
        ObjectIdCollection allClonedIds = new();
        if (viewports.Count <= 1) return allClonedIds;

        var modelEntities =
            ViewportTransformer.CollectModelEntitiesWithExtents(db, msId, log);

        log.Debug("[AUX-VP] ModelSpace snapshot: {Count} entities captured before aux processing", modelEntities.Count);

        foreach (var aux in viewports)
        {
            if (aux.VpId == mainOriginal.VpId) continue;

            var clonedIds = ProjectAuxViewport(db, msId, mainOriginal, aux, modelEntities, log, diagnosticContext);
            foreach (ObjectId id in clonedIds)
            {
                _ = allClonedIds.Add(id);
            }
        }

        // Глобальная очистка оригиналов: удаляем всё, что не попадает в Main VP, 
        // чтобы NormalizeModelSpaceScale не масштабировал мусор от удаленного центра.
        int erasedCount = 0;
        using var cleanupTrx = db.TransactionManager.StartTransaction();
        foreach (var entitySnapshot in modelEntities)
        {
            if (!ExtentsUtils.AabbIntersect(mainOriginal.ModelWindow, entitySnapshot.Extents))
            {
                if (cleanupTrx.GetObject(entitySnapshot.Id, OpenMode.ForWrite, false, true) is Entity ent && !ent.IsErased)
                {
                    ent.Erase();
                    erasedCount++;
                }
            }
        }
        cleanupTrx.Commit();

        using var countTrx = db.TransactionManager.StartTransaction();
        var msForCount = (BlockTableRecord)countTrx.GetObject(msId, OpenMode.ForRead);
        var countAfter = msForCount.Cast<ObjectId>().Count();
        countTrx.Commit();

        log.Debug(
            "[AUX-VP] Processing complete: entitiesToEraseCount={ToEraseCount}, ModelSpace total after={TotalAfter}",
            erasedCount, countAfter);

        return allClonedIds;
    }

    private static ObjectIdCollection ProjectAuxViewport(
        Database db,
        ObjectId msId,
        ViewportInfo mainOriginal,
        ViewportInfo aux,
        IReadOnlyList<ViewportTransformer.ModelEntitySnapshot> modelEntities,
        Logger log,
        MergeDiagnosticContext? diagnosticContext)
    {
        // Компонуем Aux→Main матрицу (перенос в пространство главного экрана) БЕЗ scaleMatrix.
        var matrix = ViewportTransformer.BuildMatrix(mainOriginal, aux, log);

        log.Debug(
            "[AUX-VP-MATRIX] Aux#{AuxNumber}: composedMatrix=true",
            aux.Number);

        using var selection =
            ViewportTransformer.SelectModelInside(modelEntities, aux.ModelWindow, mainOriginal.ModelWindow, log);

        MergeDiagnostics.WriteEvent(diagnosticContext, "aux.selected", new Dictionary<string, object?>
        {
            ["auxViewportNumber"] = aux.Number,
            ["cached"] = modelEntities.Count,
            ["selected"] = selection.SelectedIds.Count,
            ["toClone"] = selection.SelectedIds.Count,
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

        if (selection.SelectedIds.Count == 0)
        {
            log.Debug(
                $"Aux#{aux.Number}: candidates={selection.SelectedIds.Count}, nothing to clone");
            return [];
        }

        log.Debug(
            $"Aux#{aux.Number}: candidates={selection.SelectedIds.Count}, toClone={selection.SelectedIds.Count}");

        using var cloneResult =
            ViewportTransformer.DeepCloneAndTransform(db, selection.SelectedIds, msId, msId, matrix, log);

        ObjectIdCollection cloned = new();
        foreach (ObjectId id in cloneResult.ClonedIds)
        {
            cloned.Add(id);
        }
        return cloned;
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
