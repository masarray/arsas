using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Physical-relay FAT consumes Engineering's coalesced process image, never a second raw
/// runtime observer. LIVE VALUE is projected first on the Engineering UI flush. Evidence is
/// then published one Dispatcher turn later, below DataBind/Render priority, so Value 1/2 can
/// never become visible ahead of the process value that caused them.
/// </summary>
public partial class MainWindow
{
    private readonly Dictionary<string, StableFatProcessCursor> _p0FatSharedProcessCursors =
        new(StringComparer.OrdinalIgnoreCase);
    private IoTestMultiSessionCoordinator? _p0FatSharedProcessCoordinator;
    private bool _p0FatSharedProcessRouteAttached;

    internal void AttachIoFatSharedProcessEvidenceRoute(IoTestMultiSessionCoordinator coordinator)
    {
        ArgumentNullException.ThrowIfNull(coordinator);

        _p0FatSharedProcessCoordinator = coordinator;
        _p0FatSharedProcessCursors.Clear();

        // Primary/sibling legacy routes observe raw runtime frames before Engineering has
        // coalesced them. P0FatRuntimePointUpdated is another raw presentation observer.
        // Detach all three while FAT is open; the UI-flush route below is the single source.
        _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        _runtime.PointUpdated -= P0FatRuntimePointUpdated;

        if (_p0FatSharedProcessRouteAttached)
            return;

        _p0FatSharedProcessRouteAttached = true;
        _uiFlushTimer.Tick -= P0FatSharedProcessEvidence_Tick;
        _uiFlushTimer.Tick += P0FatSharedProcessEvidence_Tick;
    }

    internal void DetachIoFatSharedProcessEvidenceRoute(IoTestMultiSessionCoordinator coordinator)
    {
        if (!ReferenceEquals(_p0FatSharedProcessCoordinator, coordinator))
            return;

        _p0FatSharedProcessCoordinator = null;
        _p0FatSharedProcessCursors.Clear();

        if (!_p0FatSharedProcessRouteAttached)
            return;

        _uiFlushTimer.Tick -= P0FatSharedProcessEvidence_Tick;
        _p0FatSharedProcessRouteAttached = false;
    }

    private void P0FatSharedProcessEvidence_Tick(object? sender, EventArgs e)
    {
        var fat = _loadedIoFatWindow;
        var coordinator = _p0FatSharedProcessCoordinator;
        if (fat is not { IsLoaded: true } || coordinator == null)
            return;

        // UiFlushTimer_Tick was registered before this handler. device.Points therefore
        // already contains Engineering's exact visible process image for this frame.
        var pointIndex = GetP0FatPointIndex(fat.Project);
        var activeDeviceIds = coordinator.Project.Ieds
            .Where(coordinator.IsIedSessionActive)
            .Select(ResolveP0FatDevice)
            .Where(device => device != null)
            .Select(device => device!.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var publishAfterLiveCommit = new List<Iec61850EventEntry>();

        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                // Process-image projection is always first. PropertyChanged is raised here
                // on the Dispatcher that owns the FAT grid.
                ProjectSharedEngineeringPointToFat(pointIndex, point);

                var key = string.IsNullOrWhiteSpace(point.PointKey)
                    ? $"{point.DeviceId}|{IoTestLiveBindingService.NormalizeReference(point.IecReference)}"
                    : point.PointKey.Trim();
                if (key.Length == 0)
                    continue;

                _p0FatSharedProcessCursors.TryGetValue(key, out var previous);

                // Prime and continuously maintain the shared process cursor even while the
                // IED's FAT evidence session is inactive. Session.Start already captures its
                // baseline from these exact live points.
                if (!activeDeviceIds.Contains(device.DeviceId))
                {
                    if (previous == null ||
                        previous.Sequence != point.Sequence ||
                        !Iec61850MonitorPoint.AreSemanticallyEquivalent(previous.Value, point.Value) ||
                        !string.Equals(previous.Quality, point.Quality, StringComparison.Ordinal))
                    {
                        _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                            point.Sequence,
                            point.Value,
                            point.Quality);
                    }
                    continue;
                }

                if (previous != null &&
                    Iec61850MonitorPoint.AreSemanticallyEquivalent(previous.Value, point.Value) &&
                    string.Equals(previous.Quality, point.Quality, StringComparison.Ordinal))
                {
                    // Sequence-only transport churn must not create another FAT evidence job.
                    if (previous.Sequence != point.Sequence)
                    {
                        _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                            point.Sequence,
                            point.Value,
                            point.Quality);
                    }
                    continue;
                }

                var previousValue = previous?.Value ?? point.Value;
                _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                    point.Sequence,
                    point.Value,
                    point.Quality);

                publishAfterLiveCommit.Add(new Iec61850EventEntry
                {
                    Sequence = Interlocked.Increment(ref _ioTestObservationSequence),
                    DeviceId = point.DeviceId,
                    PointKey = point.PointKey,
                    DeviceTimestamp = point.DeviceTimestamp,
                    DeviceName = point.DeviceName,
                    IpAddress = point.IpAddress,
                    SignalName = point.SignalName,
                    IecReference = point.IecReference,
                    OldValue = previousValue,
                    NewValue = point.Value,
                    Quality = point.Quality,
                    SourceMode = point.SourceMode,
                    Reason = point.Reason
                });
            }
        }

        if (publishAfterLiveCommit.Count == 0)
            return;

        // WPF DataBind (8) and Render (7) both outrank Background (4). Defer evidence one
        // Dispatcher turn so the LIVE target has consumed the same process frame before
        // Value 1/Value 2 can advance. This removes the impossible visible state where
        // evidence=N while LIVE is still N-1, without adding another process-value writer.
        var routeOwner = coordinator;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!ReferenceEquals(_p0FatSharedProcessCoordinator, routeOwner) ||
                    _loadedIoFatWindow is not { IsLoaded: true })
                {
                    return;
                }

                foreach (var entry in publishAfterLiveCommit)
                {
                    routeOwner.PrimaryController.Enqueue(entry);
                    routeOwner.EnqueueAdditional(entry);
                }
            }),
            DispatcherPriority.Background);
    }

    private static void ProjectSharedEngineeringPointToFat(
        IReadOnlyDictionary<string, List<IoTestPointPlan>> pointIndex,
        Iec61850MonitorPoint point)
    {
        List<IoTestPointPlan>? plans = null;
        var pointKey = point.PointKey?.Trim() ?? string.Empty;
        if (pointKey.Length > 0)
            pointIndex.TryGetValue(pointKey, out plans);

        if (plans == null)
        {
            var fallback = P0FatKey(point.DeviceId, point.IecReference);
            if (fallback.Length > 0)
                pointIndex.TryGetValue(fallback, out plans);
        }

        if (plans == null)
            return;

        foreach (var plan in plans)
            ApplyP0FatLivePoint(plan.Runtime, point);
    }

    private sealed record StableFatProcessCursor(long Sequence, string Value, string Quality);
}
