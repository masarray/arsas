using System.Threading;
using System.Windows;
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

        var generation = Interlocked.Increment(ref _p1d5SelectionGeneration);
        _p1d5AutoReloadRunning = true;
        InvestigationTimeline.IsEnabled = false;
        try
        {
            var suspended = _p1d5SuspendedSelectionNavigation;
            var requestedViewport = suspended.IsValid
                ? new ComtradeSourceViewport(suspended.SourceStartFrame, suspended.SourceFrameCount)
                : CurrentDisturbanceViewport();

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
            else if (hasLoadedTimeline)
            {
                // If there was no restorable local view, retain the modern trigger-focused P1D.5
                // behavior rather than silently reverting to the historical full-record UX.
                DisturbanceView.ApplyTriggerFocusedDefault(_record.Info.NominalFrequency);
            }

            if (hasLoadedTimeline)
            {
                _disturbanceInitialFocusApplied = true;
                _p1d5SuspendedSelectionNavigation = default;
                _p1d5SuspendedTrackScrollOffset = 0;
                InvestigationTimeline.IsEnabled = true;
                SyncInvestigationTimeline();
                SyncInvestigationTimelineGeometry();
                QueueP1D5CursorMeasurements();
            }
        }
        finally
        {
            // Clear may supersede an in-flight Auto generation. Always release the re-entry guard;
            // only the still-current generation is allowed to re-enable the investigation ruler.
            _p1d5AutoReloadRunning = false;
            if (generation == Volatile.Read(ref _p1d5SelectionGeneration))
                InvestigationTimeline.IsEnabled = DisturbanceView.FullEndMilliseconds > DisturbanceView.FullStartMilliseconds;
        }
    }

    /// <summary>
    /// Clear is presentation-only and must be immediate: no native reload, no frame rebuild, no
    /// cursor measurement work. Keep one small navigation snapshot so Auto can restore the same
    /// investigation context after it loads the practical signal set again.
    /// </summary>
    private void P1D5ClearSignals_Click(object sender, RoutedEventArgs e)
    {
        Interlocked.Increment(ref _p1d5SelectionGeneration);
        CaptureP1D5SelectionNavigation();
        CancelP1D5SelectionWork();

        _disturbanceVisibleSignals.Clear();
        SyncSignalVisibilityCheckboxes();

        DisturbanceView.ShowMessage("Select signals to display.");
        DigitalEventGrid.ItemsSource = Array.Empty<ComtradeDigitalEventRow>();
        DigitalEventExpander.Visibility = Visibility.Collapsed;
        ResetViewButton.IsEnabled = false;
        FullRecordButton.IsEnabled = false;
        InvestigationTimeline.IsEnabled = false;
        StatusTextBlock.Text = "No Time Signals tracks selected • use the checkboxes in Signals or choose Auto.";
        NavigationTextBlock.Text = "Selection cleared • Auto restores the previous investigation window with the practical signal set.";
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

    private void CancelP1D5SelectionWork()
    {
        _disturbanceLoadCts?.Cancel();
        _disturbanceLoadCts?.Dispose();
        _disturbanceLoadCts = null;

        _disturbanceCursorSnapCts?.Cancel();
        _disturbanceCursorSnapCts?.Dispose();
        _disturbanceCursorSnapCts = null;

        // Empty selection invalidates every cursor request that referenced the previous visible
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
