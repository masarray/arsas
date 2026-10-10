using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Media.Animation;
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
    private Ellipse? _globalSntpPulseRing;
    private bool _globalSntpBreathing;
    private long _globalSntpLastRenderedRequestCount;
    private long _globalSntpLastRenderedReplyCount;
    private DateTimeOffset _globalSntpSessionStartedUtc = DateTimeOffset.MaxValue;
    private DateTimeOffset _globalSntpLastActivityPulseUtc = DateTimeOffset.MinValue;
    private SntpNetworkBinding? _selectedSntpBinding;
    private readonly SntpSavedSettings? _savedSntpSettings = SntpUserSettingsStore.Load();

    internal bool IsClockSyncEnabled => _clockSyncEnabled;

    private void InstallGlobalSntpToggle()
    {
        if (_globalSntpToggle is not null || WorkflowNavShell.Parent is not Grid headerGrid)
            return;

        // Use the SAME shared ARSAS design tokens as the rest of the workstation.
        // Only activity is animated; there is no timer, network polling, or
        // UI thread callback dedicated to the NTP header.
        var ink = (Brush)FindResource("Ink");
        var muted = (Brush)FindResource("Muted");
        var accent = (Brush)FindResource("Accent");
        var line = (Brush)FindResource("BorderStrong");
        var surface = (Brush)FindResource("SurfaceElevated");
        var font = (FontFamily)FindResource("AppFontFamily");

        var track = new FrameworkElementFactory(typeof(Border));
        track.SetValue(Border.CornerRadiusProperty, new CornerRadius(12));
        track.SetValue(Border.BorderThicknessProperty, new Thickness(0));
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
            VerticalAlignment = VerticalAlignment.Center,
            BorderThickness = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Background = muted,
            Template = new ControlTemplate(typeof(ToggleButton)) { VisualTree = track },
            ToolTip = "Enable or disable the independent NTP Server."
        };
        toggle.Checked += GlobalSntpToggle_Changed;
        toggle.Unchecked += GlobalSntpToggle_Changed;

        var label = new TextBlock
        {
            Text = "NTP Server",
            FontFamily = font,
            FontSize = 12.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 12, 0)
        };

        var itemStyle = new Style(typeof(ComboBoxItem));
        itemStyle.Setters.Add(new Setter(Control.ForegroundProperty, ink));
        itemStyle.Setters.Add(new Setter(Control.FontFamilyProperty, font));
        itemStyle.Setters.Add(new Setter(Control.FontSizeProperty, 12.8));
        itemStyle.Setters.Add(new Setter(Control.FontWeightProperty, FontWeights.SemiBold));
        itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(9, 5, 9, 5)));

        var picker = new ComboBox
        {
            Name = "GlobalSntpPcIpPicker",
            Width = 156, Height = 32,
            DisplayMemberPath = nameof(SntpNetworkBinding.LocalAddress),
            FontFamily = font,
            FontSize = 12.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = ink,
            Background = surface,
            BorderBrush = line,
            BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 3, 4, 3),
            ItemContainerStyle = itemStyle,
            Margin = new Thickness(0, 0, 11, 0),
            VerticalAlignment = VerticalAlignment.Center,
            VerticalContentAlignment = VerticalAlignment.Center,
            ToolTip = "Select the PC's local IPv4 address."
        };
        TextOptions.SetTextFormattingMode(picker, TextFormattingMode.Display);
        TextOptions.SetTextRenderingMode(picker, TextRenderingMode.ClearType);
        picker.DropDownOpened += (_, _) => RefreshGlobalSntpPcAddresses();
        picker.SelectionChanged += GlobalSntpIp_SelectionChanged;

        // One shared visual: steady center dot indicates serving; outer ring
        // flashes only when a new NTP request or reply is actually observed.
        var heartbeat = new Grid
        {
            Width = 22, Height = 24,
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = "NTP Server is stopped."
        };
        var ring = new Ellipse
        {
            Width = 16, Height = 16, Opacity = 0,
            Fill = Brushes.Transparent,
            Stroke = accent, StrokeThickness = 1.6,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            RenderTransform = new ScaleTransform(1, 1),
            RenderTransformOrigin = new Point(0.5, 0.5),
            IsHitTestVisible = false
        };
        var led = new Ellipse
        {
            Name = "GlobalSntpTrafficIndicator",
            Width = 9, Height = 9,
            Fill = muted,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = "NTP Server is stopped."
        };
        heartbeat.Children.Add(ring);
        heartbeat.Children.Add(led);

        var contents = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };
        contents.Children.Add(toggle);
        contents.Children.Add(label);
        contents.Children.Add(picker);
        contents.Children.Add(heartbeat);
        var toolbar = new Border
        {
            Name = "StandaloneSntpToolbar",
            Height = 42,
            CornerRadius = new CornerRadius(10),
            Background = surface,
            BorderBrush = (Brush)FindResource("Line"),
            BorderThickness = new Thickness(1),
            Padding = new Thickness(11, 0, 11, 0),
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0),
            Child = contents,
            SnapsToDevicePixels = true
        };
        Grid.SetColumn(toolbar, 2);
        headerGrid.Children.Add(toolbar);

        _globalSntpToggle = toggle;
        _globalSntpIpPicker = picker;
        _globalSntpStateDot = led;
        _globalSntpPulseRing = ring;
        RefreshGlobalSntpPcAddresses();
        RefreshGlobalSntpToggle(_sntpClockService.Snapshot);
        // Restore a previously explicit ON only on the exact same live NIC/IP.
        if (_savedSntpSettings?.Enabled == true && _selectedSntpBinding is not null)
            _ = SetClockSyncEnabledAsync(true);
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
        var restored = previous is null ? SntpUserSettingsStore.Match(choices, _savedSntpSettings) : null;
        var selected = matched ?? restored ??
            (previous is null && _savedSntpSettings is null && !_clockSyncEnabled ? choices.FirstOrDefault() : null);
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
        {
            BeginNtpVisualSession();
            await ReconcileStandaloneClockAsync();
        }
        else
            PublishGlobalSntpUiState();
        SaveSntpPreference();
    }

    private async void GlobalSntpToggle_Changed(object sender, RoutedEventArgs e)
    {
        if (_globalSntpUiRefreshing || _globalSntpToggle is null) return;
        // Keep the switch interactive even while a slow Npcap fallback starts.
        // Serialized lifecycle + versioned intent safely handle rapid ON/OFF.
        await SetClockSyncEnabledAsync(_globalSntpToggle.IsChecked == true);
    }

    internal async Task SetClockSyncEnabledAsync(bool enabled)
    {
        if (enabled && !_clockSyncEnabled)
            BeginNtpVisualSession();
        _clockSyncEnabled = enabled && _selectedSntpBinding is not null;
        await ReconcileStandaloneClockAsync();
        SaveSntpPreference();
    }

    private void SaveSntpPreference()
    {
        // No disk IO per packet. Shutdown stopping SNTP must not change saved state.
        if (_selectedSntpBinding is not { } binding) return;
        var saved = new SntpSavedSettings(
            binding.InterfaceId, binding.LocalAddress.ToString(),
            _clockSyncEnabled && _sntpClockService.Snapshot.State == SntpClockServiceState.Serving);
        if (!SntpUserSettingsStore.Save(saved))
            AddLog("WARN", "SNTP Server", "Could not save the local SNTP preference.");
    }

    private void BeginNtpVisualSession()
    {
        var snapshot = _sntpClockService.Snapshot;
        _globalSntpSessionStartedUtc = DateTimeOffset.UtcNow;
        _globalSntpLastRenderedRequestCount = snapshot.ClientRequestCount;
        _globalSntpLastRenderedReplyCount = snapshot.ReplyCount;
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

        var state = SntpHeaderHeartbeatPolicy.Evaluate(
            _clockSyncEnabled, snapshot, _globalSntpSessionStartedUtc,
            _globalSntpLastRenderedRequestCount, _globalSntpLastRenderedReplyCount);
        _globalSntpToggle.Background = _clockSyncEnabled
            ? (Brush)FindResource("Accent")
            : (Brush)FindResource("Muted");
        _globalSntpToggle.HorizontalContentAlignment = _clockSyncEnabled
            ? HorizontalAlignment.Right : HorizontalAlignment.Left;

        var dot = _globalSntpStateDot;
        dot.Fill = state.Tone switch
        {
            SntpHeaderHeartbeatTone.Fault => (Brush)FindResource("Danger"),
            SntpHeaderHeartbeatTone.Reply => (Brush)FindResource("Success"),
            SntpHeaderHeartbeatTone.Request => (Brush)FindResource("Warning"),
            SntpHeaderHeartbeatTone.Serving => (Brush)FindResource("Accent"),
            _ => (Brush)FindResource("Muted")
        };

        // A single WPF composition clock performs the gentle running
        // heartbeat. It is started/stopped on state transitions, never per
        // packet; no DispatcherTimer, Thread.Sleep or background worker.
        if (state.Serving != _globalSntpBreathing)
        {
            _globalSntpBreathing = state.Serving;
            if (state.Serving)
            {
                dot.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
                {
                    From = 0.53, To = 1.0,
                    Duration = TimeSpan.FromMilliseconds(950),
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever
                });
            }
            else
            {
                dot.BeginAnimation(UIElement.OpacityProperty, null);
                dot.Opacity = 1;
                if (_globalSntpPulseRing is { } ring)
                {
                    ring.BeginAnimation(UIElement.OpacityProperty, null);
                    ring.Opacity = 0;
                }
            }
        }

        // Status callbacks are already coalesced at the integration layer;
        // avoid restarting the activity animation on rapid packet bursts.
        var now = DateTimeOffset.UtcNow;
        if (state.Serving &&
            (state.NewRequest || state.NewReply) &&
            now - _globalSntpLastActivityPulseUtc >= TimeSpan.FromMilliseconds(280))
        {
            _globalSntpLastActivityPulseUtc = now;
            FlashNtpTrafficRing(state.NewReply);
        }
        _globalSntpLastRenderedRequestCount = snapshot.ClientRequestCount;
        _globalSntpLastRenderedReplyCount = snapshot.ReplyCount;

        var tooltip =
            $"NTP: {snapshot.State}\n" +
            $"PC IP: {snapshot.Binding?.LocalAddress.ToString() ?? _selectedSntpBinding?.LocalAddress.ToString() ?? "—"}\n" +
            $"Transport: {snapshot.TransportMode}\n" +
            $"Requests: {snapshot.ClientRequestCount} · Replies: {snapshot.ReplyCount}\n" +
            $"Last reply: {snapshot.LastReplyUtc?.ToLocalTime().ToString("HH:mm:ss") ?? "—"}\n" +
            "Activity indicates packets, not proof of relay clock synchronization.";
        dot.ToolTip = tooltip;
        if (dot.Parent is FrameworkElement parent)
            parent.ToolTip = tooltip;
        _globalSntpIpPicker.ToolTip = _selectedSntpBinding is null
            ? "Select an active PC IPv4 address."
            : $"{_selectedSntpBinding.InterfaceName} · {_selectedSntpBinding.LocalAddress}\n" +
              $"Broadcast: {_selectedSntpBinding.DirectedBroadcast?.ToString() ?? "N/A"}";
    }

    private void FlashNtpTrafficRing(bool successfulReply)
    {
        if (_globalSntpPulseRing is not { } ring)
            return;
        ring.Stroke = (Brush)FindResource(successfulReply ? "Success" : "Warning");
        ring.BeginAnimation(UIElement.OpacityProperty, new DoubleAnimation
        {
            From = 0.95, To = 0,
            Duration = TimeSpan.FromMilliseconds(720),
            FillBehavior = FillBehavior.Stop
        });
        if (ring.RenderTransform is ScaleTransform scale)
        {
            var duration = new Duration(TimeSpan.FromMilliseconds(720));
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.65, 1.55, duration));
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.65, 1.55, duration));
        }
    }
}
