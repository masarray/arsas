using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P8MemoryPerformanceTests
{
    [Fact]
    public void AllocationSnapshot_DeltaNeverReportsNegativeAllocatedBytesOrCollectionCounts()
    {
        var before = RuntimeAllocationSnapshot.Capture();
        _ = new byte[4096];
        var after = RuntimeAllocationSnapshot.Capture();

        var delta = after.DeltaFrom(before);

        Assert.True(delta.AllocatedBytes >= 0);
        Assert.True(delta.Gen0Collections >= 0);
        Assert.True(delta.Gen1Collections >= 0);
        Assert.True(delta.Gen2Collections >= 0);
        Assert.True(delta.AllocatedMegabytesPerSecond >= 0d);
    }

    [Fact]
    public void AllocationSnapshot_DoesNotForceGarbageCollection()
    {
        var gen0Before = GC.CollectionCount(0);
        var gen1Before = GC.CollectionCount(1);
        var gen2Before = GC.CollectionCount(2);

        _ = RuntimeAllocationSnapshot.Capture();

        Assert.Equal(gen0Before, GC.CollectionCount(0));
        Assert.Equal(gen1Before, GC.CollectionCount(1));
        Assert.Equal(gen2Before, GC.CollectionCount(2));
    }
}
