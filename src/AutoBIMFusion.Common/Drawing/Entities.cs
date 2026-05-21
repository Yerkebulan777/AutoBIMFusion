using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.AcadSupport;

namespace AutoBIMFusion.Common.Drawing;

public static class Entities
{
    public static List<ObjectId> AddToDrawing(this IEnumerable<Entity> entities, int? ColorIndex = null,
        bool Clone = false)
    {
        List<ObjectId> objs = [];
        foreach (Entity entity in entities)
        {
            Entity ent = entity;
            if (Clone)
            {
                ent = (Entity)ent.Clone();
            }

            if (ColorIndex != null)
            {
                ent.ColorIndex = (int)ColorIndex;
            }

            objs.Add(ent.AddToDrawing());
        }

        return objs;
    }

    public static List<ObjectId> AddToDrawing(this DBObjectCollection entities, int? ColorIndex = null,
        bool Clone = false)
    {
        return entities.Cast<Entity>().AddToDrawing(ColorIndex, Clone);
    }

    public static ObjectId AddToDrawing(this Entity entity, int? ColorIndex = null, bool Clone = false)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction acTrans = db.TransactionManager.StartTransaction();
        BlockTableRecord acBlkTblRec = AcadContext.GetCurrentSpaceBlockTableRecord(acTrans);

        // Проверяем, находится ли сущность уже в базе данных
        if (entity?.IsErased != false)
        {
            acTrans.Abort();
            return ObjectId.Null;
        }

        if (Clone)
        {
            entity = entity.Clone() as Entity;
        }

        if (ColorIndex != null)
        {
            entity.ColorIndex = (int)ColorIndex;
        }

        try
        {
            _ = acBlkTblRec.AppendEntity(entity);
            acTrans.AddNewlyCreatedDBObject(entity, true);
            acTrans.Commit();
            return entity.ObjectId;
        }
        catch
        {
            return ObjectId.Null;
        }
    }

    public static ObjectId ReplaceInDrawing(this Entity OriginalEntity, Entity ReplaceEntity)
    {
        Database db = AcadContext.GetDatabase();
        using Transaction acTrans = db.TransactionManager.StartTransaction();
        if (ReplaceEntity?.IsErased != false ||
            acTrans.GetObject(OriginalEntity.OwnerId, OpenMode.ForWrite) is not BlockTableRecord ownerBtr)
        {
            acTrans.Abort();
            return ObjectId.Null;
        }

        OriginalEntity.TryUpgradeOpen();
        ReplaceEntity.TryUpgradeOpen();

        _ = ownerBtr.AppendEntity(ReplaceEntity);
        acTrans.AddNewlyCreatedDBObject(ReplaceEntity, true);
        OriginalEntity.Erase();
        acTrans.Commit();
        return ReplaceEntity.ObjectId;
    }


    public static ObjectId AddToDrawingCurrentTransaction(this Entity entity)
    {
        Database db = AcadContext.GetDatabase();
        Transaction acTrans = db.TransactionManager.TopTransaction;
        BlockTableRecord acBlkTblRec = AcadContext.GetCurrentSpaceBlockTableRecord(acTrans);

        ObjectId objid = acBlkTblRec.AppendEntity(entity);
        acTrans.AddNewlyCreatedDBObject(entity, true);
        return objid;
    }
}
