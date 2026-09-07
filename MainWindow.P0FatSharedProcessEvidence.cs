using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Physical-relay FAT keeps two deliberately different responsibilities:
/// 1) operator-facing LIVE VALUE is a presentation-only mirror of the newest runtime
///    PointUpdated snapshot, drained at DataBind priority; and
/// 2) Value 1 / Value 2 evidence is authorized only from Engineering's coalesced process
///    image. Evidence is emitted only after the exact same value is already committed to
///    every mapped FAT LIVE row.
///
/// This separation removes the field regression where evidence could visibly advance while
/// LIVE VALUE waited for the slower 200 ms Engineering UI flush. The raw mirror never writes
/// evidence and the evidence controllers never write CurrentValue.
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

        // Primary/sibling legacy evidence routes observe raw runtime frames before
        // Engineering has coalesced them, so they stay detached. P0FatRuntimePointUpdated is
        // different: it is presentation-only and updates Runtime.CurrentValue/quality/source;
        // keep exactly one subscription so FAT LIVE follows the report immediately rather
        // than waiting for the Engineering 200 ms UI-flush cadence.
        _runtime.PointUpdated -= Runtime_IoTestPointUpdated;
        _runtime.PointUpdated -= Runtime_IoTestAdditionalPointUpdated;
        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _runtime.PointUpdated += P0FatRuntimePointUpdated;

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
        // already contains Engineering's exact visible process image for this frame. The
        // presentation-only raw mirror may already have shown the same value in FAT; this
        // pass remains the evidence authority and reconfirms the committed process image.
        var pointIndex = GetP0FatPointIndex(fat.Project);
        var activeDeviceIds = coordinator.Project.Ieds
            .Where(coordinator.IsIedSessionActive)
            .Select(ResolveP0FatDevice)
            .Where(device => device != null)
            .Select(device => device!.DeviceId)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var publishAfterLiveCommit = new List<CommittedFatProcessEvent>();

        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                // Resolve and commit the operator-facing LIVE row first. Evidence is not
                // allowed to advance when a point cannot be mapped to its FAT row; showing
                // Value2=N while LIVE=N-1 is an invalid process image and must fail closed.
                var projectedPlans = ProjectSharedEngineeringPointToFat(pointIndex, point);

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

                // An active FAT session may have a valid evidence binding even if a stale UI
                // projection index was built before the live point became ready. Rebuild once
                // before giving up; never publish evidence against an unmapped LIVE row.
                if (projectedPlans.Count == 0)
                {
                    pointIndex = GetP0FatPointIndex(fat.Project, forceRebuild: true);
                    projectedPlans = ProjectSharedEngineeringPointToFat(pointIndex, point);
                }
                if (projectedPlans.Count == 0)
                    continue;

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
                publishAfterLiveCommit.Add(new CommittedFatProcessEvent(entry, projectedPlans));
            }
        }

        if (publishAfterLiveCommit.Count == 0)
            return;

        // WPF DataBind (8) and Render (7) both outrank Background (4). Defer evidence one
        // Dispatcher turn, then verify the actual runtime property bound by LIVE VALUE still
        // contains the same process value. This is a frame barrier rather than a timing guess.
        var routeOwner = coordinator;
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                if (!ReferenceEquals(_p0FatSharedProcessCoordinator, routeOwner) ||
                    _loadedIoFatWindow is not { IsLoaded: true })
                {
                    return;
                }

                foreach (var committed in publishAfterLiveCommit)
                {
                    if (!IsFatLiveCommitCurrent(committed))
                        continue;

                    routeOwner.PrimaryController.Enqueue(committed.Entry);
                    routeOwner.EnqueueAdditional(committed.Entry);
                }
            }),
            DispatcherPriority.Background);
    }

    private static IReadOnlyList<IoTestPointPlan> ProjectSharedEngineeringPointToFat(
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

        if (plans == null || plans.Count == 0)
            return Array.Empty<IoTestPointPlan>();

        foreach (var plan in plans)
            ApplyP0FatLivePoint(plan.Runtime, point);
        return plans;
    }

    private static bool IsFatLiveCommitCurrent(CommittedFatProcessEvent committed)
    {
        foreach (var plan in committed.Plans)
        {
            if (!Iec61850MonitorPoint.AreSemanticallyEquivalent(
                    plan.Runtime.CurrentValue,
                    committed.Entry.NewValue))
            {
                return false;
            }

            var expectedQuality = string.IsNullOrWhiteSpace(committed.Entry.Quality)
                ? "Unknown"
                : committed.Entry.Quality.Trim();
            if (!string.Equals(plan.Runtime.CurrentQuality, expectedQuality, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    private sealed record StableFatProcessCursor(long Sequence, string Value, string Quality);
    private sealed record CommittedFatProcessEvent(
        Iec61850EventEntry Entry,
        IReadOnlyList<IoTestPointPlan> Plans);
}
