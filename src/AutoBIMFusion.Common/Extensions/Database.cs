using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace SioForgeCAD.Commun.Extensions;

public static class DatabaseExtensions
{
    public static void OpenAsNewTab(this Database db)
    {
        var docCol = Application.DocumentManager;
        var FilName = Path.Combine(Path.GetTempPath(), $"Memory_{DateTime.Now.Ticks}.dwg");
        db.SaveAs(FilName, DwgVersion.Current);
        var newDoc = docCol.Open(FilName, false);
        docCol.MdiActiveDocument = newDoc;
    }

    public static Dictionary<ObjectId, string> GetAllObjects(this Database db)
    {
        var dict = new Dictionary<ObjectId, string>();
        for (long i = 0; i < db.Handseed.Value; i++)
            if (db.TryGetObjectId(new Handle(i), out var id))
                dict.Add(id, id.ObjectClass.Name);

        return dict;
    }

    public static Dictionary<ObjectId, string> GetAllEntities(this Database db)
    {
        var dict = new Dictionary<ObjectId, string>();
        using (var tr = db.TransactionManager.StartOpenCloseTransaction())
        {
            var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (var btrId in bt)
            {
                var btr = (BlockTableRecord)tr.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout)
                    foreach (var id in btr)
                        dict.Add(id, id.ObjectClass.Name);
            }

            tr.Commit();
        }

        return dict;
    }

    public static ObjectId EntLast(this Database db, Type type = null)
    {
        // Autodesk.AutoCAD.Internal.Utils.EntLast();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            var btr = Generic.GetCurrentSpaceBlockTableRecord(tr);
            var RXClassType = type == null ? null : RXObject.GetClass(type);
            var EntLastObjectId = ObjectId.Null;
            foreach (var objId in btr)
                if (RXClassType == null || objId.ObjectClass == RXClassType)
                    EntLastObjectId = objId;

            tr.Commit();
            return EntLastObjectId;
        }
    }

    public static void SetAnnotativeScale(this Database db, string Name, double PaperUnits, double DrawingUnits)
    {
        var ed = Generic.GetEditor();
        if (db.Cannoscale.Name != Name)
            using (var tr = db.TransactionManager.StartTransaction())
            {
                var ocm = db.ObjectContextManager;
                var occ = ocm.GetContextCollection("ACDB_ANNOTATIONSCALES");

                if (occ != null)
                {
                    AnnotationScale scale = null;
                    foreach (var obj in occ)
                        if (obj is AnnotationScale annoScale && annoScale.Name == Name)
                        {
                            scale = annoScale;
                            break;
                        }

                    if (scale == null)
                    {
                        scale = new AnnotationScale
                        {
                            Name = Name,
                            PaperUnits = PaperUnits,
                            DrawingUnits = DrawingUnits
                        };
                        occ.AddContext(scale);
                    }

                    db.Cannoscale = scale;
                    Generic.WriteMessage($"Échelle annotative définie sur {Name}.");
                }
                else
                {
                    Generic.WriteMessage("Impossible d'accéder aux échelles annotatives.");
                }

                ed.Regen();
                tr.Commit();
            }
    }


    public static ObjectId GetObjectIdFromAppDictionary(this Database db, Transaction tr, string appDictName,
        string keyName)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

        if (!nod.Contains(appDictName))
            return ObjectId.Null;

        var appDict = (DBDictionary)tr.GetObject(nod.GetAt(appDictName), OpenMode.ForRead);

        if (!appDict.Contains(keyName))
            return ObjectId.Null;

        var xrec = (Xrecord)tr.GetObject(appDict.GetAt(keyName), OpenMode.ForRead);
        var data = xrec.Data.AsArray();

        if (data.Length == 0 || !(data[0].Value is ObjectId))
            return ObjectId.Null;

        return (ObjectId)data[0].Value;
    }


    public static void StoreObjectIdInAppDictionary(this Database db, Transaction tr, string appDictName,
        string keyName, ObjectId objectId)
    {
        var nod = (DBDictionary)tr.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

        DBDictionary appDict;
        if (!nod.Contains(appDictName))
        {
            nod.UpgradeOpen();
            appDict = new DBDictionary();
            nod.SetAt(appDictName, appDict);
            tr.AddNewlyCreatedDBObject(appDict, true);
        }
        else
        {
            appDict = (DBDictionary)tr.GetObject(nod.GetAt(appDictName), OpenMode.ForRead);
        }

        if (appDict.Contains(keyName)) return;

        appDict.UpgradeOpen();
        var xrec = new Xrecord
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.SoftPointerId, objectId))
        };
        appDict.SetAt(keyName, xrec);
        tr.AddNewlyCreatedDBObject(xrec, true);
    }


    public static DwgVersion GetDwgVersion(this Database db)
    {
        var LastSaved = db.LastSavedAsVersion;
        if (LastSaved == DwgVersion.MC0To0) //Not saved
            return DwgVersion.Current;

        return LastSaved;
    }

    public static long GetSize(this Database db, DwgVersion version)
    {
        var tempFileName = $"SioForgeCAD_SizeCheck_{DateTime.Now:yyMMdd_HHmmss}_{Guid.NewGuid()}.dwg";
        var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
        long sizeBytes = 0;

        try
        {
            db.SaveAs(tempFilePath, version);

            var fi = new FileInfo(tempFilePath);
            sizeBytes = fi.Length;
        }
        catch (Exception ex)
        {
            Debug.WriteLine("Erreur GetSize: " + ex.Message);
        }
        finally
        {
            try
            {
                if (File.Exists(tempFilePath)) File.Delete(tempFilePath);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Impossible de supprimer le temp : " + ex.Message);
            }
        }

        return sizeBytes;
    }
}
