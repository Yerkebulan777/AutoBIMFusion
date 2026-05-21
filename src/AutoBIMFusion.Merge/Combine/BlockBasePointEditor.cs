using AutoBIMFusion.Common.Helpers;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
///     Позволяет изменять базовую точку блока.
///     Поддерживает обычные и динамические блоки, сохраняя положение их вставок.
/// </summary>
public static class BlockBasePointEditor
{
    private const double BasePointTolerance = 0.001;

    /// <summary>
    ///     Переносит базовые точки блоков в левый нижний угол без изменения вида вставок.
    ///     Обрабатывает обычные, анонимные и динамические блоки.
    ///     Игнорирует мелкую геометрию и не трогает слишком маленькие блоки.
    /// </summary>
    public static void NormalizeAllBlocksBasePoints(Database db, double minEntityDiagonal = 25,
        double minBlockDiagonal = 50)
    {
        ArgumentNullException.ThrowIfNull(db);

        using var trx = db.TransactionManager.StartTransaction();
        if (trx.GetObject(db.BlockTableId, OpenMode.ForRead) is not BlockTable blockTable)
        {
            trx.Commit();
            return;
        }

        NormalizeBlockBasePoints(trx, blockTable.Cast<ObjectId>(), minEntityDiagonal, minBlockDiagonal);

        trx.Commit();
    }

    internal static void NormalizeBlockBasePoints(Transaction trx, IEnumerable<ObjectId> blockDefinitionIds,
        double minEntityDiagonal = 25, double minBlockDiagonal = 50)
    {
        ArgumentNullException.ThrowIfNull(trx);
        ArgumentNullException.ThrowIfNull(blockDefinitionIds);

        HashSet<ObjectId> normalized = [];

        foreach (ObjectId blockRecordId in OrderNestedDefinitionsFirst(trx, blockDefinitionIds))
        {
            if (!normalized.Add(blockRecordId)) continue;

            if (trx.GetObject(blockRecordId, OpenMode.ForRead, false, true) is not BlockTableRecord blockDef) continue;

            NormalizeBlockDefinition(blockDef, trx, minEntityDiagonal, minBlockDiagonal);
        }
    }

    private static void NormalizeBlockDefinition(BlockTableRecord blockDef, Transaction trx,
        double minEntityDiagonal, double minBlockDiagonal)
    {
        if (ShouldSkipBlockDefinition(blockDef)) return;

        var blockExtents = GetBlockDefinitionExtents(blockDef, trx, minEntityDiagonal);
        if (!blockExtents.HasValue) return;

        var blockDiagonal = blockExtents.Value.MaxPoint.DistanceTo(blockExtents.Value.MinPoint);
        if (blockDiagonal < minBlockDiagonal) return;

        Point3d bottomLeft = new(blockExtents.Value.MinPoint.X, blockExtents.Value.MinPoint.Y, 0);
        var offset = Point3d.Origin.GetVectorTo(bottomLeft);
        if (offset.Length < BasePointTolerance) return;

        MoveBlockDefinitionGeometry(blockDef, trx, -offset);
        MoveBlockReferences(blockDef, trx, offset);
    }

    private static bool ShouldSkipBlockDefinition(BlockTableRecord blockDef)
    {
        return blockDef.IsLayout
               || blockDef.IsFromExternalReference
               || blockDef.IsFromOverlayReference;
    }

    private static Extents3d? GetBlockDefinitionExtents(BlockTableRecord blockDef, Transaction trx,
        double minEntityDiagonal)
    {
        Extents3d? blockExtents = null;

        foreach (var entityId in blockDef)
        {
            if (trx.GetObject(entityId, OpenMode.ForRead) is not Entity entity) continue;

            var entityExtents = ExtentsUtils.TryGetExtents(entity);
            if (!entityExtents.HasValue) continue;

            var entityDiagonal = entityExtents.Value.MaxPoint.DistanceTo(entityExtents.Value.MinPoint);
            if (entityDiagonal < minEntityDiagonal) continue;

            blockExtents = blockExtents.HasValue
                ? ExtentsUtils.Union(blockExtents.Value, entityExtents.Value)
                : entityExtents.Value;
        }

        return blockExtents;
    }

    private static void MoveBlockDefinitionGeometry(BlockTableRecord blockDef, Transaction trx, Vector3d displacement)
    {
        var matrix = Matrix3d.Displacement(displacement);

        foreach (var entityId in blockDef)
            if (trx.GetObject(entityId, OpenMode.ForWrite) is Entity entity)
                entity.TransformBy(matrix);
    }

    private static void MoveBlockReferences(BlockTableRecord blockDef, Transaction trx, Vector3d offset)
    {
        using var blockReferenceIds = blockDef.GetBlockReferenceIds(true, true);
        foreach (ObjectId blockReferenceId in blockReferenceIds)
        {
            if (trx.GetObject(blockReferenceId, OpenMode.ForWrite) is not BlockReference blockReference) continue;

            var compensation = offset.TransformBy(GetMatrixWithoutTranslation(blockReference.BlockTransform));
            blockReference.TransformBy(Matrix3d.Displacement(compensation));
            blockReference.RecordGraphicsModified(true);
        }
    }

    private static Matrix3d GetMatrixWithoutTranslation(Matrix3d matrix)
    {
        var coordinateSystem = matrix.CoordinateSystem3d;

        return Matrix3d.AlignCoordinateSystem(
            Point3d.Origin,
            Vector3d.XAxis,
            Vector3d.YAxis,
            Vector3d.ZAxis,
            Point3d.Origin,
            coordinateSystem.Xaxis,
            coordinateSystem.Yaxis,
            coordinateSystem.Zaxis);
    }

    private static IReadOnlyList<ObjectId> OrderNestedDefinitionsFirst(Transaction trx,
        IEnumerable<ObjectId> blockDefinitionIds)
    {
        HashSet<ObjectId> candidates = blockDefinitionIds
            .Where(id => id.IsValid && !id.IsNull && !id.IsErased)
            .ToHashSet();

        List<ObjectId> result = [];
        HashSet<ObjectId> visited = [];
        HashSet<ObjectId> visiting = [];

        foreach (ObjectId blockDefinitionId in candidates)
        {
            VisitBlockDefinition(trx, blockDefinitionId, candidates, visited, visiting, result);
        }

        return result;
    }

    private static void VisitBlockDefinition(Transaction trx, ObjectId blockDefinitionId, HashSet<ObjectId> candidates,
        HashSet<ObjectId> visited, HashSet<ObjectId> visiting, List<ObjectId> result)
    {
        if (visited.Contains(blockDefinitionId) || !visiting.Add(blockDefinitionId)) return;

        if (trx.GetObject(blockDefinitionId, OpenMode.ForRead, false, true) is BlockTableRecord blockDef)
        {
            foreach (ObjectId entityId in blockDef)
            {
                if (trx.GetObject(entityId, OpenMode.ForRead, false, true) is not BlockReference blockReference)
                {
                    continue;
                }

                ObjectId nestedDefinitionId = blockReference.BlockTableRecord;
                if (candidates.Contains(nestedDefinitionId))
                {
                    VisitBlockDefinition(trx, nestedDefinitionId, candidates, visited, visiting, result);
                }
            }
        }

        _ = visiting.Remove(blockDefinitionId);
        _ = visited.Add(blockDefinitionId);
        result.Add(blockDefinitionId);
    }
}
