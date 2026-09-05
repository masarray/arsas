using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private static readonly bool FatLiveValueMirrorClassHandlerRegistered = RegisterFatLiveValueMirrorClassHandler();
    private readonly Dictionary<string, Iec61850MonitorPoint> _fatEngineeringLivePoints =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _fatLiveValueMirrorInstalled;
    private MainWindow? _fatLiveValueMirrorOwner;

    private static bool RegisterFatLiveValueMirrorClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(FatLiveValueMirrorClassLoaded));
        return true;
    }

    private static void FatLiveValueMirrorClassLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._fatLiveValueMirrorInstalled)
            return;

        window._fatLiveValueMirrorInstalled = true;
        window.Closed += window.FatLiveValueMirrorWindow_Closed;
        if (window.Owner is MainWindow engineeringWindow)
        {
            window._fatLiveValueMirrorOwner = engineeringWindow;
            engineeringWindow.AttachIoFatLiveValueMirror(window);
        }

        // Keep Build #1888 virtualized, but do not recycle realized rows. Recycling can
        // briefly paint a previous row's value/quality while scrolling and looks like a
        // false/False or Open/Closed flicker even though the live model is already correct.
        window.Dispatcher.BeginInvoke(
            new Action(window.ConfigureFatLiveValueVirtualization),
            DispatcherPriority.ContextIdle);
    }

    private void FatLiveValueMirrorWindow_Closed(object? sender, EventArgs e)
    {
        Closed -= FatLiveValueMirrorWindow_Closed;
        _fatLiveValueMirrorOwner?.DetachIoFatLiveValueMirror(this);
        _fatLiveValueMirrorOwner = null;
        _fatEngineeringLivePoints.Clear();
    }

    private void ConfigureFatLiveValueVirtualization()
    {
        var grid = FindFirstVisualDescendant<DataGrid>(this);
        if (grid == null)
            return;

        VirtualizingPanel.SetIsVirtualizing(grid, true);
        VirtualizingPanel.SetVirtualizationMode(grid, VirtualizationMode.Standard);
        grid.EnableRowVirtualization = true;
        // Keep Build #1888 column virtualization behavior untouched.
    }

    /// <summary>
    /// Mirrors presentation fields from the authoritative Engineering live image.
    /// Value 1 / Value 2 evidence is intentionally untouched. Runtime.CurrentValue is
    /// the same value consumed by the existing FAT auto-capture path, so analog capture
    /// now receives the current Engineering value instead of a stale bind-time snapshot.
    /// </summary>
    internal void RefreshEngineeringLiveMirror(IEnumerable<Iec61850MonitorDevice> devices)
    {
        ArgumentNullException.ThrowIfNull(devices);

        _fatEngineeringLivePoints.Clear();
        foreach (var device in devices)
        {
            foreach (var livePoint in device.Points)
            {
                var key = LiveMirrorKey(livePoint.DeviceId, livePoint.IecReference);
                if (key.Length > 0)
                    _fatEngineeringLivePoints[key] = livePoint;
            }
        }

        foreach (var ied in Project.Ieds)
        {
            foreach (var point in ied.TestPoints)
            {
                if (!point.IsLiveBound ||
                    string.IsNullOrWhiteSpace(point.LiveDeviceId) ||
                    string.IsNullOrWhiteSpace(point.LiveSignalReference))
                {
                    continue;
                }

                var key = LiveMirrorKey(point.LiveDeviceId, point.LiveSignalReference);
                if (!_fatEngineeringLivePoints.TryGetValue(key, out var livePoint))
                    continue;

                // Stabilize Boolean presentation. "false" and "False" are the same live
                // state and must not invalidate/repaint the FAT row. A real state change
                // (for example Open -> Closed) still flows through immediately.
                var nextValue = NormalizeFatLiveValue(livePoint.Value);
                if (!SameFatLiveValue(point.Runtime.CurrentValue, nextValue))
                    point.Runtime.CurrentValue = nextValue;

                var nextQuality = (livePoint.Quality ?? string.Empty).Trim();
                if (!string.Equals(point.Runtime.CurrentQuality, nextQuality, StringComparison.Ordinal))
                    point.Runtime.CurrentQuality = nextQuality;

                var nextSource = (livePoint.SourceMode ?? string.Empty).Trim();
                if (!string.Equals(point.Runtime.CurrentSource, nextSource, StringComparison.Ordinal))
                    point.Runtime.CurrentSource = nextSource;

                var nextTimestamp =
                    string.IsNullOrWhiteSpace(livePoint.DeviceTimestamp) || livePoint.DeviceTimestamp == "-"
                        ? "—"
                        : livePoint.DeviceTimestamp.Trim();
                if (!string.Equals(point.Runtime.CurrentIedTimestamp, nextTimestamp, StringComparison.Ordinal))
                    point.Runtime.CurrentIedTimestamp = nextTimestamp;
            }
        }
    }

    private static string NormalizeFatLiveValue(string? value)
    {
        var normalized = (value ?? string.Empty).Trim();
        if (bool.TryParse(normalized, out var boolean))
            return boolean ? "true" : "false";
        return normalized;
    }

    private static bool SameFatLiveValue(string? current, string next)
    {
        var existing = (current ?? string.Empty).Trim();
        if (string.Equals(existing, next, StringComparison.Ordinal))
            return true;

        // Case-only Boolean churn is presentation noise, not a process transition.
        return bool.TryParse(existing, out var currentBoolean) &&
               bool.TryParse(next, out var nextBoolean) &&
               currentBoolean == nextBoolean;
    }

    private static string LiveMirrorKey(string? deviceId, string? reference)
    {
        var id = (deviceId ?? string.Empty).Trim();
        var normalized = IoTestLiveBindingService.NormalizeReference(reference);
        return id.Length == 0 || normalized.Length == 0 ? string.Empty : id + "|" + normalized;
    }

    private static T? FindFirstVisualDescendant<T>(DependencyObject root) where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                return typed;

            var nested = FindFirstVisualDescendant<T>(child);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
