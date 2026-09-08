#if NETFRAMEWORK
namespace AutoBIMFusion.Common.Compatibility;

/// <summary>Legacy min-priority queue; equal priorities retain every queued element.</summary>
internal sealed class PriorityQueue<TElement, TPriority> where TPriority : notnull
{
    private readonly SortedDictionary<TPriority, Queue<TElement>> _buckets = new();

    public int Count { get; private set; }

    public void Enqueue(TElement element, TPriority priority)
    {
        if (!_buckets.TryGetValue(priority, out var bucket))
        {
            bucket = new Queue<TElement>();
            _buckets.Add(priority, bucket);
        }

        bucket.Enqueue(element);
        Count++;
    }

    public TElement Dequeue()
    {
        if (Count == 0)
        {
            throw new InvalidOperationException("The queue is empty.");
        }

        var first = _buckets.First();
        var element = first.Value.Dequeue();
        if (first.Value.Count == 0)
        {
            _buckets.Remove(first.Key);
        }

        Count--;
        return element;
    }
}
#endif
