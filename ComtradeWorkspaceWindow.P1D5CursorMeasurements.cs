using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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

    private readonly Dictionary<uint, P1D5CursorReadoutControls> _p1d5CursorReadoutControls = new();
    private ComtradeSignalItem[] _p1d5VisibleTrackOrder = Array.Empty<ComtradeSignalItem>();
    private ComtradeSignalItem[] _p1d5VisibleAnalogTrackOrder = Array.Empty<ComtradeSignalItem>();
    private bool _p1d5MeasurementRenderingHooked;
    private bool _p1d5MeasurementDirty;
    private bool _p1d5MeasurementWorkerRunning;
    private long _p1d5MeasurementRevision;
    private CancellationTokenSource? _p1d5MeasurementCts = new();
    private bool _p1d5MeasurementEventsAttached;

    private void AttachP1D5MeasurementEvents()
    {
        if (_p1d5MeasurementEventsAttached)
            return;
        _p1d5MeasurementEventsAttached = true;
        InvestigationTimeline.CursorChanged += P1D5InvestigationTimeline_CursorChanged;
        DisturbanceView.AddHandler(Mouse.MouseMoveEvent, new MouseEventHandler(P1D5DisturbanceMouseMove), handledEventsToo: true);
        DisturbanceView.AddHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(P1D5DisturbanceMouseUp), handledEventsToo: true);
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
            DisturbanceView.RemoveHandler(Mouse.MouseMoveEvent, new MouseEventHandler(P1D5DisturbanceMouseMove));
            DisturbanceView.RemoveHandler(Mouse.MouseUpEvent, new MouseButtonEventHandler(P1D5DisturbanceMouseUp));
            _p1d5MeasurementEventsAttached = false;
        }
    }

    private void P1D5InvestigationTimeline_CursorChanged(object? sender, ComtradeInvestigationTimelineCursorChangedEventArgs e)
    {
        if (e.Cursor is ComtradeInvestigationTimelineCursor.Cursor1 or ComtradeInvestigationTimelineCursor.Cursor2)
            QueueP1D5CursorMeasurements();
    }

    private void P1D5DisturbanceMouseMove(object sender, MouseEventArgs e)
    {
        if (_analysisMode == AnalysisMode.Waveform && e.LeftButton == MouseButtonState.Pressed)
            QueueP1D5CursorMeasurements();
    }

    private void P1D5DisturbanceMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_analysisMode == AnalysisMode.Waveform)
            QueueP1D5CursorMeasurements();
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

    private void QueueP1D5CursorMeasurements()
    {
        if (_analysisMode != AnalysisMode.Waveform || _p1d5VisibleAnalogTrackOrder.Length == 0 ||
            !_record.Supports(ArdIrecNativeBridge.CapCursorMeasurement))
            return;

        unchecked { _p1d5MeasurementRevision++; }
        if (_p1d5MeasurementRevision <= 0) _p1d5MeasurementRevision = 1;
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

        _p1d5MeasurementDirty = false;
        var revision = _p1d5MeasurementRevision;
        var c1 = DisturbanceView.Cursor1Milliseconds;
        var c2 = DisturbanceView.Cursor2Milliseconds;
        ulong? c1Frame = c1 is { } first && TryResolveDisturbanceFrameAtMilliseconds(first, out var firstFrame) ? firstFrame : null;
        ulong? c2Frame = c2 is { } second && TryResolveDisturbanceFrameAtMilliseconds(second, out var secondFrame) ? secondFrame : null;
        if (c1Frame is null && c2Frame is null)
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }

        // Track selection changes are rare compared with pointer frames. Keep the immutable analog
        // order cached at track-load time so cursor scrubbing does not allocate LINQ arrays at
        // composition cadence.
        var analogSignals = _p1d5VisibleAnalogTrackOrder;
        if (analogSignals.Length == 0)
        {
            StopP1D5MeasurementRenderingPump();
            return;
        }

        _p1d5MeasurementWorkerRunning = true;
        StopP1D5MeasurementRenderingPump();
        _ = ExecuteP1D5CursorMeasurementsAsync(
            new P1D5MeasurementRequest(revision, analogSignals, c1Frame, c2Frame, _p1d5ValueRepresentation));
    }

    private async Task ExecuteP1D5CursorMeasurementsAsync(P1D5MeasurementRequest request)
    {
        try
        {
            var token = EnsureP1D5MeasurementToken();
            await _nativeGate.WaitAsync(token).ConfigureAwait(false);
            P1D5MeasurementResult result;
            try
            {
                result = await Task.Run(() => ReadP1D5CursorMeasurements(request, token), token).ConfigureAwait(false);
            }
            finally
            {
                _nativeGate.Release();
            }

            if (token.IsCancellationRequested || request.Revision != _p1d5MeasurementRevision)
                return;

            await Dispatcher.InvokeAsync(() => PresentP1D5CursorMeasurements(result));
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
                $"revision={request.Revision}; representation={request.Representation}",
                ex);
        }
        finally
        {
            _p1d5MeasurementWorkerRunning = false;
            if (_p1d5MeasurementDirty && _analysisMode == AnalysisMode.Waveform)
                EnsureP1D5MeasurementRenderingPump();
        }
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
            if (request.Cursor1Frame is { } first)
                _record.TryReadCursorMeasurement(signal.Index, first, request.Representation, out c1);
            if (request.Cursor2Frame is { } second)
                _record.TryReadCursorMeasurement(signal.Index, second, request.Representation, out c2);
            rows[index] = new P1D5MeasurementRow(signal.Index, c1, c2);
        }
        return new P1D5MeasurementResult(rows);
    }

    private void PresentP1D5CursorMeasurements(P1D5MeasurementResult result)
    {
        for (var index = 0; index < result.Rows.Length; index++)
        {
            var row = result.Rows[index];
            if (!_p1d5CursorReadoutControls.TryGetValue(row.ChannelIndex, out var controls))
                continue;
            controls.Cursor1.Text = FormatP1D5CursorValue("C1", row.Cursor1);
            controls.Cursor2.Text = FormatP1D5CursorValue("C2", row.Cursor2);
        }
    }

    private string FormatP1D5CursorValue(string cursor, ComtradeCursorMeasurement? measurement)
    {
        if (measurement is not { Valid: true })
            return string.Empty;
        var value = P1D5IsRmsTrace ? measurement.Rms : measurement.Instantaneous;
        var mode = P1D5IsRmsTrace ? "RMS" : "Inst";
        return $"{cursor} {mode} {value.ToString("G6", CultureInfo.CurrentCulture)}";
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
        int Representation);
    private readonly record struct P1D5MeasurementRow(
        uint ChannelIndex,
        ComtradeCursorMeasurement? Cursor1,
        ComtradeCursorMeasurement? Cursor2);
    private sealed record P1D5MeasurementResult(P1D5MeasurementRow[] Rows);
}