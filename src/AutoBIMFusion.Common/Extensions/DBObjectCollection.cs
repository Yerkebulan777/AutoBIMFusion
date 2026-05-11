namespace SioForgeCAD.Commun.Extensions;

public static class DBObjectCollectionExtensions
{
    public static DBObjectCollection AddRange(this DBObjectCollection A, DBObjectCollection B)
    {
        foreach (DBObject ent in B)
            if (!A.Contains(ent))
                A.Add(ent);

        return A;
    }

    public static void DeepDispose(this DBObjectCollection collection)
    {
        foreach (DBObject item in collection)
            if (item?.IsDisposed == false)
                item.Dispose();

        collection.Dispose();
    }

    public static DBObjectCollection DeepClone(this DBObjectCollection collection)
    {
        var clones = new DBObjectCollection();
        foreach (Entity ent in collection) clones.Add(ent.Clone() as Entity);
        return clones;
    }

    public static DBObject[] ToArray(this DBObjectCollection collection)
    {
        var list = new DBObject[collection.Count];
        for (var i = 0; i < collection.Count; i++) list.SetValue(collection[i], i);
        return list;
    }
}
