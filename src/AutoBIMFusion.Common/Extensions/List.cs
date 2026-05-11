namespace SioForgeCAD.Commun.Extensions;

public static class ListExtensions
{
    public static void DeepDispose<T>(this IList<T> list)
    {
        (list as IEnumerable<T>).DeepDispose();
    }

    public static void DeepDispose<T>(this IEnumerable<T> list)
    {
        foreach (var item in list)
            if (item is IDisposable disposable)
                disposable.Dispose();
    }

    public static List<T> RemoveCommun<T>(this IEnumerable<T> list, IEnumerable<T> ItemsToRemove)
    {
        var NewList = list.ToList();
        foreach (var item in list)
            if (ItemsToRemove.Contains(item))
                NewList.Remove(item);

        return NewList;
    }

    public static List<T> AddRangeUnique<T>(this IEnumerable<T> list, IEnumerable<T> ItemsToAddIfNotAlreadyInside)
    {
        var NewList = list.ToList();
        foreach (var item in ItemsToAddIfNotAlreadyInside)
            if (!NewList.Contains(item))
                NewList.Add(item);

        return NewList;
    }

    public static double SumNumeric(this List<object> values)
    {
        var doubles = values.ConvertAll(v =>
        {
            if (v is double d) return d;
            if (double.TryParse(v.ToString(), out var res)) return res;
            return 0.0;
        });

        return doubles.Count != 0 ? doubles.Sum() : 0d;
    }

    public static bool HasTypeOf<T>(this List<T> list, Type type)
    {
        return list.Any(item => item != null && type.IsAssignableFrom(item.GetType()));
    }
}
