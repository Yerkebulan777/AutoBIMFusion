using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.AutoCAD;
using AutoBIMFusion.Common.Mist.Geometry;
using System.Diagnostics;

namespace AutoBIMFusion.Common.Functions;

/// <summary>
/// Позволяет изменять базовую точку блока.
/// Поддерживает обычные и динамические блоки, сохраняя положение их вставок.
/// </summary>
public static class BlockBasePointEditor
{
    /// <summary>
    /// Запрашивает выбор блока и переносит его базовую точку в указанное пользователем место.
    /// </summary>
    public static void MoveBasePoint()
    {
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();
        TypedValue[] filterList = new[] { new TypedValue((int)DxfCode.Start, "INSERT") };
        PromptSelectionOptions selectionOptions = new()
        {
            SingleOnly = true,
            SinglePickInSpace = true,
            RejectObjectsOnLockedLayers = true,
            MessageForAdding = "Selectionnez un bloc",
        };

        PromptSelectionResult promptResult;
        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            while (true)
            {
                promptResult = ed.GetSelection(selectionOptions, new SelectionFilter(filterList));

                if (promptResult.Status == PromptStatus.Cancel)
                {
                    trx.Commit();
                    return;
                }

                if (promptResult.Status == PromptStatus.OK)
                {
                    if (promptResult.Value.Count > 0)
                    {
                        break;
                    }
                }
            }

            trx.Commit();
        }

        ObjectIdCollection iter;
        BlockReference blockRef;
        BlockTableRecord blockDef;
        PromptPointResult pointResult;
        bool IsDynamicBlock = false;
        ObjectId blockRefId = promptResult.Value.GetObjectIds().First();
        using (Transaction trx = db.TransactionManager.StartTransaction())
        {
            blockRefId.RegisterHighlight();
            PromptPointOptions pointOptions = new("Veuillez sélectionner son nouveau point de base : ");
            pointResult = ed.GetPoint(pointOptions);
            blockRefId.RegisterUnhighlight();
            if (pointResult.Status != PromptStatus.OK)
            {
                return;
            }

            if (blockRefId.GetDBObject() is not BlockReference blockRefOut)
            {
                return;
            }

            blockDef = blockRefOut.BlockTableRecord.GetDBObject(OpenMode.ForWrite) as BlockTableRecord;
            IsDynamicBlock = blockRefOut.IsDynamicBlock || blockDef.IsDynamicBlock;
            blockRef = blockRefOut;
            trx.Commit();
        }

        Point3d selectedPoint = Points.GetFromPromptPointResult(pointResult).SCG;
        Vector3d FixPosition = selectedPoint - blockRef.Position;
        Point3d BlockReferenceTransformedPoint = selectedPoint.TranformToBlockReferenceTransformation(blockRef);

