namespace ArIED61850Tester.Services;

/// <summary>
/// Low-overhead allocation/GC snapshot for field diagnostics and regression baselines.
/// Capturing a snapshot does not force a collection and is safe on the monitoring path.
/// </summary>
public readonly record struct RuntimeAllocationSnapshot(
    DateTimeOffset CapturedAtUtc,
    long TotalAllocatedBytes,
    long HeapSizeBytes,
    long FragmentedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public static RuntimeAllocationSnapshot Capture()
    {
        var memory = GC.GetGCMemoryInfo();
        return new RuntimeAllocationSnapshot(
            DateTimeOffset.UtcNow,
            GC.GetTotalAllocatedBytes(precise: false),
            memory.HeapSizeBytes,
            memory.FragmentedBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    public RuntimeAllocationDelta DeltaFrom(RuntimeAllocationSnapshot earlier)
        => new(
            Math.Max(0, TotalAllocatedBytes - earlier.TotalAllocatedBytes),
            HeapSizeBytes - earlier.HeapSizeBytes,
            FragmentedBytes - earlier.FragmentedBytes,
            Math.Max(0, Gen0Collections - earlier.Gen0Collections),
            Math.Max(0, Gen1Collections - earlier.Gen1Collections),
            Math.Max(0, Gen2Collections - earlier.Gen2Collections),
            CapturedAtUtc - earlier.CapturedAtUtc);
}

public readonly record struct RuntimeAllocationDelta(
    long AllocatedBytes,
    long HeapSizeDeltaBytes,
    long FragmentedBytesDelta,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    TimeSpan Elapsed)
{
    public double AllocatedMegabytes => AllocatedBytes / (1024d * 1024d);
    public double AllocatedMegabytesPerSecond =>
        Elapsed.TotalSeconds <= 0d ? 0d : AllocatedMegabytes / Elapsed.TotalSeconds;
}
