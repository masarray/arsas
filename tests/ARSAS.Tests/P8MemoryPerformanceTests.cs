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
    public void AllocationSnapshot_ImplementationNeverForcesCollection()
    {
        var source = ReadRepoFile("Services/RuntimeAllocationSnapshot.cs");

        Assert.Contains("GC.GetGCMemoryInfo()", source, StringComparison.Ordinal);
        Assert.Contains("GC.GetTotalAllocatedBytes(precise: false)", source, StringComparison.Ordinal);
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
