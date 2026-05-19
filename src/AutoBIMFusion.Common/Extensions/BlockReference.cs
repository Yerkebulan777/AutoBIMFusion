using AutoBIMFusion.Common.Drawing;
using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.ApplicationServices;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoBIMFusion.Common.Extensions;

internal static class BlockReferenceExtensions
{
    public static bool IsXref(this BlockReference blockRef)
    {
        return (blockRef?.BlockTableRecord.GetDBObject() as BlockTableRecord)?.IsFromExternalReference ?? false;
    }

    public static bool IsLayoutOrModel(this BlockReference br)
    {
        BlockTableRecord ownerBtr = (BlockTableRecord)br.OwnerId.GetDBObject();
        if (ownerBtr.IsLayout || ownerBtr.Name == BlockTableRecord.ModelSpace)
        {
            return true;
        }

        BlockTableRecord referencedBtr = (BlockTableRecord)br.BlockTableRecord.GetDBObject();
        return referencedBtr.IsLayout;
    }


    public static Handle GetDynamicBlockHandleFromAnonymousBlock(this BlockTableRecord btr)
    {
        if (!btr.IsAnonymous)
        {
            return ObjectId.Null.Handle;
        }

        ResultBuffer rb = btr.GetXDataForApplication("AcDbBlockRepBTag");
        if (rb == null)
        {
            return ObjectId.Null.Handle;
        }

        foreach (TypedValue tv in rb)
        {
            if (tv.TypeCode == 1005 && tv.Value is string strValue)
            {
                var nHandle = Convert.ToInt64(strValue, 16);
                return new Handle(nHandle);
            }
        }

        return ObjectId.Null.Handle;
    }

    public static BlockTableRecord GetBlocDefinition(this Database db, string BlocName)
    {
        BlockTable? bt = db.BlockTableId.GetDBObject() as BlockTable;
        return !bt.Has(BlocName)
            ? throw new Exception($"Le bloc {BlocName} n'existe pas dans le dessin")
            : bt[BlocName].GetObject(OpenMode.ForRead) as BlockTableRecord;
    }

    public static BlockTableRecord GetBlocDefinition(this BlockReference blkRef, OpenMode OpenMode = OpenMode.ForRead)
    {
        return blkRef is not null
            ? (BlockTableRecord)blkRef.GetBlocDefinitionObjectId().GetDBObject(OpenMode)
            : null;
    }

    public static ObjectId GetBlocDefinitionObjectId(this BlockReference blkRef)
    {
        return blkRef is not null
            ? blkRef.IsDynamicBlock ? blkRef.DynamicBlockTableRecord : blkRef.BlockTableRecord
            : ObjectId.Null;
    }

    public static string GetBlockReferenceName(this BlockReference blockRef)
    {
        if (blockRef?.IsDynamicBlock == true)
        {
            // Если это динамический блок, получаем настоящее имя из DynamicBlockTableRecord
            using BlockTableRecord? btr = blockRef.DynamicBlockTableRecord.GetDBObject() as BlockTableRecord;
            return btr.Name;
        }

        return blockRef?.Name;
    }

    public static string GetDescription(this BlockReference blkRef)
    {
        BlockTableRecord? blockDef = blkRef.BlockTableRecord.GetDBObject() as BlockTableRecord;
        return blockDef.Comments;
    }

    public static List<DynamicBlockReferenceProperty> GetDynamicProperties(this BlockReference blockReference)
    {
        List<DynamicBlockReferenceProperty> Values = [];
        DynamicBlockReferencePropertyCollection propertyCollection = blockReference.DynamicBlockReferencePropertyCollection;

        if (propertyCollection != null)
        {
            foreach (DynamicBlockReferenceProperty prop in propertyCollection)
            {
                Values.Add(prop);
            }
        }

        return Values;
    }

    public static void SetDynamicBlockReferenceProperty(this BlockReference blockReference, string propertyName,
        object value)
    {
        foreach (DynamicBlockReferenceProperty prop in blockReference.GetDynamicProperties())
        {
            if (!prop.ReadOnly && prop.PropertyName.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                prop.Value = value;
                return;
            }
        }
    }

    public static IEnumerable<KeyValuePair<string, AttributeReference>> GetAttributesByTag(this BlockReference source)
    {
        foreach (AttributeReference att in source.AttributeCollection.GetObjects())
        {
            yield return new KeyValuePair<string, AttributeReference>(att.Tag, att);
        }
    }

    /// <summary>
    ///     Получает все значения атрибутов по тегу.
    /// </summary>
    /// <param name="source">Экземпляр, к которому применяется метод.</param>
    /// <returns>Коллекция пар Тег/Значение.</returns>
    public static Dictionary<string, string> GetAttributesValues(this BlockReference source)
    {
        return source.GetAttributesByTag().ToDictionary(p => p.Key, p => p.Value.TextString);
    }

