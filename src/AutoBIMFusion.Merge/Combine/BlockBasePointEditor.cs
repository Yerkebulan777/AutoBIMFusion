using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Helpers;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.Geometry;
using System.Diagnostics;

namespace AutoBIMFusion.Merge.Combine;

/// <summary>
/// Позволяет изменять базовую точку блока.
/// Поддерживает обычные и динамические блоки, сохраняя положение их вставок.
/// </summary>
public static class BlockBasePointEditor
{
    private const double BasePointTolerance = 0.001;

    /// <summary>
    ///     Переносит базовые точки пользовательских блоков в левый нижний угол без изменения вида вставок.
    ///     Игнорирует мелкую геометрию и не трогает слишком маленькие блоки.
    /// </summary>
    public static void NormalizeAllBlocksBasePoints(Database db, double minEntityDiagonal = 25, double minBlockDiagonal = 50)
    {
        ArgumentNullException.ThrowIfNull(db);

        using Transaction trx = db.TransactionManager.StartTransaction();
        if (trx.GetObject(db.BlockTableId, OpenMode.ForRead) is not BlockTable blockTable)
        {
            trx.Commit();
            return;
        }

        foreach (ObjectId blockRecordId in blockTable)
        {
            if (trx.GetObject(blockRecordId, OpenMode.ForRead) is not BlockTableRecord blockDef)
            {
                continue;
            }

            if (ShouldSkipBlockDefinition(blockDef))
            {
                continue;
            }

            Extents3d? blockExtents = GetBlockDefinitionExtents(blockDef, trx, minEntityDiagonal);
            if (!blockExtents.HasValue)
            {
                continue;
            }

            double blockDiagonal = blockExtents.Value.MaxPoint.DistanceTo(blockExtents.Value.MinPoint);
            if (blockDiagonal < minBlockDiagonal)
            {
                continue;
            }

            Point3d bottomLeft = new(blockExtents.Value.MinPoint.X, blockExtents.Value.MinPoint.Y, 0);
            Vector3d offset = Point3d.Origin.GetVectorTo(bottomLeft);
            if (offset.Length < BasePointTolerance)
            {
                continue;
            }

            MoveBlockDefinitionGeometry(blockDef, trx, -offset);
            MoveBlockReferences(blockDef, trx, offset);
        }

        trx.Commit();
    }

    /// <summary>
    ///   Вычисляет матрицу смещения между исходной и временной базовой точкой динамического блока.
    /// </summary>
    public static Vector3d GetFakeOriginalBasePointInDynamicBlockMatrix(ObjectId OriginalBlockObjectId, out Extents3d OriginalBounds, out Extents3d EditedBounds)
    {
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();

        ObjectId insertedBtrId;
        ObjectId insertedCopyBtrId;

        string oldName;
        string newName;

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            BlockReference? OriginalBlockRef = OriginalBlockObjectId.GetEntity() as BlockReference;
            oldName = OriginalBlockRef!.GetBlockReferenceName();
            newName = BlockReferences.GetUniqueBlockName("INTERNAL-" + oldName);
            insertedBtrId = BlockReferences.InsertFromName(oldName, new Points(new Point3d(0, 0, 0)));
            BlockReference? insertedBlockRef = insertedBtrId.GetEntity() as BlockReference;
            trx.Commit();
            OriginalBounds = insertedBlockRef!.GeometricExtents;
        }

        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            insertedCopyBtrId = BlockReferences.RenameBlockAndInsert(insertedBtrId, newName);
            if (insertedCopyBtrId == ObjectId.Null)
            {
                Generic.WriteMessage("Echec lors de l'opération");
                trx.Abort();
                EditedBounds = OriginalBounds;
                return Vector3d.ZAxis;
            }

            Generic.Command("_-BEDIT", newName);
            SelectionFilter filter = new(new[] { new TypedValue((int)DxfCode.Start, "BASEPOINTPARAMETERENTITY") });
            PromptSelectionResult selRes = ed.SelectAll(filter);
            if (selRes.Status == PromptStatus.OK)
            {
                foreach (ObjectId objectId in selRes.Value.GetObjectIds())
                {
                    _ = objectId.GetDBObject();
                    objectId.EraseObject();
                    Debug.WriteLine("Erase BASEPOINTPARAMETERENTITY");
                }
            }

            trx.Commit();
        }

        using (Transaction tr2 = db.TransactionManager.StartTransaction())
        {
            Generic.Command("_BCLOSE", "_Save");
            Generic.Command("_RESETBLOCK", insertedCopyBtrId, "");
            EditedBounds = insertedCopyBtrId.GetEntity().GeometricExtents;
            // Очистка временных объектов.
            insertedBtrId.EraseObject();
            insertedCopyBtrId.EraseObject();
            tr2.Commit();
        }

        BlockReferences.Purge(newName);
        Vector3d Matrix = OriginalBounds.TopLeft() - EditedBounds.TopLeft();
        return Matrix;
    }

    private static bool ShouldSkipBlockDefinition(BlockTableRecord blockDef)
    {
        return blockDef.IsLayout
            || blockDef.IsAnonymous
            || blockDef.IsDynamicBlock
            || blockDef.IsFromExternalReference
            || blockDef.IsFromOverlayReference
            || blockDef.Name.StartsWith('*');
    }

    private static Extents3d? GetBlockDefinitionExtents(BlockTableRecord blockDef, Transaction trx, double minEntityDiagonal)
    {
        Extents3d? blockExtents = null;

        foreach (ObjectId entityId in blockDef)
        {
            if (trx.GetObject(entityId, OpenMode.ForRead) is not Entity entity)
            {
                continue;
            }

            Extents3d? entityExtents = ExtentsUtils.TryGetExtents(entity);
            if (!entityExtents.HasValue)
            {
                continue;
            }

            double entityDiagonal = entityExtents.Value.MaxPoint.DistanceTo(entityExtents.Value.MinPoint);
            if (entityDiagonal < minEntityDiagonal)
            {
                continue;
            }

            blockExtents = blockExtents.HasValue
                ? ExtentsUtils.Union(blockExtents.Value, entityExtents.Value)
                : entityExtents.Value;
        }

        return blockExtents;
    }

    private static void MoveBlockDefinitionGeometry(BlockTableRecord blockDef, Transaction trx, Vector3d displacement)
    {
        Matrix3d matrix = Matrix3d.Displacement(displacement);

        foreach (ObjectId entityId in blockDef)
        {
            if (trx.GetObject(entityId, OpenMode.ForWrite) is Entity entity)
            {
                entity.TransformBy(matrix);
            }
        }
    }

    private static void MoveBlockReferences(BlockTableRecord blockDef, Transaction trx, Vector3d offset)
    {
        ObjectIdCollection blockReferenceIds = blockDef.GetBlockReferenceIds(true, true);
        foreach (ObjectId blockReferenceId in blockReferenceIds)
        {
            if (trx.GetObject(blockReferenceId, OpenMode.ForWrite) is not BlockReference blockReference)
            {
                continue;
            }

            Vector3d compensation = offset.TransformBy(GetMatrixWithoutTranslation(blockReference.BlockTransform));
            blockReference.TransformBy(Matrix3d.Displacement(compensation));
            blockReference.RecordGraphicsModified(true);
        }
    }

    private static Matrix3d GetMatrixWithoutTranslation(Matrix3d matrix)
    {
        CoordinateSystem3d coordinateSystem = matrix.CoordinateSystem3d;

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
