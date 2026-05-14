namespace AutoBIMFusion.Common.Extensions;

public static class SymbolTableExtensions
{
    public static IEnumerable<T> GetObjects<T>(this SymbolTable source, Transaction trx,
        OpenMode mode = OpenMode.ForRead, bool openErased = false)
        where T : SymbolTableRecord
    {
        foreach (ObjectId id in openErased ? source.IncludingErased : source)
        {
            yield return (T)trx.GetObject(id, mode, openErased, false);
        }
    }
}
