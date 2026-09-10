using System.Windows;
using System.Windows.Threading;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private bool _investigationTimelineAttached;
    private double? _harmonicCursorMilliseconds;
    private object? _normalizedDigitalEventSource;

    private void InvestigationTimeline_Loaded(object sender, RoutedEventArgs e)
    {
        if (_investigationTimelineAttached) return;
        _investigationTimelineAttached = true;

        InvestigationTimeline.CursorChanged += InvestigationTimeline_CursorChanged;
        DisturbanceView.NavigationChanged += DisturbanceView_ShellNavigationChanged;
        DisturbanceView.CursorChanged += DisturbanceView_ShellCursorChanged;
        DisturbanceView.SizeChanged += DisturbanceView_ShellSizeChanged;
        DisturbanceScrollViewer.SizeChanged += DisturbanceView_ShellSizeChanged;
        SignalList.SelectionChanged += SignalList_ShellSelectionChanged;
        Closed += InvestigationShell_Closed;
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
    }

    private void InvestigationShell_Closed(object? sender, EventArgs e)
    {
        if (!_investigationTimelineAttached) return;

        InvestigationTimeline.CursorChanged -= InvestigationTimeline_CursorChanged;
        DisturbanceView.NavigationChanged -= DisturbanceView_ShellNavigationChanged;
        DisturbanceView.CursorChanged -= DisturbanceView_ShellCursorChanged;
        DisturbanceView.SizeChanged -= DisturbanceView_ShellSizeChanged;
        DisturbanceScrollViewer.SizeChanged -= DisturbanceView_ShellSizeChanged;
        SignalList.SelectionChanged -= SignalList_ShellSelectionChanged;
        _investigationTimelineAttached = false;
    }

    private void TimeSignalsModeShell_Click(object sender, RoutedEventArgs e)
    {
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
        SetAnalysisMode(AnalysisMode.Waveform);
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
    }

    private void PhasorModeShell_Click(object sender, RoutedEventArgs e)
    {
        EnsurePhasorCursor();
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.PhasorCursor);
        SetAnalysisMode(AnalysisMode.Phasor);
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
    }

    private void HarmonicsModeShell_Click(object sender, RoutedEventArgs e)
    {
        EnsureHarmonicCursor();
        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.HarmonicCursor);
        SetAnalysisMode(AnalysisMode.Harmonics);
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
    }

    private void SignalList_ShellSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (_analysisMode == AnalysisMode.Waveform && InvestigationTimeline.Mode != ComtradeInvestigationTimelineMode.DualCursor)
                InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
            else if (_analysisMode == AnalysisMode.Phasor && InvestigationTimeline.Mode != ComtradeInvestigationTimelineMode.PhasorCursor)
                InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.PhasorCursor);
            else if (_analysisMode == AnalysisMode.Harmonics && InvestigationTimeline.Mode != ComtradeInvestigationTimelineMode.HarmonicCursor)
                InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.HarmonicCursor);
            SyncInvestigationTimeline();
        }, DispatcherPriority.Background);
    }

    private void DisturbanceView_ShellSizeChanged(object sender, SizeChangedEventArgs e)
        => SyncInvestigationTimelineGeometry();

    private void SyncInvestigationTimelineGeometry()
    {
        if (!_investigationTimelineAttached) return;
        InvestigationTimeline.SetPlotGeometry(
            DisturbanceView.PlotLeftInset,
            DisturbanceView.PlotRightInset,
            Math.Max(0.0, DisturbanceView.ActualWidth));
    }

    private void DisturbanceView_ShellNavigationChanged(object? sender, ComtradeDisturbanceNavigationChangedEventArgs e)
    {
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
        QueueDigitalEventTimelineNormalization();
    }

    private void DisturbanceView_ShellCursorChanged(object? sender, ComtradeDisturbanceCursorChangedEventArgs e)
    {
        var shellCursor = e.Cursor == ComtradeDisturbanceCursor.Cursor1
            ? ComtradeInvestigationTimelineCursor.Cursor1
            : ComtradeInvestigationTimelineCursor.Cursor2;
        InvestigationTimeline.SetCursorFromHost(shellCursor, e.AbsoluteMilliseconds);
        SyncInvestigationTimeline();
    }

    private void InvestigationTimeline_CursorChanged(object? sender, ComtradeInvestigationTimelineCursorChangedEventArgs e)
    {
        switch (e.Cursor)
        {
            case ComtradeInvestigationTimelineCursor.Cursor1:
            {
                var actual = DisturbanceView.PlaceCursorFromShell(
                    ComtradeDisturbanceCursor.Cursor1,
                    e.AbsoluteMilliseconds,
                    e.SnapToleranceMilliseconds,
                    e.IsFinal);
                InvestigationTimeline.SetCursorFromHost(ComtradeInvestigationTimelineCursor.Cursor1, actual);
                break;
            }
            case ComtradeInvestigationTimelineCursor.Cursor2:
            {
                var actual = DisturbanceView.PlaceCursorFromShell(
                    ComtradeDisturbanceCursor.Cursor2,
                    e.AbsoluteMilliseconds,
                    e.SnapToleranceMilliseconds,
                    e.IsFinal);
                InvestigationTimeline.SetCursorFromHost(ComtradeInvestigationTimelineCursor.Cursor2, actual);
                break;
            }
            case ComtradeInvestigationTimelineCursor.Phasor:
            {
                var actual = DisturbanceView.SnapAnalysisCursorFromShell(e.AbsoluteMilliseconds, e.SnapToleranceMilliseconds);
                _phasorCursorMilliseconds = actual;
                InvestigationTimeline.SetCursorFromHost(ComtradeInvestigationTimelineCursor.Phasor, actual);
                QueueRealtimeAnalysisScrub(e.IsFinal);
                break;
            }
            case ComtradeInvestigationTimelineCursor.Harmonic:
            {
                var actual = DisturbanceView.SnapAnalysisCursorFromShell(e.AbsoluteMilliseconds, e.SnapToleranceMilliseconds);
                _harmonicCursorMilliseconds = actual;
                InvestigationTimeline.SetCursorFromHost(ComtradeInvestigationTimelineCursor.Harmonic, actual);
                QueueRealtimeAnalysisScrub(e.IsFinal);
                break;
            }
        }

        SyncInvestigationTimeline();
    }

    private void EnsurePhasorCursor()
    {
        if (_phasorCursorMilliseconds.HasValue) return;
        _phasorCursorMilliseconds = DisturbanceView.Cursor1Milliseconds
            ?? DisturbanceView.EffectiveTriggerMilliseconds
            ?? (DisturbanceView.ViewStartMilliseconds + DisturbanceView.ViewEndMilliseconds) * 0.5;
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
        if (_analysisMode == AnalysisMode.Phasor)
            EnsurePhasorCursor();
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
            _phasorCursorMilliseconds,
            _harmonicCursorMilliseconds);
    }

    private void QueueDigitalEventTimelineNormalization()
    {
        Dispatcher.BeginInvoke(() =>
        {
            var source = DigitalEventGrid.ItemsSource;
            if (source is null || ReferenceEquals(source, _normalizedDigitalEventSource) ||
                source is not IEnumerable<ComtradeDigitalEventRow> rows)
                return;

            var trigger = DisturbanceView.EffectiveTriggerMilliseconds ?? ResolveTriggerMilliseconds();
            if (trigger is not { } triggerMilliseconds) return;

            var normalized = rows
                .Select(row => row with
                {
                    TimeText = ComtradeDisturbanceTimelineMath.FormatRelativeTime(
                        row.AbsoluteMilliseconds - triggerMilliseconds)
                })
                .ToArray();
            _normalizedDigitalEventSource = normalized;
            DigitalEventGrid.ItemsSource = normalized;
        }, DispatcherPriority.Background);
    }
}
