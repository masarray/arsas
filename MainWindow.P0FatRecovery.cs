using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P0 recovery guard anchored to Build #1888.
///
/// This file deliberately does not change DataGrid virtualization, FAT evidence scope,
/// RCB/DataSet configuration, or add a polling timer. It projects the existing ARSAS
/// runtime event stream into the already-bound FAT runtime model, canonicalizes Boolean
/// presentation before the first visible FAT frame, and makes an SCL-backed Engineering
/// workspace reusable by Play without forcing a second live discovery.
/// </summary>
public partial class MainWindow
{
    private static readonly bool P0FatRecoveryClassHandlersRegistered = RegisterP0FatRecoveryClassHandlers();
    private readonly ConcurrentDictionary<string, Iec61850PointSnapshot> _p0FatLatestSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private int _p0FatDrainScheduled;
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
        EventManager.RegisterClassHandler(
            typeof(Button),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0HideLegacyManualCaptureButton));
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
        if (!_p0FatRuntimeProjectionAttached)
            return;

        _runtime.PointUpdated -= P0FatRuntimePointUpdated;
        _p0FatRuntimeProjectionAttached = false;
        _p0FatLatestSnapshots.Clear();
    }

    private static void P0FatWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow fat || fat.Owner is not MainWindow engineering)
            return;

        // A direct SCL FAT import has already populated the shared Engineering model.
        // Mark that model reusable so Engineering Play follows ConnectUsingSavedModelAsync
        // instead of opening a second live-discovery workflow.
        foreach (var device in engineering.Devices)
        {
            if (device.SclWorkspace != null && device.Signals.Count > 0)
                device.HasDiscoveryCache = true;
        }

        // Loaded runs before the first rendered frame. Normalize/copy the current Engineering
        // image now so FAT never needs to first paint raw "true/false" and later change case.
        engineering.P0RefreshFatFromEngineeringImage(fat);
    }

    private void P0FatRuntimePointUpdated(Iec61850PointSnapshot snapshot)
    {
        if (_loadedIoFatWindow is not { IsLoaded: true })
            return;

        var key = P0FatKey(snapshot.Point.DeviceId, snapshot.Point.IecReference);
        if (key.Length == 0)
            return;

        _p0FatLatestSnapshots[key] = snapshot;
        if (Interlocked.Exchange(ref _p0FatDrainScheduled, 1) != 0)
            return;

        Dispatcher.BeginInvoke(new Action(P0DrainFatRuntimeProjection), DispatcherPriority.DataBind);
    }

    private void P0DrainFatRuntimeProjection()
    {
        try
        {
            if (_loadedIoFatWindow is not { IsLoaded: true } fat)
            {
                _p0FatLatestSnapshots.Clear();
                return;
            }

            var latest = _p0FatLatestSnapshots.ToArray();
            _p0FatLatestSnapshots.Clear();
            if (latest.Length == 0)
                return;

            // Build one small index per coalesced dispatcher drain, never one subscription
            // per cell/row. Typical FAT projects have tens of points; this keeps the UI work
            // deterministic while multiple report updates collapse to the latest sample.
            var index = BuildP0FatPointIndex(fat.Project);
            foreach (var pair in latest)
            {
                if (!index.TryGetValue(pair.Key, out var plans))
                    continue;

                var snapshot = pair.Value;
                foreach (var plan in plans)
                    ApplyP0FatSnapshot(plan.Runtime, snapshot);
            }
        }
        finally
        {
            Interlocked.Exchange(ref _p0FatDrainScheduled, 0);
            if (!_p0FatLatestSnapshots.IsEmpty &&
                Interlocked.Exchange(ref _p0FatDrainScheduled, 1) == 0)
            {
                Dispatcher.BeginInvoke(new Action(P0DrainFatRuntimeProjection), DispatcherPriority.DataBind);
            }
        }
    }

    private void P0RefreshFatFromEngineeringImage(IoListTestingWindow fat)
    {
        var index = BuildP0FatPointIndex(fat.Project);
        foreach (var device in Devices)
        {
            foreach (var point in device.Points)
            {
                var key = P0FatKey(device.DeviceId, point.IecReference);
                if (key.Length == 0 || !index.TryGetValue(key, out var plans))
                    continue;

                foreach (var plan in plans)
                {
                    var runtime = plan.Runtime;
                    var value = P0CanonicalLiveValue(point.Value);
                    if (!string.Equals(runtime.CurrentValue, value, StringComparison.Ordinal))
                        runtime.CurrentValue = value;
                    if (!string.Equals(runtime.CurrentQuality, point.Quality, StringComparison.Ordinal))
                        runtime.CurrentQuality = point.Quality;
                    if (!string.Equals(runtime.CurrentSource, point.SourceMode, StringComparison.Ordinal))
                        runtime.CurrentSource = point.SourceMode;
                    var timestamp = string.IsNullOrWhiteSpace(point.DeviceTimestamp) || point.DeviceTimestamp == "-"
                        ? "—"
                        : point.DeviceTimestamp.Trim();
                    if (!string.Equals(runtime.CurrentIedTimestamp, timestamp, StringComparison.Ordinal))
                        runtime.CurrentIedTimestamp = timestamp;
                }
            }
        }
    }

    private static Dictionary<string, List<IoTestPointPlan>> BuildP0FatPointIndex(IoTestProject project)
    {
        var index = new Dictionary<string, List<IoTestPointPlan>>(StringComparer.OrdinalIgnoreCase);
        foreach (var plan in project.Ieds.SelectMany(ied => ied.TestPoints))
        {
            var deviceId = plan.LiveDeviceId;
            var reference = !string.IsNullOrWhiteSpace(plan.LiveSignalReference)
                ? plan.LiveSignalReference
                : plan.ObjectReference;
            var key = P0FatKey(deviceId, reference);
            if (key.Length == 0)
                continue;

            if (!index.TryGetValue(key, out var bucket))
            {
                bucket = new List<IoTestPointPlan>();
                index[key] = bucket;
            }
            bucket.Add(plan);
        }
        return index;
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

    private static void P0HideLegacyManualCaptureButton(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || !string.Equals(button.Content as string, "✓ Capture", StringComparison.Ordinal))
            return;

        if (FindP0Ancestor<IoListTestingWindow>(button) == null)
            return;

        // Analog capture remains owned by FatAutoCaptureCoordinator. The explicit Recapture
        // context action is retained for evidence correction, but normal manual Capture is
        // removed from the operator row UX.
        button.Visibility = Visibility.Collapsed;
        button.IsHitTestVisible = false;
        button.IsTabStop = false;
    }

    private static T? FindP0Ancestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject? current = start;
        while (current != null)
        {
            if (current is T match)
                return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
