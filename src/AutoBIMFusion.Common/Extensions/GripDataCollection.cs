namespace AutoBIMFusion.Common.Extensions;

public static class GripDataCollectionExtensions
{
    public static GripData[] ToArray(this GripDataCollection grips)
    {
        var newArray = new GripData[grips.Count];
        var index = 0;
        foreach (var item in grips) newArray.SetValue(item, index++);
        return newArray;
    }
}
