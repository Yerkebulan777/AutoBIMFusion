using System.Diagnostics;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Windows.Data;
using AcAp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace SioForgeCAD.Commun;

public static class Layers
{
    public static string GetCurrentLayerName()
    {
        return Application.GetSystemVariable("clayer").ToString();
    }

    public static void SetCurrentLayerName(string LayerName)
    {
        var db = Generic.GetDatabase();
        using (var trx = db.TransactionManager.StartTransaction())
        {
            var ltb = (LayerTable)db.LayerTableId.GetDBObject();
            db.Clayer = ltb[LayerName];
            trx.Commit();
        }
    }

    public static DataItemCollection GetAllLayersInDrawing()
    {
        return AcAp.UIBindings.Collections.Layers;
    }

    public static bool IsEntityOnLockedLayer(ObjectId entity)
    {
        return entity.GetEntity().IsEntityOnLockedLayer();
    }

    public static bool IsEntityOnLockedLayer(this Entity entity)
    {
        var layerId = entity.LayerId;
        if (layerId.GetNoTransactionDBObject() is LayerTableRecord layerRecord) return layerRecord?.IsLocked == true;
        return true;
    }

    public static bool IsLayerLocked(string Name)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            var layerId = layerTable[Name];
            var layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
            trans.Commit();
            return layerRecord?.IsLocked == true;
        }
    }

    public static Transparency GetTransparency(string Name)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            var layerId = layerTable[Name];
            var layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
            trans.Commit();
            return layerRecord.Transparency;
        }
    }

    public static void SetTransparency(string Name, Transparency transparency)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            var layerId = layerTable[Name];
            var layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
            layerRecord.Transparency = transparency;
            trans.Commit();
        }
    }

    public static void SetLock(string Name, bool Lock)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            var layerId = layerTable[Name];
            var layerRecord = layerId.GetObject(OpenMode.ForWrite) as LayerTableRecord;
            layerRecord.IsLocked = Lock;
            trans.Commit();
        }
    }

    public static ObjectId GetLayerIdByName(string layerName, Database db = null)
    {
        if (db == null) db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (layerTable.Has(layerName))
            {
                var layerId = layerTable[layerName];
                trans.Commit();
                return layerId;
            }

            return ObjectId.Null;
        }
    }

    public static bool CheckIfLayerExist(string layername)
    {
        var doc = Generic.GetDocument();
        var db = Generic.GetDatabase();
        using (var acTrans = doc.TransactionManager.StartTransaction())
        {
            var acLyrTbl = acTrans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
            acTrans.Commit();
            return acLyrTbl.Has(layername);
        }
    }

    public static void CreateLayer(string Name, Color Color, LineWeight LineWeight, Transparency Transparence,
        bool IsPlottable)
    {
        var db = Generic.GetDatabase();
        using (var acTrans = db.TransactionManager.StartTransaction())
        {
            var acLyrTbl = acTrans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

            if (!acLyrTbl.Has(Name))
            {
                using (var acLyrTblRec = new LayerTableRecord())
                {
                    acLyrTblRec.Name = Name;
                    acLyrTblRec.Color = Color;
                    acLyrTblRec.IsPlottable = IsPlottable;
                    acLyrTblRec.LineWeight = LineWeight;
                    acLyrTbl.UpgradeOpen();
                    acLyrTbl.Add(acLyrTblRec);
                    acTrans.AddNewlyCreatedDBObject(acLyrTblRec, true);
                    acLyrTblRec.Transparency = Transparence;
                }
            }
            else
            {
                var ltr = (LayerTableRecord)acTrans.GetObject(acLyrTbl[Name], OpenMode.ForWrite);
                ltr.Name = Name;
            }

            acTrans.Commit();
        }
    }

    public static bool Rename(string OldName, string NewName)
    {
        var db = Generic.GetDatabase();

        using (var trans = db.TransactionManager.StartTransaction())
        {
            try
            {
                var layerName = OldName;
                var newLayerName = NewName;

                // Renommer le calque
                var lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
                if (lt.Has(layerName))
                {
                    var ltr = (LayerTableRecord)trans.GetObject(lt[layerName], OpenMode.ForWrite);
                    ltr.Name = newLayerName;
                    return true;
                }

                return false;
            }
            finally
            {
                trans.Commit();
            }
        }
    }

    public static void SetLayerColor(string LayerName, Color color)
    {
        var db = Generic.GetDatabase();

        using (var trans = db.TransactionManager.StartTransaction())
        {
            // Renommer le calque
            var lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
            if (lt.Has(LayerName))
            {
                var ltr = (LayerTableRecord)trans.GetObject(lt[LayerName], OpenMode.ForWrite);
                ltr.Color = color;
            }

            trans.Commit();
        }
    }

    public static void Merge(string sourceLayerName, string targetLayerName)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var bt = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
            var btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
            var lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
            //Has is IgnoreCase, so we need to check that we are not merging the same layer
            if (!string.Equals(sourceLayerName, targetLayerName, StringComparison.InvariantCultureIgnoreCase))
            {
                if (lt.Has(sourceLayerName) && lt.Has(targetLayerName))
                    // Iterate through all entities in the drawing
                    foreach (var objId in btr)
                    {
                        var ent = objId.GetEntity();
                        if (ent.Layer == sourceLayerName)
                        {
                            ent.UpgradeOpen();
                            ent.Layer = targetLayerName;
                        }
                    }

                try
                {
                    var sourceLayerId = lt[sourceLayerName];
                    var sourceLayer = (LayerTableRecord)trans.GetObject(sourceLayerId, OpenMode.ForWrite);
                    if (sourceLayerName != targetLayerName)
                    {
                        if (GetCurrentLayerName() == sourceLayerName) SetCurrentLayerName(targetLayerName);
                        sourceLayer.Erase();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.Message);
                }
            }

            trans.Commit();
        }
    }

    public static void Merge(Transaction trx, Database db, ObjectId sourceLayerId, ObjectId targetLayerId)
    {
        var sourceLayer = trx.GetObject(sourceLayerId, OpenMode.ForRead) as LayerTableRecord;
        var targetLayer = trx.GetObject(targetLayerId, OpenMode.ForRead) as LayerTableRecord;

        if (sourceLayer == null || targetLayer == null) return;

        var sourceLayerName = sourceLayer.Name;

        // Move every entities
        foreach (var blockId in trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable)
        foreach (var entId in trx.GetObject(blockId, OpenMode.ForWrite) as BlockTableRecord)
        {
            var ent = trx.GetObject(entId, OpenMode.ForRead) as Entity;
            if (ent != null && ent.LayerId == sourceLayerId)
            {
                if (ent.IsEntityOnLockedLayer()) SetLock(ent.Layer, false);
                ent.UpgradeOpen();
                ent.LayerId = targetLayerId;
            }
        }

        try
        {
            if (!sourceLayer.IsErased && !sourceLayer.IsDependent)
            {
                sourceLayer.UpgradeOpen();
                sourceLayer.Erase(true);
            }
        }
        catch (Autodesk.AutoCAD.Runtime.Exception ex)
        {
            Generic.WriteMessage($"Impossible de supprimer le calque {sourceLayerName}: {ex.Message}");
        }
    }

    public static Color GetLayerColor(string LayerName)
    {
        var LayerTableRecordObjId = GetLayerIdByName(LayerName);
        return GetLayerColor(LayerTableRecordObjId);
    }

    public static Color GetLayerColor(ObjectId LayerTableRecordObjId)
    {
        var db = Generic.GetDatabase();
        using (var trans = db.TransactionManager.StartTransaction())
        {
            var layerTableRecord = LayerTableRecordObjId.GetDBObject() as LayerTableRecord;
            trans.Commit();
            return layerTableRecord?.Color ?? Color.FromRgb(0, 0, 0);
        }
    }
}
