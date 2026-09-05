using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // Keep the existing commissioning default for compatibility, but make the state explicit
    // in the global ARSAS chrome. The service is application-scoped: FAT never owns it.
    private bool _clockSyncEnabled = true;
    private ToggleButton? _globalSntpToggle;
    private Ellipse? _globalSntpStateDot;
    private TextBlock? _globalSntpCaption;
    private bool _globalSntpToggleRefreshing;

    internal bool IsClockSyncEnabled => _clockSyncEnabled;

    private void InstallGlobalSntpToggle()
    {
        if (_globalSntpToggle != null || WorkflowNavShell.Parent is not Grid headerGrid)
            return;

        var stateDot = new Ellipse
        {
            Width = 8,
            Height = 8,
            Margin = new Thickness(0, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Fill = Brushes.SlateGray
        };
        var caption = new TextBlock
        {
            Text = "SNTP Server",
            FontSize = 11.3,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var content = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        content.Children.Add(stateDot);
        content.Children.Add(caption);

        var toggle = new ToggleButton
        {
            Name = "GlobalSntpServerToggle",
            MinWidth = 282,
            MaxWidth = 340,
            Height = 38,
            Margin = new Thickness(12, 0, 0, 0),
            Padding = new Thickness(12, 0, 12, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(1),
            Background = new SolidColorBrush(Color.FromRgb(244, 247, 251)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(205, 217, 234)),
            Foreground = new SolidColorBrush(Color.FromRgb(82, 103, 126)),
            Focusable = false,
            IsThreeState = false,
            Content = content
        };

        toggle.Checked += GlobalSntpToggle_Changed;
        toggle.Unchecked += GlobalSntpToggle_Changed;
        Grid.SetColumn(toggle, 2);
        headerGrid.Children.Add(toggle);

        _globalSntpToggle = toggle;
        _globalSntpStateDot = stateDot;
        _globalSntpCaption = caption;
        RefreshGlobalSntpToggle(_sntpClockService.Snapshot);
    }

    private async void GlobalSntpToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_globalSntpToggleRefreshing || _globalSntpToggle == null)
            return;

        _globalSntpToggle.IsEnabled = false;
        try
        {
            await SetClockSyncEnabledAsync(_globalSntpToggle.IsChecked == true);
        }
        finally
        {
            _globalSntpToggle.IsEnabled = true;
        }
    }

    internal async Task SetClockSyncEnabledAsync(bool enabled)
    {
        if (_clockSyncEnabled == enabled)
        {
            if (enabled)
                _sntpClockService.RequestImmediateBroadcast();
            PublishGlobalSntpUiState();
            return;
        }

        _clockSyncEnabled = enabled;
        PublishGlobalSntpUiState();

        if (!enabled)
        {
            Devices.CollectionChanged -= ClockSyncDevices_CollectionChanged;
            foreach (var device in Devices)
                device.PropertyChanged -= ClockSyncDevice_PropertyChanged;

            await _clockSyncIntegrationGate.WaitAsync();
            try
            {
                await _sntpClockService.StopAsync();
                _clockSyncObservedClients.Clear();
                _clockSyncRepliedClients.Clear();
                AddLog("INFO", "SNTP Server",
                    "Global SNTP Server disabled. IEC 61850 monitoring and FAT sessions remain active.");
            }
            catch (Exception ex)
            {
                AddLog("WARN", "SNTP Server",
                    $"Global SNTP Server disable requested, but the clock service reported: {ex.Message}");
            }
            finally
            {
                _clockSyncIntegrationGate.Release();
            }

            PublishGlobalSntpUiState();
            return;
        }

        Devices.CollectionChanged -= ClockSyncDevices_CollectionChanged;
        Devices.CollectionChanged += ClockSyncDevices_CollectionChanged;
        foreach (var device in Devices)
            AttachClockSyncDevice(device);

        AddLog("INFO", "SNTP Server",
            "Global SNTP Server enabled. It remains active independently of FAT while ARSAS is running. Connected IPv4 IEDs select the station-bus interface; ARSAS serves UDP/123 and broadcasts Mode 5 time, with Npcap RAW fallback when Windows already owns UDP/123. Windows Time is never stopped or reconfigured.");
        PublishGlobalSntpUiState();
    }

    private void PublishGlobalSntpUiState()
    {
        var snapshot = _sntpClockService.Snapshot;

        void Publish()
        {
            RefreshGlobalSntpToggle(snapshot);
            ClockSyncSnapshotChanged?.Invoke(snapshot);
        }

        if (Dispatcher.CheckAccess())
            Publish();
        else
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void RefreshGlobalSntpToggle(SntpClockServiceSnapshot snapshot)
    {
        if (_globalSntpToggle == null || _globalSntpCaption == null || _globalSntpStateDot == null)
            return;

        _globalSntpToggleRefreshing = true;
        try
        {
            _globalSntpToggle.IsChecked = _clockSyncEnabled;
        }
        finally
        {
            _globalSntpToggleRefreshing = false;
        }

        var localAddress = snapshot.Binding?.LocalAddress.ToString();
        var isServing = _clockSyncEnabled && snapshot.State == SntpClockServiceState.Serving;
        var isStarting = _clockSyncEnabled && snapshot.State == SntpClockServiceState.Starting;
        var isFault = _clockSyncEnabled && snapshot.State is SntpClockServiceState.Faulted or SntpClockServiceState.PortUnavailable;

        _globalSntpCaption.Text = !_clockSyncEnabled
            ? "SNTP Server Off"
            : isServing
                ? $"SNTP Server Active: {localAddress ?? "station bus"}"
                : isStarting
                    ? "SNTP Server Starting…"
                    : isFault
                        ? "SNTP Server Attention"
                        : "SNTP Server Enabled · waiting for IED";

        if (isServing)
        {
            _globalSntpToggle.Background = new SolidColorBrush(Color.FromRgb(236, 253, 245));
            _globalSntpToggle.BorderBrush = new SolidColorBrush(Color.FromRgb(167, 243, 208));
            _globalSntpToggle.Foreground = new SolidColorBrush(Color.FromRgb(4, 120, 87));
            _globalSntpStateDot.Fill = new SolidColorBrush(Color.FromRgb(16, 185, 129));
        }
        else if (isFault)
        {
            _globalSntpToggle.Background = new SolidColorBrush(Color.FromRgb(255, 248, 230));
            _globalSntpToggle.BorderBrush = new SolidColorBrush(Color.FromRgb(242, 210, 138));
            _globalSntpToggle.Foreground = new SolidColorBrush(Color.FromRgb(148, 98, 0));
            _globalSntpStateDot.Fill = new SolidColorBrush(Color.FromRgb(245, 158, 11));
        }
        else if (_clockSyncEnabled)
        {
            _globalSntpToggle.Background = new SolidColorBrush(Color.FromRgb(238, 244, 255));
            _globalSntpToggle.BorderBrush = new SolidColorBrush(Color.FromRgb(201, 217, 241));
            _globalSntpToggle.Foreground = new SolidColorBrush(Color.FromRgb(69, 100, 142));
            _globalSntpStateDot.Fill = new SolidColorBrush(Color.FromRgb(47, 128, 237));
        }
        else
        {
            _globalSntpToggle.Background = new SolidColorBrush(Color.FromRgb(244, 247, 251));
            _globalSntpToggle.BorderBrush = new SolidColorBrush(Color.FromRgb(205, 217, 234));
            _globalSntpToggle.Foreground = new SolidColorBrush(Color.FromRgb(82, 103, 126));
            _globalSntpStateDot.Fill = new SolidColorBrush(Color.FromRgb(148, 163, 184));
        }

        _globalSntpToggle.ToolTip = BuildGlobalSntpToolTip(snapshot);
    }

    private string BuildGlobalSntpToolTip(SntpClockServiceSnapshot snapshot)
    {
        var binding = snapshot.Binding;
        var local = binding?.LocalAddress.ToString() ?? "waiting for first connected IPv4 IED";
        var broadcast = binding?.DirectedBroadcast?.ToString() ?? "—";
        var adapter = binding?.InterfaceName ?? "—";
        var transport = snapshot.TransportMode switch
        {
            SntpClockTransportMode.UdpSocket => "UDP/123",
            SntpClockTransportMode.NpcapRaw => "Npcap RAW",
            _ => "—"
        };
        var lastBroadcast = snapshot.LastBroadcastUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";
        var lastRequest = snapshot.LastRequestUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";
        var lastReply = snapshot.LastReplyUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";

        return $"Global ARSAS SNTP Server\n" +
               $"Enabled: {_clockSyncEnabled}\n" +
               $"State: {snapshot.State}\n" +
               $"Local server IP: {local}\n" +
               $"Station-bus adapter: {adapter}\n" +
               $"Directed broadcast: {broadcast}\n" +
               $"Transport: {transport}\n" +
               $"Mode 5 broadcasts: {snapshot.BroadcastCount} (last {lastBroadcast})\n" +
               $"Client requests observed: {snapshot.ClientRequestCount} (last {lastRequest})\n" +
               $"Mode 4 replies sent: {snapshot.ReplyCount} (last {lastReply})\n\n" +
               "When enabled, this service is global and continues outside FAT until disabled or ARSAS closes. " +
               "A sent broadcast or reply proves SNTP packet activity, not that an IED accepted or locked its internal clock. " +
               "Relay/event timestamps are trustworthy only after device-side synchronization evidence confirms the clock.\n\n" +
               snapshot.Detail;
    }
}
