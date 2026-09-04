using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private TextBlock? _clockSyncGlobalStatusText;
    private TextBlock? _clockSyncEvidenceText;
    private MainWindow? _clockSyncSnapshotOwner;

    protected override void OnInitialized(EventArgs e)
    {
        base.OnInitialized(e);
        Loaded += ClockSyncUx_Loaded;
        Closed += ClockSyncUx_Closed;
    }

    private void ClockSyncUx_Loaded(object sender, RoutedEventArgs e)
    {
        if (_clockSyncGlobalStatusText != null)
        {
            AttachClockSyncSnapshotOwner();
            return;
        }

        if (WorkspacePreviewToggle.Parent is not Panel actionPanel)
            return;

        var previewIndex = actionPanel.Children.IndexOf(WorkspacePreviewToggle);
        if (previewIndex < 0)
            return;

        var globalStatus = new TextBlock
        {
            Name = "GlobalSntpStatusTextBlock",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(2, 0, 2, 0),
            FontSize = 11.2,
            FontWeight = FontWeights.SemiBold,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.DimGray,
            Text = "Global SNTP",
            ToolTip = "Global SNTP is controlled from the ARSAS header. It is not owned by FAT and continues running when the FAT window closes while the global server toggle remains enabled."
        };

        var evidence = new TextBlock
        {
            Name = "ClockSyncEvidenceTextBlock",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0),
            FontSize = 10.4,
            FontWeight = FontWeights.Medium,
            Foreground = TryFindResource("MutedInk") as Brush ?? Brushes.SlateGray,
            Text = "SNTP: waiting"
        };

        _clockSyncGlobalStatusText = globalStatus;
        _clockSyncEvidenceText = evidence;
        actionPanel.Children.Insert(previewIndex + 1, globalStatus);
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
        if (_clockSyncEvidenceText == null || _clockSyncGlobalStatusText == null)
            return;

        var enabled = _clockSyncSnapshotOwner?.IsClockSyncEnabled == true;
        var transport = snapshot.TransportMode switch
        {
            SntpClockTransportMode.NpcapRaw => "RAW",
            SntpClockTransportMode.UdpSocket => "UDP",
            _ => "—"
        };
        var localAddress = snapshot.Binding?.LocalAddress.ToString();

        _clockSyncGlobalStatusText.Text = !enabled
            ? "Global SNTP · Off"
            : snapshot.State == SntpClockServiceState.Serving
                ? $"Global SNTP · {localAddress ?? "Active"}"
                : "Global SNTP · Enabled";

        _clockSyncEvidenceText.Text = !enabled
            ? "SNTP: off"
            : snapshot.State switch
            {
                SntpClockServiceState.Serving =>
                    $"{transport} · B {snapshot.BroadcastCount} · Req {snapshot.ClientRequestCount} · Reply {snapshot.ReplyCount} · sync not proven",
                SntpClockServiceState.Starting => "SNTP: starting…",
                SntpClockServiceState.Stopped => "SNTP: waiting for connected IED",
                SntpClockServiceState.PortUnavailable => "SNTP: unavailable",
                SntpClockServiceState.Faulted => "SNTP: fault",
                _ => $"SNTP: {snapshot.State}"
            };

        var toolTip = BuildClockSyncEvidenceToolTip(snapshot, transport, enabled);
        _clockSyncGlobalStatusText.ToolTip = toolTip;
        _clockSyncEvidenceText.ToolTip = toolTip;
        _clockSyncEvidenceText.Foreground = snapshot.State switch
        {
            SntpClockServiceState.PortUnavailable or SntpClockServiceState.Faulted => Brushes.DarkOrange,
            SntpClockServiceState.Serving when snapshot.ReplyCount > 0 => Brushes.SeaGreen,
            _ => TryFindResource("MutedInk") as Brush ?? Brushes.SlateGray
        };
    }

    private static string BuildClockSyncEvidenceToolTip(
        SntpClockServiceSnapshot snapshot,
        string transport,
        bool enabled)
    {
        var binding = snapshot.Binding == null ? "—" : snapshot.Binding.Summary;
        var lastBroadcast = snapshot.LastBroadcastUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        var lastRequest = snapshot.LastRequestUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        var lastReply = snapshot.LastReplyUtc?.ToLocalTime().ToString("HH:mm:ss.fff") ?? "—";
        return $"Global ARSAS SNTP Server\n" +
               $"Enabled: {enabled}\n" +
               $"State: {snapshot.State}\n" +
               $"Transport: {transport}\n" +
               $"Binding: {binding}\n" +
               $"Broadcast sent: {snapshot.BroadcastCount} (last {lastBroadcast})\n" +
               $"Client request seen: {snapshot.ClientRequestCount} (last {lastRequest})\n" +
               $"Mode 4 reply sent: {snapshot.ReplyCount} (last {lastReply})\n\n" +
               "The global server is controlled from the ARSAS header and continues outside FAT while enabled. " +
               "These counters prove packet activity only. ARSAS does not claim that the relay clock is synchronized without device-side evidence.\n\n" +
               snapshot.Detail;
    }

    private void ClockSyncUx_Closed(object? sender, EventArgs e)
    {
        if (_clockSyncSnapshotOwner != null)
            _clockSyncSnapshotOwner.ClockSyncSnapshotChanged -= ClockSyncSnapshotChanged;
        _clockSyncSnapshotOwner = null;
    }
}
