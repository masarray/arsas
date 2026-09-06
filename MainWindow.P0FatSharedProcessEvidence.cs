using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Physical-relay FAT must be a consumer of Engineering's process image, not a second raw
/// runtime observer. Engineering already coalesces runtime traffic, applies the CSWI position
/// stability guard, and updates device.Points on its UI flush. FAT samples that exact image
/// immediately after the Engineering flush, so LIVE VALUE and evidence use one authority.
///
/// This also removes two high-rate raw PointUpdated consumers while FAT is open. A 57-point
/// relay therefore produces one coalesced shared-image pass per Engineering UI frame instead
/// of three independent WPF projection/evidence pipelines.
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

        // UiFlushTimer_Tick was registered in MainWindow's constructor. This handler is
        // appended later when FAT opens, therefore device.Points already contains the exact
        // value visible in Engineering for this frame.
        var pointIndex = GetP0FatPointIndex(fat.Project);
        var activeDeviceIds = coordinator.Project.Ieds
            .Where(coordinator.IsIedSessionActive)
            .Select(ResolveP0FatDevice)
            .Where(device => device != null)
            .Select(device => device!.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                ProjectSharedEngineeringPointToFat(pointIndex, point);

                if (!activeDeviceIds.Contains(device.DeviceId))
                    continue;

                var key = string.IsNullOrWhiteSpace(point.PointKey)
                    ? $"{point.DeviceId}|{IoTestLiveBindingService.NormalizeReference(point.IecReference)}"
                    : point.PointKey.Trim();
                if (key.Length == 0)
                    continue;

                _p0FatSharedProcessCursors.TryGetValue(key, out var previous);
                if (previous != null &&
                    previous.Sequence == point.Sequence &&
                    string.Equals(previous.Value, point.Value, StringComparison.Ordinal) &&
                    string.Equals(previous.Quality, point.Quality, StringComparison.Ordinal))
                {
                    continue;
                }

                var previousValue = previous?.Value ?? point.Value;
                _p0FatSharedProcessCursors[key] = new StableFatProcessCursor(
                    point.Sequence,
                    point.Value,
                    point.Quality);

                var entry = new Iec61850EventEntry
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
                };

                coordinator.PrimaryController.Enqueue(entry);
                coordinator.EnqueueAdditional(entry);
            }
        }
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
