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

        foreach (var blockRecordId in blockTable)
        {
            if (trx.GetObject(blockRecordId, OpenMode.ForRead) is not BlockTableRecord blockDef) continue;

            if (ShouldSkipBlockDefinition(blockDef)) continue;

            var blockExtents = GetBlockDefinitionExtents(blockDef, trx, minEntityDiagonal);
            if (!blockExtents.HasValue) continue;

            var blockDiagonal = blockExtents.Value.MaxPoint.DistanceTo(blockExtents.Value.MinPoint);
            if (blockDiagonal < minBlockDiagonal) continue;

            Point3d bottomLeft = new(blockExtents.Value.MinPoint.X, blockExtents.Value.MinPoint.Y, 0);
            var offset = Point3d.Origin.GetVectorTo(bottomLeft);
            if (offset.Length < BasePointTolerance) continue;

            MoveBlockDefinitionGeometry(blockDef, trx, -offset);
            MoveBlockReferences(blockDef, trx, offset);
        }

        trx.Commit();
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
        var blockReferenceIds = blockDef.GetBlockReferenceIds(true, true);
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
}
