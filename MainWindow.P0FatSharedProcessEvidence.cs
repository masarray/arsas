using System.Collections.Concurrent;
using System.Threading;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// One physical-relay process-frame route for FAT. The exact runtime PointUpdated snapshot is
/// queued on the worker callback, then one DataBind-priority Dispatcher drain commits LIVE VALUE
/// to every mapped FAT row first and publishes Value 1 / Value 2 evidence from that same snapshot
/// immediately afterwards. FAT evidence no longer waits for Engineering's 200 ms UI flush and
/// therefore cannot visibly advance ahead of LIVE VALUE.
/// </summary>
public partial class MainWindow
{
    private readonly ConcurrentQueue<Iec61850PointSnapshot> _p0FatAtomicSnapshots = new();
    private readonly Dictionary<string, StableFatProcessCursor> _p0FatSharedProcessCursors =
        new(StringComparer.OrdinalIgnoreCase);
    private IoTestMultiSessionCoordinator? _p0FatSharedProcessCoordinator;
    private bool _p0FatSharedProcessRouteAttached;
    private int _p0FatAtomicDrainScheduled;

    internal void AttachIoFatSharedProcessEvidenceRoute(IoTestMultiSessionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        _p0FatSharedProcessCoordinator = coordinator;
        _p0FatSharedProcessCursors.Clear();
        while (_p0FatAtomicSnapshots.TryDequeue(out _)) { }

        // The FAT session owns one report-frame route. Legacy evidence observers and the older
        // presentation-only mirror are detached so no second writer or slower 200 ms path can
        // race this atomic LIVE -> evidence commit.
        _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _runtime.PointUpdated -= P0FatAtomicPointUpdated;
        _runtime.PointUpdated += P0FatAtomicPointUpdated;

        _p0FatSharedProcessRouteAttached = true;
    }

    internal void DetachIoFatSharedProcessEvidenceRoute(IoTestMultiSessionCoordinator coordinator)
    {
        if (!ReferenceEquals(_p0FatSharedProcessCoordinator, coordinator))
            return;

        _p0FatSharedProcessCoordinator = null;
        _p0FatSharedProcessCursors.Clear();
        while (_p0FatAtomicSnapshots.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 0);

        _runtime.PointUpdated -= P0FatAtomicPointUpdated;

        // FAT may remain open after Stop. Restore the presentation-only live mirror so the
        // workspace continues showing the relay process image while no evidence session runs.
        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _runtime.PointUpdated += P0FatRuntimePointUpdated;
        _p0FatSharedProcessRouteAttached = false;
    }

    /// <summary>
    /// Runtime worker callback: queue immutable snapshots only. Never touch WPF here.
    /// </summary>
    private void P0FatAtomicPointUpdated(Iec61850PointSnapshot snapshot)
    {
        if (Volatile.Read(ref _p0FatProjectionActive) == 0 || !_p0FatSharedProcessRouteAttached)
            return;

        _p0FatAtomicSnapshots.Enqueue(snapshot);
        if (Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 1) != 0)
            return;

