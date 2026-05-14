using Autodesk.AutoCAD.ApplicationServices;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public static class DWGDataStorage
{
    public static void SaveTextToDrawing(Database db, string key, string content)
    {
        using DocumentLock docLock = Generic.GetDocument().LockDocument();

        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

        string myDictName = Generic.GetExtensionDLLName();

        DBDictionary myDict;
        if (!nod.Contains(myDictName))
        {
            nod.UpgradeOpen();
            myDict = new DBDictionary();
            _ = nod.SetAt(myDictName, myDict);
            trx.AddNewlyCreatedDBObject(myDict, true);
        }
        else
        {
            myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
        }

        Xrecord record = new()
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, content))
        };

        if (myDict.Contains(key))
        {
            myDict.UpgradeOpen();
            _ = myDict.Remove(key);
        }

        _ = myDict.SetAt(key, record);
        trx.AddNewlyCreatedDBObject(record, true);
        trx.Commit();
    }

    public static string LoadTextFromDrawing(Database db, string key)
    {
        using DocumentLock docLock = Generic.GetDocument().LockDocument();
        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        string myDictName = Generic.GetExtensionDLLName();
        if (!nod.Contains(myDictName))
        {
            return null;
        }

        DBDictionary myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForRead);
        if (!myDict.Contains(key))
        {
            return null;
        }

        Xrecord record = (Xrecord)trx.GetObject(myDict.GetAt(key), OpenMode.ForRead);
        TypedValue[] values = record.Data.AsArray();
        return values.Length > 0 ? values[0].Value.ToString() : null;
    }

    public static void DeleteKey(Database db, string key)
    {
        using Transaction trx = db.TransactionManager.StartTransaction();
        DBDictionary nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
        string myDictName = Generic.GetExtensionDLLName();
        if (!nod.Contains(myDictName))
        {
            return;
        }

        DBDictionary myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
        if (myDict.Contains(key))
        {
            _ = myDict.Remove(key);
        }

        trx.Commit();
    }
}
