using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const uint P1D5LocusMaximumPointsPerLoop = 900;

    private Button? _p1d5LocusButton;
    private ComtradeLocusView? _p1d5LocusView;
    private ArdIrecLocusNativeSession? _p1d5LocusSession;
    private CancellationTokenSource? _p1d5LocusLoadCts;
    private bool _p1d5LocusUiInitialized;
    private bool _p1d5LocusActive;
    private long _p1d5LocusLoadGeneration;
    private long _p1d5LocusCursorRevision;
    private bool _p1d5LocusCursorDirty;
    private bool _p1d5LocusCursorWorkerRunning;
    private bool _p1d5LocusCursorRenderingHooked;

    private void InitializeP1D5LocusUi()
    {
        if (_p1d5LocusUiInitialized) return;
        _p1d5LocusUiInitialized = true;

        if (HarmonicsModeButton.Parent is Panel modePanel)
        {
            _p1d5LocusButton = new Button
            {
                Content = "Locus",
                Height = 27,
                MinWidth = 72,
                Margin = new Thickness(5, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                FontSize = 10.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                ToolTip = "Protection R-X locus using the validated ArdIrec distance engine."
            };
            var index = modePanel.Children.IndexOf(HarmonicsModeButton);
            modePanel.Children.Insert(Math.Max(0, index + 1), _p1d5LocusButton);
            _p1d5LocusButton.Click += P1D5Locus_Click;
            ApplyModeButton(_p1d5LocusButton, false);
        }

        if (WaveformWorkspaceHost.Parent is Grid workspaceGrid)
        {
            _p1d5LocusView = new ComtradeLocusView
            {
                Visibility = Visibility.Collapsed,
                MinHeight = 360
            };
            workspaceGrid.Children.Add(_p1d5LocusView);
        }

        WaveformModeButton.Click += P1D5StandardModeButton_Click;
        PhasorModeButton.Click += P1D5StandardModeButton_Click;
        HarmonicsModeButton.Click += P1D5StandardModeButton_Click;
        InvestigationTimeline.CursorChanged += P1D5LocusTimeline_CursorChanged;
        Closed += P1D5LocusWindow_Closed;
    }

    private async void P1D5Locus_Click(object sender, RoutedEventArgs e)
    {
        StopP1D4LiveAnalysisScrub();
        SetAnalysisMode(AnalysisMode.Waveform);
        _p1d5LocusActive = true;

        WaveformWorkspaceHost.Visibility = Visibility.Collapsed;
        PhasorView.Visibility = Visibility.Collapsed;
        HarmonicsView.Visibility = Visibility.Collapsed;
        if (_p1d5LocusView is not null) _p1d5LocusView.Visibility = Visibility.Visible;
        TimeNavigationPanel.Visibility = Visibility.Collapsed;
        WaveformTraceModePanel.Visibility = Visibility.Collapsed;

        ApplyModeButton(WaveformModeButton, false);
        ApplyModeButton(PhasorModeButton, false);
        ApplyModeButton(HarmonicsModeButton, false);
        if (_p1d5LocusButton is not null) ApplyModeButton(_p1d5LocusButton, true);

        InvestigationTimeline.SetMode(ComtradeInvestigationTimelineMode.DualCursor);
        SyncInvestigationTimeline();
        SyncInvestigationTimelineGeometry();
        StatusTextBlock.Text = "Loading protection R-X locus…";
        await RefreshP1D5LocusStaticAsync(forceReopen: _p1d5LocusSession is null).ConfigureAwait(true);
    }

    private void P1D5StandardModeButton_Click(object sender, RoutedEventArgs e)
    {
        if (!_p1d5LocusActive) return;
        ExitP1D5LocusMode();
    }

    private void ExitP1D5LocusMode()
    {
        if (!_p1d5LocusActive) return;
        _p1d5LocusActive = false;
        unchecked { _p1d5LocusLoadGeneration++; }
        _p1d5LocusLoadCts?.Cancel();
        _p1d5LocusLoadCts?.Dispose();
        _p1d5LocusLoadCts = null;
        StopP1D5LocusCursorPump();
        _p1d5LocusCursorDirty = false;

        _p1d5LocusSession?.Dispose();
        _p1d5LocusSession = null;
        if (_p1d5LocusView is not null) _p1d5LocusView.Visibility = Visibility.Collapsed;
        if (_p1d5LocusButton is not null) ApplyModeButton(_p1d5LocusButton, false);

        WaveformWorkspaceHost.Visibility = _analysisMode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        PhasorView.Visibility = _analysisMode == AnalysisMode.Phasor ? Visibility.Visible : Visibility.Collapsed;
        HarmonicsView.Visibility = _analysisMode == AnalysisMode.Harmonics ? Visibility.Visible : Visibility.Collapsed;
        TimeNavigationPanel.Visibility = _analysisMode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        WaveformTraceModePanel.Visibility = _analysisMode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
    }

    private void P1D5LocusWindow_Closed(object? sender, EventArgs e)
    {
        _p1d5LocusActive = false;
        _p1d5LocusLoadCts?.Cancel();
        _p1d5LocusLoadCts?.Dispose();
        _p1d5LocusLoadCts = null;
        StopP1D5LocusCursorPump();
        _p1d5LocusSession?.Dispose();
        _p1d5LocusSession = null;

        if (_p1d5LocusButton is not null) _p1d5LocusButton.Click -= P1D5Locus_Click;
        WaveformModeButton.Click -= P1D5StandardModeButton_Click;
        PhasorModeButton.Click -= P1D5StandardModeButton_Click;
        HarmonicsModeButton.Click -= P1D5StandardModeButton_Click;
        InvestigationTimeline.CursorChanged -= P1D5LocusTimeline_CursorChanged;
    }

    private async Task RefreshP1D5LocusStaticAsync(bool forceReopen = false)
    {
        if (!_p1d5LocusActive || _p1d5LocusView is null) return;

        unchecked { _p1d5LocusLoadGeneration++; }
        if (_p1d5LocusLoadGeneration <= 0) _p1d5LocusLoadGeneration = 1;
        var generation = _p1d5LocusLoadGeneration;
        _p1d5LocusLoadCts?.Cancel();
        _p1d5LocusLoadCts?.Dispose();
        _p1d5LocusLoadCts = new CancellationTokenSource();
        var token = _p1d5LocusLoadCts.Token;

        if (forceReopen)
        {
            _p1d5LocusSession?.Dispose();
            _p1d5LocusSession = null;
        }

        try
        {
            if (_p1d5LocusSession is null)
            {
                var open = await Task.Run(() =>
                {
                    var ok = ArdIrecLocusNativeSession.TryOpen(_record.CfgPath, out var session, out var error);
                    return (Ok: ok, Session: session, Error: error);
                }, token).ConfigureAwait(true);

                if (token.IsCancellationRequested || generation != _p1d5LocusLoadGeneration || !_p1d5LocusActive)
                {
                    open.Session?.Dispose();
                    return;
                }

                if (!open.Ok || open.Session is null)
                {
                    _p1d5LocusView.ShowMessage("Protection locus", open.Error);
                    StatusTextBlock.Text = "Locus unavailable: the installed COMTRADE bridge does not include P1D.5 locus exports.";
                    return;
                }
                _p1d5LocusSession = open.Session;
            }

            var sessionSnapshot = _p1d5LocusSession;
            if (sessionSnapshot is null) return;
            var representation = _p1d5ValueRepresentation;
            var result = await Task.Run(() => BuildP1D5LocusSeries(sessionSnapshot, representation, token), token)
                .ConfigureAwait(true);

            if (token.IsCancellationRequested || generation != _p1d5LocusLoadGeneration || !_p1d5LocusActive)
                return;

            if (!result.Success)
            {
                _p1d5LocusView.ShowMessage("Protection locus", result.Error);
                StatusTextBlock.Text = "Locus calculation unavailable • diagnostics captured.";
                return;
            }

            _p1d5LocusView.ShowTrajectories(
                "Protection distance locus",
                $"Full-record sampled trajectory • {P1D5RepresentationLabel} Ω • phase loops use validated differential V/I • earth loops shown with kL=0",
                P1D5RepresentationLabel,
                result.Earth,
                result.Phase);
            StatusTextBlock.Text =
                $"Locus • {result.Earth.Count} earth + {result.Phase.Count} phase loop(s) • {P1D5RepresentationLabel} • native distance equations";
            QueueP1D5LocusCursorRefresh();
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ComtradeDiagnosticQueue.TryEnqueue(
                "P1D5.Locus",
                "LOCUS_STATIC_FAILURE",
                $"generation={generation}; representation={_p1d5ValueRepresentation}",
                ex);
            if (_p1d5LocusActive)
            {
                _p1d5LocusView.ShowMessage("Protection locus", "Locus calculation failed. Diagnostics captured.");
                StatusTextBlock.Text = "Locus calculation failed • diagnostics captured.";
            }
        }
    }

    private P1D5LocusLoadResult BuildP1D5LocusSeries(
        ArdIrecLocusNativeSession session,
        int representation,
        CancellationToken token)
    {
        var earth = new List<ComtradeLocusSeries>(3);
        var phase = new List<ComtradeLocusSeries>(3);
        for (var loop = ArdIrecLocusNativeSession.LoopL1E; loop <= ArdIrecLocusNativeSession.LoopL3L1; loop++)
        {
            token.ThrowIfCancellationRequested();
            if (!session.TryReadLocus(
                    loop,
                    0,
                    session.FrameCount,
                    P1D5LocusMaximumPointsPerLoop,
                    representation,
                    0.0,
                    0.0,
                    out var points,
                    out var error))
            {
                ComtradeDiagnosticQueue.TryEnqueue(
                    "P1D5.Locus",
                    "LOCUS_LOOP_FAILURE",
                    $"loop={loop}; {error}",
                    null);
                continue;
            }

            var anyValid = false;
            for (var index = 0; index < points.Length; index++)
            {
                if (points[index].Valid)
                {
                    anyValid = true;
                    break;
                }
            }
            if (!anyValid) continue;

            var item = new ComtradeLocusSeries(
                ComtradeLocusView.LoopName(loop),
                P1D5LocusColor(loop),
                points);
            if (loop <= ArdIrecLocusNativeSession.LoopL3E) earth.Add(item);
            else phase.Add(item);
        }

        if (earth.Count == 0 && phase.Count == 0)
            return new P1D5LocusLoadResult(false, earth, phase,
                "No valid protection loop can be formed from the record's mapped three-phase Voltage/Current channels.");
        return new P1D5LocusLoadResult(true, earth, phase, string.Empty);
    }

    private static Color P1D5LocusColor(int loop) => loop switch
    {
        ArdIrecLocusNativeSession.LoopL1E => Color.FromRgb(0, 146, 63),
        ArdIrecLocusNativeSession.LoopL2E => Color.FromRgb(224, 0, 208),
        ArdIrecLocusNativeSession.LoopL3E => Color.FromRgb(23, 105, 210),
        ArdIrecLocusNativeSession.LoopL1L2 => Color.FromRgb(103, 137, 238),
        ArdIrecLocusNativeSession.LoopL2L3 => Color.FromRgb(0, 161, 132),
        ArdIrecLocusNativeSession.LoopL3L1 => Color.FromRgb(181, 104, 196),
        _ => Color.FromRgb(111, 119, 128)
    };

    private void P1D5LocusTimeline_CursorChanged(object? sender, ComtradeInvestigationTimelineCursorChangedEventArgs e)
    {
        if (!_p1d5LocusActive || e.Cursor is not (ComtradeInvestigationTimelineCursor.Cursor1 or ComtradeInvestigationTimelineCursor.Cursor2))
            return;
        QueueP1D5LocusCursorRefresh();
    }

    private void QueueP1D5LocusCursorRefresh()
    {
        if (!_p1d5LocusActive || _p1d5LocusSession is null) return;
        unchecked { _p1d5LocusCursorRevision++; }
        if (_p1d5LocusCursorRevision <= 0) _p1d5LocusCursorRevision = 1;
        _p1d5LocusCursorDirty = true;
        if (!_p1d5LocusCursorWorkerRunning) EnsureP1D5LocusCursorPump();
    }

    private void EnsureP1D5LocusCursorPump()
    {
        if (_p1d5LocusCursorRenderingHooked) return;
        CompositionTarget.Rendering += P1D5LocusCompositionFrame;
        _p1d5LocusCursorRenderingHooked = true;
    }

    private void StopP1D5LocusCursorPump()
    {
        if (!_p1d5LocusCursorRenderingHooked) return;
        CompositionTarget.Rendering -= P1D5LocusCompositionFrame;
        _p1d5LocusCursorRenderingHooked = false;
    }

    private void P1D5LocusCompositionFrame(object? sender, EventArgs e)
    {
        if (!_p1d5LocusActive || _p1d5LocusSession is null)
        {
            StopP1D5LocusCursorPump();
            return;
        }
        if (_p1d5LocusCursorWorkerRunning || !_p1d5LocusCursorDirty) return;

        _p1d5LocusCursorDirty = false;
        var revision = _p1d5LocusCursorRevision;
        ulong? c1Frame = DisturbanceView.Cursor1Milliseconds is { } c1 &&
                         TryResolveDisturbanceFrameAtMilliseconds(c1, out var first) ? first : null;
        ulong? c2Frame = DisturbanceView.Cursor2Milliseconds is { } c2 &&
                         TryResolveDisturbanceFrameAtMilliseconds(c2, out var second) ? second : null;
        if (c1Frame is null && c2Frame is null)
        {
            StopP1D5LocusCursorPump();
            return;
        }

        var session = _p1d5LocusSession;
        var representation = _p1d5ValueRepresentation;
        _p1d5LocusCursorWorkerRunning = true;
        StopP1D5LocusCursorPump();
        _ = ExecuteP1D5LocusCursorAsync(session, representation, c1Frame, c2Frame, revision);
    }

    private async Task ExecuteP1D5LocusCursorAsync(
        ArdIrecLocusNativeSession session,
        int representation,
        ulong? c1Frame,
        ulong? c2Frame,
        long revision)
    {
        try
        {
            var result = await Task.Run(() =>
            {
                ComtradeDistancePoint[] c1 = Array.Empty<ComtradeDistancePoint>();
                ComtradeDistancePoint[] c2 = Array.Empty<ComtradeDistancePoint>();
                string error = string.Empty;
                if (c1Frame is { } first && !session.TryReadLoops(first, representation, 0.0, 0.0, out c1, out error))
                    return new P1D5LocusCursorResult(false, c1, c2, error);
                if (c2Frame is { } second && !session.TryReadLoops(second, representation, 0.0, 0.0, out c2, out error))
                    return new P1D5LocusCursorResult(false, c1, c2, error);
                return new P1D5LocusCursorResult(true, c1, c2, string.Empty);
            }).ConfigureAwait(true);

            if (!_p1d5LocusActive || revision != _p1d5LocusCursorRevision || !ReferenceEquals(session, _p1d5LocusSession))
                return;
            if (!result.Success)
            {
                ComtradeDiagnosticQueue.TryEnqueue("P1D5.Locus", "LOCUS_CURSOR_FAILURE", result.Error, null);
                return;
            }
            _p1d5LocusView?.SetCursorPoints(result.Cursor1, result.Cursor2);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            ComtradeDiagnosticQueue.TryEnqueue(
                "P1D5.Locus", "LOCUS_CURSOR_FAILURE", $"revision={revision}", ex);
        }
        finally
        {
            _p1d5LocusCursorWorkerRunning = false;
            if (_p1d5LocusCursorDirty && _p1d5LocusActive) EnsureP1D5LocusCursorPump();
        }
    }

    private sealed record P1D5LocusLoadResult(
        bool Success,
        IReadOnlyList<ComtradeLocusSeries> Earth,
        IReadOnlyList<ComtradeLocusSeries> Phase,
        string Error);

    private sealed record P1D5LocusCursorResult(
        bool Success,
        IReadOnlyList<ComtradeDistancePoint> Cursor1,
        IReadOnlyList<ComtradeDistancePoint> Cursor2,
        string Error);
}
