namespace SioForgeCAD.Commun.Mist;

public static class DWGDataStorage
{
    public static void SaveTextToDrawing(Database db, string key, string content)
    {
        using (var docLock = Generic.GetDocument().LockDocument())

        using (var trx = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            var myDictName = Generic.GetExtensionDLLName();

            DBDictionary myDict;
            if (!nod.Contains(myDictName))
            {
                nod.UpgradeOpen();
                myDict = new DBDictionary();
                nod.SetAt(myDictName, myDict);
                trx.AddNewlyCreatedDBObject(myDict, true);
            }
            else
            {
                myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
            }

            var record = new Xrecord
            {
                Data = new ResultBuffer(new TypedValue((int)DxfCode.Text, content))
            };

            if (myDict.Contains(key))
            {
                myDict.UpgradeOpen();
                myDict.Remove(key);
            }

            myDict.SetAt(key, record);
            trx.AddNewlyCreatedDBObject(record, true);
            trx.Commit();
        }
    }

    public static string LoadTextFromDrawing(Database db, string key)
    {
        using (var docLock = Generic.GetDocument().LockDocument())
        using (var trx = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var myDictName = Generic.GetExtensionDLLName();
            if (!nod.Contains(myDictName)) return null;

            var myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForRead);
            if (!myDict.Contains(key)) return null;

            var record = (Xrecord)trx.GetObject(myDict.GetAt(key), OpenMode.ForRead);
            var values = record.Data.AsArray();
            return values.Length > 0 ? values[0].Value.ToString() : null;
        }
    }

    public static void DeleteKey(Database db, string key)
    {
        using (var trx = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var myDictName = Generic.GetExtensionDLLName();
            if (!nod.Contains(myDictName)) return;

            var myDict = (DBDictionary)trx.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
            if (myDict.Contains(key)) myDict.Remove(key);

            trx.Commit();
        }
    }
}
