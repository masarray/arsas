using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Bench P0: evidence journal hashing / durable I/O must not monopolize the WPF Dispatcher.
///
/// Connection preparation stays on its existing async path. Once the live capture scope is
/// proven, the evidence controller can build/seal its journal on a worker while all visual
/// updates continue through the existing PropertyChanged/Dispatcher routes. The same rule
/// applies to Stop and workspace close. No IEC 61850 polling, RCB, DataSet, or live-value
/// ownership is changed here.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0EvidenceResponsivenessRegistered = RegisterP0EvidenceResponsiveness();

    private bool _p0EvidenceResponsivenessInstalled;
    private bool _p0EvidenceTransitionBusy;
    private Button? _p0ResponsiveStartButton;
    private Button? _p0ResponsiveStopButton;

    private static bool RegisterP0EvidenceResponsiveness()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0EvidenceResponsiveness_Loaded));
        return true;
    }

    private static void P0EvidenceResponsiveness_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0EvidenceResponsivenessInstalled)
            return;

        window._p0EvidenceResponsivenessInstalled = true;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallP0ResponsiveSessionActions),
            DispatcherPriority.Loaded);
    }

    private void InstallP0ResponsiveSessionActions()
    {
        if (_p0ResponsiveStartButton == null)
        {
            _p0ResponsiveStartButton = FindVisualDescendants<Button>(this)
                .FirstOrDefault(button =>
                    BindingOperations.GetBinding(button, ContentControl.ContentProperty)?.Path?.Path ==
                    nameof(SelectedStartWorkflowText));

            if (_p0ResponsiveStartButton != null)
            {
                _p0ResponsiveStartButton.Click -= StartSelectedIedSafely_Click;
                _p0ResponsiveStartButton.Click += P0StartSelectedIedResponsive_Click;
            }
        }

        if (_p0ResponsiveStopButton == null)
        {
            _p0ResponsiveStopButton = FindVisualDescendants<Button>(this)
                .FirstOrDefault(button =>
                    string.Equals(button.Content?.ToString(), "Stop", StringComparison.OrdinalIgnoreCase) &&
                    BindingOperations.GetBinding(button, UIElement.IsEnabledProperty)?.Path?.Path ==
                    nameof(SelectedCanStop));

            if (_p0ResponsiveStopButton != null)
            {
                _p0ResponsiveStopButton.Click -= StopSession_Click;
                _p0ResponsiveStopButton.Click += P0StopSessionResponsive_Click;
            }
        }

        // P0Lifecycle replaces Window_Closing during Loaded. Run after Loaded so this
        // responsive edge is the only close handler that remains for the bench branch.
        Closing -= Window_Closing;
        Closing -= P0Window_Closing;
        Closing += P0ResponsiveWindow_Closing;
        Closed += P0EvidenceResponsiveness_Closed;
    }

    private async void P0StartSelectedIedResponsive_Click(object sender, RoutedEventArgs e)
    {
        if (_p0EvidenceTransitionBusy)
            return;

        var selectedIed = SelectedIed;
        if (selectedIed?.IsPreparing == true)
            return;

        if (selectedIed == null)
        {
            ShowActionResult(IoTestSessionPreflight.Validate(null), "FAT session scope is not ready");
            return;
        }

        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        var captureScope = selectedIed.TestPoints
            .Where(point => point.WorkspaceSelected && point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
            .ToList();

        SetP0EvidenceTransitionBusy(true);
        try
        {
            SetPreparingIed(selectedIed, $"Preparing {selectedIed.IedName} FAT evidence…");

            if (Owner is MainWindow engineeringWindow)
            {
                var progress = new Progress<string>(message =>
                {
                    PreparationStatusText = message;
                    RaiseStatusProperties();
                    RaiseSelectedIedContextProperties();
                });

                var preparation = await engineeringWindow.PrepareIoTestIedForFatAsync(
                    Project,
                    selectedIed,
                    progress);
                RaiseStatusProperties();
                RaiseSelectedIedContextProperties();
                if (!preparation.Succeeded)
                {
                    PreparationStatusText = preparation.Message;
                    ShowActionResult(preparation, "IED acquisition could not start");
                    return;
                }

                await CaptureTimeSyncEvidenceAfterPreparationAsync(engineeringWindow, selectedIed);
            }

            var liveCaptureScope = captureScope
                .Where(point => point.LiveBindingState == IoTestLiveBindingState.LivePointReady)
                .ToList();
            if (liveCaptureScope.Count == 0)
            {
                var noLive = IoTestSessionActionResult.Failure(
                    $"{selectedIed.IedName} is monitoring, but none of the {captureScope.Count} operator-selected FAT row(s) has a unique live point yet.");
                PreparationStatusText = noLive.Message;
                ShowActionResult(noLive, "FAT evidence session could not start");
                return;
            }

            var waitingCount = captureScope.Count - liveCaptureScope.Count;
            PreparationStatusText = $"Starting FAT evidence for {selectedIed.IedName}…";
            RaiseSelectedIedContextProperties();
            await Dispatcher.Yield(DispatcherPriority.Render);

            // The expensive baseline hash + WriteThrough journal flush is intentionally
            // off Dispatcher. Existing session notifications already converge through the
            // window's Dispatcher refresh path; no WPF control is accessed from this worker.
            var result = await Task.Run(() => Session.Start(selectedIed, liveCaptureScope));

            ShowActionResult(result, "FAT evidence session could not start");
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            if (result.Succeeded)
            {
                PreparationStatusText = waitingCount == 0
                    ? $"{selectedIed.IedName} FAT active · digital transitions and stable analog Value 1 / Value 2 capture are armed"
                    : $"{selectedIed.IedName} FAT active on {liveCaptureScope.Count}/{captureScope.Count} live selected row(s) · {waitingCount} row(s) remain waiting for safe live binding";
                Storage?.ScheduleSave();
            }
            else
            {
                PreparationStatusText = result.Message;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            PreparationStatusText = ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                "Connect and start IED failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            selectedIed.SetPreparationState(false, selectedIed.LiveStatusText);
            SetPreparingIed(null, string.Empty);
            SetP0EvidenceTransitionBusy(false);
            RaiseSelectedIedContextProperties();
        }
    }

    private async void P0StopSessionResponsive_Click(object sender, RoutedEventArgs e)
    {
        if (_p0EvidenceTransitionBusy)
            return;

        SetP0EvidenceTransitionBusy(true);
        PreparationStatusText = $"Sealing {SelectedIed?.IedName ?? "IED"} FAT evidence…";
        RaiseSelectedIedContextProperties();
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            // Stop includes a final WriteThrough append, journal disposal, full hash-chain
            // verification, and workspace save. Keep all durable work away from Dispatcher.
            var result = await Task.Run(() => Session.Stop());
            ShowActionResult(result, "FAT session could not stop");
            if (result.Succeeded && Storage != null)
                await Task.Run(Storage.SaveNow);

            PreparationStatusText = result.Succeeded ? result.Message : $"Stop failed · {result.Message}";
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            PreparationStatusText = ex.Message;
            MessageBox.Show(this, ex.Message, "Stop FAT failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            SetP0EvidenceTransitionBusy(false);
            RaiseSelectedIedContextProperties();
        }
    }

    private async void P0ResponsiveWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        if (_p0AllowClose)
            return;

        e.Cancel = true;
        if (_p0CloseInProgress || _p0EvidenceTransitionBusy)
            return;

        if (IsPreparingIed)
        {
            var activeNames = string.Join(", ", Project.Ieds
                .Where(ied => ied.IsPreparing)
                .Select(ied => ied.IedName));
            MessageBox.Show(
                this,
                $"ARSAS is still preparing {activeNames}. Finish preparation before closing this workspace.",
                "IED preparation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Session.HasActiveSessions)
        {
            var answer = MessageBox.Show(
                this,
                $"{Session.ActiveSessionCount} FAT evidence session(s) are active. Stop and seal them before returning to Engineering?",
                "Stop active FAT sessions",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;
        }

        _p0CloseInProgress = true;
        SetP0EvidenceTransitionBusy(true);
        if (Owner is MainWindow engineeringWindow)
            engineeringWindow.SuspendIoFatRuntimeProjection(this);

        IsEnabled = false;
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            if (Session.HasActiveSessions)
            {
                var stopAll = await Task.Run(() => Session.StopAll(
                    "Workspace closed by operator; per-IED evidence journal sealed."));
                if (!stopAll.Succeeded)
                {
                    MessageBox.Show(
                        this,
                        stopAll.Message,
                        "Evidence journals could not be sealed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    return;
                }
            }

            if (Storage != null)
                await Task.Run(Storage.SaveNow);

            _p0AllowClose = true;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            var answer = MessageBox.Show(
                this,
                $"ARSAS could not seal/save the latest IO FAT progress.\n\n{ex.Message}\n\nClose the workspace anyway?",
                "Progress save failed",
                MessageBoxButton.YesNo,
                MessageBoxImage.Error,
                MessageBoxResult.No);
            if (answer == MessageBoxResult.Yes)
            {
                _p0AllowClose = true;
                Close();
            }
        }
        finally
        {
            if (!_p0AllowClose)
            {
                _p0CloseInProgress = false;
                IsEnabled = true;
                SetP0EvidenceTransitionBusy(false);
                if (Owner is MainWindow resumeOwner)
                    resumeOwner.ResumeIoFatRuntimeProjection(this);
            }
        }
    }

    private void SetP0EvidenceTransitionBusy(bool busy)
    {
        _p0EvidenceTransitionBusy = busy;
        if (_p0ResponsiveStartButton != null)
            _p0ResponsiveStartButton.IsHitTestVisible = !busy;
        if (_p0ResponsiveStopButton != null)
            _p0ResponsiveStopButton.IsHitTestVisible = !busy;
    }

    private void P0EvidenceResponsiveness_Closed(object? sender, EventArgs e)
    {
        Closed -= P0EvidenceResponsiveness_Closed;
        Closing -= P0ResponsiveWindow_Closing;

        if (_p0ResponsiveStartButton != null)
        {
            _p0ResponsiveStartButton.Click -= P0StartSelectedIedResponsive_Click;
            _p0ResponsiveStartButton = null;
        }

        if (_p0ResponsiveStopButton != null)
        {
            _p0ResponsiveStopButton.Click -= P0StopSessionResponsive_Click;
            _p0ResponsiveStopButton = null;
        }
    }
}
