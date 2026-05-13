using SioForgeCAD.Commun.Extensions;

namespace SioForgeCAD.Commun;

public static class SelectInXref
{
    public static (ObjectId[] XrefObjectId, ObjectId SelectedObjectId, PromptStatus PromptStatus) Select(string Message,
        Point3d? NonInterractivePickedPoint = null)
    {
        var ed = Generic.GetEditor();

        var nestedEntOpt = new PromptNestedEntityOptions(Message);
        if (NonInterractivePickedPoint != null)
        {
            nestedEntOpt.NonInteractivePickPoint = NonInterractivePickedPoint ?? Point3d.Origin;
            nestedEntOpt.UseNonInteractivePickPoint = true;
        }

        var nestedEntRes = ed.GetNestedEntity(nestedEntOpt);
        if (nestedEntRes.Status != PromptStatus.OK)
            return (Array.Empty<ObjectId>(), ObjectId.Null, nestedEntRes.Status);
        var (XrefObjectId, SelectedObjectId) = nestedEntRes.GetEntityInChildXref();
        return (XrefObjectId, SelectedObjectId, nestedEntRes.Status);
    }

    public static (ObjectId[] XrefObjectId, ObjectId SelectedObjectId) GetEntityInChildXref(
        this PromptNestedEntityResult res)
    {
        return (res.GetContainers(), res.ObjectId);
    }

    public static string GetEntityPathInChildXref(this PromptNestedEntityResult res)
    {
        var db = HostApplicationServices.WorkingDatabase;
        using (var trx = db.TransactionManager.StartTransaction())
        {
            var Path = new List<string>();
            foreach (var id in res.GetContainers().Reverse())
            {
                var container = trx.GetObject(id, OpenMode.ForRead) as BlockReference;

                Path.Add(container.Name);
            }

            trx.Commit();
            return string.Join(">", Path);
        }
    }

    public static Points TransformPointInXrefsToCurrent(Point3d XrefPosition,
        IEnumerable<ObjectId> NestedXrefsContainer)
    {
        var BlkPosition = XrefPosition;
        foreach (var objectId in NestedXrefsContainer)
            BlkPosition = Points.From3DPoint(BlkPosition.ProjectXrefPointToCurrentSpace(objectId)).SCG;
        return BlkPosition.ToPoints();
    }
}
