using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private const int IoFatRuntimeProjectionDrainLimit = 128;
    private static readonly bool IoFatRuntimeAuthorityRegistered = RegisterIoFatRuntimeAuthority();

    private readonly ConcurrentDictionary<string, Iec61850PointSnapshot> _ioFatPendingRuntimeSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<IoTestPointPlan>> _ioFatRuntimePointIndex =
        new(StringComparer.OrdinalIgnoreCase);
    private IoTestProject? _ioFatRuntimeIndexedProject;
    private int _ioFatRuntimeIndexedPointCount = -1;
    private int _ioFatRuntimeProjectionScheduled;
    private bool _ioFatRuntimeAuthorityAttached;

    private static bool RegisterIoFatRuntimeAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(IoFatRuntimeAuthority_Loaded));
        return true;
    }

    private static void IoFatRuntimeAuthority_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is MainWindow window)
            window.AttachIoFatRuntimeAuthority();
    }

    private void AttachIoFatRuntimeAuthority()
    {
        if (_ioFatRuntimeAuthorityAttached)
            return;

        _ioFatRuntimeAuthorityAttached = true;
        _runtime.PointUpdated += Runtime_IoFatRuntimeAuthorityPointUpdated;
        Closed += IoFatRuntimeAuthorityWindow_Closed;
    }

    private void IoFatRuntimeAuthorityWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= IoFatRuntimeAuthorityWindow_Closed;
        _runtime.PointUpdated -= Runtime_IoFatRuntimeAuthorityPointUpdated;
        _ioFatPendingRuntimeSnapshots.Clear();
        _ioFatRuntimePointIndex.Clear();
        _ioFatRuntimeIndexedProject = null;
        _ioFatRuntimeIndexedPointCount = -1;
        _ioFatRuntimeAuthorityAttached = false;
    }

    private void Runtime_IoFatRuntimeAuthorityPointUpdated(Iec61850PointSnapshot snapshot)
    {
        if (snapshot?.Point == null || _ioFatSelectionBridgeProject == null)
            return;

        var key = IoFatRuntimeKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
        if (key.Length == 0)
            return;

        _ioFatPendingRuntimeSnapshots.AddOrUpdate(
            key,
            snapshot,
            (_, current) => snapshot.Sequence >= current.Sequence ? snapshot : current);
        ScheduleIoFatRuntimeProjection();
    }

    private void ScheduleIoFatRuntimeProjection()
    {
        if (Interlocked.Exchange(ref _ioFatRuntimeProjectionScheduled, 1) != 0)
            return;

        Dispatcher.BeginInvoke(
            new Action(DrainIoFatRuntimeProjection),
            DispatcherPriority.Background);
    }

    private void DrainIoFatRuntimeProjection()
    {
        try
        {
            EnsureIoFatRuntimePointIndex();
            var processed = 0;
            while (processed < IoFatRuntimeProjectionDrainLimit)
            {
                var key = _ioFatPendingRuntimeSnapshots.Keys.FirstOrDefault();
                if (key == null || !_ioFatPendingRuntimeSnapshots.TryRemove(key, out var snapshot))
                    break;

                ApplyIoFatRuntimeSnapshot(key, snapshot);
                processed++;
            }
        }
        finally
        {
            Interlocked.Exchange(ref _ioFatRuntimeProjectionScheduled, 0);
            if (!_ioFatPendingRuntimeSnapshots.IsEmpty && _ioFatSelectionBridgeProject != null)
                ScheduleIoFatRuntimeProjection();
        }
    }

    private void EnsureIoFatRuntimePointIndex()
    {
        var project = _ioFatSelectionBridgeProject;
        if (project == null)
        {
            _ioFatRuntimePointIndex.Clear();
            _ioFatRuntimeIndexedProject = null;
            _ioFatRuntimeIndexedPointCount = -1;
            return;
        }

        var pointCount = project.Ieds.Sum(ied => ied.TestPoints.Count);
        if (ReferenceEquals(project, _ioFatRuntimeIndexedProject) &&
            pointCount == _ioFatRuntimeIndexedPointCount)
        {
            return;
        }

        _ioFatRuntimePointIndex.Clear();
        foreach (var ied in project.Ieds)
        {
            var device = ResolveIoTestDevice(ied.LiveDeviceId)
                         ?? ResolveIoTestDevice(ied.IpAddress)
                         ?? ResolveIoTestDevice(ied.IedName);
            if (device == null)
                continue;

            foreach (var point in ied.TestPoints)
            {
                foreach (var reference in IoTestLiveBindingService.ImportedReferences(point))
                    AddIoFatRuntimePointIndex(device.DeviceId, reference, point);

                if (!string.IsNullOrWhiteSpace(point.LiveSignalReference))
                    AddIoFatRuntimePointIndex(device.DeviceId, point.LiveSignalReference, point);
            }
        }

        _ioFatRuntimeIndexedProject = project;
        _ioFatRuntimeIndexedPointCount = pointCount;
    }

    private void AddIoFatRuntimePointIndex(string? deviceId, string? reference, IoTestPointPlan point)
    {
        var key = IoFatRuntimeKey(deviceId, reference);
        if (key.Length == 0)
            return;

        if (!_ioFatRuntimePointIndex.TryGetValue(key, out var points))
        {
            points = new List<IoTestPointPlan>();
            _ioFatRuntimePointIndex[key] = points;
        }

        if (!points.Contains(point))
            points.Add(point);
    }

    private void ApplyIoFatRuntimeSnapshot(string key, Iec61850PointSnapshot snapshot)
    {
        if (!_ioFatRuntimePointIndex.TryGetValue(key, out var points))
            return;

        var value = IoFatValuePresentation.Canonicalize(snapshot.Value);
        var quality = string.IsNullOrWhiteSpace(snapshot.Quality) ? "Unknown" : snapshot.Quality.Trim();
        var source = string.IsNullOrWhiteSpace(snapshot.SourceMode) ? "Unknown" : snapshot.SourceMode.Trim();
        var iedTimestamp = string.IsNullOrWhiteSpace(snapshot.DeviceTimestamp) || snapshot.DeviceTimestamp == "-"
            ? "—"
            : snapshot.DeviceTimestamp.Trim();

        foreach (var point in points)
        {
            point.Runtime.CurrentValue = value;
            point.Runtime.CurrentQuality = quality;
            point.Runtime.CurrentSource = source;
            point.Runtime.CurrentIedTimestamp = iedTimestamp;
        }
    }

    private static string IoFatRuntimeKey(string? deviceId, string? reference)
    {
        var id = (deviceId ?? string.Empty).Trim();
        var normalizedReference = IoTestLiveBindingService.NormalizeReference(reference);
        return id.Length == 0 || normalizedReference.Length == 0
            ? string.Empty
            : id + "|" + normalizedReference;
    }
}
