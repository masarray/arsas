using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private bool _selectedIedContextInstalled;

    public bool SelectedCanStartWorkflow =>
        SelectedIed != null && !SelectedIed.IsPreparing && Session.CanStart;

    public bool SelectedCanPause =>
        IsSelectedSessionIed && Session.CanPause;

    public bool SelectedCanResume =>
        IsSelectedSessionIed && Session.CanResume;

    public bool SelectedCanStop =>
        IsSelectedSessionIed && Session.CanStop;

    public string SelectedStartWorkflowText
    {
        get
        {
            if (SelectedIed == null)
                return "Select IED";

            if (SelectedIed.IsPreparing)
                return $"Connecting {SelectedIed.IedName}…";

            if (Session.IsSessionActive)
            {
                return IsSelectedSessionIed
                    ? "IED session active"
                    : $"{Session.ActiveIed?.IedName ?? "Another IED"} FAT active";
            }

            var enabled = EnabledPoints(SelectedIed);
            var allPassed = enabled.Count > 0 && enabled.All(point => point.Runtime.State == IoTestPointState.Passed);
            var hasCompleted = enabled.Any(point => point.Runtime.IsComplete);

            if (SelectedIed.IsLiveMonitoring)
            {
                if (allPassed)
                    return "Retest FAT";
                return hasCompleted ? "Continue FAT" : "Start FAT";
            }

            if (allPassed)
                return "Reconnect / Retest";
            return hasCompleted ? "Connect & Continue IED" : "Connect & Start IED";
        }
    }

    public string SelectedFooterStatusText
    {
        get
        {
            if (SelectedIed == null)
                return "Select an imported IED.";

            if (SelectedIed.IsPreparing)
                return SelectedIed.PreparationStatusText;

            if (IsSelectedSessionIed && Session.State != IoTestSessionState.Idle)
                return Session.StatusText;

            var enabled = EnabledPoints(SelectedIed);
            var complete = enabled.Count(point => point.Runtime.IsComplete);
            var passed = enabled.Count(point => point.Runtime.State == IoTestPointState.Passed);

            if (enabled.Count > 0 && passed == enabled.Count)
                return $"{SelectedIed.IedName} complete · all {enabled.Count} selected points PASS · Start FAT can capture a newer cycle without clearing current evidence.";

            if (complete > 0)
                return $"{SelectedIed.IedName} selected · {complete}/{enabled.Count} current results complete · selected rows remain eligible for newer evidence.";

            return $"{SelectedIed.IedName} selected · {SelectedIed.LiveStatusText}";
        }
    }

    public string SelectedProgressText
    {
        get
        {
            if (SelectedIed == null)
                return "0 / 0 complete";

            var enabled = EnabledPoints(SelectedIed);
            var complete = enabled.Count(point => point.Runtime.IsComplete);
            var passed = enabled.Count(point => point.Runtime.State == IoTestPointState.Passed);
            var review = enabled.Count(point => point.Runtime.State == IoTestPointState.Review);
            var failed = enabled.Count(point => point.Runtime.State == IoTestPointState.Failed);
            return $"{complete} / {enabled.Count} complete · {passed} PASS · {review} review · {failed} fail";
        }
    }

    public int SelectedEvidenceCount =>
        (SelectedIed?.TestPoints.Sum(point =>
            (point.Runtime.OnEvidence == null ? 0 : 1) +
            (point.Runtime.OffEvidence == null ? 0 : 1)) ?? 0) +
        SelectedSupplementalEvidenceCount;

    private bool IsSelectedSessionIed =>
        SelectedIed != null && ReferenceEquals(Session.ActiveIed, SelectedIed);

    private void IoListTestingWindow_ContentRendered(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                InstallSelectedIedContext();
                InstallSupplementalEvidenceControls();
                RefreshSupplementalEvidenceControls();
            }),
            DispatcherPriority.ContextIdle);
    }

    private void InstallSelectedIedContext()
    {
        AdoptWorkspacePreviewToggle();
        if (_selectedIedContextInstalled)
        {
            RaiseSelectedIedContextProperties();
            return;
        }

        foreach (var ied in Project.Ieds)
            ied.PropertyChanged += ContextIed_PropertyChanged;

        PropertyChanged += ContextWindow_PropertyChanged;
        Session.PropertyChanged += ContextSession_PropertyChanged;
        Closed += ContextWindow_Closed;
        _selectedIedContextInstalled = true;
        RaiseSelectedIedContextProperties();
    }

    private void AdoptWorkspacePreviewToggle()
    {
        if (_printPreviewToggle != null && !ReferenceEquals(_printPreviewToggle, WorkspacePreviewToggle))
        {
            if (LogicalTreeHelper.GetParent(_printPreviewToggle) is Panel parent)
                parent.Children.Remove(_printPreviewToggle);
        }

        _printPreviewToggle = WorkspacePreviewToggle;
        _printPreviewToggle.Content = _printPreviewActive ? "Signals View" : "Print Preview";
        _printPreviewToggle.ToolTip = "Toggle the selected IED between signal evidence and native print preview";
    }

    /// <summary>
    /// Card-local connection action. Several different IED cards can run this method at
    /// once; each device owns its own MainWindow connection workflow and model progress.
    /// Evidence capture remains a separate, single-active session selected by Start FAT.
    /// </summary>
    private async void ConnectIed_Click(object sender, RoutedEventArgs e)
    {
        var targetIed = (sender as FrameworkElement)?.DataContext as IoTestIedPlan;
        if (targetIed == null || targetIed.IsPreparing)
            return;

        var enabledReady = targetIed.TestPoints
            .Where(point => point.TestEnabled && point.ImportReady)
            .ToList();
        if (enabledReady.Count == 0)
        {
            ShowActionResult(
                IoTestSessionActionResult.Failure("No import-ready operator-selected signal is enabled for this IED."),
                "IED connection scope is not ready");
            return;
        }

        // Operator selection is the authority. Completed rows remain in the connection
        // scope because a checked row is intentionally eligible for newer evidence.
        // The engine never rewrites TestEnabled to manufacture a smaller continuation.
        IReadOnlyCollection<IoTestPointPlan> connectionScope = enabledReady;

        if (ReferenceEquals(SelectedIed, targetIed))
            PreparationStatusText = $"Connecting {targetIed.IedName} · {targetIed.IpAddress}:102";
        RaisePreparationProperties();
        RaiseSelectedIedContextProperties();

        try
        {
            if (Owner is not MainWindow engineeringWindow)
                return;

            var progress = new Progress<string>(message =>
            {
                if (ReferenceEquals(SelectedIed, targetIed))
                    PreparationStatusText = message;
                RaiseStatusProperties();
                RaiseSelectedIedContextProperties();
            });

            var preparation = await PrepareIndependentIedConnectionAsync(
                engineeringWindow,
                targetIed,
                progress,
                connectionScope);

            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            if (!preparation.Succeeded)
            {
                if (ReferenceEquals(SelectedIed, targetIed))
                    PreparationStatusText = preparation.Message;
                ShowActionResult(preparation, $"{targetIed.IedName} acquisition could not start");
                return;
            }

            await CaptureTimeSyncEvidenceAfterPreparationAsync(engineeringWindow, targetIed);
            if (ReferenceEquals(SelectedIed, targetIed))
                PreparationStatusText = $"{targetIed.IedName} live · ready for FAT evidence";
            Storage?.ScheduleSave();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            if (ReferenceEquals(SelectedIed, targetIed))
                PreparationStatusText = ex.Message;
            MessageBox.Show(
                this,
                ex.Message,
                $"Connect {targetIed.IedName} failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            targetIed.SetPreparationState(false, targetIed.LiveStatusText);
            RaisePreparationProperties();
            RaiseSelectedIedContextProperties();
        }
    }

    private async void StartSelectedIedSafely_Click(object sender, RoutedEventArgs e)
    {
        var selectedIed = SelectedIed;
        if (selectedIed?.IsPreparing == true)
            return;

        if (selectedIed == null)
        {
            var missingSelection = IoTestSessionPreflight.Validate(null);
            ShowActionResult(missingSelection, "FAT session scope is not ready");
            return;
        }

        // Preflight sees the real operator selection. No temporary checkbox mutation is
        // permitted anywhere in this workflow. After preflight succeeds, the exact ready
        // selection is carried explicitly through preparation and Session.Start.
        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        var captureScope = selectedIed.TestPoints
            .Where(point => point.TestEnabled && point.ImportReady)
            .ToList();

        try
        {
            SetPreparingIed(selectedIed, $"Connecting {selectedIed.IedName} · {selectedIed.IpAddress}:102");
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
                    progress,
                    captureScope);
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

            var result = Session.Start(selectedIed, captureScope);
            ShowActionResult(result, "FAT evidence session could not start");
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            if (result.Succeeded)
            {
                PreparationStatusText =
                    $"{selectedIed.IedName} live · capture remains active until Stop · complete newer cycles replace current evidence atomically";
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
            RaiseSelectedIedContextProperties();
        }
    }

    private Task<IoTestSessionActionResult> PrepareIndependentIedConnectionAsync(
        MainWindow engineeringWindow,
        IoTestIedPlan targetIed,
        IProgress<string> progress,
        IReadOnlyCollection<IoTestPointPlan> connectionScope)
        => engineeringWindow.PrepareIoTestIedForFatAsync(
            Project,
            targetIed,
            progress,
            connectionScope);

    private void ContextWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SelectedIed) or nameof(IsPreparingIed) or nameof(PreparationStatusText))
            RaiseSelectedIedContextProperties();
    }

    private void ContextSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RaiseSelectedIedContextProperties();

    private void ContextIed_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IoTestIedPlan.IsPreparing))
            RaisePreparationProperties();

        if (!ReferenceEquals(sender, SelectedIed))
            return;

        Raise(nameof(SelectedIedSummary));
        RaiseSelectedIedContextProperties();
    }

    private void RaiseSelectedIedContextProperties()
    {
        Raise(nameof(SelectedCanStartWorkflow));
        Raise(nameof(SelectedCanPause));
        Raise(nameof(SelectedCanResume));
        Raise(nameof(SelectedCanStop));
        Raise(nameof(SelectedStartWorkflowText));
        Raise(nameof(SelectedFooterStatusText));
        Raise(nameof(SelectedProgressText));
        Raise(nameof(SelectedSupplementalEvidenceCount));
        Raise(nameof(SelectedEvidenceCount));
        RefreshSupplementalEvidenceControls();
    }

    private void ContextWindow_Closed(object? sender, EventArgs e)
    {
        foreach (var ied in Project.Ieds)
            ied.PropertyChanged -= ContextIed_PropertyChanged;

        PropertyChanged -= ContextWindow_PropertyChanged;
        Session.PropertyChanged -= ContextSession_PropertyChanged;
        Closed -= ContextWindow_Closed;
    }

    private static List<IoTestPointPlan> EnabledPoints(IoTestIedPlan ied)
        => ied.TestPoints.Where(point => point.TestEnabled).ToList();
}

public sealed class IoFatAllPassedVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var selectedCount = values.Length > 0 && values[0] is int count ? count : 0;
        var passed = values.Length > 1 && values[1] is int passedCount ? passedCount : 0;
        var allPassed = selectedCount > 0 && passed >= selectedCount;
        if (string.Equals(parameter?.ToString(), "Inverse", StringComparison.OrdinalIgnoreCase))
            allPassed = !allPassed;
        return allPassed ? Visibility.Visible : Visibility.Collapsed;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
