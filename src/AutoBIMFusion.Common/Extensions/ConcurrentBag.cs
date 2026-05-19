using System.Collections.Concurrent;

namespace AutoBIMFusion.Common.Extensions;

public static class ConcurrentBagExtensions
{
    public static void AddRange<T>(this ConcurrentBag<T> @this, IEnumerable<T> toAdd)
    {
        foreach (var element in toAdd) @this.Add(element);
    }
}
