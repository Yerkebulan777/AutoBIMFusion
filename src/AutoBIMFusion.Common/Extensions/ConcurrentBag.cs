using System.Collections.Concurrent;

namespace AutoBIMFusion.Common.Extensions;

public static class ConcurrentBagExtensions
{
    public static void AddRange<T>(this ConcurrentBag<T> @this, IEnumerable<T> toAdd)
    {
        foreach (T? element in toAdd)
        {
            @this.Add(element);
        }
    }
}
