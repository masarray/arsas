using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private Button? _nativeFatComtradeButton;
    private Button? _nativeFatTimeSyncButton;
    private CancellationTokenSource? _nativeFatComtradeDiscoveryCts;
    private long _nativeFatComtradeDiscoveryGeneration;
    private DispatcherTimer? _nativeFatDiagnosticRefreshTimer;

    private void InstallNativeFatDiagnosticButtons()
    {
        if (_nativeFatPrintPreviewButton?.Parent is not StackPanel actionPanel)
            return;

        if (_nativeFatComtradeButton != null && actionPanel.Children.Contains(_nativeFatComtradeButton))
            return;

        var printIndex = actionPanel.Children.IndexOf(_nativeFatPrintPreviewButton);
        if (printIndex < 0)
            return;

        _nativeFatComtradeButton = new Button
        {
            Content = "COMTRADE —",
            MinWidth = 112,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource("SoftButton") as Style,
            IsEnabled = false,
            ToolTip = "Actual IEC 61850 FileDirectory evidence for the selected IED."
        };
        _nativeFatComtradeButton.Click += NativeFatComtradeButton_Click;

        _nativeFatTimeSyncButton = new Button
        {
            Content = "Time Sync Review",
            MinWidth = 116,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(0, 0, 8, 0),
            Style = TryFindResource("SoftButton") as Style,
            IsEnabled = false,
            ToolTip = "Device-side time synchronization evidence. SNTP activity alone never grants OK."
        };
        _nativeFatTimeSyncButton.Click += NativeFatTimeSyncButton_Click;

        actionPanel.Children.Insert(printIndex, _nativeFatComtradeButton);
        actionPanel.Children.Insert(printIndex + 1, _nativeFatTimeSyncButton);
    }

    private void BindNativeFatDiagnostics(Iec61850MonitorDevice? device)
    {
        CancelNativeFatComtradeDiscovery();

        if (_nativeFatComtradeButton == null || _nativeFatTimeSyncButton == null)
            return;

        if (device == null)
        {
            _nativeFatComtradeButton.Content = "COMTRADE —";
            _nativeFatComtradeButton.IsEnabled = false;
            _nativeFatComtradeButton.ToolTip = "Select a connected Engineering IED to inspect its IEC 61850 file store.";
            _nativeFatTimeSyncButton.Content = "Time Sync Review";
            _nativeFatTimeSyncButton.IsEnabled = false;
            _nativeFatTimeSyncButton.ToolTip = "Select an Engineering IED to inspect device-side time evidence.";
            StopNativeFatDiagnosticRefreshTimer();
            return;
        }

        _nativeFatTimeSyncButton.IsEnabled = device.Points.Count > 0;
        RefreshNativeFatTimeSyncButton(device);
        StartNativeFatDiagnosticRefreshTimer();

        if (!device.IsConnected || string.IsNullOrWhiteSpace(device.IpAddress))
        {
            _nativeFatComtradeButton.Content = "COMTRADE —";
            _nativeFatComtradeButton.IsEnabled = false;
            _nativeFatComtradeButton.ToolTip = "FileDirectory evidence is unavailable while the selected IED is disconnected.";
            return;
        }

        _nativeFatComtradeButton.IsEnabled = true;
        BeginNativeFatComtradeDiscovery(device);
    }

    private void BeginNativeFatComtradeDiscovery(Iec61850MonitorDevice device)
    {
        CancelNativeFatComtradeDiscovery();
        var generation = Interlocked.Increment(ref _nativeFatComtradeDiscoveryGeneration);
        var cts = new CancellationTokenSource();
        _nativeFatComtradeDiscoveryCts = cts;
        _ = DiscoverNativeFatComtradeAsync(device, generation, cts.Token);
    }

    private async Task DiscoverNativeFatComtradeAsync(
        Iec61850MonitorDevice device,
        long generation,
        CancellationToken cancellationToken)
    {
        if (_nativeFatComtradeButton == null)
            return;

        _nativeFatComtradeButton.Content = "COMTRADE …";
        _nativeFatComtradeButton.ToolTip = "Reading the selected IED's IEC 61850 FileDirectory catalog…";

        try
        {
            await using var client = new FaultRecordTransferClient();
            await client.ConnectAsync(device.IpAddress, device.Port, cancellationToken);
            var catalog = await client.DiscoverAsync(remoteDirectory: null, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            if (generation != _nativeFatComtradeDiscoveryGeneration ||
                !string.Equals(SelectedDevice?.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                _nativeFatComtradeButton == null)
            {
                return;
            }

            var fileCount = NativeFatComtradeDiagnosticService.CountDetectedFiles(catalog.Records);
            _nativeFatComtradeButton.Content = $"COMTRADE {fileCount} Files";
            _nativeFatComtradeButton.ToolTip =
                $"IEC 61850 FileDirectory: {fileCount:N0} actual file(s) across {catalog.Records.Count:N0} discovered fault record(s). " +
                "Click to open the existing COMTRADE / fault-record file workflow.";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or TimeoutException)
        {
            if (generation != _nativeFatComtradeDiscoveryGeneration || _nativeFatComtradeButton == null)
                return;

            _nativeFatComtradeButton.Content = "COMTRADE —";
            _nativeFatComtradeButton.ToolTip =
                $"IEC 61850 FileDirectory could not be verified: {ex.Message}\nNo COMTRADE count is fabricated.";
        }
    }

    private void NativeFatComtradeButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device == null || !device.IsConnected || string.IsNullOrWhiteSpace(device.IpAddress))
            return;

        CancelNativeFatComtradeDiscovery();
        var window = new FaultRecordWindow(device.Name, device.IpAddress, device.Port)
        {
            Owner = this
        };
        window.Closed += (_, _) =>
        {
            if (string.Equals(SelectedDevice?.DeviceId, device.DeviceId, StringComparison.OrdinalIgnoreCase) &&
                device.IsConnected)
            {
                BeginNativeFatComtradeDiscovery(device);
            }
        };
        window.Show();
    }

    private void NativeFatTimeSyncButton_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device == null)
            return;

        var diagnostic = NativeFatTimeSyncDiagnosticService.Evaluate(device, DateTimeOffset.UtcNow);
        var text = BuildNativeFatTimeSyncDiagnosticText(device, diagnostic);
        var body = new TextBox
        {
            Text = text,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            AcceptsReturn = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12.2,
            Padding = new Thickness(16),
            BorderThickness = new Thickness(0),
            Background = TryFindResource("CardBackground") as Brush ?? Brushes.White,
            Foreground = TryFindResource("Ink") as Brush ?? Brushes.Black
        };

        var window = new Window
        {
            Title = $"Time Synchronization · {device.Name}",
            Owner = this,
            Width = 760,
            Height = 640,
            MinWidth = 620,
            MinHeight = 480,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Content = body
        };
        window.Show();
    }

    private string BuildNativeFatTimeSyncDiagnosticText(
        Iec61850MonitorDevice device,
        NativeFatTimeSyncDiagnosticResult diagnostic)
    {
        var snapshot = ClockSyncSnapshot;
        var builder = new StringBuilder();
        builder.AppendLine($"IED              : {device.Name}");
        builder.AppendLine($"Endpoint         : {device.IpAddress}:{device.Port}");
        builder.AppendLine($"ARSAS verdict    : {diagnostic.Verdict}");
        builder.AppendLine($"Reason           : {diagnostic.Summary}");
        builder.AppendLine();
        builder.AppendLine("DEVICE-SIDE AUTHORITY");
        builder.AppendLine($"LTMS present     : {diagnostic.LtmsPresent}");
        builder.AppendLine($"LTMS trusted     : {diagnostic.LtmsTrusted}");
        builder.AppendLine($"Fresh IEC stamps : {diagnostic.FreshPrimaryTimestampCount}");
        builder.AppendLine($"Allowed delta    : ±{NativeFatTimeSyncDiagnosticService.MaximumTrustedClockDelta.TotalSeconds:0} s");
        builder.AppendLine($"Negative sync flag: {diagnostic.ExplicitNegativeSyncStatus}");

        if (diagnostic.PrimaryEvidence.Count == 0)
        {
            builder.AppendLine("  — no authoritative LTMS/fresh timestamp evidence in the current canonical live rows");
        }
        else
        {
            foreach (var evidence in diagnostic.PrimaryEvidence)
            {
                builder.AppendLine(
                    $"  [{evidence.Role}] {(evidence.Trusted ? "trusted" : "review")} · {ValueOrDash(evidence.IecReference)} · " +
                    $"Q={ValueOrDash(evidence.Quality)} · T={ValueOrDash(evidence.DeviceTimestamp)}" +
                    (evidence.DeltaSeconds.HasValue ? $" · Δ={evidence.DeltaSeconds.Value:0.000}s" : string.Empty));
            }
        }

        builder.AppendLine();
        builder.AppendLine("SECONDARY IED TELEMETRY");
        if (diagnostic.SecondaryTelemetry.Count == 0)
        {
            builder.AppendLine("  — Server 1 / Server 2 / Current server / TimeSynchrnz not exposed in current live rows");
        }
        else
        {
            foreach (var evidence in diagnostic.SecondaryTelemetry)
            {
                builder.AppendLine(
                    $"  {ValueOrDash(evidence.SignalName)} · {ValueOrDash(evidence.IecReference)} · " +
                    $"Value={ValueOrDash(evidence.Value)} · Q={ValueOrDash(evidence.Quality)}");
            }
        }

        builder.AppendLine();
        builder.AppendLine("ARSAS SNTP PACKET TELEMETRY (SUPPORTING ONLY)");
        builder.AppendLine($"Enabled          : {IsClockSyncEnabled}");
        builder.AppendLine($"Service state    : {snapshot.State}");
        builder.AppendLine($"Transport        : {snapshot.TransportMode}");
        builder.AppendLine($"Binding          : {snapshot.Binding?.Summary ?? "—"}");
        builder.AppendLine($"Broadcasts       : {snapshot.BroadcastCount}");
        builder.AppendLine($"Client requests  : {snapshot.ClientRequestCount}");
        builder.AppendLine($"Replies sent     : {snapshot.ReplyCount}");
        builder.AppendLine($"Selected IED request observed: {_clockSyncObservedClients.Contains(device.IpAddress)}");
        builder.AppendLine($"Selected IED reply sent      : {_clockSyncRepliedClients.Contains(device.IpAddress)}");
        builder.AppendLine();
        builder.AppendLine("SNTP server activity, request/reply counters and a positive TimeSynchrnz value are supporting evidence only.");
        builder.AppendLine("They never grant 'Time Sync OK' without the device-side LTMS/timestamp evidence evaluated above.");
        return builder.ToString();
    }

    private void RefreshNativeFatTimeSyncButton(Iec61850MonitorDevice? device)
    {
        if (_nativeFatTimeSyncButton == null)
            return;

        if (device == null || device.Points.Count == 0)
        {
            _nativeFatTimeSyncButton.Content = "Time Sync Review";
            _nativeFatTimeSyncButton.IsEnabled = false;
            _nativeFatTimeSyncButton.ToolTip = "No canonical live rows are available for device-side time evidence.";
            return;
        }

        var diagnostic = NativeFatTimeSyncDiagnosticService.Evaluate(device, DateTimeOffset.UtcNow);
        _nativeFatTimeSyncButton.IsEnabled = true;
        _nativeFatTimeSyncButton.Content = diagnostic.IsSynchronized
            ? "Time Sync OK"
            : diagnostic.ExplicitNegativeSyncStatus
                ? "Time Sync NOT OK"
                : "Time Sync Review";
        _nativeFatTimeSyncButton.ToolTip =
            $"{diagnostic.Verdict}: {diagnostic.Summary}\n" +
            "Click for LTMS, IEC timestamp/quality, vendor time telemetry and ARSAS SNTP packet evidence.";
        _nativeFatTimeSyncButton.Foreground = diagnostic.IsSynchronized
            ? Brushes.SeaGreen
            : diagnostic.ExplicitNegativeSyncStatus
                ? Brushes.Firebrick
                : TryFindResource("MutedInk") as Brush ?? Brushes.DarkSlateGray;
    }

    private void StartNativeFatDiagnosticRefreshTimer()
    {
        _nativeFatDiagnosticRefreshTimer ??= CreateNativeFatDiagnosticRefreshTimer();
        if (!_nativeFatDiagnosticRefreshTimer.IsEnabled)
            _nativeFatDiagnosticRefreshTimer.Start();
    }

    private DispatcherTimer CreateNativeFatDiagnosticRefreshTimer()
    {
        var timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        timer.Tick += NativeFatDiagnosticRefreshTimer_Tick;
        return timer;
    }

    private void NativeFatDiagnosticRefreshTimer_Tick(object? sender, EventArgs e)
    {
        var device = SelectedDevice;
        if (device == null || !string.Equals(_nativeFatBoundIedKey, device.DeviceId, StringComparison.OrdinalIgnoreCase))
        {
            StopNativeFatDiagnosticRefreshTimer();
            return;
        }

        // In-memory canonical-row evaluation only. This timer performs zero network reads.
        RefreshNativeFatTimeSyncButton(device);
    }

    private void StopNativeFatDiagnosticRefreshTimer()
        => _nativeFatDiagnosticRefreshTimer?.Stop();

    private void CancelNativeFatComtradeDiscovery()
    {
        Interlocked.Increment(ref _nativeFatComtradeDiscoveryGeneration);
        _nativeFatComtradeDiscoveryCts?.Cancel();
        _nativeFatComtradeDiscoveryCts?.Dispose();
        _nativeFatComtradeDiscoveryCts = null;
    }

    private void DisposeNativeFatDiagnostics()
    {
        CancelNativeFatComtradeDiscovery();
        if (_nativeFatDiagnosticRefreshTimer != null)
        {
            _nativeFatDiagnosticRefreshTimer.Tick -= NativeFatDiagnosticRefreshTimer_Tick;
            _nativeFatDiagnosticRefreshTimer.Stop();
            _nativeFatDiagnosticRefreshTimer = null;
        }

        if (_nativeFatComtradeButton != null)
            _nativeFatComtradeButton.Click -= NativeFatComtradeButton_Click;
        if (_nativeFatTimeSyncButton != null)
            _nativeFatTimeSyncButton.Click -= NativeFatTimeSyncButton_Click;
        _nativeFatComtradeButton = null;
        _nativeFatTimeSyncButton = null;
    }

    private static string ValueOrDash(string? value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();
}
