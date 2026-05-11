namespace SioForgeCAD.Commun.Extensions;

public static class LayerTableRecordExtensions
{
    public static bool IsXref(this LayerTableRecord ltr)
    {
        return ltr.IsDependent;
    }
}
