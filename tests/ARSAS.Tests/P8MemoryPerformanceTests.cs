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
        Assert.True(delta.Elapsed >= TimeSpan.Zero);
        Assert.True(delta.AllocatedMegabytesPerSecond >= 0d);
    }

    [Fact]
    public void AllocationSnapshot_ReversedMonotonicInputClampsElapsedAndCountersSafely()
    {
        var later = new RuntimeAllocationSnapshot(
            new DateTimeOffset(2026, 9, 11, 2, 0, 0, TimeSpan.Zero),
            2_000,
            2_000,
            1_600,
            1_500,
            100,
            5,
            3,
            1);
        var earlier = new RuntimeAllocationSnapshot(
            later.CapturedAtUtc.AddSeconds(-10),
            1_000,
            1_000,
            1_100,
            1_000,
            50,
            2,
            1,
            0);

        var reversed = earlier.DeltaFrom(later);

        Assert.Equal(TimeSpan.Zero, reversed.Elapsed);
        Assert.Equal(0, reversed.AllocatedBytes);
        Assert.Equal(0, reversed.Gen0Collections);
        Assert.Equal(0, reversed.Gen1Collections);
        Assert.Equal(0, reversed.Gen2Collections);
        Assert.Equal(0d, reversed.AllocatedMegabytesPerSecond);
    }

    [Fact]
    public void AllocationSnapshot_ImplementationUsesMonotonicRateTiming_AndHonestHeapLabels()
    {
        var source = ReadRepoFile("Services/RuntimeAllocationSnapshot.cs");

        Assert.Contains("Stopwatch.GetTimestamp()", source, StringComparison.Ordinal);
        Assert.Contains("Stopwatch.GetElapsedTime(", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalMemory(forceFullCollection: false)", source, StringComparison.Ordinal);
        Assert.Contains("LastGcHeapSizeBytes", source, StringComparison.Ordinal);
        Assert.Contains("LastGcFragmentedBytes", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetGCMemoryInfo()", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalAllocatedBytes(precise: false)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("CapturedAtUtc - earlier.CapturedAtUtc", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.Collect(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("GC.WaitForPendingFinalizers", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
