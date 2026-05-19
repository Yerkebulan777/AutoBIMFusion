using AutoBIMFusion.Common.Mist;
using AutoBIMFusion.Common.Mist.AutoCAD;
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
        if (objectId.IsNull) return null;

        var db = Generic.GetDatabase();
        return db.TransactionManager.GetObject(objectId, openMode, false, true);
    }

    public static List<ObjectId> GetObjectIds(this IEnumerable<DBObject> dBObjects)
    {
        List<ObjectId> ObjectIds = [];
        foreach (var obj in dBObjects) ObjectIds.Add(obj.ObjectId);

        return ObjectIds;
    }

    public static DBObject GetNoTransactionDBObject(this ObjectId objectId, OpenMode openMode = OpenMode.ForRead)
    {
        var db = Generic.GetDatabase();
        if (db.TransactionManager.NumberOfActiveTransactions == 0)
            using (db.TransactionManager.StartTransaction())
            {
                return objectId.GetDBObject(openMode);
            }

        return objectId.GetDBObject(openMode);
    }

    public static DBObjectCollection Explode(this IEnumerable<ObjectId> ObjectsToExplode, bool EraseOriginal = true)
    {
        DBObjectCollection objs = [];

        // Loop through the selected objects
        foreach (var ObjectToExplode in ObjectsToExplode)
        {
            var ent = ObjectToExplode.GetEntity();
            // Explode the object into our collection
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
        var ObjectIdsCollection = entities.GetObjectIds();
        DBObjectCollection NewDBObjectCollection = [];
        foreach (var ObjectId in ObjectIdsCollection) _ = NewDBObjectCollection.Add(ObjectId.GetDBObject());

        return NewDBObjectCollection;
    }

    public static ObjectIdCollection ToObjectIdCollection(this IEnumerable<ObjectId> objectId)
    {
        ObjectIdCollection NewObjectIdCollection = [];
        foreach (var ObjectId in objectId) _ = NewObjectIdCollection.Add(ObjectId);

        return NewObjectIdCollection;
    }

    public static DBObjectCollection ToDBObjectCollection(this IEnumerable<DBObject> entities)
    {
        DBObjectCollection NewDBObjectCollection = [];
        foreach (var entity in entities) _ = NewDBObjectCollection.Add(entity);

        return NewDBObjectCollection;
    }

    public static List<DBObject> ToList(this DBObjectCollection entities)
    {
        List<DBObject> list = [];
        foreach (var ent in entities) list.Add(ent as DBObject);

        return list;
    }

    public static List<ObjectId> ToList(this ObjectIdCollection collection)
    {
        List<ObjectId> list = [.. collection.Cast<ObjectId>()];

        return list;
    }

    public static void EraseObject(this ObjectId ObjectToErase)
    {
        var doc = Generic.GetDocument();
        using var trx = doc.TransactionManager.StartTransaction();
        if (ObjectToErase.IsErased) return;

        var ent = trx.GetObject(ObjectToErase, OpenMode.ForRead);
        //Can only errase if entity is not on a locked layer
        if (ent is Entity enty && enty.IsEntityOnLockedLayer())
        {
            trx.Commit();
        }
        else
        {
            ent.UpgradeOpen();
            if (!ent.IsErased) ent.Erase(true);

            trx.Commit();
        }
    }

    public static void EraseObjects(this ObjectIdCollection ids, Transaction trx)
    {
        foreach (ObjectId id in ids)
        {
            if (!id.IsValid || id.IsErased) continue;

            if (trx.GetObject(id, OpenMode.ForWrite) is DBObject obj && !obj.IsErased) obj.Erase();
        }
    }

    public static void Join(this ObjectIdCollection A, ObjectIdCollection B)
    {
        foreach (ObjectId ent in B)
            if (!A.Contains(ent))
                _ = A.Add(ent);
    }

    public static void Add(this ObjectIdCollection col, ObjectId[] ids)
    {
        foreach (var id in ids)
            if (!col.Contains(id))
                _ = col.Add(id);
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
        var currentDb = objId.Database;
        if (currentDb == null || objId == ObjectId.Null) return 0;

        using Database emptyDb = new(true, true);
        var emptyDwgByteSize = emptyDb.GetSize(currentDb.GetDwgVersion());

        var modelSpaceId = SymbolUtilityServices.GetBlockModelSpaceId(emptyDb);

        ObjectIdCollection ids = [objId];
        emptyDb.WblockCloneObjects(ids, modelSpaceId, [], DuplicateRecordCloning.Replace, false);

        var finalDwgByteSize = emptyDb.GetSize(currentDb.GetDwgVersion());
        return Max(finalDwgByteSize - emptyDwgByteSize, 0);
    }
}
