using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private Button? _comtradeEvidenceButton;
    private Button? _timeSyncEvidenceButton;

    public int SelectedSupplementalEvidenceCount
    {
        get
        {
            if (SelectedIed == null)
                return 0;

            var comtrade = IoFatSupplementalEvidenceService.Count(
                Storage,
                SelectedIed.IedName,
                IoFatSupplementalEvidenceService.ComtradeKind);
            var timeSync = IoFatSupplementalEvidenceService.ReadLatest(
                Storage,
                SelectedIed.IedName,
                IoFatSupplementalEvidenceService.TimeSyncKind) == null ? 0 : 1;
            return comtrade + timeSync;
        }
    }

    private void InstallSupplementalEvidenceControls()
    {
        if (_comtradeEvidenceButton != null || WorkspacePreviewToggle.Parent is not Panel actionPanel)
            return;

        _timeSyncEvidenceButton = CreateEvidenceButton("Time Sync · —", RefreshTimeSyncEvidence_Click);
        _timeSyncEvidenceButton.ToolTip = "Capture IEC 61850 time-synchronization evidence for the selected IED";

        _comtradeEvidenceButton = CreateEvidenceButton("COMTRADE · 0", OpenComtradeEvidence_Click);
        _comtradeEvidenceButton.ToolTip = "Browse/download relay fault records; detected local COMTRADE becomes FAT evidence";

        var insertionIndex = actionPanel.Children.IndexOf(WorkspacePreviewToggle) + 1;
        actionPanel.Children.Insert(insertionIndex, _timeSyncEvidenceButton);
        actionPanel.Children.Insert(insertionIndex + 1, _comtradeEvidenceButton);
        RefreshSupplementalEvidenceControls();
    }

    private Button CreateEvidenceButton(string content, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(10, 8),
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 92
        };
        if (TryFindResource("SoftButton") is Style style)
            button.Style = style;
        button.Click += handler;
        return button;
    }

    private void RefreshSupplementalEvidenceControls()
    {
        if (_timeSyncEvidenceButton == null || _comtradeEvidenceButton == null)
            return;

        var ied = SelectedIed;
        if (ied == null)
        {
            _timeSyncEvidenceButton.Content = "Time Sync · —";
            _timeSyncEvidenceButton.ToolTip = "Select an IED first";
            _comtradeEvidenceButton.Content = "COMTRADE · 0";
            _comtradeEvidenceButton.ToolTip = "Select an IED first";
            return;
        }

        var timeSync = IoFatSupplementalEvidenceService.ReadLatest(
            Storage,
            ied.IedName,
            IoFatSupplementalEvidenceService.TimeSyncKind);
        _timeSyncEvidenceButton.Content = timeSync == null
            ? "Time Sync · —"
            : $"Time Sync · {timeSync.Verdict}";
        _timeSyncEvidenceButton.ToolTip = timeSync == null
            ? "No time-sync evidence captured yet. It will be captured automatically after FAT connection."
            : $"{timeSync.DisplayText}\n{timeSync.Reason}\nCaptured {timeSync.RecordedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

        var comtradeCount = IoFatSupplementalEvidenceService.Count(
            Storage,
            ied.IedName,
            IoFatSupplementalEvidenceService.ComtradeKind);
        var latestComtrade = IoFatSupplementalEvidenceService.ReadLatest(
            Storage,
            ied.IedName,
            IoFatSupplementalEvidenceService.ComtradeKind);
        _comtradeEvidenceButton.Content = $"COMTRADE · {comtradeCount}";
        _comtradeEvidenceButton.ToolTip = latestComtrade == null
            ? "Open relay fault records. Downloaded or already-detected local records become IED-level FAT evidence."
            : $"Latest: {latestComtrade.DisplayText}\nSHA-256: {latestComtrade.ArtifactSha256}\n{latestComtrade.ArtifactPath}";
    }

    private async Task CaptureTimeSyncEvidenceAfterPreparationAsync(
        MainWindow engineeringWindow,
        IoTestIedPlan ied)
    {
        var device = FindFatDevice(engineeringWindow, ied);
        if (device == null)
            return;

        // Give the extra read-only sync signal a short bounded window to receive its
        // first report/poll value after the FAT monitor starts. Do not block the FAT
        // workflow when a relay does not expose a recognized status object.
        var syncSignal = IoFatSupplementalEvidenceService.FindTimeSyncSignal(device);
        if (syncSignal != null)
        {
            for (var attempt = 0; attempt < 8 && !HasLiveValue(syncSignal.Value); attempt++)
                await Task.Delay(175).ConfigureAwait(true);
        }

        IoFatSupplementalEvidenceService.CaptureTimeSync(Storage, Project, ied, device);
        Storage?.ScheduleSave();
        RefreshSupplementalEvidenceControls();
        Raise(nameof(SelectedSupplementalEvidenceCount));
        Raise(nameof(SelectedEvidenceCount));
    }

    private async void RefreshTimeSyncEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedIed == null)
            return;

        if (Owner is not MainWindow engineeringWindow)
        {
            MessageBox.Show(
                this,
                "Open this FAT workspace from the ARSAS engineering window to refresh live time-sync evidence.",
                "Time Sync Evidence",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        var device = FindFatDevice(engineeringWindow, SelectedIed);
        if (device == null || !device.IsConnected)
        {
            MessageBox.Show(
                this,
                "Connect the selected IED first. ARSAS will then capture its explicit synchronization status or timestamp fallback.",
                "Time Sync Evidence",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        await CaptureTimeSyncEvidenceAfterPreparationAsync(engineeringWindow, SelectedIed);
    }

    private void OpenComtradeEvidence_Click(object sender, RoutedEventArgs e)
    {
        var ied = SelectedIed;
        if (ied == null)
            return;

        var window = new FaultRecordWindow(ied.IedName, ied.IpAddress, 102)
        {
            Owner = this
        };
        window.ShowDialog();

        var captured = 0;
        foreach (var row in window.Records.Where(row =>
                     row.LocalState == FaultRecordLocalState.Downloaded &&
                     !string.IsNullOrWhiteSpace(row.LocalDirectory)))
        {
            var evidence = IoFatSupplementalEvidenceService.CaptureComtrade(
                Storage,
                Project,
                ied,
                row.RecordName,
                row.LocalDirectory);
            if (evidence != null)
                captured++;
        }

        if (captured > 0)
        {
            PreparationStatusText = $"{ied.IedName} · COMTRADE/fault-record evidence detected and journaled.";
            Storage?.ScheduleSave();
        }

        RefreshSupplementalEvidenceControls();
        Raise(nameof(SelectedSupplementalEvidenceCount));
        Raise(nameof(SelectedEvidenceCount));
    }

    private static Iec61850MonitorDevice? FindFatDevice(MainWindow engineeringWindow, IoTestIedPlan ied)
        => engineeringWindow.Devices.FirstOrDefault(device =>
               (device.Name.Equals(ied.IedName, StringComparison.OrdinalIgnoreCase) ||
                device.SclIedName.Equals(ied.IedName, StringComparison.OrdinalIgnoreCase)) &&
               device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase))
           ?? engineeringWindow.Devices.FirstOrDefault(device =>
               device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase));

    private static bool HasLiveValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" && text != "—" &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Contains("not probed", StringComparison.OrdinalIgnoreCase);
    }
}
