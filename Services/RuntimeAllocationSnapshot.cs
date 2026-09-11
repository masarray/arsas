using System.Diagnostics;

namespace ArIED61850Tester.Services;

/// <summary>
/// Low-overhead allocation/GC snapshot for field diagnostics and regression baselines.
/// Capturing a snapshot does not force a collection and is safe on the monitoring path.
/// </summary>
public readonly record struct RuntimeAllocationSnapshot(
    DateTimeOffset CapturedAtUtc,
    long MonotonicTimestamp,
    long TotalAllocatedBytes,
    long CurrentManagedBytes,
    long LastGcHeapSizeBytes,
    long LastGcFragmentedBytes,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections)
{
    public static RuntimeAllocationSnapshot Capture()
    {
        var monotonicTimestamp = Stopwatch.GetTimestamp();
        var memory = GC.GetGCMemoryInfo();
        return new RuntimeAllocationSnapshot(
            DateTimeOffset.UtcNow,
            monotonicTimestamp,
            GC.GetTotalAllocatedBytes(precise: false),
            GC.GetTotalMemory(forceFullCollection: false),
            memory.HeapSizeBytes,
            memory.FragmentedBytes,
            GC.CollectionCount(0),
            GC.CollectionCount(1),
            GC.CollectionCount(2));
    }

    public RuntimeAllocationDelta DeltaFrom(RuntimeAllocationSnapshot earlier)
    {
        var elapsed = MonotonicTimestamp > 0 &&
                      earlier.MonotonicTimestamp > 0 &&
                      MonotonicTimestamp >= earlier.MonotonicTimestamp
            ? Stopwatch.GetElapsedTime(earlier.MonotonicTimestamp, MonotonicTimestamp)
            : TimeSpan.Zero;

        return new RuntimeAllocationDelta(
            Math.Max(0, TotalAllocatedBytes - earlier.TotalAllocatedBytes),
            CurrentManagedBytes - earlier.CurrentManagedBytes,
            LastGcHeapSizeBytes - earlier.LastGcHeapSizeBytes,
            LastGcFragmentedBytes - earlier.LastGcFragmentedBytes,
            Math.Max(0, Gen0Collections - earlier.Gen0Collections),
            Math.Max(0, Gen1Collections - earlier.Gen1Collections),
            Math.Max(0, Gen2Collections - earlier.Gen2Collections),
            elapsed);
    }
}

public readonly record struct RuntimeAllocationDelta(
    long AllocatedBytes,
    long CurrentManagedBytesDelta,
    long LastGcHeapSizeDeltaBytes,
    long LastGcFragmentedBytesDelta,
    int Gen0Collections,
    int Gen1Collections,
    int Gen2Collections,
    TimeSpan Elapsed)
{
    public double AllocatedMegabytes => AllocatedBytes / (1024d * 1024d);
    public double AllocatedMegabytesPerSecond =>
        Elapsed.TotalSeconds <= 0d ? 0d : AllocatedMegabytes / Elapsed.TotalSeconds;
}
