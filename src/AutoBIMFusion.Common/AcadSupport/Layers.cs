using AutoBIMFusion.Common.Extensions;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.Windows.Data;
using System.Diagnostics;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace AutoBIMFusion.Common.AcadSupport;

public static class Layers
{
    public static string GetCurrentLayerName()
    {
        return AcadApp.GetSystemVariable("clayer").ToString();
    }

    public static void SetCurrentLayerName(string LayerName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trx = db.TransactionManager.StartTransaction();
        LayerTable ltb = (LayerTable)db.LayerTableId.GetDBObject();
        db.Clayer = ltb[LayerName];
        trx.Commit();
    }

    public static DataItemCollection GetAllLayersInDrawing()
    {
        return AcadApp.UIBindings.Collections.Layers;
    }

    public static bool IsEntityOnLockedLayer(ObjectId entity)
    {
        return entity.GetEntity().IsEntityOnLockedLayer();
    }

    public static bool IsEntityOnLockedLayer(this Entity entity)
    {
        ObjectId layerId = entity.LayerId;
        return layerId.GetNoTransactionDBObject() is not LayerTableRecord layerRecord || layerRecord?.IsLocked == true;
    }

    public static bool IsLayerLocked(string Name)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTable? layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
        ObjectId layerId = layerTable[Name];
        LayerTableRecord? layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
        trans.Commit();
        return layerRecord?.IsLocked == true;
    }

    public static Transparency GetTransparency(string Name)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTable? layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
        ObjectId layerId = layerTable[Name];
        LayerTableRecord? layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
        trans.Commit();
        return layerRecord.Transparency;
    }

    public static void SetTransparency(string Name, Transparency transparency)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTable? layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
        ObjectId layerId = layerTable[Name];
        LayerTableRecord? layerRecord = layerId.GetObject(OpenMode.ForRead) as LayerTableRecord;
        layerRecord.Transparency = transparency;
        trans.Commit();
    }

    public static void SetLock(string Name, bool Lock)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTable? layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
        ObjectId layerId = layerTable[Name];
        LayerTableRecord? layerRecord = layerId.GetObject(OpenMode.ForWrite) as LayerTableRecord;
        layerRecord.IsLocked = Lock;
        trans.Commit();
    }

    public static ObjectId GetLayerIdByName(string layerName, Database db = null)
    {
        if (db == null)
        {
            db = AcadContext.GetDatabase();
        }

        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTable? layerTable = trans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

        if (layerTable.Has(layerName))
        {
            ObjectId layerId = layerTable[layerName];
            trans.Commit();
            return layerId;
        }

        return ObjectId.Null;
    }

    public static bool CheckIfLayerExist(string layername)
    {
        Document doc = AcadContext.GetDocument();
        Database db = AcadContext.GetDatabase();
        using Transaction acTrans = doc.TransactionManager.StartTransaction();
        LayerTable? acLyrTbl = acTrans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;
        acTrans.Commit();
        return acLyrTbl.Has(layername);
    }

    public static void CreateLayer(string Name, Color Color, LineWeight LineWeight, Transparency Transparence,
        bool IsPlottable)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction acTrans = db.TransactionManager.StartTransaction();
        LayerTable? acLyrTbl = acTrans.GetObject(db.LayerTableId, OpenMode.ForRead) as LayerTable;

        if (!acLyrTbl.Has(Name))
        {
            using LayerTableRecord acLyrTblRec = new();
            acLyrTblRec.Name = Name;
            acLyrTblRec.Color = Color;
            acLyrTblRec.IsPlottable = IsPlottable;
            acLyrTblRec.LineWeight = LineWeight;
            acLyrTbl.UpgradeOpen();
            _ = acLyrTbl.Add(acLyrTblRec);
            acTrans.AddNewlyCreatedDBObject(acLyrTblRec, true);
            acLyrTblRec.Transparency = Transparence;
        }
        else
        {
            LayerTableRecord ltr = (LayerTableRecord)acTrans.GetObject(acLyrTbl[Name], OpenMode.ForWrite);
            ltr.Name = Name;
        }

        acTrans.Commit();
    }

    public static bool Rename(string OldName, string NewName)
    {
        Database db = AcadContext.GetDatabase();

        using Transaction trans = db.TransactionManager.StartTransaction();
        try
        {
            var layerName = OldName;
            var newLayerName = NewName;

            // Переименовать слой
            LayerTable lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
            if (lt.Has(layerName))
            {
                LayerTableRecord ltr = (LayerTableRecord)trans.GetObject(lt[layerName], OpenMode.ForWrite);
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

    public static void SetLayerColor(string LayerName, Color color)
    {
        Database db = AcadContext.GetDatabase();

        using Transaction trans = db.TransactionManager.StartTransaction();
        // Переименовать слой
        LayerTable lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
        if (lt.Has(LayerName))
        {
            LayerTableRecord ltr = (LayerTableRecord)trans.GetObject(lt[LayerName], OpenMode.ForWrite);
            ltr.Color = color;
        }

        trans.Commit();
    }

    public static void Merge(string sourceLayerName, string targetLayerName)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        BlockTable? bt = trans.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable;
        BlockTableRecord? btr = trans.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite) as BlockTableRecord;
        LayerTable lt = (LayerTable)trans.GetObject(db.LayerTableId, OpenMode.ForWrite);
        //Has is IgnoreCase, so we need to check that we are not merging the same layer
        if (!string.Equals(sourceLayerName, targetLayerName, StringComparison.InvariantCultureIgnoreCase))
        {
            if (lt.Has(sourceLayerName) && lt.Has(targetLayerName))
            {
                // Перебор всех сущностей в чертеже
                foreach (ObjectId objId in btr)
                {
                    Entity ent = objId.GetEntity();
                    if (ent.Layer == sourceLayerName)
                    {
                        ent.UpgradeOpen();
                        ent.Layer = targetLayerName;
                    }
                }
            }

            try
            {
                ObjectId sourceLayerId = lt[sourceLayerName];
                LayerTableRecord sourceLayer = (LayerTableRecord)trans.GetObject(sourceLayerId, OpenMode.ForWrite);
                if (sourceLayerName != targetLayerName)
                {
                    if (GetCurrentLayerName() == sourceLayerName)
                    {
                        SetCurrentLayerName(targetLayerName);
                    }

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

    public static void Merge(Transaction trx, Database db, ObjectId sourceLayerId, ObjectId targetLayerId)
    {
        LayerTableRecord? sourceLayer = trx.GetObject(sourceLayerId, OpenMode.ForRead) as LayerTableRecord;
        LayerTableRecord? targetLayer = trx.GetObject(targetLayerId, OpenMode.ForRead) as LayerTableRecord;

        if (sourceLayer == null || targetLayer == null)
        {
            return;
        }

        var sourceLayerName = sourceLayer.Name;

        // Перемещаем все сущности
        foreach (ObjectId blockId in trx.GetObject(db.BlockTableId, OpenMode.ForRead) as BlockTable)
        {
            foreach (ObjectId entId in trx.GetObject(blockId, OpenMode.ForWrite) as BlockTableRecord)
            {
                Entity? ent = trx.GetObject(entId, OpenMode.ForRead) as Entity;
                if (ent != null && ent.LayerId == sourceLayerId)
                {
                    if (ent.IsEntityOnLockedLayer())
                    {
                        SetLock(ent.Layer, false);
                    }

                    ent.UpgradeOpen();
                    ent.LayerId = targetLayerId;
                }
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
            AcadContext.WriteMessage($"Impossible de supprimer le calque {sourceLayerName}: {ex.Message}");
        }
    }

    public static Color GetLayerColor(string LayerName)
    {
        ObjectId LayerTableRecordObjId = GetLayerIdByName(LayerName);
        return GetLayerColor(LayerTableRecordObjId);
    }

    public static Color GetLayerColor(ObjectId LayerTableRecordObjId)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction trans = db.TransactionManager.StartTransaction();
        LayerTableRecord? layerTableRecord = LayerTableRecordObjId.GetDBObject() as LayerTableRecord;
        trans.Commit();
        return layerTableRecord?.Color ?? Color.FromRgb(0, 0, 0);
    }
}
