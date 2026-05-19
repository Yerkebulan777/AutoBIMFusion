using System.Diagnostics;
using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Extensions;

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
        Dictionary<ObjectId, string> dict = [];
        for (long i = 0; i < db.Handseed.Value; i++)
            if (db.TryGetObjectId(new Handle(i), out var id))
                dict.Add(id, id.ObjectClass.Name);

        return dict;
    }

    public static Dictionary<ObjectId, string> GetAllEntities(this Database db)
    {
        Dictionary<ObjectId, string> dict = [];
        using (var trx = db.TransactionManager.StartOpenCloseTransaction())
        {
            var bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (var btrId in bt)
            {
                var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout)
                    foreach (var id in btr)
                        dict.Add(id, id.ObjectClass.Name);
            }

            trx.Commit();
        }

        return dict;
    }

    public static ObjectId EntLast(this Database db, Type type = null)
    {
        // Autodesk.AutoCAD.Internal.Utils.EntLast();
        using var trx = db.TransactionManager.StartTransaction();
        var btr = Generic.GetCurrentSpaceBlockTableRecord(trx);
        var RXClassType = type == null ? null : RXObject.GetClass(type);
        var EntLastObjectId = ObjectId.Null;
        foreach (var objId in btr)
            if (RXClassType == null || objId.ObjectClass == RXClassType)
                EntLastObjectId = objId;

        trx.Commit();
        return EntLastObjectId;
    }

    public static void SetAnnotativeScale(this Database db, string Name, double PaperUnits, double DrawingUnits)
    {
        var ed = Generic.GetEditor();
        if (db.Cannoscale.Name != Name)
        {
            using var trx = db.TransactionManager.StartTransaction();
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
            trx.Commit();
        }
    }


    public static ObjectId GetObjectIdFromAppDictionary(this Database db, Transaction trx, string appDictName,
        string keyName)
    {
        var nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

        if (!nod.Contains(appDictName)) return ObjectId.Null;

        var appDict = (DBDictionary)trx.GetObject(nod.GetAt(appDictName), OpenMode.ForRead);

        if (!appDict.Contains(keyName)) return ObjectId.Null;

        var xrec = (Xrecord)trx.GetObject(appDict.GetAt(keyName), OpenMode.ForRead);
        var data = xrec.Data.AsArray();

        return data.Length == 0 || data[0].Value is not ObjectId ? ObjectId.Null : (ObjectId)data[0].Value;
    }


    public static void StoreObjectIdInAppDictionary(this Database db, Transaction trx, string appDictName,
        string keyName, ObjectId objectId)
    {
        var nod = (DBDictionary)trx.GetObject(db.NamedObjectsDictionaryId, OpenMode.ForRead);

        DBDictionary appDict;
        if (!nod.Contains(appDictName))
        {
            nod.UpgradeOpen();
            appDict = new DBDictionary();
            _ = nod.SetAt(appDictName, appDict);
            trx.AddNewlyCreatedDBObject(appDict, true);
        }
        else
        {
            appDict = (DBDictionary)trx.GetObject(nod.GetAt(appDictName), OpenMode.ForRead);
        }

        if (appDict.Contains(keyName)) return;

        appDict.UpgradeOpen();
        Xrecord xrec = new()
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.SoftPointerId, objectId))
        };
        _ = appDict.SetAt(keyName, xrec);
        trx.AddNewlyCreatedDBObject(xrec, true);
    }


    public static DwgVersion GetDwgVersion(this Database db)
    {
        var LastSaved = db.LastSavedAsVersion;
        return LastSaved == DwgVersion.MC0To0 ? DwgVersion.Current : LastSaved;
    }

    public static long GetSize(this Database db, DwgVersion version)
    {
        var tempFileName = $"SioForgeCAD_SizeCheck_{DateTime.Now:yyMMdd_HHmmss}_{Guid.NewGuid()}.dwg";
        var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
        long sizeBytes = 0;

        try
        {
            db.SaveAs(tempFilePath, version);

            FileInfo fi = new(tempFilePath);
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