        try
        {
            Dispatcher.BeginInvoke(new Action(P0DrainAtomicFatProcessFrames), DispatcherPriority.DataBind);
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 0);
            while (_p0FatAtomicSnapshots.TryDequeue(out _)) { }
        }
    }

    private void P0DrainAtomicFatProcessFrames()
    {
        try
        {
            var fat = _loadedIoFatWindow;
            var coordinator = _p0FatSharedProcessCoordinator;
            if (Volatile.Read(ref _p0FatProjectionActive) == 0 ||
                fat is not { IsLoaded: true } ||
                coordinator == null)
            {
                while (_p0FatAtomicSnapshots.TryDequeue(out _)) { }
                return;
            }

            var pointIndex = GetP0FatPointIndex(fat.Project);
            var activeDeviceIds = coordinator.Project.Ieds
                .Where(coordinator.IsIedSessionActive)
                .Select(ResolveP0FatDevice)
                .Where(device => device != null)
                .Select(device => device!.DeviceId)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var processed = 0;
            while (processed < 4096 && _p0FatAtomicSnapshots.TryDequeue(out var snapshot))
            {
                processed++;
                var point = snapshot.Point;
                var key = string.IsNullOrWhiteSpace(point.PointKey)
                    ? P0FatKey(point.DeviceId, point.IecReference)
                    : point.PointKey.Trim();
                if (key.Length == 0)
                    continue;

                var plans = ResolveAtomicFatPlans(pointIndex, key, snapshot);
                if (plans.Count == 0)
                {
                    pointIndex = GetP0FatPointIndex(fat.Project, forceRebuild: true);
                    plans = ResolveAtomicFatPlans(pointIndex, key, snapshot);
                }
                if (plans.Count == 0)
                    continue;

                // Atomic frame contract: this is the first mutation made from the report
                // snapshot. The properties bound by LIVE VALUE are committed before any
                // capture controller sees the observation.
                foreach (var plan in plans)
                    ApplyP0FatSnapshot(plan.Runtime, snapshot);

                _p0FatSharedProcessCursors.TryGetValue(key, out var previous);
                var currentQuality = string.IsNullOrWhiteSpace(snapshot.Quality)
                    ? "Unknown"
                    : snapshot.Quality.Trim();

                if (!activeDeviceIds.Contains(point.DeviceId))
                {
                    _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                        snapshot.Sequence,
                        snapshot.Value,
                        currentQuality);
                    continue;
                }

                if (previous != null &&
                    Iec61850MonitorPoint.AreSemanticallyEquivalent(previous.Value, snapshot.Value) &&
                    string.Equals(previous.Quality, currentQuality, StringComparison.Ordinal))
                {
                    if (previous.Sequence != snapshot.Sequence)
                    {
                        _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                            snapshot.Sequence,
                            snapshot.Value,
                            currentQuality);
                    }
                    continue;
                }

                var previousValue = previous?.Value;
                if (string.IsNullOrWhiteSpace(previousValue))
                    previousValue = string.IsNullOrWhiteSpace(snapshot.PreviousValue) ? snapshot.Value : snapshot.PreviousValue;

                _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                    snapshot.Sequence,
                    snapshot.Value,
                    currentQuality);

                if (!IsAtomicFatLiveCommitCurrent(plans, snapshot))
                    continue;

                var entry = new Iec61850EventEntry
                {
                    Sequence = Interlocked.Increment(ref _ioTestObservationSequence),
                    DeviceId = point.DeviceId,
                    PointKey = point.PointKey,
                    DeviceTimestamp = snapshot.DeviceTimestamp,
                    DeviceName = point.DeviceName,
                    IpAddress = point.IpAddress,
                    SignalName = point.SignalName,
                    IecReference = point.IecReference,
                    OldValue = previousValue,
                    NewValue = snapshot.Value,
                    Quality = currentQuality,
                    SourceMode = snapshot.SourceMode,
                    Reason = snapshot.Reason
                };

                // Same Dispatcher turn, same report snapshot, LIVE already committed above.
                coordinator.PrimaryController.Enqueue(entry);
                coordinator.EnqueueAdditional(entry);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 0);
            if (_p0FatSharedProcessRouteAttached &&
                !_p0FatAtomicSnapshots.IsEmpty &&
                Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 1) == 0)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(P0DrainAtomicFatProcessFrames), DispatcherPriority.DataBind);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _p0FatAtomicDrainScheduled, 0);
                    while (_p0FatAtomicSnapshots.TryDequeue(out _)) { }
                }
            }
        }
    }

    private static IReadOnlyList<IoTestPointPlan> ResolveAtomicFatPlans(
        IReadOnlyDictionary<string, List<IoTestPointPlan>> pointIndex,
        string key,
        Iec61850PointSnapshot snapshot)
    {
        if (pointIndex.TryGetValue(key, out var plans) && plans.Count > 0)
            return plans;

        var fallback = P0FatKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
        return fallback.Length > 0 && pointIndex.TryGetValue(fallback, out plans) && plans.Count > 0
            ? plans
            : Array.Empty<IoTestPointPlan>();
    }

    private static bool IsAtomicFatLiveCommitCurrent(
        IReadOnlyList<IoTestPointPlan> plans,
        Iec61850PointSnapshot snapshot)
    {
        var expectedQuality = string.IsNullOrWhiteSpace(snapshot.Quality)
            ? "Unknown"
            : snapshot.Quality.Trim();

        foreach (var plan in plans)
        {
            if (!Iec61850MonitorPoint.AreSemanticallyEquivalent(plan.Runtime.CurrentValue, snapshot.Value))
                return false;
            if (!string.Equals(plan.Runtime.CurrentQuality, expectedQuality, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private sealed record StableFatProcessCursor(long Sequence, string Value, string Quality);
}
