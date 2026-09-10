using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private bool _investigationTimelineAttached;
    private double? _harmonicCursorMilliseconds;
    private CancellationTokenSource? _shellAnalysisRefreshCts;

    private void InvestigationTimeline_Loaded(object sender, RoutedEventArgs e)
    {
        if (_investigationTimelineAttached) return;
        _investigationTimelineAttached = true;

        InvestigationTimeline.CursorChanged += InvestigationTimeline_CursorChanged;
        DisturbanceView.NavigationChanged += DisturbanceView_ShellNavigationChanged;
        DisturbanceView.CursorChanged += DisturbanceView_ShellCursorChanged;
        SignalList.SelectionChanged += SignalList_ShellSelectionChanged;
        Closed += InvestigationShell_Closed;
        SyncInvestigationTimeline();
    }

    private void InvestigationShell_Closed(object? sender, EventArgs e)
    {
        _shellAnalysisRefreshCts?.Cancel();
        _shellAnalysisRefreshCts?.Dispose();
        _shellAnalysisRefreshCts = null;
        if (!_investigationTimelineAttached) return;

        InvestigationTimeline.CursorChanged -= InvestigationTimeline_CursorChanged;
        DisturbanceView.NavigationChanged -= DisturbanceView_ShellNavigationChanged;
        DisturbanceView.CursorChanged -= DisturbanceView_ShellCursorChanged;
        SignalList.SelectionChanged -= SignalList_ShellSelectionChanged;
        _investigationTimelineAttached = false;
    }

    private void TimeSignalsModeShell_Click(object sender, RoutedEventArgs e)
    {
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
        SetAnalysisMode(AnalysisMode.Waveform);
        SyncInvestigationTimeline();
    }

    private void PhasorModeShell_Click(object sender, RoutedEventArgs e)
    {
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
        SetAnalysisMode(AnalysisMode.Phasor);
        SyncInvestigationTimeline();
    }

    private void HarmonicsModeShell_Click(object sender, RoutedEventArgs e)
    {
        EnsureHarmonicCursor();
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.HarmonicCursor);
        SetAnalysisMode(AnalysisMode.Harmonics);
        SyncInvestigationTimeline();
    }

    private void SignalList_ShellSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        // Existing analysis logic can return to Time Signals when a non-analog row is selected.
        // Reconcile the persistent ruler after that handler has completed.
        Dispatcher.BeginInvoke(() =>
        {
            if (_analysisMode != AnalysisMode.Harmonics &&
                InvestigationTimeline.Mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
                InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
            SyncInvestigationTimeline();
        }, DispatcherPriority.Background);
    }

    private void DisturbanceView_ShellNavigationChanged(object? sender, ComtradeDisturbanceNavigationChangedEventArgs e)
    {
        SyncInvestigationTimeline();
    }

    private void DisturbanceView_ShellCursorChanged(object? sender, ComtradeDisturbanceCursorChangedEventArgs e)
    {
        SyncInvestigationTimeline();
        if (!e.IsFinal || _analysisMode != AnalysisMode.Phasor) return;

        var selected = _phasorReferenceCursor == ComtradeDisturbanceCursor.Cursor1
            ? ComtradeInvestigationTimelineCursor.Cursor1
            : ComtradeInvestigationTimelineCursor.Cursor2;
        var changed = e.Cursor == ComtradeDisturbanceCursor.Cursor1
            ? ComtradeInvestigationTimelineCursor.Cursor1
            : ComtradeInvestigationTimelineCursor.Cursor2;
        if (changed == selected)
            QueueShellAnalysisRefresh(immediate: true);
    }

    private void InvestigationTimeline_CursorChanged(object? sender, ComtradeInvestigationTimelineCursorChangedEventArgs e)
    {
        switch (e.Cursor)
        {
            case ComtradeInvestigationTimelineCursor.Cursor1:
                DisturbanceView.SetCursorFromHost(ComtradeDisturbanceCursor.Cursor1, e.AbsoluteMilliseconds);
                if (e.IsFinal)
                    DisturbanceView_CursorChanged(DisturbanceView,
                        new ComtradeDisturbanceCursorChangedEventArgs(
                            ComtradeDisturbanceCursor.Cursor1,
                            e.AbsoluteMilliseconds,
                            e.SnapToleranceMilliseconds,
                            true,
                            false));
                if (_analysisMode == AnalysisMode.Phasor && _phasorReferenceCursor == ComtradeDisturbanceCursor.Cursor1)
                    QueueShellAnalysisRefresh(e.IsFinal);
                break;

            case ComtradeInvestigationTimelineCursor.Cursor2:
                DisturbanceView.SetCursorFromHost(ComtradeDisturbanceCursor.Cursor2, e.AbsoluteMilliseconds);
                if (e.IsFinal)
                    DisturbanceView_CursorChanged(DisturbanceView,
                        new ComtradeDisturbanceCursorChangedEventArgs(
                            ComtradeDisturbanceCursor.Cursor2,
                            e.AbsoluteMilliseconds,
                            e.SnapToleranceMilliseconds,
                            true,
                            false));
                if (_analysisMode == AnalysisMode.Phasor && _phasorReferenceCursor == ComtradeDisturbanceCursor.Cursor2)
                    QueueShellAnalysisRefresh(e.IsFinal);
                break;

            case ComtradeInvestigationTimelineCursor.Harmonic:
                _harmonicCursorMilliseconds = e.AbsoluteMilliseconds;
                if (_analysisMode == AnalysisMode.Harmonics)
                    QueueShellAnalysisRefresh(e.IsFinal);
                break;
        }

        SyncInvestigationTimeline();
    }

    private void EnsureHarmonicCursor()
    {
        if (_harmonicCursorMilliseconds.HasValue) return;

        _harmonicCursorMilliseconds = DisturbanceView.Cursor1Milliseconds
            ?? DisturbanceView.EffectiveTriggerMilliseconds
            ?? (DisturbanceView.ViewStartMilliseconds + DisturbanceView.ViewEndMilliseconds) * 0.5;
    }

    private void SyncInvestigationTimeline()
    {
        if (!_investigationTimelineAttached) return;
        if (_analysisMode == AnalysisMode.Harmonics)
            EnsureHarmonicCursor();

        InvestigationTimeline.SetContext(
            DisturbanceView.FullStartMilliseconds,
            DisturbanceView.FullEndMilliseconds,
            DisturbanceView.ViewStartMilliseconds,
            DisturbanceView.ViewEndMilliseconds,
            DisturbanceView.EffectiveTriggerMilliseconds,
            DisturbanceView.Cursor1Milliseconds,
            DisturbanceView.Cursor2Milliseconds,
            _harmonicCursorMilliseconds);
    }

    private void QueueShellAnalysisRefresh(bool immediate)
    {
        if (_analysisMode == AnalysisMode.Waveform) return;

        _shellAnalysisRefreshCts?.Cancel();
        _shellAnalysisRefreshCts?.Dispose();
        _shellAnalysisRefreshCts = new CancellationTokenSource();
        var token = _shellAnalysisRefreshCts.Token;
        _ = Dispatcher.InvokeAsync(async () =>
        {
            try
            {
                if (!immediate)
                    await Task.Delay(90, token).ConfigureAwait(true);
                if (!token.IsCancellationRequested)
                    await RefreshNativeAnalysisAsync().ConfigureAwait(true);
            }
            catch (OperationCanceledException)
            {
            }
        }, DispatcherPriority.Background);
    }
}
