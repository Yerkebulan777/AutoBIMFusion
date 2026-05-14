namespace AutoBIMFusion.Common.Extensions;

public static class DictionnaryExtensions
{
    public static string TryGetValueString<T>(this Dictionary<T, string> dictionary, T key) where T : notnull
    {
        return dictionary == null ? string.Empty : dictionary.TryGetValue(key, out string? value) ? value : string.Empty;
    }

    public static void TryAdd<TKey, TValue>(this Dictionary<TKey, TValue> dictionary, TKey key, TValue value)
        where TKey : notnull
    {
        if (dictionary.ContainsKey(key))
        {
            return;
        }

        dictionary.Add(key, value);
    }
}
