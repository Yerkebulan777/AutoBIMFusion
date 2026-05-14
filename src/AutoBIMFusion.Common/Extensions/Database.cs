using AutoBIMFusion.Common.Mist;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Runtime;
using System.Diagnostics;
using Application = Autodesk.AutoCAD.ApplicationServices.Core.Application;
using Exception = System.Exception;

namespace AutoBIMFusion.Common.Extensions;

public static class DatabaseExtensions
{
    public static void OpenAsNewTab(this Database db)
    {
        DocumentCollection docCol = Application.DocumentManager;
        string FilName = Path.Combine(Path.GetTempPath(), $"Memory_{DateTime.Now.Ticks}.dwg");
        db.SaveAs(FilName, DwgVersion.Current);
        Document newDoc = docCol.Open(FilName, false);
        docCol.MdiActiveDocument = newDoc;
    }

    public static Dictionary<ObjectId, string> GetAllObjects(this Database db)
    {
        var dict = new Dictionary<ObjectId, string>();
        for (long i = 0; i < db.Handseed.Value; i++)
        {
            if (db.TryGetObjectId(new Handle(i), out ObjectId id))
            {
                dict.Add(id, id.ObjectClass.Name);
            }
        }

        return dict;
    }

    public static Dictionary<ObjectId, string> GetAllEntities(this Database db)
    {
        var dict = new Dictionary<ObjectId, string>();
        using (OpenCloseTransaction trx = db.TransactionManager.StartOpenCloseTransaction())
        {
            var bt = (BlockTable)trx.GetObject(db.BlockTableId, OpenMode.ForRead);
            foreach (ObjectId btrId in bt)
            {
                var btr = (BlockTableRecord)trx.GetObject(btrId, OpenMode.ForRead);
                if (btr.IsLayout)
                {
                    foreach (ObjectId id in btr)
                    {
                        dict.Add(id, id.ObjectClass.Name);
                    }
                }
            }

            trx.Commit();
        }

        return dict;
    }

    public static ObjectId EntLast(this Database db, Type type = null)
    {
        // Autodesk.AutoCAD.Internal.Utils.EntLast();
        using Transaction trx = db.TransactionManager.StartTransaction();
        BlockTableRecord btr = Generic.GetCurrentSpaceBlockTableRecord(trx);
        RXClass? RXClassType = type == null ? null : RXObject.GetClass(type);
        ObjectId EntLastObjectId = ObjectId.Null;
        foreach (ObjectId objId in btr)
        {
            if (RXClassType == null || objId.ObjectClass == RXClassType)
            {
                EntLastObjectId = objId;
            }
        }

        trx.Commit();
        return EntLastObjectId;
    }

    public static void SetAnnotativeScale(this Database db, string Name, double PaperUnits, double DrawingUnits)
    {
        Editor ed = Generic.GetEditor();
        if (db.Cannoscale.Name != Name)
        {
            using Transaction trx = db.TransactionManager.StartTransaction();
            ObjectContextManager ocm = db.ObjectContextManager;
            ObjectContextCollection occ = ocm.GetContextCollection("ACDB_ANNOTATIONSCALES");

            if (occ != null)
            {
                AnnotationScale scale = null;
                foreach (ObjectContext? obj in occ)
                {
                    if (obj is AnnotationScale annoScale && annoScale.Name == Name)
                    {
                        scale = annoScale;
                        break;
                    }
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

        if (!nod.Contains(appDictName))
        {
            return ObjectId.Null;
        }

        var appDict = (DBDictionary)trx.GetObject(nod.GetAt(appDictName), OpenMode.ForRead);

        if (!appDict.Contains(keyName))
        {
            return ObjectId.Null;
        }

        var xrec = (Xrecord)trx.GetObject(appDict.GetAt(keyName), OpenMode.ForRead);
        TypedValue[] data = xrec.Data.AsArray();

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

        if (appDict.Contains(keyName))
        {
            return;
        }

        appDict.UpgradeOpen();
        var xrec = new Xrecord
        {
            Data = new ResultBuffer(new TypedValue((int)DxfCode.SoftPointerId, objectId))
        };
        _ = appDict.SetAt(keyName, xrec);
        trx.AddNewlyCreatedDBObject(xrec, true);
    }


    public static DwgVersion GetDwgVersion(this Database db)
    {
        DwgVersion LastSaved = db.LastSavedAsVersion;
        return LastSaved == DwgVersion.MC0To0 ? DwgVersion.Current : LastSaved;
    }

    public static long GetSize(this Database db, DwgVersion version)
    {
        string tempFileName = $"SioForgeCAD_SizeCheck_{DateTime.Now:yyMMdd_HHmmss}_{Guid.NewGuid()}.dwg";
        string tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
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
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Impossible de supprimer le temp : " + ex.Message);
            }
        }

        return sizeBytes;
    }
}
