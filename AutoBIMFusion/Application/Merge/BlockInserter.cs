using AutoBIMFusion.Application.Merge.Layouts;
using AutoBIMFusion.Infrastructure.Logging;

namespace AutoBIMFusion.Application.Merge;

/// <summary>
/// Вставляет содержимое временных DWG как нативные объекты в Model Space целевого чертежа,
/// располагая их вдоль оси X с заданным зазором. Использует DuplicateRecordCloning.Ignore
/// для предотвращения перезаписи стилей и слоёв.
///
/// ВАЖНО: объекты вставляются как нативные сущности (не в блоке), чтобы сохранить
/// исходный вид и структуру объектов такими же, как в исходном файле.
/// </summary>
internal sealed class BlockInserter(double gapPercent, OperationLogger log)
{
    private double _rightMax;
    private bool _hasPlacedObjects;

    private readonly record struct DimensionSnapshot(
        string Handle,
        string EntityType,
        string LayerName,
        string StyleName,
        double Dimscale);

    /// <summary>
    /// Открывает временный DWG, клонирует все объекты из его Model Space
    /// в целевой чертёж как нативные сущности с учётом смещения.
    /// Возвращает мировые границы вставленных объектов или null при ошибке.
    /// </summary>
    public Extents3d? InsertNativeObjects(Database targetDb, string sourceFilePath, string sourceName, Extents3d sourceBounds)
    {
        Point3d insertPt = CalcInsertionPoint(sourceBounds);
        Matrix3d displacement = Matrix3d.Displacement(new Vector3d(insertPt.X, insertPt.Y, insertPt.Z));

        try
        {
            ObjectIdCollection sourceIds = [];

            using Database sourceDb = new(false, true);

            sourceDb.ReadDwgFile(sourceFilePath, FileOpenMode.OpenForReadAndAllShare, true, string.Empty);

            ObjectId sourceMsId = SymbolUtilityServices.GetBlockModelSpaceId(sourceDb);
            int sourceDimensionCount = 0;

            using (Transaction tr = sourceDb.TransactionManager.StartTransaction())
            {
                BlockTableRecord ms = (BlockTableRecord)tr.GetObject(sourceMsId, OpenMode.ForRead);

                foreach (ObjectId id in ms)
                {
                    if (!id.IsNull && !id.IsErased)
                    {
                        _ = sourceIds.Add(id);

                        if (tr.GetObject(id, OpenMode.ForRead) is Dimension dim)
                        {
                            sourceDimensionCount++;
                            DimensionSnapshot snapshot = CreateDimensionSnapshot(tr, dim);
                            log.Debug(
                                $"MergeDimscale source={sourceName}: SourceHandle={snapshot.Handle}, " +
                                $"Type={snapshot.EntityType}, Layer=\"{snapshot.LayerName}\", " +
                                $"DimStyle=\"{snapshot.StyleName}\", Dimscale={snapshot.Dimscale:F6}");
                        }
                    }
                }

                tr.Commit();
            }

            if (sourceIds.Count == 0)
            {
                log.Warn($"{sourceName}: пустой Model Space");
                return null;
            }

            ObjectId targetMsId = SymbolUtilityServices.GetBlockModelSpaceId(targetDb);
            IdMapping map = [];
            Extents3d? worldBounds = null;
            int clonedCount = 0;
            int targetDimensionCount = 0;

            using (Transaction tr = targetDb.TransactionManager.StartTransaction())
            {
                // Клонируем объекты из временной базы в целевую
                targetDb.WblockCloneObjects(sourceIds, targetMsId, map, DuplicateRecordCloning.Ignore, false);

                foreach (IdPair pair in map)
                {
                    if (!pair.IsCloned || !pair.IsPrimary)
                    {
                        continue;
                    }

                    if (tr.GetObject(pair.Value, OpenMode.ForWrite) is Entity ent)
                    {
                        DimensionSnapshot? dimensionBeforeMove = null;
                        if (ent is Dimension dimBeforeMove)
                        {
                            targetDimensionCount++;
                            dimensionBeforeMove = CreateDimensionSnapshot(tr, dimBeforeMove);
                        }

                        ent.TransformBy(displacement);
                        clonedCount++;

                        if (ent is Dimension dimAfterMove && dimensionBeforeMove.HasValue)
                        {
                            DimensionSnapshot afterMove = CreateDimensionSnapshot(tr, dimAfterMove);
                            log.Debug(
                                $"MergeDimscale target={sourceName}: SourceHandle={pair.Key.Handle}, " +
                                $"TargetHandle={afterMove.Handle}, Type={afterMove.EntityType}, " +
                                $"Layer=\"{afterMove.LayerName}\", DimStyleBeforeMove=\"{dimensionBeforeMove.Value.StyleName}\", " +
                                $"DimStyleAfterMove=\"{afterMove.StyleName}\", DimscaleBeforeMove={dimensionBeforeMove.Value.Dimscale:F6}, " +
                                $"DimscaleAfterMove={afterMove.Dimscale:F6}");
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

                tr.Commit();
            }

            if (clonedCount == 0)
            {
                log.Warn($"{sourceName}: не удалось клонировать объекты");
                return null;
            }

            if (!worldBounds.HasValue)
            {
                worldBounds = ExtentsUtils.Transform(sourceBounds, displacement);
            }

            _rightMax = worldBounds.Value.MaxPoint.X;
            _hasPlacedObjects = true;
            log.Info(
                $"{sourceName}: вставлено {clonedCount} нативных объектов, " +
                $"sourceDimensions={sourceDimensionCount}, targetDimensions={targetDimensionCount}");
            return worldBounds;
        }
        catch (System.Exception ex)
        {
            log.Error(ex, $"Ошибка вставки: {sourceName}");
            return null;
        }
    }

    private Point3d CalcInsertionPoint(Extents3d bounds)
    {
        double width = Math.Max(0, bounds.MaxPoint.X - bounds.MinPoint.X);
        double height = Math.Max(0, bounds.MaxPoint.Y - bounds.MinPoint.Y);
        double maxDimension = Math.Max(width, height);
        double gap = Math.Max(1.0, Math.Round(maxDimension * gapPercent, 0));

        double insertX = _hasPlacedObjects
            ? _rightMax + gap - bounds.MinPoint.X
            : -bounds.MinPoint.X;
        Point3d insertPt = new(insertX, -bounds.MinPoint.Y, 0);

        log.Debug($"Позиция вставки: X={insertPt.X:F2}, Y={insertPt.Y:F2}, gap={gap:F0}");
        return insertPt;
    }

    private static DimensionSnapshot CreateDimensionSnapshot(Transaction tr, Dimension dim)
    {
        return new DimensionSnapshot(
            dim.Handle.ToString(),
            dim.GetType().Name,
            dim.Layer,
            ResolveDimensionStyleName(tr, dim.DimensionStyle),
            dim.Dimscale);
    }

    private static string ResolveDimensionStyleName(Transaction tr, ObjectId dimStyleId)
    {
        if (dimStyleId.IsNull)
        {
            return "<null>";
        }

        try
        {
            return tr.GetObject(dimStyleId, OpenMode.ForRead) is DimStyleTableRecord style
                ? style.Name
                : "<not DimStyleTableRecord>";
        }
        catch
        {
            return "<unavailable>";
        }
    }
}
