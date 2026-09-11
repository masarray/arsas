using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const double P1D5TrackTopMargin = 14.0;
    private const double P1D5AnalogTrackHeight = 92.0;
    private const double P1D5DigitalTrackHeight = 38.0;
    private const double P1D5TrackGap = 5.0;

    [Flags]
    private enum P1D5MeasurementTargets
    {
        None = 0,
        Cursor1 = 1,
        Cursor2 = 2,
        Both = Cursor1 | Cursor2
    }

    private readonly Dictionary<uint, P1D5CursorReadoutControls> _p1d5CursorReadoutControls = new();
    private ComtradeSignalItem[] _p1d5VisibleTrackOrder = Array.Empty<ComtradeSignalItem>();
    private ComtradeSignalItem[] _p1d5VisibleAnalogTrackOrder = Array.Empty<ComtradeSignalItem>();
    private bool _p1d5MeasurementRenderingHooked;
    private bool _p1d5MeasurementDirty;
    private bool _p1d5MeasurementWorkerRunning;
    private P1D5MeasurementTargets _p1d5PendingMeasurementTargets;
    private P1D5MeasurementTargets _p1d5InFlightMeasurementTargets;
    private long _p1d5MeasurementRevision;
    private long _p1d5LastPresentedMeasurementRevision;
    private CancellationTokenSource? _p1d5MeasurementCts = new();
    private bool _p1d5MeasurementEventsAttached;

    private void AttachP1D5MeasurementEvents()
    {
        if (_p1d5MeasurementEventsAttached)
            return;
        _p1d5MeasurementEventsAttached = true;

        InvestigationTimeline.CursorChanged += P1D5InvestigationTimeline_CursorChanged;
        DisturbanceView.CursorChanged += P1D5DisturbanceCursorChanged;
        Closed += P1D5MeasurementWindow_Closed;
    }

    private void P1D5MeasurementWindow_Closed(object? sender, EventArgs e)
    {
        StopP1D5MeasurementRenderingPump();
        _p1d5MeasurementCts?.Cancel();
        _p1d5MeasurementCts?.Dispose();
        _p1d5MeasurementCts = null;
        if (_p1d5MeasurementEventsAttached)
        {
            InvestigationTimeline.CursorChanged -= P1D5InvestigationTimeline_CursorChanged;
            DisturbanceView.CursorChanged -= P1D5DisturbanceCursorChanged;
            _p1d5MeasurementEventsAttached = false;
        }
    }

    private void P1D5InvestigationTimeline_CursorChanged(object? sender, ComtradeInvestigationTimelineCursorChangedEventArgs e)
    {
        var target = e.Cursor switch
        {
            ComtradeInvestigationTimelineCursor.Cursor1 => P1D5MeasurementTargets.Cursor1,
            ComtradeInvestigationTimelineCursor.Cursor2 => P1D5MeasurementTargets.Cursor2,
            _ => P1D5MeasurementTargets.None
        };
        if (target != P1D5MeasurementTargets.None)
            QueueP1D5CursorMeasurements(target);
    }

    private void P1D5DisturbanceCursorChanged(object? sender, ComtradeDisturbanceCursorChangedEventArgs e)
    {
        if (_analysisMode != AnalysisMode.Waveform)
            return;
        QueueP1D5CursorMeasurements(e.Cursor == ComtradeDisturbanceCursor.Cursor1
            ? P1D5MeasurementTargets.Cursor1
            : P1D5MeasurementTargets.Cursor2);
    }

    private void P1D5RememberTrackOrder(IReadOnlyList<LoadedDisturbanceTrack> tracks)
    {
        var next = new ComtradeSignalItem[tracks.Count];
        var analogCount = 0;
        for (var index = 0; index < tracks.Count; index++)
        {
            var signal = tracks[index].Signal;
            next[index] = signal;
            if (signal.IsAnalog) analogCount++;
        }

        var analog = new ComtradeSignalItem[analogCount];
        var write = 0;
        for (var index = 0; index < next.Length; index++)
        {
            if (next[index].IsAnalog)
                analog[write++] = next[index];
        }

        _p1d5VisibleTrackOrder = next;
        _p1d5VisibleAnalogTrackOrder = analog;
        RebuildP1D5CursorReadoutOverlay();
        AttachP1D5MeasurementEvents();
        QueueP1D5CursorMeasurements();
    }

    private void RebuildP1D5CursorReadoutOverlay()
    {
        CursorReadoutCanvas.Children.Clear();
        _p1d5CursorReadoutControls.Clear();
        var top = P1D5TrackTopMargin;

        for (var index = 0; index < _p1d5VisibleTrackOrder.Length; index++)
        {
            var signal = _p1d5VisibleTrackOrder[index];
            if (!signal.IsAnalog)
            {
                top += P1D5DigitalTrackHeight + P1D5TrackGap;
                continue;
            }

            var c1 = CreateP1D5CursorReadout(Color.FromRgb(205, 126, 20));
            var c2 = CreateP1D5CursorReadout(Color.FromRgb(20, 143, 183));
            c1.Text = ComtradeCursorReadoutPolicy.FormatValue("C1", P1D5IsRmsTrace, null, CultureInfo.CurrentCulture);
            c2.Text = ComtradeCursorReadoutPolicy.FormatValue("C2", P1D5IsRmsTrace, null, CultureInfo.CurrentCulture);
            Canvas.SetLeft(c1, 22.0);
            Canvas.SetTop(c1, top + 45.0);
            Canvas.SetLeft(c2, 22.0);
            Canvas.SetTop(c2, top + 62.0);
            CursorReadoutCanvas.Children.Add(c1);
            CursorReadoutCanvas.Children.Add(c2);
            _p1d5CursorReadoutControls[signal.Index] = new P1D5CursorReadoutControls(c1, c2);
            top += P1D5AnalogTrackHeight + P1D5TrackGap;
        }
    }

    private static TextBlock CreateP1D5CursorReadout(Color color)
        => new()
        {
            FontFamily = new FontFamily("Segoe UI"),
            FontSize = 8.8,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(color),
            IsHitTestVisible = false,
            Text = string.Empty
        };

    private void QueueP1D5CursorMeasurements(P1D5MeasurementTargets targets = P1D5MeasurementTargets.Both)
    {
        if (_analysisMode != AnalysisMode.Waveform || targets == P1D5MeasurementTargets.None ||
            _p1d5VisibleAnalogTrackOrder.Length == 0 || !_record.Supports(ArdIrecNativeBridge.CapCursorMeasurement))
            return;

        // If a newer cursor move invalidates a worker already in flight, carry that worker's target
        // into the replacement request. This preserves eventual values for both cursors while the
        // normal scrub path reads only the cursor that actually moved.
        if (_p1d5MeasurementWorkerRunning)
            _p1d5PendingMeasurementTargets |= _p1d5InFlightMeasurementTargets;
        _p1d5PendingMeasurementTargets |= targets;

        var revision = Interlocked.Increment(ref _p1d5MeasurementRevision);
        if (revision <= 0)
            Interlocked.Exchange(ref _p1d5MeasurementRevision, 1);

        _p1d5MeasurementDirty = true;
        if (!_p1d5MeasurementWorkerRunning)
            EnsureP1D5MeasurementRenderingPump();
    }

    private void EnsureP1D5MeasurementRenderingPump()
    {
        if (_p1d5MeasurementRenderingHooked)
            return;
        CompositionTarget.Rendering += P1D5MeasurementCompositionFrame;
        _p1d5MeasurementRenderingHooked = true;
    }

    private void StopP1D5MeasurementRenderingPump()
    {
        if (!_p1d5MeasurementRenderingHooked)
            return;
        CompositionTarget.Rendering -= P1D5MeasurementCompositionFrame;
        _p1d5MeasurementRenderingHooked = false;
    }

    private void P1D5MeasurementCompositionFrame(object? sender, EventArgs e)
    {
        if (_analysisMode != AnalysisMode.Waveform)
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }
        if (_p1d5MeasurementWorkerRunning || !_p1d5MeasurementDirty)
            return;

        var targets = _p1d5PendingMeasurementTargets;
        _p1d5PendingMeasurementTargets = P1D5MeasurementTargets.None;
        _p1d5MeasurementDirty = false;
        if (targets == P1D5MeasurementTargets.None)
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }

        var revision = Interlocked.Read(ref _p1d5MeasurementRevision);
        var c1 = DisturbanceView.Cursor1Milliseconds;
        var c2 = DisturbanceView.Cursor2Milliseconds;
        ulong? c1Frame = c1 is { } first && TryResolveDisturbanceFrameAtMilliseconds(first, out var firstFrame) ? firstFrame : null;
        ulong? c2Frame = c2 is { } second && TryResolveDisturbanceFrameAtMilliseconds(second, out var secondFrame) ? secondFrame : null;
        if ((targets.HasFlag(P1D5MeasurementTargets.Cursor1) && c1Frame is null) &&
            (targets.HasFlag(P1D5MeasurementTargets.Cursor2) && c2Frame is null))
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }

        var analogSignals = _p1d5VisibleAnalogTrackOrder;
        if (analogSignals.Length == 0)
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }

        var token = EnsureP1D5MeasurementToken();
        _p1d5MeasurementWorkerRunning = true;
        _p1d5InFlightMeasurementTargets = targets;
        StopP1D5MeasurementRenderingPump();
        _ = ExecuteP1D5CursorMeasurementsAsync(
            new P1D5MeasurementRequest(revision, analogSignals, c1Frame, c2Frame, _p1d5ValueRepresentation, targets),
            token);
    }

    private async Task ExecuteP1D5CursorMeasurementsAsync(P1D5MeasurementRequest request, CancellationToken token)
    {
        try
        {
            if (!P1D5MeasurementRequestIsCurrent(request.Revision, token))
                return;

            await _nativeGate.WaitAsync(token).ConfigureAwait(false);
            P1D5MeasurementResult result;
            try
            {
                if (!P1D5MeasurementRequestIsCurrent(request.Revision, token))
                    return;

                result = await Task.Run(() => ReadP1D5CursorMeasurements(request, token), token).ConfigureAwait(false);
            }
            finally
            {
                _nativeGate.Release();
            }

            if (!P1D5MeasurementRequestIsCurrent(request.Revision, token))
                return;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!P1D5MeasurementRequestIsCurrent(request.Revision, token) ||
                    request.Revision < _p1d5LastPresentedMeasurementRevision)
                    return;

                PresentP1D5CursorMeasurements(result);
                _p1d5LastPresentedMeasurementRevision = request.Revision;
            });
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
                "P1D5.CursorMeasurement",
                "CURSOR_MEASUREMENT_FAILURE",
                $"revision={request.Revision}; representation={request.Representation}; targets={request.Targets}",
                ex);
        }
        finally
        {
            if (!Dispatcher.HasShutdownStarted && !Dispatcher.HasShutdownFinished)
            {
                try
                {
                    await Dispatcher.InvokeAsync(CompleteP1D5MeasurementWorker);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
    }

    private bool P1D5MeasurementRequestIsCurrent(long revision, CancellationToken token)
        => ComtradeCursorReadoutPolicy.IsCurrent(
            revision,
            Interlocked.Read(ref _p1d5MeasurementRevision),
            token.IsCancellationRequested);

    private void CompleteP1D5MeasurementWorker()
    {
        _p1d5MeasurementWorkerRunning = false;
        _p1d5InFlightMeasurementTargets = P1D5MeasurementTargets.None;
        if (_p1d5MeasurementDirty && _analysisMode == AnalysisMode.Waveform)
            EnsureP1D5MeasurementRenderingPump();
        else
            StopP1D5MeasurementRenderingPump();
    }

    private P1D5MeasurementResult ReadP1D5CursorMeasurements(P1D5MeasurementRequest request, CancellationToken token)
    {
        var rows = new P1D5MeasurementRow[request.Signals.Length];
        for (var index = 0; index < request.Signals.Length; index++)
        {
            token.ThrowIfCancellationRequested();
            var signal = request.Signals[index];
            ComtradeCursorMeasurement? c1 = null;
            ComtradeCursorMeasurement? c2 = null;
            if (request.Targets.HasFlag(P1D5MeasurementTargets.Cursor1) && request.Cursor1Frame is { } first)
                _record.TryReadCursorMeasurement(signal.Index, first, request.Representation, out c1);
            if (request.Targets.HasFlag(P1D5MeasurementTargets.Cursor2) && request.Cursor2Frame is { } second)
                _record.TryReadCursorMeasurement(signal.Index, second, request.Representation, out c2);
            rows[index] = new P1D5MeasurementRow(signal.Index, c1, c2);
        }
        return new P1D5MeasurementResult(rows, request.Targets);
    }

    private void PresentP1D5CursorMeasurements(P1D5MeasurementResult result)
    {
        for (var index = 0; index < result.Rows.Length; index++)
        {
            var row = result.Rows[index];
            if (!_p1d5CursorReadoutControls.TryGetValue(row.ChannelIndex, out var controls))
                continue;
            if (result.Targets.HasFlag(P1D5MeasurementTargets.Cursor1))
                controls.Cursor1.Text = FormatP1D5CursorValue("C1", row.Cursor1);
            if (result.Targets.HasFlag(P1D5MeasurementTargets.Cursor2))
                controls.Cursor2.Text = FormatP1D5CursorValue("C2", row.Cursor2);
        }
    }

    private string FormatP1D5CursorValue(string cursor, ComtradeCursorMeasurement? measurement)
    {
        double? value = measurement is { Valid: true }
            ? P1D5IsRmsTrace ? measurement.Rms : measurement.Instantaneous
            : null;
        return ComtradeCursorReadoutPolicy.FormatValue(cursor, P1D5IsRmsTrace, value, CultureInfo.CurrentCulture);
    }

    private CancellationToken EnsureP1D5MeasurementToken()
    {
        if (_p1d5MeasurementCts is null || _p1d5MeasurementCts.IsCancellationRequested)
        {
            _p1d5MeasurementCts?.Dispose();
            _p1d5MeasurementCts = new CancellationTokenSource();
        }
        return _p1d5MeasurementCts.Token;
    }

    private sealed record P1D5CursorReadoutControls(TextBlock Cursor1, TextBlock Cursor2);
    private readonly record struct P1D5MeasurementRequest(
        long Revision,
        ComtradeSignalItem[] Signals,
        ulong? Cursor1Frame,
        ulong? Cursor2Frame,
        int Representation,
        P1D5MeasurementTargets Targets);
    private readonly record struct P1D5MeasurementRow(
        uint ChannelIndex,
        ComtradeCursorMeasurement? Cursor1,
        ComtradeCursorMeasurement? Cursor2);
    private sealed record P1D5MeasurementResult(P1D5MeasurementRow[] Rows, P1D5MeasurementTargets Targets);
}
