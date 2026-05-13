using System.Diagnostics;
using AutoBIMFusion.Common.Extensions;

namespace AutoBIMFusion.Common.Mist.AutoCAD;

internal static class HightLighter
{
    private static readonly List<ObjectId> HightLightedObject = new();

    public static void RegisterHighlight(this ObjectId ObjectId)
    {
        if (!HightLightedObject.Contains(ObjectId)) HightLightedObject.Add(ObjectId);
        ObjectId.GetEntity().Highlight();
    }

    public static void RegisterHighlight(this Entity Entity)
    {
        Entity.ObjectId.RegisterHighlight();
    }

    public static void RegisterUnhighlight(this Entity Entity)
    {
        Entity.ObjectId.RegisterUnhighlight();
    }

    public static void RegisterUnhighlight(this ObjectId ObjectId)
    {
        try
        {
            HightLightedObject.Remove(ObjectId);
            ObjectId.GetEntity().Unhighlight();
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex);
        }
    }

    public static void UnhighlightAll()
    {
        foreach (var objectId in HightLightedObject.ToArray()) objectId.RegisterUnhighlight();
    }

    public static void UnhighlightAll(IEnumerable<ObjectId> HightLightedObject)
    {
        foreach (var objectId in HightLightedObject) objectId.RegisterUnhighlight();
    }
}
