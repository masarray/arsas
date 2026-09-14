using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private Button? _smartConnectButton;
    private TextBlock? _smartConnectLabel;
    private bool _smartConnectInstalled;
    private readonly HashSet<Iec61850MonitorDevice> _smartConnectTrackedDevices = new();

    [ModuleInitializer]
    internal static void RegisterSmartMultiIedConnect()
    {
        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(SmartMultiIedConnect_MainWindowLoaded),
            handledEventsToo: true);
    }

    private static void SmartMultiIedConnect_MainWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not MainWindow window)
            return;

        // Let the modular Explorer/onboarding convergence settle first, then bind the
        // smart action to the final left-rail button instance.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(window.InstallSmartMultiIedConnect));
    }

    private void InstallSmartMultiIedConnect()
    {
        if (_smartConnectInstalled)
        {
            RefreshSmartConnectAction();
            return;
        }

        _smartConnectButton = EnumerateVisualDescendants<Button>(this)
            .FirstOrDefault(button => ButtonHasLabel(button, "Connect All"));
        if (_smartConnectButton == null)
            return;

        _smartConnectLabel = EnumerateVisualDescendants<TextBlock>(_smartConnectButton)
            .FirstOrDefault(text => string.Equals(text.Text?.Trim(), "Connect All", StringComparison.OrdinalIgnoreCase));

        _smartConnectButton.Click -= ConnectAllIeds_Click;
        _smartConnectButton.Click += ConnectOfflineIeds_Click;
        _smartConnectButton.ToolTip = "Connect only offline IEDs that are ready to connect; busy, live and unbound SCL workspaces are skipped";

        Devices.CollectionChanged += SmartConnectDevices_CollectionChanged;
        foreach (var device in Devices)
            TrackSmartConnectDevice(device);

        _smartConnectInstalled = true;
        RefreshSmartConnectAction();
    }

    private void SmartConnectDevices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems != null)
        {
            foreach (var device in e.OldItems.OfType<Iec61850MonitorDevice>())
                UntrackSmartConnectDevice(device);
        }

        if (e.Action == NotifyCollectionChangedAction.Reset)
        {
            foreach (var device in _smartConnectTrackedDevices.ToArray())
                UntrackSmartConnectDevice(device);
            foreach (var device in Devices)
                TrackSmartConnectDevice(device);
        }
        else if (e.NewItems != null)
        {
            foreach (var device in e.NewItems.OfType<Iec61850MonitorDevice>())
                TrackSmartConnectDevice(device);
        }

        QueueSmartConnectRefresh();
    }

    private void TrackSmartConnectDevice(Iec61850MonitorDevice device)
    {
        if (!_smartConnectTrackedDevices.Add(device))
            return;

        device.PropertyChanged += SmartConnectDevice_PropertyChanged;
    }

    private void UntrackSmartConnectDevice(Iec61850MonitorDevice device)
    {
        if (!_smartConnectTrackedDevices.Remove(device))
            return;

        device.PropertyChanged -= SmartConnectDevice_PropertyChanged;
    }

    private void SmartConnectDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Ignore high-frequency discovery/progress/value notifications. The button only
        // depends on eligibility state, so it should never add work to the render loop.
        if (e.PropertyName is not (
                nameof(Iec61850MonitorDevice.IsConnected) or
                nameof(Iec61850MonitorDevice.IsMonitoring) or
                nameof(Iec61850MonitorDevice.IsBusy) or
                nameof(Iec61850MonitorDevice.IpAddress) or
                nameof(Iec61850MonitorDevice.RequiresEndpointBinding) or
                nameof(Iec61850MonitorDevice.CanPlayAction)))
        {
            return;
        }

        QueueSmartConnectRefresh();
    }

    private void QueueSmartConnectRefresh()
    {
        if (_smartConnectButton == null)
            return;

        if (Dispatcher.CheckAccess())
            RefreshSmartConnectAction();
        else
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(RefreshSmartConnectAction));
    }

    private Iec61850MonitorDevice[] GetConnectableOfflineDevices()
        => Devices
            .Where(IsConnectableOfflineDevice)
            .ToArray();

    private static bool IsConnectableOfflineDevice(Iec61850MonitorDevice device)
        => !device.IsDemo &&
           !device.IsConnected &&
           !device.IsMonitoring &&
           !device.IsBusy &&
           !device.RequiresEndpointBinding &&
           device.CanPlayAction &&
           !string.IsNullOrWhiteSpace(device.IpAddress);

    private void RefreshSmartConnectAction()
    {
        if (_smartConnectButton == null)
            return;

        var connectableCount = GetConnectableOfflineDevices().Length;
        var useful = Devices.Count >= 2 && connectableCount > 0;
        var show = useful && !_connectAllInProgress;

        // While a batch owns the eligible devices the action disappears. It returns only
        // if another offline/connectable IED remains after that batch has settled.
        _smartConnectButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _smartConnectButton.IsEnabled = show;

        // The original action grid reserves a six-pixel spacer below Connect All. Collapse
        // that spacer with the button so the remaining Add IED action does not float low.
        if (_smartConnectButton.Parent is Grid actionGrid && actionGrid.RowDefinitions.Count > 1)
            actionGrid.RowDefinitions[1].Height = show ? new GridLength(6) : new GridLength(0);

        if (_smartConnectLabel != null)
            _smartConnectLabel.Text = $"Connect {connectableCount} Offline";
    }

    private async void ConnectOfflineIeds_Click(object sender, RoutedEventArgs e)
    {
        if (_connectAllInProgress)
            return;

        var candidates = GetConnectableOfflineDevices();
        if (Devices.Count < 2 || candidates.Length == 0)
        {
            RefreshSmartConnectAction();
            return;
        }

        _connectAllInProgress = true;
        RefreshSmartConnectAction();

        var originalSelection = SelectedDevice;
        SetStatus($"Connecting {candidates.Length} offline IED(s)…");

        using var throttle = new SemaphoreSlim(3, 3);
        try
        {
            var results = await Task.WhenAll(candidates.Select(device =>
                ConnectSmartCandidateAsync(device, throttle)));

            var succeeded = results.Count(result => result);
            var monitoring = candidates.Count(device => device.IsMonitoring);
            var needsSelection = candidates.Count(device => device.IsConnected && device.SelectedLiveSignalCount == 0);
            var failed = candidates.Length - succeeded;

            if (originalSelection != null && Devices.Contains(originalSelection))
                SelectedDevice = originalSelection;

            var details = new List<string>
            {
                $"{succeeded}/{candidates.Length} connected",
                $"{monitoring} monitoring"
            };
            if (needsSelection > 0)
                details.Add($"{needsSelection} need signal selection");
            if (failed > 0)
                details.Add($"{failed} failed/skipped");

            SetStatus($"Smart connect complete: {string.Join(", ", details)}.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Smart connect cancelled; completed IED sessions were kept independent.");
        }
        finally
        {
            _connectAllInProgress = false;
            RaiseWorkspaceCounts();
            RefreshSmartConnectAction();
        }
    }

    private async Task<bool> ConnectSmartCandidateAsync(
        Iec61850MonitorDevice device,
        SemaphoreSlim throttle)
    {
        await throttle.WaitAsync(_applicationCancellation.Token);
        try
        {
            // A queued device can change state while waiting for a worker slot. Re-check
            // immediately before dispatch so user actions always win over the batch.
            if (!IsConnectableOfflineDevice(device))
                return false;

            return await ConnectAndStartWorkspaceDeviceAsync(device);
        }
        finally
        {
            throttle.Release();
        }
    }
}
