using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P8LatestValueUiBatcherTests
{
    [Fact]
    public async Task SameVisualKey_IsCoalescedToLatestValue()
    {
        var flushed = new List<int>();
        await using var batcher = new LatestValueUiBatcher<string, int>(
            (batch, _) =>
            {
                lock (flushed)
                    flushed.AddRange(batch);
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromSeconds(2));

        for (var value = 1; value <= 100; value++)
            Assert.True(batcher.TryPublish("IED1|LD0/XCBR1.Pos.stVal", value));

        await batcher.FlushNowAsync();

        Assert.Single(flushed);
        Assert.Equal(100, flushed[0]);
        Assert.Equal(100, batcher.PublishedCount);
        Assert.Equal(1, batcher.FlushedCount);
        Assert.True(batcher.CoalescedCount >= 99);
    }

    [Fact]
    public async Task IndependentKeys_AreFlushedIndependently()
    {
        var flushed = new List<string>();
        await using var batcher = new LatestValueUiBatcher<string, string>(
            (batch, _) =>
            {
                lock (flushed)
                    flushed.AddRange(batch);
                return ValueTask.CompletedTask;
            },
            TimeSpan.FromSeconds(2));

        batcher.TryPublish("IED1|A", "IED1-new");
        batcher.TryPublish("IED2|B", "IED2-new");
        await batcher.FlushNowAsync();

        Assert.Equal(2, flushed.Count);
        Assert.Contains("IED1-new", flushed);
        Assert.Contains("IED2-new", flushed);
    }

    [Fact]
    public async Task Dispose_StopsAcceptingNewVisualUpdates()
    {
        var batcher = new LatestValueUiBatcher<string, int>(
            (_, _) => ValueTask.CompletedTask,
            TimeSpan.FromSeconds(2));

        Assert.True(batcher.TryPublish("IED1|A", 1));
        await batcher.DisposeAsync();

        Assert.False(batcher.TryPublish("IED1|A", 2));
    }
}
