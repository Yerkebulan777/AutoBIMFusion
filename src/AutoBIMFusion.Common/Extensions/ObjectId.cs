namespace AutoBIMFusion.Common.Extensions;

public static class ObjectIdExtensions
{
    public static DBObject GetDBObject(this ObjectId objectId, OpenMode openMode = OpenMode.ForRead)
    {
        if (objectId.IsNull)
        {
            return null;
        }

        Database db = HostApplicationServices.WorkingDatabase;
        return db.TransactionManager.GetObject(objectId, openMode, false, true);
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

    public static bool IsValidForOperation(this ObjectId id)
    {
        return !id.IsNull && !id.IsErased;
    }
}
