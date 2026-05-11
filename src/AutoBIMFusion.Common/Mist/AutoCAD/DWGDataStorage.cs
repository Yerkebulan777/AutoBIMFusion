namespace SioForgeCAD.Commun.Mist;

public static class DWGDataStorage
{
    public static void SaveTextToDrawing(Database db, string key, string content)
    {
        using (var docLock = Generic.GetDocument().LockDocument())

        using (var tr = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

            var myDictName = Generic.GetExtensionDLLName();

            DBDictionary myDict;
            if (!nod.Contains(myDictName))
            {
                nod.UpgradeOpen();
                myDict = new DBDictionary();
                nod.SetAt(myDictName, myDict);
                tr.AddNewlyCreatedDBObject(myDict, true);
            }
            else
            {
                myDict = (DBDictionary)tr.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
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
            tr.AddNewlyCreatedDBObject(record, true);
            tr.Commit();
        }
    }

    public static string LoadTextFromDrawing(Database db, string key)
    {
        using (var docLock = Generic.GetDocument().LockDocument())
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var myDictName = Generic.GetExtensionDLLName();
            if (!nod.Contains(myDictName)) return null;

            var myDict = (DBDictionary)tr.GetObject(nod.GetAt(myDictName), OpenMode.ForRead);
            if (!myDict.Contains(key)) return null;

            var record = (Xrecord)tr.GetObject(myDict.GetAt(key), OpenMode.ForRead);
            var values = record.Data.AsArray();
            return values.Length > 0 ? values[0].Value.ToString() : null;
        }
    }

    public static void DeleteKey(Database db, string key)
    {
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);
            var myDictName = Generic.GetExtensionDLLName();
            if (!nod.Contains(myDictName)) return;

            var myDict = (DBDictionary)tr.GetObject(nod.GetAt(myDictName), OpenMode.ForWrite);
            if (myDict.Contains(key)) myDict.Remove(key);

            tr.Commit();
        }
    }
}
