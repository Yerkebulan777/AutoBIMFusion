namespace SioForgeCAD.Commun.Extensions;

public static class ObjectExtensions
{
    public static bool TryGetDoubleValue(this object obj, out double value)
    {
        if (obj is double)
        {
            value = (double)obj;
        }
        else if (obj is float)
        {
            value = (float)obj;
        }
        else if (obj is int)
        {
            value = (int)obj;
        }
        else if (obj is short)
        {
            value = (short)obj;
        }
        else
        {
            value = 0;
            return false;
        }

        return true;
    }

    public static ObjectId[] GetObjectIds(this object obj)
    {
        if (obj is SelectionSet selectionSet) return selectionSet.GetObjectIds();

        if (obj is IEnumerable<ObjectId> IEnumerableSelectionSet) return IEnumerableSelectionSet.ToArray();

        if (obj is IEnumerable<DBObject> collection) return collection.Select(ent => ent.ObjectId).ToArray();

        if (obj is DBObjectCollection DbObjectCollection)
        {
            var ObjIds = (from DBObject item in DbObjectCollection
                select item.ObjectId).ToList();
        }

        return Array.Empty<ObjectId>();
    }

    public static IEnumerable<ObjectId> GetSelectionSet(this object obj)
    {
        if (obj is SelectionSet selectionSet)
            foreach (SelectedObject item in selectionSet)
                yield return item.ObjectId;
    }
}
