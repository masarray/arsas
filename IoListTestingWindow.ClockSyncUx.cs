using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private CheckBox? _clockSyncCheckBox;
    private TextBlock? _clockSyncEvidenceText;
    private MainWindow? _clockSyncSnapshotOwner;
    private bool _clockSyncCheckBoxRefreshing;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += ClockSyncUx_Loaded;
        Closed += ClockSyncUx_Closed;
    }

    private void ClockSyncUx_Loaded(object sender, RoutedEventArgs e)
    {
        if (_clockSyncCheckBox != null)
        {
            RefreshClockSyncCheckBox();
            AttachClockSyncSnapshotOwner();
            return;
        }

        if (WorkspacePreviewToggle.Parent is not Panel actionPanel)
            return;

        var previewIndex = actionPanel.Children.IndexOf(WorkspacePreviewToggle);
        if (previewIndex < 0)
            return;

        var checkBox = new CheckBox
        {
            Name = "ClockSyncEnabledCheckBox",
            Content = "Clock Sync",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(2, 0, 2, 0),
            FontSize = 11.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.DimGray,
            Focusable = false,
            ToolTip = "SNTP laptop → IED. Checked: ARSAS serves laptop time using normal UDP/123 when available, with an Npcap RAW fallback when Windows already owns UDP/123. Unchecked: ARSAS stops only its Clock Sync service. IEC 61850 remains unaffected."
        };

        var evidence = new TextBlock
        {
            Name = "ClockSyncEvidenceTextBlock",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            FontSize = 10.4,
            FontWeight = FontWeights.Medium,
            Foreground = TryFindResource("MutedInk") as Brush ?? Brushes.SlateGray,
            Text = "Clock: waiting"
        };

        _clockSyncCheckBox = checkBox;
        _clockSyncEvidenceText = evidence;
        RefreshClockSyncCheckBox();
        checkBox.Checked += ClockSyncCheckBox_Changed;
        checkBox.Unchecked += ClockSyncCheckBox_Changed;
        actionPanel.Children.Insert(previewIndex + 1, checkBox);
        actionPanel.Children.Insert(previewIndex + 2, evidence);
        AttachClockSyncSnapshotOwner();
    }

    private void AttachClockSyncSnapshotOwner()
    {
        if (Owner is not MainWindow mainWindow)
            return;

        if (!ReferenceEquals(_clockSyncSnapshotOwner, mainWindow))
        {
            if (_clockSyncSnapshotOwner != null)
                _clockSyncSnapshotOwner.ClockSyncSnapshotChanged -= ClockSyncSnapshotChanged;

            _clockSyncSnapshotOwner = mainWindow;
            mainWindow.ClockSyncSnapshotChanged += ClockSyncSnapshotChanged;
        }

        RefreshClockSyncEvidence(mainWindow.ClockSyncSnapshot);
    }

    private void ClockSyncSnapshotChanged(SntpClockServiceSnapshot snapshot)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() => RefreshClockSyncEvidence(snapshot)));
            return;
        }

        RefreshClockSyncEvidence(snapshot);
    }

    private void RefreshClockSyncEvidence(SntpClockServiceSnapshot snapshot)
    {
        if (_clockSyncEvidenceText == null)
            return;

        var transport = snapshot.TransportMode switch
        {
            SntpClockTransportMode.NpcapRaw => "RAW",
            SntpClockTransportMode.UdpSocket => "UDP",
            _ => "—"
        };

        _clockSyncEvidenceText.Text = snapshot.State switch
        {
            SntpClockServiceState.Serving =>
                $"{transport} · B {snapshot.BroadcastCount} · Req {snapshot.ClientRequestCount} · Reply {snapshot.ReplyCount} · sync not proven",
            SntpClockServiceState.Starting => "Clock: starting…",
            SntpClockServiceState.Stopped => "Clock: off",
            SntpClockServiceState.PortUnavailable => "Clock: unavailable",
            SntpClockServiceState.Faulted => "Clock: fault",
            _ => $"Clock: {snapshot.State}"
        };

        _clockSyncEvidenceText.ToolTip = BuildClockSyncEvidenceToolTip(snapshot, transport);
        _clockSyncEvidenceText.Foreground = snapshot.State switch
        {
            SntpClockServiceState.PortUnavailable or SntpClockServiceState.Faulted => Brushes.DarkOrange,
            SntpClockServiceState.Serving when snapshot.ReplyCount > 0 => Brushes.SeaGreen,
            _ => TryFindResource("MutedInk") as Brush ?? Brushes.SlateGray
        };
    }

    private static string BuildClockSyncEvidenceToolTip(SntpClockServiceSnapshot snapshot, string transport)
    {
        var binding = snapshot.Binding == null ? "—" : snapshot.Binding.Summary;
        var lastBroadcast = snapshot.LastBroadcastUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        var lastRequest = snapshot.LastRequestUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        var lastReply = snapshot.LastReplyUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        return $"Clock Sync evidence\n" +
               $"State: {snapshot.State}\n" +
               $"Transport: {transport}\n" +
               $"Binding: {binding}\n" +
               $"Broadcast sent: {snapshot.BroadcastCount} (last {lastBroadcast})\n" +
               $"Client request seen: {snapshot.ClientRequestCount} (last {lastRequest})\n" +
               $"Mode 4 reply sent: {snapshot.ReplyCount} (last {lastReply})\n\n" +
               "These counters prove packet activity only. ARSAS does not claim that the relay clock is synchronized without device-side evidence.\n\n" +
               snapshot.Detail;
    }

    private void RefreshClockSyncCheckBox()
    {
        if (_clockSyncCheckBox == null || Owner is not MainWindow mainWindow)
            return;

        _clockSyncCheckBoxRefreshing = true;
        try
        {
            _clockSyncCheckBox.IsChecked = mainWindow.IsClockSyncEnabled;
        }
        finally
        {
            _clockSyncCheckBoxRefreshing = false;
        }
    }

    private async void ClockSyncCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (_clockSyncCheckBoxRefreshing ||
            _clockSyncCheckBox == null ||
            Owner is not MainWindow mainWindow)
            return;

        var requested = _clockSyncCheckBox.IsChecked == true;
        _clockSyncCheckBox.IsEnabled = false;
        try
        {
            await mainWindow.SetClockSyncEnabledAsync(requested);
            RefreshClockSyncCheckBox();
            RefreshClockSyncEvidence(mainWindow.ClockSyncSnapshot);
        }
        finally
        {
            _clockSyncCheckBox.IsEnabled = true;
        }
    }

    private void ClockSyncUx_Closed(object? sender, EventArgs e)
    {
        if (_clockSyncSnapshotOwner != null)
            _clockSyncSnapshotOwner.ClockSyncSnapshotChanged -= ClockSyncSnapshotChanged;
        _clockSyncSnapshotOwner = null;
    }
}
