using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P8MemoryPerformanceTests
{
    [Fact]
    public void PooledByteBufferLease_ExposesRequestedLength_AndDisposeIsIdempotent()
    {
        var lease = PooledByteBufferLease.Rent(1024, clearOnReturn: true);
        Assert.Equal(1024, lease.Length);
        Assert.Equal(1024, lease.Memory.Length);

        lease.Span[0] = 0x61;
        Assert.Equal((byte)0x61, lease.Span[0]);

        lease.Dispose();
        lease.Dispose();
        Assert.Throws<ObjectDisposedException>(() => _ = lease.Memory);
    }

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
}
