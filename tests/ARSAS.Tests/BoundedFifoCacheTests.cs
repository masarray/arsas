using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class BoundedFifoCacheTests
{
    [Fact]
    public void Set_NeverGrowsPastCapacityAndEvictsOldestEntry()
    {
        var cache = new BoundedFifoCache<int, string>(3);
        cache.Set(1, "one");
        cache.Set(2, "two");
        cache.Set(3, "three");
        cache.Set(4, "four");

        Assert.Equal(3, cache.Count);
        Assert.False(cache.TryGetValue(1, out _));
        Assert.True(cache.TryGetValue(2, out var two));
        Assert.Equal("two", two);
        Assert.True(cache.TryGetValue(4, out var four));
        Assert.Equal("four", four);
    }

    [Fact]
    public void Set_UpdatingExistingKeyDoesNotConsumeAnotherSlot()
    {
        var cache = new BoundedFifoCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(1, "updated");
        cache.Set(2, "two");

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGetValue(1, out var value));
        Assert.Equal("updated", value);
    }

    [Fact]
    public void Clear_ReleasesAllRetainedEntries()
    {
        var cache = new BoundedFifoCache<int, string>(2);
        cache.Set(1, "one");
        cache.Set(2, "two");

        cache.Clear();

        Assert.Equal(0, cache.Count);
        Assert.False(cache.TryGetValue(1, out _));
    }
}
