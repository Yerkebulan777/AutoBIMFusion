using AutoBIMFusion.Common.Extensions;
using AutoBIMFusion.Common.Mist.Geometry;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

public static class SelectInXref
{
    public static (ObjectId[] XrefObjectId, ObjectId SelectedObjectId, PromptStatus PromptStatus) Select(string Message,
        Point3d? NonInterractivePickedPoint = null)
    {
        Editor ed = Generic.GetEditor();

        PromptNestedEntityOptions nestedEntOpt = new(Message);
        if (NonInterractivePickedPoint != null)
        {
            nestedEntOpt.NonInteractivePickPoint = NonInterractivePickedPoint ?? Point3d.Origin;
            nestedEntOpt.UseNonInteractivePickPoint = true;
        }

        PromptNestedEntityResult nestedEntRes = ed.GetNestedEntity(nestedEntOpt);
        if (nestedEntRes.Status != PromptStatus.OK)
        {
            return (Array.Empty<ObjectId>(), ObjectId.Null, nestedEntRes.Status);
        }

        (ObjectId[]? XrefObjectId, ObjectId SelectedObjectId) = nestedEntRes.GetEntityInChildXref();
        return (XrefObjectId, SelectedObjectId, nestedEntRes.Status);
    }

    public static (ObjectId[] XrefObjectId, ObjectId SelectedObjectId) GetEntityInChildXref(
        this PromptNestedEntityResult res)
    {
        return (res.GetContainers(), res.ObjectId);
    }

    public static string GetEntityPathInChildXref(this PromptNestedEntityResult res)
    {
        Database db = HostApplicationServices.WorkingDatabase;
        using Transaction trx = db.TransactionManager.StartTransaction();
        List<string> Path = new();
        foreach (ObjectId id in res.GetContainers().Reverse())
        {
            BlockReference? container = trx.GetObject(id, OpenMode.ForRead) as BlockReference;

            Path.Add(container.Name);
        }

        trx.Commit();
        return string.Join(">", Path);
    }

    public static Points TransformPointInXrefsToCurrent(Point3d XrefPosition,
        IEnumerable<ObjectId> NestedXrefsContainer)
    {
        Point3d BlkPosition = XrefPosition;
        foreach (ObjectId objectId in NestedXrefsContainer)
        {
            BlkPosition = Points.From3DPoint(BlkPosition.ProjectXrefPointToCurrentSpace(objectId)).SCG;
        }

        return BlkPosition.ToPoints();
    }
}
