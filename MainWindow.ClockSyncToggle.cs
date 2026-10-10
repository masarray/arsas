using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class MainWindow
{
    // Default OFF: no unsolicited station-bus broadcast before operator action.
    private bool _clockSyncEnabled;
    private bool _globalSntpUiRefreshing;
    private ToggleButton? _globalSntpToggle;
    private ComboBox? _globalSntpIpPicker;
    private Ellipse? _globalSntpStateDot;
    private SntpNetworkBinding? _selectedSntpBinding;

    internal bool IsClockSyncEnabled => _clockSyncEnabled;

    private void InstallGlobalSntpToggle()
    {
        if (_globalSntpToggle is not null || WorkflowNavShell.Parent is not Grid headerGrid)
            return;

        var track = new FrameworkElementFactory(typeof(Border));
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        track.SetBinding(Border.BackgroundProperty,
            new Binding(nameof(ToggleButton.Background))
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        var knob = new FrameworkElementFactory(typeof(Ellipse));
        knob.SetValue(FrameworkElement.WidthProperty, 16d);
        knob.SetValue(FrameworkElement.HeightProperty, 16d);
        knob.SetValue(Shape.FillProperty, Brushes.White);
        knob.SetValue(FrameworkElement.MarginProperty, new Thickness(3));
        knob.SetBinding(FrameworkElement.HorizontalAlignmentProperty,
            new Binding(nameof(ToggleButton.HorizontalContentAlignment))
            { RelativeSource = new RelativeSource(RelativeSourceMode.TemplatedParent) });
        track.AppendChild(knob);
        var toggle = new ToggleButton
        {
            Name = "GlobalSntpServerToggle",
            Width = 42, Height = 22,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = new SolidColorBrush(Color.FromRgb(153, 166, 181)),
            Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = track },
            ToolTip = "Start or stop the independent SNTP service on the selected PC IP."
        };
        toggle.Checked += GlobalSntpToggle_Changed;
        toggle.Unchecked += GlobalSntpToggle_Changed;

        var label = new TextBlock
        {
            Text = "NTP Server",
            FontSize = 11.4,
            FontWeight = FontWeights.Medium,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(7, 0, 10, 0)
        };

        var picker = new ComboBox
        {
            Name = "GlobalSntpPcIpPicker",
            Width = 145, Height = 27,
            DisplayMemberPath = nameof(SntpNetworkBinding.LocalAddress),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalContentAlignment = VerticalAlignment.Center,
            FontSize = 11,
            ToolTip = "Choose a local PC IPv4 address. Refreshes when opened; no IED connection required."
        };
        picker.DropDownOpened += (_, _) => RefreshGlobalSntpPcAddresses();
        picker.SelectionChanged += GlobalSntpIp_SelectionChanged;

        var led = new Ellipse
        {
            Name = "GlobalSntpTrafficIndicator",
            Width = 9, Height = 9,
            Fill = Brushes.SlateGray,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "No SNTP communication yet."
        };

        var toolbar = new StackPanel
        {
            Name = "StandaloneSntpToolbar",
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 0, 0, 0)
        };
        toolbar.Children.Add(toggle);
        toolbar.Children.Add(label);
        toolbar.Children.Add(picker);
        toolbar.Children.Add(led);
        Grid.SetColumn(toolbar, 2);
        headerGrid.Children.Add(toolbar);

        _globalSntpToggle = toggle;
        _globalSntpIpPicker = picker;
        _globalSntpStateDot = led;
        RefreshGlobalSntpPcAddresses();
        RefreshGlobalSntpToggle(_sntpClockService.Snapshot);
    }

    private void RefreshGlobalSntpPcAddresses()
    {
        if (_globalSntpIpPicker is null) return;
        IReadOnlyList<SntpNetworkBinding> choices;
        try { choices = SntpNetworkRouteResolver.GetLocalBindings(); }
        catch (Exception ex)
        {
            AddLog("WARN", "SNTP Server", $"Could not enumerate PC IPv4 addresses: {ex.Message}");
            return;
        }

        var previous = _selectedSntpBinding;
        var matched = choices.FirstOrDefault(item =>
            previous is not null &&
            item.LocalAddress.Equals(previous.LocalAddress) &&
            item.InterfaceId.Equals(previous.InterfaceId, StringComparison.OrdinalIgnoreCase));
        // An active server whose NIC vanished is stopped, never silently
        // moved onto another PC network (especially a different station LAN).
        var selected = matched ?? (previous is null && !_clockSyncEnabled ? choices.FirstOrDefault() : null);
        _globalSntpUiRefreshing = true;
        try
        {
            _globalSntpIpPicker.ItemsSource = choices;
            _globalSntpIpPicker.SelectedItem = selected;
            _selectedSntpBinding = selected;
        }
        finally { _globalSntpUiRefreshing = false; }

        if (_clockSyncEnabled && selected is null)
            _ = ReconcileStandaloneClockAsync();
    }

    private async void GlobalSntpIp_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_globalSntpUiRefreshing || _globalSntpIpPicker is null) return;
        _selectedSntpBinding = _globalSntpIpPicker.SelectedItem as SntpNetworkBinding;
        if (_clockSyncEnabled)
            await ReconcileStandaloneClockAsync();
        else
            PublishGlobalSntpUiState();
    }

    private async void GlobalSntpToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_globalSntpUiRefreshing || _globalSntpToggle is null) return;
        _globalSntpToggle.IsEnabled = false;
        try { await SetClockSyncEnabledAsync(_globalSntpToggle.IsChecked == true); }
        finally { _globalSntpToggle.IsEnabled = true; }
    }

    internal async Task SetClockSyncEnabledAsync(bool enabled)
    {
        _clockSyncEnabled = enabled && _selectedSntpBinding is not null;
        await ReconcileStandaloneClockAsync();
    }

    private void PublishGlobalSntpUiState()
    {
        var snapshot = _sntpClockService.Snapshot;
        void Publish()
        {
            RefreshGlobalSntpToggle(snapshot);
            ClockSyncSnapshotChanged?.Invoke(snapshot);
        }
        if (Dispatcher.CheckAccess()) Publish();
        else if (!Dispatcher.HasShutdownStarted)
            Dispatcher.BeginInvoke(new Action(Publish));
    }

    private void RefreshGlobalSntpToggle(SntpClockServiceSnapshot snapshot)
    {
        if (_globalSntpToggle is null || _globalSntpIpPicker is null || _globalSntpStateDot is null)
            return;
        _globalSntpUiRefreshing = true;
        try { _globalSntpToggle.IsChecked = _clockSyncEnabled; }
        finally { _globalSntpUiRefreshing = false; }

        var serving = _clockSyncEnabled && snapshot.State == SntpClockServiceState.Serving;
        var fault = _clockSyncEnabled && snapshot.State is
            SntpClockServiceState.Faulted or SntpClockServiceState.PortUnavailable;
        _globalSntpToggle.Background = new SolidColorBrush(serving
            ? Color.FromRgb(25, 164, 117)
            : fault ? Color.FromRgb(218, 142, 36)
            : _clockSyncEnabled ? Color.FromRgb(64, 124, 210) : Color.FromRgb(153, 166, 181));
        _globalSntpToggle.HorizontalContentAlignment = _clockSyncEnabled
            ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        var exchanged = serving && snapshot.ReplyCount > 0;
        var unanswered = serving && snapshot.ClientRequestCount > snapshot.ReplyCount;
        _globalSntpStateDot.Fill = new SolidColorBrush(
            fault ? Color.FromRgb(226, 79, 78)
            : exchanged ? Color.FromRgb(16, 185, 129)
            : unanswered ? Color.FromRgb(235, 162, 48)
            : serving ? Color.FromRgb(64, 137, 228)
            : Color.FromRgb(148, 163, 184));
        _globalSntpStateDot.ToolTip =
            $"SNTP: {snapshot.State}\n" +
            $"PC IP: {snapshot.Binding?.LocalAddress.ToString() ?? _selectedSntpBinding?.LocalAddress.ToString() ?? "—"}\n" +
            $"Mode: {snapshot.TransportMode}\n" +
            $"Requests: {snapshot.ClientRequestCount} · Replies: {snapshot.ReplyCount}\n" +
            $"Last reply: {snapshot.LastReplyUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—"}\n" +
            "Replies show packet activity, not device clock synchronization.";
        _globalSntpIpPicker.ToolTip = _selectedSntpBinding is null
            ? "Select an active PC IPv4 address."
            : $"{_selectedSntpBinding.InterfaceName} · {_selectedSntpBinding.LocalAddress}\n" +
              $"Broadcast: {_selectedSntpBinding.DirectedBroadcast?.ToString() ?? "N/A"}";
    }
}
