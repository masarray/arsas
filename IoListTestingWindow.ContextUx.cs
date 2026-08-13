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
                return $"{SelectedIed.IedName} complete · all {enabled.Count} enabled points PASS · evidence protected.";

            if (complete > 0)
                return $"{SelectedIed.IedName} selected · {complete}/{enabled.Count} complete · completed evidence will be preserved.";

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
                IoTestSessionActionResult.Failure("No import-ready IO-list signal is enabled for this IED."),
                "IED connection scope is not ready");
            return;
        }

        // For a continuation, connect only what still needs evidence. Completed rows keep
        // their sealed evidence and do not have to exist in a replacement relay model.
        // If every row is already complete, refresh the complete enabled scope instead.
        var pendingScope = enabledReady.Where(point => !point.Runtime.IsComplete).ToList();
        IReadOnlyCollection<IoTestPointPlan> connectionScope = pendingScope.Count > 0
            ? pendingScope
            : enabledReady;

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

            var preparation = await engineeringWindow.PrepareIoTestIedForFatAsync(
                Project,
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

        var enabledReady = selectedIed.TestPoints
            .Where(point => point.TestEnabled && point.ImportReady)
            .ToList();
        var completedPoints = enabledReady
            .Where(point => point.Runtime.IsComplete)
            .ToList();
        var incompletePoints = enabledReady
            .Where(point => !point.Runtime.IsComplete)
            .ToList();

        var explicitRetest = false;
        if (completedPoints.Count > 0 && incompletePoints.Count == 0)
        {
            var answer = MessageBox.Show(
                this,
                $"All {completedPoints.Count} enabled rows for {selectedIed.IedName} already contain completed evidence.\n\nA normal Start FAT click will not erase them. Choose Yes only when you intentionally want to retest every completed row and replace its ON/OFF evidence.",
                "Retest completed evidence?",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
            {
                PreparationStatusText = $"{selectedIed.IedName} evidence preserved · no retest started.";
                RaiseSelectedIedContextProperties();
                return;
            }

            explicitRetest = true;
        }

        var protectedPoints = explicitRetest
            ? Array.Empty<IoTestPointPlan>()
            : completedPoints.ToArray();

        // Completed evidence is outside the continuation scope from the first preflight
        // through live-model preparation and Session.Start. This prevents a completed row
        // that has since disappeared from the relay model from blocking otherwise-valid
        // pending rows. The original TestEnabled flags are restored in the outer finally.
        foreach (var point in protectedPoints)
            point.TestEnabled = false;

        try
        {
            var preflight = IoTestSessionPreflight.Validate(selectedIed);
            if (!preflight.Succeeded)
            {
                ShowActionResult(preflight, "FAT session scope is not ready");
                return;
            }

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

            var result = Session.Start(selectedIed);
            ShowActionResult(result, "FAT evidence session could not start");
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            if (result.Succeeded)
            {
                PreparationStatusText = protectedPoints.Length > 0
                    ? $"{selectedIed.IedName} live · {protectedPoints.Length} completed row(s) preserved · waiting for pending OFF → ON → OFF tests"
                    : $"{selectedIed.IedName} live · waiting for OFF → ON → OFF";
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
            foreach (var point in protectedPoints)
                point.TestEnabled = true;

            selectedIed.SetPreparationState(false, selectedIed.LiveStatusText);
            SetPreparingIed(null, string.Empty);
            RaiseSelectedIedContextProperties();
        }
    }

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
