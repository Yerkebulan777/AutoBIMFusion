namespace SioForgeCAD.Commun.Extensions;

public static class SymbolTableExtensions
{
    public static IEnumerable<T> GetObjects<T>(this SymbolTable source, Transaction tr,
        OpenMode mode = OpenMode.ForRead, bool openErased = false)
        where T : SymbolTableRecord
    {
        foreach (var id in openErased ? source.IncludingErased : source)
            yield return (T)tr.GetObject(id, mode, openErased, false);
    }
}
