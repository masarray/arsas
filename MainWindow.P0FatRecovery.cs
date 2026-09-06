using System.Collections.Concurrent;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P0 recovery guard anchored to Build #1888.
///
/// The runtime callback is deliberately WPF-free: it only coalesces immutable point
/// snapshots and schedules one Dispatcher drain. Every Window/DataGrid/project mutation
/// happens later on the Engineering Dispatcher. This keeps successful IEC 61850 control
/// operations independent from FAT presentation and prevents a UI projection exception
/// from being reported as a command failure.
/// </summary>
public partial class MainWindow
{
    private static readonly bool P0FatRecoveryClassHandlersRegistered = RegisterP0FatRecoveryClassHandlers();
    private readonly ConcurrentDictionary<string, Iec61850PointSnapshot> _p0FatLatestSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private int _p0FatDrainScheduled;
    private int _p0FatProjectionActive;
    private bool _p0FatRuntimeProjectionAttached;

    private static bool RegisterP0FatRecoveryClassHandlers()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0MainWindowLoaded));
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0FatWindowLoaded));
        return true;
    }

    private static void P0MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachP0FatRuntimeProjection();
    }

    private void AttachP0FatRuntimeProjection()
    {
        if (_p0FatRuntimeProjectionAttached)
            return;

        _p0FatRuntimeProjectionAttached = true;
        _runtime.PointUpdated += P0FatRuntimePointUpdated;
        Closed += P0MainWindowClosed;
    }

    private void P0MainWindowClosed(object? sender, EventArgs e)
    {
        Closed -= P0MainWindowClosed;
        Interlocked.Exchange(ref _p0FatProjectionActive, 0);
        _p0FatLatestSnapshots.Clear();

        if (!_p0FatRuntimeProjectionAttached)
            return;

        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _p0FatRuntimeProjectionAttached = false;
    }

    private static void P0FatWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow fat || fat.Owner is not MainWindow engineering)
            return;

        fat.Closed -= engineering.P0FatWindowClosed;
        fat.Closed += engineering.P0FatWindowClosed;
        Interlocked.Exchange(ref engineering._p0FatProjectionActive, 1);

        // A direct SCL FAT import has already populated the shared Engineering model.
        // Mark that model reusable so Engineering Play follows ConnectUsingSavedModelAsync
        // instead of opening a second live-discovery workflow.
        foreach (var device in engineering.Devices)
        {
            if (device.SclWorkspace != null && device.Signals.Count > 0)
                device.HasDiscoveryCache = true;
        }

        // Loaded runs before the first rendered frame. Copy/canonicalize the current image
        // and repair any exact static-DataSet-member -> resolved-runtime-leaf bridge before
        // the operator starts evidence capture.
        engineering.P0RefreshFatFromEngineeringImage(fat);
    }

    private void P0FatWindowClosed(object? sender, EventArgs e)
    {
        if (sender is not IoListTestingWindow fat)
            return;

        fat.Closed -= P0FatWindowClosed;
        SuspendIoFatRuntimeProjection(fat);
    }

    internal void SuspendIoFatRuntimeProjection(IoListTestingWindow fat)
    {
        if (_loadedIoFatWindow != null && !ReferenceEquals(_loadedIoFatWindow, fat))
            return;

        Interlocked.Exchange(ref _p0FatProjectionActive, 0);
        _p0FatLatestSnapshots.Clear();
    }

    internal void ResumeIoFatRuntimeProjection(IoListTestingWindow fat)
    {
        if (_loadedIoFatWindow != null && !ReferenceEquals(_loadedIoFatWindow, fat))
            return;

        Interlocked.Exchange(ref _p0FatProjectionActive, 1);
        P0RefreshFatFromEngineeringImage(fat);
    }

    /// <summary>
    /// Runtime worker-thread callback. Never touch Window.IsLoaded, visual-tree objects,
    /// DataGrid rows, bindings, or any other DispatcherObject here. A presentation failure
    /// must never escape this observer and change the outcome of an IEC 61850 command.
    /// </summary>
    private void P0FatRuntimePointUpdated(Iec61850PointSnapshot snapshot)
    {
        if (Volatile.Read(ref _p0FatProjectionActive) == 0)
            return;

        try
        {
            var key = snapshot.Point.PointKey?.Trim() ?? string.Empty;
            if (key.Length == 0)
                key = P0FatKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
            if (key.Length == 0)
                return;

            _p0FatLatestSnapshots[key] = snapshot;
            if (Interlocked.Exchange(ref _p0FatDrainScheduled, 1) != 0)
                return;

            try
            {
                Dispatcher.BeginInvoke(new Action(P0DrainFatRuntimeProjection), DispatcherPriority.DataBind);
            }
            catch (InvalidOperationException)
            {
                // Dispatcher shutdown/teardown is presentation-only. Never rethrow through
                // the runtime PointUpdated multicast event into control/acquisition code.
                Interlocked.Exchange(ref _p0FatDrainScheduled, 0);
                _p0FatLatestSnapshots.Clear();
            }
        }
        catch
        {
            // This observer is intentionally fail-isolated. Runtime acquisition and control
            // remain authoritative even if a malformed presentation snapshot is dropped.
            Interlocked.Exchange(ref _p0FatDrainScheduled, 0);
        }
    }

    private void P0DrainFatRuntimeProjection()
    {
        try
        {
            if (Volatile.Read(ref _p0FatProjectionActive) == 0 ||
                _loadedIoFatWindow is not { IsLoaded: true } fat)
            {
                _p0FatLatestSnapshots.Clear();
                return;
            }

            var latest = _p0FatLatestSnapshots.ToArray();
            _p0FatLatestSnapshots.Clear();
            if (latest.Length == 0)
                return;

            // Resolve against the exact live-point identity on the Dispatcher. This also
            // bridges an engine-owned structured static member (for example MMXU A.phsA)
            // to its one resolved scalar runtime ObjectReference when that bridge is unique.
            var index = BuildP0FatPointIndex(fat.Project);
            foreach (var pair in latest)
            {
                if (!index.TryGetValue(pair.Key, out var plans))
                {
                    var snapshotFallback = P0FatKey(pair.Value.Point.DeviceId, pair.Value.Point.IecReference);
                    if (snapshotFallback.Length == 0 || !index.TryGetValue(snapshotFallback, out plans))
                        continue;
                }

                foreach (var plan in plans)
                    ApplyP0FatSnapshot(plan.Runtime, pair.Value);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _p0FatDrainScheduled, 0);
            if (Volatile.Read(ref _p0FatProjectionActive) != 0 &&
                !_p0FatLatestSnapshots.IsEmpty &&
                Interlocked.Exchange(ref _p0FatDrainScheduled, 1) == 0)
            {
                try
                {
                    Dispatcher.BeginInvoke(new Action(P0DrainFatRuntimeProjection), DispatcherPriority.DataBind);
                }
                catch (InvalidOperationException)
                {
                    Interlocked.Exchange(ref _p0FatDrainScheduled, 0);
                    _p0FatLatestSnapshots.Clear();
                }
            }
        }
    }

    private void P0RefreshFatFromEngineeringImage(IoListTestingWindow fat)
    {
        var index = BuildP0FatPointIndex(fat.Project);
        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                var pointKey = point.PointKey?.Trim() ?? string.Empty;
                var fallbackKey = P0FatKey(device.DeviceId, point.IecReference);
                List<IoTestPointPlan>? plans = null;
                if (pointKey.Length > 0)
                    index.TryGetValue(pointKey, out plans);
                if (plans == null && fallbackKey.Length > 0)
                    index.TryGetValue(fallbackKey, out plans);
                if (plans == null)
                    continue;

                foreach (var plan in plans)
                {
                    var dedupe = plan.TestPointId + "|" + pointKey + "|" + fallbackKey;
                    if (!applied.Add(dedupe))
                        continue;
                    ApplyP0FatLivePoint(plan.Runtime, point);
                }
            }
        }
    }

    private Dictionary<string, List<IoTestPointPlan>> BuildP0FatPointIndex(IoTestProject project)
    {
        var index = new Dictionary<string, List<IoTestPointPlan>>(StringComparer.OrdinalIgnoreCase);
        foreach (var ied in project.Ieds)
        {
            var device = ResolveP0FatDevice(ied);
            if (device == null)
                continue;

            foreach (var plan in ied.TestPoints)
            {
                var livePoint = ResolveP0FatLivePoint(plan, device);
                if (livePoint == null)
                    continue;

                if (plan.LiveBindingState != IoTestLiveBindingState.LivePointReady ||
                    !string.Equals(plan.LiveDeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(
                        IoTestLiveBindingService.NormalizeReference(plan.LiveSignalReference),
                        IoTestLiveBindingService.NormalizeReference(livePoint.IecReference),
                        StringComparison.OrdinalIgnoreCase))
                {
                    plan.ApplyLiveBinding(
                        IoTestLiveBindingState.LivePointReady,
                        "Unique shared Engineering live point resolved from the exact FAT/static DataSet runtime identity.",
                        device.DeviceId,
                        livePoint.IecReference);
                }

                AddP0FatIndexKey(index, livePoint.PointKey, plan);
                AddP0FatIndexKey(index, P0FatKey(device.DeviceId, livePoint.IecReference), plan);
            }
        }
        return index;
    }

    private Iec61850MonitorDevice? ResolveP0FatDevice(IoTestIedPlan ied)
    {
        if (!string.IsNullOrWhiteSpace(ied.LiveDeviceId))
        {
            var byId = Devices.FirstOrDefault(device =>
                string.Equals(device.DeviceId, ied.LiveDeviceId, StringComparison.OrdinalIgnoreCase));
            if (byId != null)
                return byId;
        }

        var exact = Devices.FirstOrDefault(device =>
            (string.Equals(device.Name, ied.IedName, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(device.SclIedName, ied.IedName, StringComparison.OrdinalIgnoreCase)) &&
            string.Equals(device.IpAddress, ied.IpAddress, StringComparison.OrdinalIgnoreCase));
        if (exact != null)
            return exact;

        var byIp = Devices.Where(device =>
            string.Equals(device.IpAddress, ied.IpAddress, StringComparison.OrdinalIgnoreCase)).ToArray();
        return byIp.Length == 1 ? byIp[0] : null;
    }

    private static Iec61850MonitorPoint? ResolveP0FatLivePoint(
        IoTestPointPlan plan,
        Iec61850MonitorDevice device)
    {
        var liveReference = IoTestLiveBindingService.NormalizeReference(plan.LiveSignalReference);
        if (liveReference.Length > 0)
        {
            var liveMatches = device.Points.Where(point =>
                string.Equals(
                    IoTestLiveBindingService.NormalizeReference(point.IecReference),
                    liveReference,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            if (liveMatches.Length == 1)
                return liveMatches[0];
        }

        var imported = IoTestLiveBindingService.ImportedReferences(plan)
            .Select(IoTestLiveBindingService.NormalizeReference)
            .Where(reference => reference.Length > 0)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (imported.Count == 0)
            return null;

        var direct = device.Points.Where(point =>
            imported.Contains(IoTestLiveBindingService.NormalizeReference(point.IecReference))).ToArray();
        if (direct.Length == 1)
            return direct[0];

        // Structured static DataSet members are presentation identities, while ARIEC may
        // resolve their readable primary value to a scalar runtime leaf. Bridge only when
        // the exact DisplayReference identifies one unique runtime ObjectReference.
        var runtimeReferences = device.Signals
            .Where(signal => !signal.IsControlSignal)
            .Where(signal => imported.Contains(
                IoTestLiveBindingService.NormalizeReference(signal.DisplayReference)))
            .Select(signal => IoTestLiveBindingService.NormalizeReference(signal.ObjectReference))
            .Where(reference => reference.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (runtimeReferences.Length != 1)
            return null;

        var bridged = device.Points.Where(point =>
            string.Equals(
                IoTestLiveBindingService.NormalizeReference(point.IecReference),
                runtimeReferences[0],
                StringComparison.OrdinalIgnoreCase)).ToArray();
        return bridged.Length == 1 ? bridged[0] : null;
    }

    private static void AddP0FatIndexKey(
        IDictionary<string, List<IoTestPointPlan>> index,
        string? key,
        IoTestPointPlan plan)
    {
        var normalized = key?.Trim() ?? string.Empty;
        if (normalized.Length == 0)
            return;
        if (!index.TryGetValue(normalized, out var bucket))
        {
            bucket = new List<IoTestPointPlan>();
            index[normalized] = bucket;
        }
        if (!bucket.Contains(plan))
            bucket.Add(plan);
    }

    private static void ApplyP0FatSnapshot(IoTestPointRuntime runtime, Iec61850PointSnapshot snapshot)
    {
        var value = P0CanonicalLiveValue(snapshot.Value);
        if (!string.Equals(runtime.CurrentValue, value, StringComparison.Ordinal))
            runtime.CurrentValue = value;

        var quality = string.IsNullOrWhiteSpace(snapshot.Quality) ? "Unknown" : snapshot.Quality.Trim();
        if (!string.Equals(runtime.CurrentQuality, quality, StringComparison.Ordinal))
            runtime.CurrentQuality = quality;

        var source = string.IsNullOrWhiteSpace(snapshot.SourceMode) ? "-" : snapshot.SourceMode.Trim();
        if (!string.Equals(runtime.CurrentSource, source, StringComparison.Ordinal))
            runtime.CurrentSource = source;

        var timestamp = string.IsNullOrWhiteSpace(snapshot.DeviceTimestamp) || snapshot.DeviceTimestamp == "-"
            ? "—"
            : snapshot.DeviceTimestamp.Trim();
        if (!string.Equals(runtime.CurrentIedTimestamp, timestamp, StringComparison.Ordinal))
            runtime.CurrentIedTimestamp = timestamp;
    }

    private static void ApplyP0FatLivePoint(IoTestPointRuntime runtime, Iec61850MonitorPoint point)
    {
        var value = P0CanonicalLiveValue(point.Value);
        if (!string.Equals(runtime.CurrentValue, value, StringComparison.Ordinal))
            runtime.CurrentValue = value;
        if (!string.Equals(runtime.CurrentQuality, point.Quality, StringComparison.Ordinal))
            runtime.CurrentQuality = string.IsNullOrWhiteSpace(point.Quality) ? "Unknown" : point.Quality.Trim();
        if (!string.Equals(runtime.CurrentSource, point.SourceMode, StringComparison.Ordinal))
            runtime.CurrentSource = string.IsNullOrWhiteSpace(point.SourceMode) ? "-" : point.SourceMode.Trim();
        var timestamp = string.IsNullOrWhiteSpace(point.DeviceTimestamp) || point.DeviceTimestamp == "-"
            ? "—"
            : point.DeviceTimestamp.Trim();
        if (!string.Equals(runtime.CurrentIedTimestamp, timestamp, StringComparison.Ordinal))
            runtime.CurrentIedTimestamp = timestamp;
    }

    internal static string P0CanonicalLiveValue(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        return bool.TryParse(text, out var boolean)
            ? boolean ? "True" : "False"
            : text;
    }

    private static string P0FatKey(string? deviceId, string? reference)
    {
        var id = (deviceId ?? string.Empty).Trim();
        var normalized = IoTestLiveBindingService.NormalizeReference(reference);
        return id.Length == 0 || normalized.Length == 0
            ? string.Empty
            : id + "|" + normalized;
    }
}