        if (IsDynamicBlock)
        {
            iter = ChangeBasePointDynamicBlock(blockRefId, BlockReferenceTransformedPoint, out _);
            // Показательные отладочные вызовы оставлены закомментированными намеренно.

            // Перемещаем вставки блока, чтобы сохранить их исходное положение.
            using Transaction tr2 = db.TransactionManager.StartTransaction();
            foreach (ObjectId entId in iter)
            {
                if (entId.GetDBObject(OpenMode.ForWrite) is BlockReference otherBlockRef)
                {
                    // Инвертируем вектор относительно текущего преобразования блока и применяем его к вставке.
                    Vector3d TransformedFixPositionV2 = FixPosition.TransformBy(blockRef.BlockTransform.Inverse())
                        .TransformBy(otherBlockRef.BlockTransform);
                    otherBlockRef.Position =
                        otherBlockRef.Position.TransformBy(Matrix3d.Displacement(TransformedFixPositionV2));
                    otherBlockRef.RecordGraphicsModified(true);
                }
            }

            tr2.Commit();
        }
        else
        {
            Matrix3d rotationMatrix = Matrix3d.Rotation(PI, Vector3d.ZAxis, Point3d.Origin);

            iter = ChangeBasePointStaticBlock(blockRefId, BlockReferenceTransformedPoint.TransformBy(rotationMatrix));
            // Перемещаем вставки блока, чтобы сохранить их исходное положение.
            using Transaction tr2 = db.TransactionManager.StartTransaction();
            foreach (ObjectId entId in iter)
            {
                if (entId.GetDBObject(OpenMode.ForWrite) is BlockReference otherBlockRef)
                {
                    Vector3d TransformedFixPositionV2 = FixPosition.TransformBy(blockRef.BlockTransform.Inverse())
                        .TransformBy(otherBlockRef.BlockTransform);
                    otherBlockRef.TransformBy(Matrix3d.Displacement(TransformedFixPositionV2));
                    otherBlockRef.RecordGraphicsModified(true);
                }
            }

            tr2.Commit();
        }
    }

    /// <summary>
    ///     Переносит базовую точку динамического блока через временное редактирование определения.
    /// </summary>
    private static ObjectIdCollection ChangeBasePointDynamicBlock(ObjectId blockRefObjId,
        Point3d BlockReferenceTransformedPoint, out Point3d OriginalBlocBasePointInModelSpace)
    {
        OriginalBlocBasePointInModelSpace = new Point3d(0, 0, 0);
        Editor ed = Generic.GetEditor();
        Database db = Generic.GetDatabase();

        // Получаем матрицу между фиктивной и исходной точкой базирования.
        Vector3d FakeOriginalBasePointMatrix = GetFakeOriginalBasePointInDynamicBlockMatrix(blockRefObjId, out Extents3d OriginalBounds, out Extents3d EditedBounds);

        if (OriginalBounds.Size() != EditedBounds.Size())
        {
            Application.ShowAlertDialog("Impossible de changer le point de base de ce bloc dynamique.");
            return [];
        }

        ObjectIdCollection iter;
        using Transaction trx = db.TransactionManager.StartTransaction();
        if (blockRefObjId.GetDBObject(OpenMode.ForWrite) is not BlockReference blockRef)
        {
            return [];
        }

        OriginalBlocBasePointInModelSpace =
            blockRef.Position.TransformBy(Matrix3d.Displacement(FakeOriginalBasePointMatrix));
        Point3d FakeBlocBasePointInBlocSpace =
            new Point3d(0, 0, 0).TransformBy(Matrix3d.Displacement(FakeOriginalBasePointMatrix * -1));

        string BlockName = blockRef.GetBlockReferenceName();
        // Собираем все ссылки динамического блока, чтобы после BEDIT обновить их без задержек.
        iter = BlockReferences.GetDynamicBlockReferences(BlockName);
        iter.Join((blockRef.BlockTableRecord.GetDBObject() as BlockTableRecord).GetBlockReferenceIds(true, false));
        // Входим в режим редактирования блока.
        Generic.Command("_-BEDIT", BlockName);

        // У динамического блока может быть только одна базовая точка, поэтому удаляем старую.
        SelectionFilter filter = new(new[] { new TypedValue((int)DxfCode.Start, "BASEPOINTPARAMETERENTITY") });
        PromptSelectionResult selRes = ed.SelectAll(filter);
        if (selRes.Status == PromptStatus.OK)
        {
            foreach (ObjectId objectId in selRes.Value.GetObjectIds())
            {
                objectId.EraseObject();
            }
        }

        Point3d ReelBlockReferenceTransformedPoint = BlockReferenceTransformedPoint.TransformBy(Matrix3d.Displacement(FakeBlocBasePointInBlocSpace - new Point3d(0, 0, 0))).Flatten();

        // Создаём временную точку, чтобы избежать некорректного позиционирования параметра.
        ObjectId PtObjectId;
        using (DBPoint Pt = new(ReelBlockReferenceTransformedPoint))
        {
            PtObjectId = Pt.AddToDrawingCurrentTransaction();
        }

        // Завершаем удаление старой базовой точки.
        trx.Commit();
        // Добавляем новую базовую точку блока.
        Generic.Command("_BPARAMETER", "_Base", ReelBlockReferenceTransformedPoint);
        PtObjectId.EraseObject();
        Generic.Command("_BCLOSE", "_S");
        return iter;
    }

    /// <summary>
    ///     Переносит базовую точку обычного блока через смещение геометрии определения.
    /// </summary>
    private static ObjectIdCollection ChangeBasePointStaticBlock(ObjectId blockRefObjId, Point3d BlockReferenceTransformedPoint)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        if (blockRefObjId.GetDBObject(OpenMode.ForWrite) is not BlockReference blockRef)
        {
            return [];
        }

        BlockTableRecord? blockDef = trx.GetObject(blockRef.BlockTableRecord, OpenMode.ForWrite) as BlockTableRecord;

        Matrix3d DisplacementVector =
            Matrix3d.Displacement(
                new Point3d(0, 0, BlockReferenceTransformedPoint.Z).GetVectorTo(BlockReferenceTransformedPoint
                    .Flatten()));

        foreach (ObjectId entId in blockDef)
        {
            Entity? entity = trx.GetObject(entId, OpenMode.ForWrite) as Entity;
            entity?.TransformBy(DisplacementVector);
        }

        blockRef.DowngradeOpen();

        trx.Commit();
        return blockDef.GetBlockReferenceIds(true, false);
    }

    /// <summary>
    ///     Вычисляет матрицу смещения между исходной и временной базовой точкой динамического блока.
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
            oldName = OriginalBlockRef.GetBlockReferenceName();
            newName = BlockReferences.GetUniqueBlockName("SIOFORGE_INTERNAL_" + oldName);
            insertedBtrId = BlockReferences.InsertFromName(oldName, new Points(new Point3d(0, 0, 0)));
            BlockReference? insertedBlockRef = insertedBtrId.GetEntity() as BlockReference;
            trx.Commit();
            OriginalBounds = insertedBlockRef.GeometricExtents;
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
}
