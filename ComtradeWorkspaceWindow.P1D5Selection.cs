using System.Threading;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private ComtradeSelectionNavigationSnapshot _p1d5SuspendedSelectionNavigation;
    private double _p1d5SuspendedTrackScrollOffset;
    private bool _p1d5AutoReloadRunning;
    private int _p1d5SelectionGeneration;

    /// <summary>
    /// P1D.5 Auto is a single bounded selection transaction. It never rebuilds the workstation when
    /// the practical default selection is already active, and after Clear it restores the exact
    /// investigation viewport/cursors instead of falling back to the old full-record presentation.
    /// </summary>
    private async void P1D5AutoSignals_Click(object sender, RoutedEventArgs e)
    {
        if (_p1d5AutoReloadRunning)
            return;

        var previousSelection = _disturbanceVisibleSignals.ToArray();
        BuildDefaultVisibleSignals();
        SyncSignalVisibilityCheckboxes();

        var alreadyDefault = previousSelection.Length == _disturbanceVisibleSignals.Count;
        if (alreadyDefault)
        {
            for (var index = 0; index < previousSelection.Length; index++)
            {
                if (_disturbanceVisibleSignals.Contains(previousSelection[index]))
                    continue;
                alreadyDefault = false;
                break;
            }
        }

        // Repeated Auto must be O(n) UI state only. Do not touch the native bridge when the exact
        // default set is already on screen and there is no suspended Clear state to restore.
        if (alreadyDefault && !_p1d5SuspendedSelectionNavigation.IsValid &&
            DisturbanceView.FullEndMilliseconds > DisturbanceView.FullStartMilliseconds)
        {
            StatusTextBlock.Text = "Auto signal set is already active.";
            return;
        }

        _p1d5AutoReloadRunning = true;
        try
        {
            await ReloadP1D5VisibleSelectionAsync(
                restoreSuspendedNavigation: _p1d5SuspendedSelectionNavigation.IsValid,
                applyTriggerFallback: true).ConfigureAwait(true);
        }
        finally
        {
            _p1d5AutoReloadRunning = false;
        }
    }

    private async void P1D5SignalVisibility_Checked(object sender, RoutedEventArgs e)
    {
        if (_disturbanceCheckboxSync || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        if (_disturbanceVisibleSignals.Contains(signal))
            return;
        if (_disturbanceVisibleSignals.Count >= MaxVisibleDisturbanceTracks)
        {
            _disturbanceCheckboxSync = true;
            checkBox.IsChecked = false;
            _disturbanceCheckboxSync = false;
            StatusTextBlock.Text = $"Time Signals supports up to {MaxVisibleDisturbanceTracks} visible tracks at once. Hide another signal first.";
            return;
        }

        var restoringFromEmpty = _disturbanceVisibleSignals.Count == 0 && _p1d5SuspendedSelectionNavigation.IsValid;
        _disturbanceVisibleSignals.Add(signal);
        if (restoringFromEmpty)
        {
            await ReloadP1D5VisibleSelectionAsync(
                restoreSuspendedNavigation: true,
                applyTriggerFallback: true).ConfigureAwait(true);
            return;
        }

        await ReloadP1D5IncrementalSelectionAsync().ConfigureAwait(true);
    }

    private async void P1D5SignalVisibility_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_disturbanceCheckboxSync || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        if (!_disturbanceVisibleSignals.Contains(signal))
            return;

        if (_disturbanceVisibleSignals.Count == 1)
        {
            CaptureP1D5SelectionNavigation();
            _disturbanceVisibleSignals.Remove(signal);
            Interlocked.Increment(ref _p1d5SelectionGeneration);
            CancelP1D5SelectionWork();
            PresentP1D5EmptySelection();
            return;
        }

        _disturbanceVisibleSignals.Remove(signal);
        await ReloadP1D5IncrementalSelectionAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// Clear is presentation-only and must be immediate: no native reload, no frame rebuild, no
    /// cursor measurement work. Keep one small navigation snapshot so Auto or a manually reselected
    /// signal can restore the same investigation context.
    /// </summary>
    private void P1D5ClearSignals_Click(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _p1d5SelectionGeneration);
        CaptureP1D5SelectionNavigation();
        CancelP1D5SelectionWork();

        _disturbanceVisibleSignals.Clear();
        SyncSignalVisibilityCheckboxes();
        PresentP1D5EmptySelection();
    }

    private async Task ReloadP1D5IncrementalSelectionAsync()
    {
        var generation = Interlocked.Increment(ref _p1d5SelectionGeneration);
        InvalidateP1D5MeasurementWork();
        await ReloadDisturbanceAsync(
            CurrentDisturbanceViewport(),
            initialLoad: false,
            preserveLocalView: true).ConfigureAwait(true);

        if (generation != Volatile.Read(ref _p1d5SelectionGeneration))
            return;
        if (DisturbanceView.FullEndMilliseconds <= DisturbanceView.FullStartMilliseconds)
            return;

        InvestigationTimeline.IsEnabled = true;
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
        QueueP1D5CursorMeasurements();
    }

    private async Task ReloadP1D5VisibleSelectionAsync(
        bool restoreSuspendedNavigation,
        bool applyTriggerFallback)
    {
        var generation = Interlocked.Increment(ref _p1d5SelectionGeneration);
        var suspended = restoreSuspendedNavigation ? _p1d5SuspendedSelectionNavigation : default;
        var requestedViewport = suspended.IsValid
            ? new ComtradeSourceViewport(suspended.SourceStartFrame, suspended.SourceFrameCount)
            : CurrentDisturbanceViewport();

        InvestigationTimeline.IsEnabled = false;
        InvalidateP1D5MeasurementWork();
        await ReloadDisturbanceAsync(
            requestedViewport,
            initialLoad: false,
            preserveLocalView: false).ConfigureAwait(true);

        if (generation != Volatile.Read(ref _p1d5SelectionGeneration))
            return;

        var hasLoadedTimeline = DisturbanceView.FullEndMilliseconds > DisturbanceView.FullStartMilliseconds;
        if (hasLoadedTimeline && suspended.MatchesSource(
                _disturbanceLoadedViewport.StartFrame,
                _disturbanceLoadedViewport.FrameCount))
        {
            DisturbanceView.SetViewWindow(
                suspended.ViewStartMilliseconds,
                suspended.ViewEndMilliseconds);
            if (suspended.Cursor1Milliseconds is { } c1)
                DisturbanceView.SetCursorFromHost(ComtradeDisturbanceCursor.Cursor1, c1);
            if (suspended.Cursor2Milliseconds is { } c2)
                DisturbanceView.SetCursorFromHost(ComtradeDisturbanceCursor.Cursor2, c2);
            DisturbanceScrollViewer.ScrollToVerticalOffset(_p1d5SuspendedTrackScrollOffset);
        }
        else if (hasLoadedTimeline && applyTriggerFallback)
        {
            // If there was no restorable local view, retain the modern trigger-focused P1D.5
            // behavior rather than silently reverting to the historical full-record UX.
            DisturbanceView.ApplyTriggerFocusedDefault(_record.Info.NominalFrequency);
        }

        if (!hasLoadedTimeline)
            return;

        _disturbanceInitialFocusApplied = true;
        _p1d5SuspendedSelectionNavigation = default;
        _p1d5SuspendedTrackScrollOffset = 0;
        InvestigationTimeline.IsEnabled = true;
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
        QueueP1D5CursorMeasurements();
    }

    private void CaptureP1D5SelectionNavigation()
    {
        var snapshot = ComtradeSelectionNavigationSnapshot.Capture(
            DisturbanceView.ViewStartMilliseconds,
            DisturbanceView.ViewEndMilliseconds,
            DisturbanceView.Cursor1Milliseconds,
            DisturbanceView.Cursor2Milliseconds,
            _disturbanceLoadedViewport.StartFrame,
            _disturbanceLoadedViewport.FrameCount);
        if (!snapshot.IsValid)
            return;

        _p1d5SuspendedSelectionNavigation = snapshot;
        _p1d5SuspendedTrackScrollOffset = DisturbanceScrollViewer.VerticalOffset;
    }

    private void PresentP1D5EmptySelection()
    {
        DisturbanceView.ShowMessage("Select signals to display.");
        DigitalEventGrid.ItemsSource = Array.Empty<ComtradeDigitalEventRow>();
        DigitalEventExpander.Visibility = Visibility.Collapsed;
        ResetViewButton.IsEnabled = false;
        FullRecordButton.IsEnabled = false;
        InvestigationTimeline.IsEnabled = false;
        StatusTextBlock.Text = "No Time Signals tracks selected • use the checkboxes in Signals or choose Auto.";
        NavigationTextBlock.Text = "Selection cleared • the next selection restores the previous investigation window.";
    }

    private void CancelP1D5SelectionWork()
    {
        _disturbanceLoadCts?.Cancel();
        _disturbanceLoadCts?.Dispose();
        _disturbanceLoadCts = null;

        _disturbanceCursorSnapCts?.Cancel();
        _disturbanceCursorSnapCts?.Dispose();
        _disturbanceCursorSnapCts = null;
        InvalidateP1D5MeasurementWork();
    }

    private void InvalidateP1D5MeasurementWork()
    {
        // Selection changes invalidate every cursor request that referenced the previous visible
        // channels. Clear BOTH projections; leaving the analog projection alive was able to keep
        // native C1/C2 work queued behind Auto's track reload and made the workstation feel frozen.
        Interlocked.Increment(ref _p1d5MeasurementRevision);
        _p1d5MeasurementDirty = false;
        StopP1D5MeasurementRenderingPump();
        _p1d5MeasurementCts?.Cancel();
        _p1d5MeasurementCts?.Dispose();
        _p1d5MeasurementCts = null;
        _p1d5VisibleTrackOrder = Array.Empty<ComtradeSignalItem>();
        _p1d5VisibleAnalogTrackOrder = Array.Empty<ComtradeSignalItem>();
        CursorReadoutCanvas.Children.Clear();
        _p1d5CursorReadoutControls.Clear();
    }
}
