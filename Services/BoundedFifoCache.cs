namespace ArIED61850Tester.Services;

/// <summary>
/// Small single-owner bounded cache for interactive presentation/analysis data. Capacity is fixed at
/// construction and eviction removes one oldest live key at a time instead of clearing the entire
/// cache, avoiding periodic cache-miss and allocation spikes during long scrubs.
/// </summary>
internal sealed class BoundedFifoCache<TKey, TValue> where TKey : notnull
{
    private readonly Dictionary<TKey, TValue> _items;
    private readonly Queue<TKey> _insertionOrder;

    internal BoundedFifoCache(int capacity, IEqualityComparer<TKey>? comparer = null)
    {
        Capacity = Math.Max(1, capacity);
        _items = new Dictionary<TKey, TValue>(Capacity, comparer);
        _insertionOrder = new Queue<TKey>(Capacity);
    }

    internal int Capacity { get; }
    internal int Count => _items.Count;

    internal bool TryGetValue(TKey key, out TValue value)
        => _items.TryGetValue(key, out value!);

    internal void Set(TKey key, TValue value)
    {
        if (_items.ContainsKey(key))
        {
            _items[key] = value;
            return;
        }

        while (_items.Count >= Capacity && _insertionOrder.Count > 0)
        {
            var oldest = _insertionOrder.Dequeue();
            if (_items.Remove(oldest))
                break;
        }

        // The queue and dictionary are owned together, but keep a defensive final bound in case a
        // future caller mutates lifecycle assumptions. Never grow past Capacity.
        if (_items.Count >= Capacity)
        {
            using var enumerator = _items.Keys.GetEnumerator();
            if (enumerator.MoveNext())
                _items.Remove(enumerator.Current);
        }

        _items[key] = value;
        _insertionOrder.Enqueue(key);
    }

    internal void Clear()
    {
        _items.Clear();
        _insertionOrder.Clear();
    }
}
