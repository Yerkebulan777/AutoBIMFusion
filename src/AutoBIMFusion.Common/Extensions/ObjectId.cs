using AutoBIMFusion.Common.AcadSupport;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Runtime;

namespace AutoBIMFusion.Common.Extensions;

public static class ObjectIdExtensions
{
    public static Entity GetEntity(this ObjectId objectId, OpenMode openMode = OpenMode.ForRead)
    {
        return objectId.GetDBObject(openMode) is Entity ent
            ? ent
            : throw new InvalidCastException("Impossible de convertir l'entité, utilisez GetDBObject");
    }

    public static DBObject GetDBObject(this ObjectId objectId, OpenMode openMode = OpenMode.ForRead)
    {
        if (objectId.IsNull)
        {
            return null;
        }

        Database db = AcadContext.GetDatabase();
        return db.TransactionManager.GetObject(objectId, openMode, false, true);
    }

    public static List<ObjectId> GetObjectIds(this IEnumerable<DBObject> dBObjects)
    {
        List<ObjectId> ObjectIds = [];
        foreach (DBObject obj in dBObjects)
        {
            ObjectIds.Add(obj.ObjectId);
        }

        return ObjectIds;
    }

    public static DBObject GetNoTransactionDBObject(this ObjectId objectId, OpenMode openMode = OpenMode.ForRead)
    {
        Database db = AcadContext.GetDatabase();
        if (db.TransactionManager.NumberOfActiveTransactions == 0)
        {
            using (db.TransactionManager.StartTransaction())
            {
                return objectId.GetDBObject(openMode);
            }
        }

        return objectId.GetDBObject(openMode);
    }

    public static DBObjectCollection Explode(this IEnumerable<ObjectId> ObjectsToExplode, bool EraseOriginal = true)
    {
        DBObjectCollection objs = [];

        // Проходим по выбранным объектам
        foreach (ObjectId ObjectToExplode in ObjectsToExplode)
        {
            Entity ent = ObjectToExplode.GetEntity();
            // Взрываем объект в нашу коллекцию
            ent.Explode(objs);
            if (EraseOriginal)
            {
                ent.UpgradeOpen();
                ent.Erase();
            }
        }

        return objs;
    }

    public static DBObjectCollection ToDBObjectCollection(this IEnumerable<Entity> entities)
    {
        return entities.Cast<DBObject>().ToDBObjectCollection();
    }

    public static DBObjectCollection ToDBObjectCollection(this SelectionSet entities)
    {
        ObjectId[] ObjectIdsCollection = entities.GetObjectIds();
        DBObjectCollection NewDBObjectCollection = [];
        foreach (ObjectId ObjectId in ObjectIdsCollection)
        {
            _ = NewDBObjectCollection.Add(ObjectId.GetDBObject());
        }

        return NewDBObjectCollection;
    }

    public static ObjectIdCollection ToObjectIdCollection(this IEnumerable<ObjectId> objectId)
    {
        ObjectIdCollection NewObjectIdCollection = [];
        foreach (ObjectId ObjectId in objectId)
        {
            _ = NewObjectIdCollection.Add(ObjectId);
        }

        return NewObjectIdCollection;
    }

    public static DBObjectCollection ToDBObjectCollection(this IEnumerable<DBObject> entities)
    {
        DBObjectCollection NewDBObjectCollection = [];
        foreach (DBObject entity in entities)
        {
            _ = NewDBObjectCollection.Add(entity);
        }

        return NewDBObjectCollection;
    }

    public static List<DBObject> ToList(this DBObjectCollection entities)
    {
        List<DBObject> list = [];
        foreach (object? ent in entities)
        {
            list.Add(ent as DBObject);
        }

        return list;
    }

    public static List<ObjectId> ToList(this ObjectIdCollection collection)
    {
        List<ObjectId> list = [.. collection.Cast<ObjectId>()];

        return list;
    }

    public static void EraseObject(this ObjectId ObjectToErase)
    {
        Document doc = AcadContext.GetDocument();
        using Transaction trx = doc.TransactionManager.StartTransaction();
        if (ObjectToErase.IsErased)
        {
            return;
        }

        DBObject ent = trx.GetObject(ObjectToErase, OpenMode.ForRead);
        //Can only errase if entity is not on a locked layer
        if (ent is Entity enty && enty.IsEntityOnLockedLayer())
        {
            trx.Commit();
        }
        else
        {
            ent.UpgradeOpen();
            if (!ent.IsErased)
            {
                ent.Erase(true);
            }

            trx.Commit();
        }
    }

    public static void EraseObjects(this ObjectIdCollection ids, Transaction trx)
    {
        foreach (ObjectId id in ids)
        {
            if (!id.IsValid || id.IsErased)
            {
                continue;
            }

            if (trx.GetObject(id, OpenMode.ForWrite) is DBObject obj && !obj.IsErased)
            {
                obj.Erase();
            }
        }
    }

    public static void Join(this ObjectIdCollection A, ObjectIdCollection B)
    {
        foreach (ObjectId ent in B)
        {
            if (!A.Contains(ent))
            {
                _ = A.Add(ent);
            }
        }
    }

    public static void Add(this ObjectIdCollection col, ObjectId[] ids)
    {
        foreach (ObjectId id in ids)
        {
            if (!col.Contains(id))
            {
                _ = col.Add(id);
            }
        }
    }

    public static Hatch HatchObject(this ObjectId Obj, string Layer)
    {
        ObjectIdCollection acObjIdColl =
        [
            Obj
        ];
        Hatch acHatch = new();

        acHatch.SetHatchPattern(HatchPatternType.PreDefined, "SOLID");
        acHatch.Associative = true;
        acHatch.Layer = Layer;
        acHatch.ColorIndex = 256;
        acHatch.Transparency = new Transparency(TransparencyMethod.ByBlock);
        acHatch.AppendLoop(HatchLoopTypes.Outermost, acObjIdColl);
        acHatch.EvaluateHatch(true);
        return acHatch;
    }

    public static bool IsDerivedFrom(this ObjectId objId, Type type)
    {
        return objId != ObjectId.Null && objId.ObjectClass.IsDerivedFrom(RXObject.GetClass(type));
    }

    public static bool IsValidForOperation(this ObjectId id)
    {
        return !id.IsNull && !id.IsErased;
    }

    public static long GetObjectByteSize(this ObjectId objId)
    {
        Database currentDb = objId.Database;
        if (currentDb == null || objId == ObjectId.Null)
        {
            return 0;
        }

        using Database emptyDb = new(true, true);
        long emptyDwgByteSize = emptyDb.GetSize(currentDb.GetDwgVersion());

        ObjectId modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(emptyDb);

        ObjectIdCollection ids = [objId];
        emptyDb.WblockCloneObjects(ids, modelSpaceId, [], DuplicateRecordCloning.Replace, false);

        long finalDwgByteSize = emptyDb.GetSize(currentDb.GetDwgVersion());
        return Max(finalDwgByteSize - emptyDwgByteSize, 0);
    }
}
