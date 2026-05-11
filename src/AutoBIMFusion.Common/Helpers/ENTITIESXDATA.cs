using System.Diagnostics;
using SioForgeCAD.Commun;
using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Functions;

public static class ENTITIESXDATA
{
    public static void Read()
    {
        var ed = Generic.GetEditor();
        var db = Generic.GetDatabase();
        var result = ed.GetEntity("Selectionnez un object");
        if (result.Status != PromptStatus.OK) return;
        using (var tr = db.TransactionManager.StartTransaction())
        {
            if (result.ObjectId.GetDBObject() is Entity ent)
                foreach (var item in ent.ReadXData())
                    Generic.WriteMessage(item.ToString());
        }
    }

    public static void Remove()
    {
        var ed = Generic.GetEditor();

        if (ed.GetImpliedSelection(out var AllSelectedObjectImplied))
        {
            RemoveAllXDataFromCollection(AllSelectedObjectImplied.Value.GetObjectIds());
        }
        else
        {
            var AllSelectedObjectRedraw =
                ed.GetSelectionRedraw("Selectionnez des entités pour lequels vous souhaitez supprimer les XDATAs");
            RemoveAllXDataFromCollection(AllSelectedObjectRedraw.Value.GetObjectIds());
        }
    }

    public static void RemoveAll()
    {
        var db = Generic.GetDatabase();
        RemoveAllXDataFromCollection(db.GetAllObjects().Keys.ToList());
    }

    private static void RemoveAllXDataFromCollection(IList<ObjectId> objectIds)
    {
        var db = Generic.GetDatabase();
        using (var tr = db.TransactionManager.StartTransaction())
        {
            foreach (var item in objectIds)
                try
                {
                    item.GetDBObject().RemoveAllXdata();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(ex.ToString());
                }

            tr.Commit();
        }
    }
}
