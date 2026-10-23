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
    private Button? _cleanSessionButton;

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

        _timeSyncEvidenceButton = CreateEvidenceButton("Sync · —", RefreshTimeSyncEvidence_Click);
        _timeSyncEvidenceButton.ToolTip = "Capture IEC 61850 time-synchronization evidence for the selected IED";

        _comtradeEvidenceButton = CreateEvidenceButton("COMTRADE · —", OpenComtradeEvidence_Click);
        _comtradeEvidenceButton.ToolTip = "Browse relay fault records. A remote COMTRADE listing is sufficient File Service FAT evidence; download is optional.";

        _cleanSessionButton = CreateEvidenceButton("Clean FAT", NewCleanFatSession_Click);
        _cleanSessionButton.ToolTip = "Archive all current FAT evidence and reset the project to a zero-evidence retest session";

        var insertionIndex = actionPanel.Children.IndexOf(WorkspacePreviewToggle) + 1;
        actionPanel.Children.Insert(insertionIndex, _timeSyncEvidenceButton);
        actionPanel.Children.Insert(insertionIndex + 1, _comtradeEvidenceButton);
        actionPanel.Children.Insert(insertionIndex + 2, _cleanSessionButton);
        RefreshSupplementalEvidenceControls();
    }

    private Button CreateEvidenceButton(string content, RoutedEventHandler handler)
    {
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 0, 6, 0),
            MinWidth = 0
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
            _timeSyncEvidenceButton.Content = "Sync · —";
            _timeSyncEvidenceButton.ToolTip = "Select an IED first";
            _comtradeEvidenceButton.Content = "COMTRADE · —";
            _comtradeEvidenceButton.ToolTip = "Select an IED first";
            return;
        }

        var timeSync = IoFatSupplementalEvidenceService.ReadLatest(
            Storage,
            ied.IedName,
            IoFatSupplementalEvidenceService.TimeSyncKind);
        _timeSyncEvidenceButton.Content = timeSync == null
            ? "Sync · —"
            : $"Sync · {timeSync.Verdict}";
        _timeSyncEvidenceButton.ToolTip = timeSync == null
            ? "No time-sync evidence captured yet. It will be captured automatically after FAT connection."
            : $"{timeSync.DisplayText}\n{timeSync.Reason}\nCaptured {timeSync.RecordedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm:ss}";

        var comtradeCount = IoFatSupplementalEvidenceService.Count(
            Storage,
            ied.IedName,
            IoFatSupplementalEvidenceService.ComtradeKind);
        _comtradeEvidenceButton.Content = ied.HasRemoteComtradeEvidence
            ? $"COMTRADE · PASS · {comtradeCount}"
            : $"COMTRADE · {comtradeCount}";
        _comtradeEvidenceButton.ToolTip = ied.HasRemoteComtradeEvidence
            ? $"File Service PASS via IEC 61850 FileDirectory\nLatest remote COMTRADE: {ied.LatestComtradeFiles}\nRemote path: {ied.LatestComtradeRemotePath}\nDownload is optional additional verification."
            : "Open relay fault records. A supported remote COMTRADE returned by IEC 61850 FileDirectory becomes IED-level FAT evidence immediately; download is optional.";
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

    private void NewCleanFatSession_Click(object sender, RoutedEventArgs e)
    {
        var answer = MessageBox.Show(
            this,
            "Create a NEW CLEAN FAT session for this project?\n\n" +
            "• All current ON/OFF evidence and relay timestamps will be cleared from the active workspace.\n" +
            "• Time Sync and COMTRADE evidence will be cleared from the active report.\n" +
            "• Existing hash-chained evidence files will NOT be deleted or rewritten; they will be verified and archived outside the new export scope.\n" +
            "• All import-ready IO points will be enabled again for retest.\n\n" +
            "Use this after correcting relay time synchronization or whenever the customer requires a completely fresh FAT run.",
            "New Clean FAT Session",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            if (Session.IsSessionActive)
            {
                var stopped = Session.Stop("Sealed before New Clean FAT retest reset.");
                if (!stopped.Succeeded)
                {
                    ShowActionResult(stopped, "Could not seal the active FAT session");
                    return;
                }
            }

            var storage = Storage ?? throw new InvalidOperationException("FAT workspace persistence is not available.");
            var result = IoFatCleanSessionService.ResetForRetest(storage, Project);
            var sessionReset = Session.ResetForCleanRetest();
            if (!sessionReset.Succeeded)
            {
                ShowActionResult(sessionReset, "Clean FAT session state could not be reset");
                return;
            }

            PreparationStatusText = result.ArchivedJournalCount > 0
                ? $"NEW CLEAN FAT ready · {result.ResetPointCount} point(s) reset · {result.ArchivedJournalCount} prior journal(s) archived"
                : $"NEW CLEAN FAT ready · {result.ResetPointCount} point(s) reset · active evidence is empty";
            RefreshSupplementalEvidenceControls();
            Raise(nameof(SelectedSupplementalEvidenceCount));
            Raise(nameof(SelectedEvidenceCount));
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "New Clean FAT Session failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
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

        // A successful FileDirectory browse that returns a supported fault record is the
        // primary FAT evidence. Download is intentionally not a prerequisite.
        var remoteEvidence = IoFatRemoteComtradeEvidenceService.CaptureLatest(
            Storage,
            Project,
            ied,
            window.Records.Select(row => row.Record));

        // Preserve stronger optional local-artifact evidence when the operator also
        // downloaded a record. A failed or skipped download never removes remote PASS.
        var downloadedCaptured = 0;
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
                downloadedCaptured++;
        }

        if (remoteEvidence != null)
        {
            PreparationStatusText =
                $"{ied.IedName} · File Service PASS · latest COMTRADE {ied.LatestComtradeFiles} · download optional.";
            Storage?.ScheduleSave();
        }
        else if (downloadedCaptured > 0)
        {
            PreparationStatusText = $"{ied.IedName} · downloaded COMTRADE/fault-record evidence journaled.";
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