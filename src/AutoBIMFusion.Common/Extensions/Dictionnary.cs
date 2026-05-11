namespace SioForgeCAD.Commun.Extensions;

public static class DictionnaryExtensions
{
    public static string TryGetValueString<T>(this Dictionary<T, string> dictionary, T key) where T : notnull
    {
        if (dictionary == null) return string.Empty;
        if (dictionary.TryGetValue(key, out var value)) return value;
        return string.Empty;
    }

    public static void TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key)) return;
        dictionary.Add(key, value);
    }
}
