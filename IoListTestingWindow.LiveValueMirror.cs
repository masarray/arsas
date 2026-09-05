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

        // Build #1888 remains virtualized. Only recycling is disabled so a realized row
        // cannot briefly display another signal's cell values while the operator scrolls.
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
        // Keep the Build #1888 column behavior untouched.
    }

    /// <summary>
    /// Mirrors only presentation fields from the authoritative Engineering live points.
    /// Value 1 / Value 2, transition state, verdicts and evidence are intentionally untouched.
    /// This runs on the existing Engineering WPF UI-flush clock; it performs no MMS read,
    /// report activation, polling, Dispatcher loop, or per-cell subscription.
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

                point.Runtime.CurrentValue = livePoint.Value;
                point.Runtime.CurrentQuality = livePoint.Quality;
                point.Runtime.CurrentSource = livePoint.SourceMode;
                point.Runtime.CurrentIedTimestamp =
                    string.IsNullOrWhiteSpace(livePoint.DeviceTimestamp) || livePoint.DeviceTimestamp == "-"
                        ? "—"
                        : livePoint.DeviceTimestamp;
            }
        }
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