    /// <summary>
    ///     Устанавливает значение атрибута.
    /// </summary>
    /// <param name="target">Экземпляр, к которому применяется метод.</param>
    /// <param name="tag">Тег атрибута.</param>
    /// <param name="value">Новое значение.</param>
    /// <returns>Значение, если атрибут найден, иначе null.</returns>
    public static string SetAttributeValue(this BlockReference target, string tag, string value)
    {
        foreach (AttributeReference attRef in target.AttributeCollection.GetObjects())
        {
            if (attRef.Tag == tag)
            {
                attRef.TextString = value;
                return value;
            }
        }

        return null;
    }

    /// <summary>
    ///     Устанавливает значения атрибутов.
    /// </summary>
    /// <param name="target">Экземпляр, к которому применяется метод.</param>
    /// <param name="attribs">Коллекция пар Тег/Значение.</param>
    public static void SetAttributeValues(this BlockReference target, Dictionary<string, string> attribs)
    {
        Transaction trx = Generic.GetDatabase().TransactionManager.TopTransaction;
        foreach (AttributeReference attRef in target.AttributeCollection.GetObjects())
        {
            if (attribs.TryGetValue(attRef.Tag, out var value))
            {
                _ = trx.GetObject(attRef.ObjectId, OpenMode.ForWrite);
                attRef.TextString = value;
            }
        }
    }

    public static Point3d ProjectXrefPointToCurrentSpace(this Point3d pointInXref, ObjectId xrefId)
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;

        using (Transaction transaction = db.TransactionManager.StartTransaction())
        {
            BlockReference? xrefBlockReference = transaction.GetObject(xrefId, OpenMode.ForRead) as BlockReference;

            if (xrefBlockReference != null)
            {
                Matrix3d xrefTransform = xrefBlockReference.BlockTransform;
                Point3d worldPoint = pointInXref.TransformBy(xrefTransform);
                transaction.Commit();

                return worldPoint;
            }
        }

        return Point3d.Origin;
    }

    public static bool IsThereABlockReference(this Point3d position, string blockName, string attributeValue,
        out BlockReference blockReference)
    {
        Document doc = AcadApp.DocumentManager.MdiActiveDocument;
        Database db = doc.Database;

        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTable? bt = trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        BlockTableRecord? modelSpace = trx.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;

        foreach (ObjectId objId in modelSpace)
        {
            if (objId.ObjectClass.DxfName == "INSERT")
            {
                blockReference = trx.GetObject(objId, OpenMode.ForRead) as BlockReference;

                if (blockReference != null && blockReference.Name == blockName &&
                    blockReference.Position.IsEqualTo(position, Tolerance.Global))
                {
                    // Проверка значений атрибутов
                    foreach (ObjectId attId in blockReference.AttributeCollection)
                    {
                        DBObject obj = trx.GetObject(attId, OpenMode.ForRead);
                        if (obj is AttributeReference attributeReference)
                        {
                            if (attributeReference.TextString == attributeValue)
                            {
                                // Блок с такой же позицией и значениями атрибутов существует
                                trx.Commit();
                                return true;
                            }
                        }
                    }
                }
            }
        }

        // Блок не существует в той же позиции с теми же значениями атрибутов
        trx.Commit();
        blockReference = null;
        return false;
    }


    public static ObjectIdCollection GetAllBlkDefinition(this BlockReference BlockRef, bool IncludeParents = false)
    {
        BlockTableRecord BlkDef = BlockRef.GetBlocDefinition();
        ObjectIdCollection DynamicBlkRefs = BlockReferences.GetDynamicBlockReferences(BlockRef.GetBlockReferenceName());
        ObjectIdCollection ClassicBlkRefs = BlkDef.GetBlockReferenceIds(!IncludeParents, true);
        ObjectIdCollection AllBlkRefs = [];
        AllBlkRefs.Join(DynamicBlkRefs);
        AllBlkRefs.Join(ClassicBlkRefs);
        return AllBlkRefs;
    }


    public static void RegenAllBlkDefinition(this BlockReference BlockRef)
    {
        Database db = Generic.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord BlkDef = BlockRef.GetBlocDefinition();

        foreach (ObjectId entId in BlockRef.GetAllBlkDefinition(true))
        {
            if (entId.GetDBObject(OpenMode.ForWrite) is BlockReference otherBlockRef)
            {
                otherBlockRef.RecordGraphicsModified(true);
            }
        }

        BlkDef.UpdateAnonymousBlocks();
        trx.Commit();
    }
}
