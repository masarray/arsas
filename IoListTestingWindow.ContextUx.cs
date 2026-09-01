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
            var allComplete = enabled.Count > 0 && enabled.All(point => point.IsFatEvidenceComplete);
            var hasCompleted = enabled.Any(point => point.IsFatEvidenceComplete);

            if (SelectedIed.IsLiveMonitoring)
            {
                if (allComplete)
                    return "Retest FAT";
                return hasCompleted ? "Continue FAT" : "Start FAT";
            }

            if (allComplete)
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
            var complete = enabled.Count(point => point.IsFatEvidenceComplete);
            if (enabled.Count > 0 && complete == enabled.Count)
            {
                return $"{SelectedIed.IedName} current Value 1 / Value 2 evidence complete for all {enabled.Count} selected signal(s). Start FAT can capture newer evidence without clearing current evidence.";
            }

            if (complete > 0)
                return $"{SelectedIed.IedName} selected · {complete}/{enabled.Count} current Value 1 / Value 2 evidence complete · included selected rows remain eligible for recapture.";

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
            var complete = enabled.Count(point => point.IsFatEvidenceComplete);
            var passed = enabled.Count(point =>
                point.CaptureMode == FatCaptureMode.AutomaticTransition &&
                point.Runtime.State == IoTestPointState.Passed);
            var review = enabled.Count(point =>
                point.CaptureMode == FatCaptureMode.AutomaticTransition &&
                point.Runtime.State == IoTestPointState.Review);
            var manual = enabled.Count(point =>
                point.CaptureMode == FatCaptureMode.OperatorSnapshot &&
                point.IsFatEvidenceComplete);
            return $"{complete} / {enabled.Count} complete · {passed} digital PASS · {review} review · {manual} snapshot complete";
        }
    }

    public int SelectedEvidenceCount =>
        (SelectedIed?.TestPoints.Sum(point => point.CaptureMode == FatCaptureMode.AutomaticTransition
            ? (point.Runtime.OnEvidence == null ? 0 : 1) + (point.Runtime.OffEvidence == null ? 0 : 1)
            : (point.Runtime.Value1Evidence == null ? 0 : 1) + (point.Runtime.Value2Evidence == null ? 0 : 1)) ?? 0) +
        SelectedSupplementalEvidenceCount;

    private bool IsSelectedSessionIed =>
        SelectedIed != null && ReferenceEquals(Session.ActiveIed, SelectedIed);

    private void IoListTestingWindow_ContentRendered(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            new Action(() =>
            {
                InstallSelectedIedContext();
                InstallFatV2WorkspaceUx();
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

    private async void ConnectIed_Click(object sender, RoutedEventArgs e)
    {
        var targetIed = (sender as FrameworkElement)?.DataContext as IoTestIedPlan;
        if (targetIed == null || targetIed.IsPreparing)
            return;

        var enabledReady = targetIed.TestPoints
            .Where(point => point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
            .ToList();
        if (enabledReady.Count == 0)
        {
            ShowActionResult(
                IoTestSessionActionResult.Failure("No import-ready operator-selected signal is enabled in the active FAT scope for this IED."),
                "IED connection scope is not ready");
            return;
        }

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
                PreparationStatusText = preparation.Message;
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

        var preflight = IoTestSessionPreflight.Validate(selectedIed);
        if (!preflight.Succeeded)
        {
            ShowActionResult(preflight, "FAT session scope is not ready");
            return;
        }

        var captureScope = selectedIed.TestPoints
            .Where(point => point.IsIncludedInFat && point.TestEnabled && point.ImportReady)
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

            // Operator selection remains authoritative. A selected static DataSet row that
            // has no unique live point stays checked/included and visible, but it cannot be
            // allowed to manufacture evidence. Arm only the currently proven live subset.
            // Legacy source-contract spelling retained only as documentation:
            // Session.Start(selectedIed, captureScope)
            // P5.3 deliberately does not execute that all-or-nothing call.
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
            var result = Session.Start(selectedIed, liveCaptureScope);
            ShowActionResult(result, "FAT evidence session could not start");
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            if (result.Succeeded)
            {
                PreparationStatusText = waitingCount == 0
                    ? $"{selectedIed.IedName} live · automatic rows track transitions · operator-snapshot rows expose ✓ Value 1 / Value 2 capture · session remains active until Stop"
                    : $"{selectedIed.IedName} FAT active on {liveCaptureScope.Count}/{captureScope.Count} live selected row(s) · {waitingCount} selected row(s) remain waiting for safe live binding · checkbox/disposition unchanged";
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
        {
            RaiseSelectedIedContextProperties();
            if (e.PropertyName == nameof(SelectedIed))
                Dispatcher.BeginInvoke(new Action(RefreshFatV2WorkspaceUx), DispatcherPriority.DataBind);
        }
    }

    private void ContextSession_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        RaiseSelectedIedContextProperties();
        RefreshFatV2WorkspaceUx();
    }

    private void ContextIed_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(IoTestIedPlan.IsPreparing))
            RaisePreparationProperties();

        RefreshFatV2WorkspaceUx();
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
        => ied.TestPoints.Where(point => point.IsIncludedInFat && point.TestEnabled).ToList();
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
