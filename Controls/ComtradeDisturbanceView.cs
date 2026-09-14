using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

internal enum ComtradeDisturbanceCursor
{
    Cursor1,
    Cursor2
}

internal sealed record ComtradeDisturbanceDigitalEdge(
    uint Timestamp,
    ulong SourceFrame,
    byte BeforeState,
    byte AfterState,
    int NormalState);

internal sealed record ComtradeDisturbanceTrack(
    string Title,
    string Subtitle,
    string Units,
    bool IsDigital,
    double[]? Analog,
    byte[]? Digital,
    uint[] Timestamps,
    Color StrokeColor,
    bool PreserveAllPoints = false,
    ulong[]? SourceFrames = null,
    int DigitalNormalState = 0,
    IReadOnlyList<ComtradeDisturbanceDigitalEdge>? DigitalEdges = null,
    bool DigitalIsLossy = false);

internal sealed class ComtradeDisturbanceNavigationChangedEventArgs : EventArgs
{
    internal ComtradeDisturbanceNavigationChangedEventArgs(string summary) => Summary = summary;
    internal string Summary { get; }
}

internal sealed class ComtradeDisturbanceCursorChangedEventArgs : EventArgs
{
    internal ComtradeDisturbanceCursorChangedEventArgs(
        ComtradeDisturbanceCursor cursor,
        double absoluteMilliseconds,
        double snapToleranceMilliseconds,
        bool isFinal,
        bool locallySnapped)
    {
        Cursor = cursor;
        AbsoluteMilliseconds = absoluteMilliseconds;
        SnapToleranceMilliseconds = snapToleranceMilliseconds;
        IsFinal = isFinal;
        LocallySnapped = locallySnapped;
    }

    internal ComtradeDisturbanceCursor Cursor { get; }
    internal double AbsoluteMilliseconds { get; }
    internal double SnapToleranceMilliseconds { get; }
    internal bool IsFinal { get; }
    internal bool LocallySnapped { get; }
}

internal sealed class ComtradeDisturbancePanRequestedEventArgs : EventArgs
{
    internal ComtradeDisturbancePanRequestedEventArgs(double deltaFraction) => DeltaFraction = deltaFraction;
    internal double DeltaFraction { get; }
}

public sealed class ComtradeDisturbanceView : FrameworkElement
{
    private const double LabelWidth = 150.0;
    private const double RightMargin = 18.0;
    private const double TopMargin = 14.0;
    private const double BottomAxisHeight = 34.0;
    private const double AnalogTrackHeight = 92.0;
    private const double DigitalTrackHeight = 40.0;
    private const double TrackGap = 6.0;
    private const double CursorHitRadius = 9.0;
    private const double PanActivationPixels = 4.0;

    private enum PointerMode
    {
        None,
        PendingPan,
        Panning,
        Cursor1,
        Cursor2
    }

    private IReadOnlyList<ComtradeDisturbanceTrack> _tracks = Array.Empty<ComtradeDisturbanceTrack>();
    private double _timeMultiplier = 1.0;
    private double? _triggerMilliseconds;
    private double _nominalFrequencyHz;
    private double _fullStartMilliseconds;
    private double _fullEndMilliseconds;
    private double _viewStartMilliseconds;
    private double _viewEndMilliseconds;
    private double? _cursor1Milliseconds;
    private double? _cursor2Milliseconds;
    private Rect _lastPlot;
    private PointerMode _pointerMode;
    private Point _pointerStartPoint;
    private double _panStartMilliseconds;
    private double _panEndMilliseconds;

    internal event EventHandler<ComtradeDisturbanceNavigationChangedEventArgs>? NavigationChanged;
    internal event EventHandler<ComtradeDisturbanceCursorChangedEventArgs>? CursorChanged;
    internal event EventHandler<ComtradeDisturbancePanRequestedEventArgs>? PanRequested;

    internal double? Cursor1Milliseconds => _cursor1Milliseconds;
    internal double? Cursor2Milliseconds => _cursor2Milliseconds;
    // Compatibility aliases while P1D analysis consumers are being migrated from A/B naming.
    internal double? CursorAMilliseconds => _cursor1Milliseconds;
    internal double? CursorBMilliseconds => _cursor2Milliseconds;
    internal double ViewStartMilliseconds => _viewStartMilliseconds;
    internal double ViewEndMilliseconds => _viewEndMilliseconds;
    internal double FullStartMilliseconds => _fullStartMilliseconds;
    internal double FullEndMilliseconds => _fullEndMilliseconds;

    public ComtradeDisturbanceView()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        ToolTip = "Wheel: scroll signals • Ctrl+wheel: zoom • Drag plot: pan • Drag C1/C2: move • Right-click: C2 • cursors snap to digital edges";
    }

    internal void ShowTracks(
        IReadOnlyList<ComtradeDisturbanceTrack> tracks,
        double timeMultiplier,
        double? triggerMilliseconds,
        bool preserveCursor = true)
    {
        _tracks = tracks ?? Array.Empty<ComtradeDisturbanceTrack>();
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        _triggerMilliseconds = triggerMilliseconds is { } trigger && double.IsFinite(trigger) ? trigger : null;

        var bounds = GetTimeBounds(_tracks);
        _fullStartMilliseconds = bounds.Start;
        _fullEndMilliseconds = bounds.End;
        _viewStartMilliseconds = bounds.Start;
        _viewEndMilliseconds = bounds.End;
        if (!preserveCursor)
        {
            _cursor1Milliseconds = null;
            _cursor2Milliseconds = null;
        }

        Height = Math.Max(330, TopMargin + BottomAxisHeight + _tracks.Sum(track => TrackHeight(track) + TrackGap));
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ShowMessage(string message)
    {
        _tracks = Array.Empty<ComtradeDisturbanceTrack>();
        _fullStartMilliseconds = 0;
        _fullEndMilliseconds = 0;
        _viewStartMilliseconds = 0;
        _viewEndMilliseconds = 0;
        _cursor1Milliseconds = null;
        _cursor2Milliseconds = null;
        Height = 330;
        ToolTip = message;
        InvalidateVisual();
    }

    internal void ApplyTriggerFocusedDefault(double nominalFrequencyHz)
    {
        _nominalFrequencyHz = nominalFrequencyHz;
        if (_fullEndMilliseconds <= _fullStartMilliseconds)
            return;

        var window = ComtradeTimeSignalsNavigationMath.CreateTriggerFocusedWindow(
            _fullStartMilliseconds,
            _fullEndMilliseconds,
            _triggerMilliseconds,
            nominalFrequencyHz);
        SetView(window.StartMilliseconds, window.EndMilliseconds);

        var cursors = ComtradeTimeSignalsNavigationMath.CreateInitialCursors(
            _fullStartMilliseconds,
            _fullEndMilliseconds,
            _triggerMilliseconds,
            nominalFrequencyHz);
        _cursor1Milliseconds = cursors.Cursor1Milliseconds;
        _cursor2Milliseconds = cursors.Cursor2Milliseconds;
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ResetToTriggerView()
    {
        if (_nominalFrequencyHz > 0)
        {
            ApplyTriggerFocusedDefault(_nominalFrequencyHz);
            return;
        }
        ResetNavigation();
    }

    internal void SetCursorAFromAbsoluteMilliseconds(double milliseconds)
        => SetCursorFromHost(ComtradeDisturbanceCursor.Cursor1, milliseconds);

    internal void SetCursorFromHost(ComtradeDisturbanceCursor cursor, double milliseconds)
    {
        if (!double.IsFinite(milliseconds))
            return;
        if (cursor == ComtradeDisturbanceCursor.Cursor1)
            _cursor1Milliseconds = milliseconds;
        else
            _cursor2Milliseconds = milliseconds;
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ResetNavigation()
    {
        if (_fullEndMilliseconds <= _fullStartMilliseconds)
            return;
        _viewStartMilliseconds = _fullStartMilliseconds;
        _viewEndMilliseconds = _fullEndMilliseconds;
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void SetViewWindow(double startMilliseconds, double endMilliseconds)
    {
        SetView(startMilliseconds, endMilliseconds);
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal double PlotFractionAt(double x)
    {
        if (_lastPlot.Width <= 0) return 0;
        return Math.Clamp((x - _lastPlot.Left) / _lastPlot.Width, 0.0, 1.0);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 320 || bounds.Height < 160)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        var plotWidth = Math.Max(80, bounds.Width - LabelWidth - RightMargin);
        _lastPlot = new Rect(LabelWidth, TopMargin, plotWidth, Math.Max(80, bounds.Height - TopMargin - BottomAxisHeight));

        if (_tracks.Count == 0 || _viewEndMilliseconds <= _viewStartMilliseconds)
        {
            DrawText(dc, "Select signals from the left panel to build a synchronized disturbance timeline.", 12,
                body, Color.FromRgb(119, 133, 151), new Point(LabelWidth + 18, 42), dpi);
            return;
        }

        var y = TopMargin;
        foreach (var track in _tracks)
        {
            var height = TrackHeight(track);
            var row = new Rect(0, y, bounds.Width, height);
            var plot = new Rect(LabelWidth, y, plotWidth, height);
            DrawTrackBackground(dc, row, plot);
            DrawTrackLabel(dc, track, row, dpi, body, semibold);
            if (track.IsDigital)
                DrawDigitalTrack(dc, track, plot, dpi, body);
            else
                DrawAnalogTrack(dc, track, plot, dpi, body);
            y += height + TrackGap;
        }

        var tracksBottom = Math.Min(bounds.Height - BottomAxisHeight, y - TrackGap);
        var timelinePlot = new Rect(LabelWidth, TopMargin, plotWidth, Math.Max(1, tracksBottom - TopMargin));
        DrawTrigger(dc, timelinePlot, dpi, semibold);
        DrawCursor(dc, timelinePlot, _cursor1Milliseconds, "C1", Color.FromRgb(221, 142, 32), dpi, semibold);
        DrawCursor(dc, timelinePlot, _cursor2Milliseconds, "C2", Color.FromRgb(36, 172, 211), dpi, semibold);
        DrawTimeAxis(dc, new Rect(LabelWidth, tracksBottom, plotWidth, BottomAxisHeight), dpi, body, semibold);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return; // Deliberately bubble normal wheel to the outer vertical signal ScrollViewer.
        if (!_lastPlot.Contains(e.GetPosition(this)) || _viewEndMilliseconds <= _viewStartMilliseconds)
            return;

        var span = _viewEndMilliseconds - _viewStartMilliseconds;
        var fraction = PlotFractionAt(e.GetPosition(this).X);
        var anchor = _viewStartMilliseconds + span * fraction;
        var factor = e.Delta > 0 ? 0.72 : 1.38;
        var nextSpan = Math.Clamp(span * factor, MinimumViewSpan(), Math.Max(MinimumViewSpan(), _fullEndMilliseconds - _fullStartMilliseconds));
        var start = anchor - nextSpan * fraction;
        SetView(start, start + nextSpan);
        InvalidateVisual();
        RaiseNavigationChanged();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        if (!_lastPlot.Contains(point) || _viewEndMilliseconds <= _viewStartMilliseconds)
            return;

        Focus();
        if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
        {
            ResetToTriggerView();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Right)
        {
            PlaceCursor(ComtradeDisturbanceCursor.Cursor2, TimeAtFraction(PlotFractionAt(point.X)), true);
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Left)
        {
            _pointerStartPoint = point;
            if (IsNearCursor(point.X, _cursor1Milliseconds))
                _pointerMode = PointerMode.Cursor1;
            else if (IsNearCursor(point.X, _cursor2Milliseconds))
                _pointerMode = PointerMode.Cursor2;
            else
                _pointerMode = PointerMode.PendingPan;

            _panStartMilliseconds = _viewStartMilliseconds;
            _panEndMilliseconds = _viewEndMilliseconds;
            CaptureMouse();
            Cursor = _pointerMode is PointerMode.Cursor1 or PointerMode.Cursor2 ? Cursors.SizeWE : Cursors.Hand;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton == MouseButton.Middle)
        {
            _pointerStartPoint = point;
            _pointerMode = PointerMode.Panning;
            _panStartMilliseconds = _viewStartMilliseconds;
            _panEndMilliseconds = _viewEndMilliseconds;
            CaptureMouse();
            Cursor = Cursors.Hand;
            e.Handled = true;
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_pointerMode == PointerMode.None || !IsMouseCaptured || _lastPlot.Width <= 0)
        {
            Cursor = HoverCursor(e.GetPosition(this));
            return;
        }

        var point = e.GetPosition(this);
        if (_pointerMode is PointerMode.Cursor1 or PointerMode.Cursor2)
        {
            var cursor = _pointerMode == PointerMode.Cursor1
                ? ComtradeDisturbanceCursor.Cursor1
                : ComtradeDisturbanceCursor.Cursor2;
            PlaceCursor(cursor, TimeAtFraction(PlotFractionAt(point.X)), false);
            e.Handled = true;
            return;
        }

        var deltaPixels = point.X - _pointerStartPoint.X;
        if (_pointerMode == PointerMode.PendingPan && Math.Abs(deltaPixels) >= PanActivationPixels)
            _pointerMode = PointerMode.Panning;
        if (_pointerMode != PointerMode.Panning)
            return;

        var span = _panEndMilliseconds - _panStartMilliseconds;
        var delta = -deltaPixels / _lastPlot.Width * span;
        SetView(_panStartMilliseconds + delta, _panEndMilliseconds + delta);
        InvalidateVisual();
        RaiseNavigationChanged();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pointerMode == PointerMode.None)
            return;

        var mode = _pointerMode;
        var point = e.GetPosition(this);
        _pointerMode = PointerMode.None;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Cross;

        if (mode is PointerMode.Cursor1 or PointerMode.Cursor2)
        {
            var cursor = mode == PointerMode.Cursor1
                ? ComtradeDisturbanceCursor.Cursor1
                : ComtradeDisturbanceCursor.Cursor2;
            PlaceCursor(cursor, TimeAtFraction(PlotFractionAt(point.X)), true);
        }
        else if (mode == PointerMode.PendingPan && e.ChangedButton == MouseButton.Left)
        {
            PlaceCursor(ComtradeDisturbanceCursor.Cursor1, TimeAtFraction(PlotFractionAt(point.X)), true);
        }
        else if (mode == PointerMode.Panning && _lastPlot.Width > 0)
        {
            var deltaFraction = -(point.X - _pointerStartPoint.X) / _lastPlot.Width;
            if (Math.Abs(deltaFraction) > 1e-6)
                PanRequested?.Invoke(this, new ComtradeDisturbancePanRequestedEventArgs(deltaFraction));
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _pointerMode = PointerMode.None;
        Cursor = Cursors.Cross;
    }

    private Cursor HoverCursor(Point point)
    {
        if (!_lastPlot.Contains(point)) return Cursors.Arrow;
        if (IsNearCursor(point.X, _cursor1Milliseconds) || IsNearCursor(point.X, _cursor2Milliseconds))
            return Cursors.SizeWE;
        return Cursors.Cross;
    }

    private bool IsNearCursor(double x, double? time)
    {
        if (time is not { } milliseconds || _viewEndMilliseconds <= _viewStartMilliseconds ||
            milliseconds < _viewStartMilliseconds || milliseconds > _viewEndMilliseconds)
            return false;
        return Math.Abs(x - XForTime(milliseconds, _lastPlot)) <= CursorHitRadius;
    }

    private void PlaceCursor(ComtradeDisturbanceCursor cursor, double requestedMilliseconds, bool isFinal)
    {
        if (!double.IsFinite(requestedMilliseconds)) return;
        requestedMilliseconds = Math.Clamp(requestedMilliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        var tolerance = ComtradeTimeSignalsNavigationMath.SnapToleranceMilliseconds(
            _viewEndMilliseconds - _viewStartMilliseconds,
            Math.Max(1.0, _lastPlot.Width));
        var snapped = TrySnapToVisibleDigitalEdge(requestedMilliseconds, tolerance, out var snappedMilliseconds);
        var value = snapped ? snappedMilliseconds : requestedMilliseconds;

        if (cursor == ComtradeDisturbanceCursor.Cursor1)
            _cursor1Milliseconds = value;
        else
            _cursor2Milliseconds = value;

        InvalidateVisual();
        RaiseNavigationChanged();
        CursorChanged?.Invoke(this, new ComtradeDisturbanceCursorChangedEventArgs(cursor, value, tolerance, isFinal, snapped));
    }

    private bool TrySnapToVisibleDigitalEdge(double requestedMilliseconds, double toleranceMilliseconds, out double snappedMilliseconds)
    {
        snappedMilliseconds = requestedMilliseconds;
        if (toleranceMilliseconds <= 0) return false;

        var bestDistance = double.PositiveInfinity;
        foreach (var track in _tracks.Where(item => item.IsDigital))
        {
            if (track.DigitalEdges is { Count: > 0 })
            {
                foreach (var edge in track.DigitalEdges)
                {
                    var time = ToMilliseconds(edge.Timestamp);
                    var distance = Math.Abs(time - requestedMilliseconds);
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        snappedMilliseconds = time;
                    }
                }
                continue;
            }

            if (track.Digital is null) continue;
            var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
            for (var i = 1; i < count; i++)
            {
                if ((track.Digital[i - 1] != 0) == (track.Digital[i] != 0)) continue;
                var time = ToMilliseconds(track.Timestamps[i]);
                var distance = Math.Abs(time - requestedMilliseconds);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    snappedMilliseconds = time;
                }
            }
        }
        return bestDistance <= toleranceMilliseconds;
    }

    private void DrawTrackBackground(DrawingContext dc, Rect row, Rect plot)
    {
        var border = FrozenPen(Color.FromRgb(226, 232, 240), 1);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(252, 253, 255)), null, row);
        dc.DrawLine(border, new Point(0, row.Bottom), new Point(row.Right, row.Bottom));
        for (var i = 0; i <= 10; i++)
        {
            var x = plot.Left + plot.Width * i / 10.0;
            dc.DrawLine(FrozenPen(Color.FromRgb(238, 242, 247), 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private void DrawTrackLabel(DrawingContext dc, ComtradeDisturbanceTrack track, Rect row, double dpi, Typeface body, Typeface semibold)
    {
        var accent = new SolidColorBrush(track.StrokeColor); accent.Freeze();
        dc.DrawRoundedRectangle(accent, null, new Rect(10, row.Top + 10, 4, Math.Max(14, row.Height - 20)), 2, 2);
        DrawText(dc, track.Title, 10.7, semibold, Color.FromRgb(43, 61, 82), new Point(22, row.Top + 8), dpi, maxWidth: LabelWidth - 30);
        var subtitle = string.IsNullOrWhiteSpace(track.Units) ? track.Subtitle : $"{track.Subtitle} • {track.Units}";
        if (track.DigitalIsLossy) subtitle = string.IsNullOrWhiteSpace(subtitle) ? "sampled events" : $"{subtitle} • sampled events";
        DrawText(dc, subtitle, 8.8, body, Color.FromRgb(119, 132, 149), new Point(22, row.Top + 27), dpi, maxWidth: LabelWidth - 30);
    }

    private void DrawAnalogTrack(DrawingContext dc, ComtradeDisturbanceTrack track, Rect plot, double dpi, Typeface body)
    {
        if (track.Analog is null || track.Timestamps.Length == 0) return;
        var count = Math.Min(track.Analog.Length, track.Timestamps.Length);
        if (count <= 0) return;

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var i = 0; i < count; i++)
        {
            var ms = ToMilliseconds(track.Timestamps[i]);
            if (ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) continue;
            var value = track.Analog[i];
            if (!double.IsFinite(value)) continue;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        if (!double.IsFinite(min) || !double.IsFinite(max)) return;
        if (Math.Abs(max - min) < 1e-12)
        {
            var pad = Math.Max(1.0, Math.Abs(max) * 0.1);
            min -= pad;
            max += pad;
        }

        if (min < 0 && max > 0)
        {
            var zeroY = plot.Bottom - (0 - min) / (max - min) * plot.Height;
            dc.DrawLine(FrozenPen(Color.FromRgb(202, 211, 222), 1), new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));
        }

        var pen = FrozenPen(track.StrokeColor, 1.15);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var maxPoints = Math.Max(80, (int)Math.Ceiling(plot.Width * 2));
            var stride = track.PreserveAllPoints ? 1 : Math.Max(1, count / maxPoints);
            var started = false;
            for (var i = 0; i < count; i += stride)
            {
                var ms = ToMilliseconds(track.Timestamps[i]);
                if (ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) continue;
                var value = track.Analog[i];
                if (!double.IsFinite(value)) continue;
                var x = XForTime(ms, plot);
                var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
                if (!started) { context.BeginFigure(new Point(x, y), false, false); started = true; }
                else context.LineTo(new Point(x, y), true, false);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        DrawText(dc, max.ToString("G4", CultureInfo.CurrentCulture), 7.8, body, Color.FromRgb(126, 139, 155), new Point(plot.Left + 4, plot.Top + 2), dpi);
        DrawText(dc, min.ToString("G4", CultureInfo.CurrentCulture), 7.8, body, Color.FromRgb(126, 139, 155), new Point(plot.Left + 4, plot.Bottom - 14), dpi);
    }

    private void DrawDigitalTrack(DrawingContext dc, ComtradeDisturbanceTrack track, Rect plot, double dpi, Typeface body)
    {
        if (track.Digital is null || track.Timestamps.Length == 0) return;
        var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
        if (count <= 0) return;
        var mid = plot.Top + plot.Height * 0.5;
        dc.DrawLine(FrozenPen(Color.FromRgb(216, 224, 233), 1), new Point(plot.Left, mid), new Point(plot.Right, mid));

        var activeBrush = new SolidColorBrush(Color.FromArgb(46, track.StrokeColor.R, track.StrokeColor.G, track.StrokeColor.B)); activeBrush.Freeze();
        var pen = FrozenPen(track.StrokeColor, 1.35);
        if (!track.DigitalIsLossy)
        {
            var previousRaw = track.Digital[0] != 0 ? 1 : 0;
            var previousMs = ToMilliseconds(track.Timestamps[0]);
            for (var i = 1; i <= count; i++)
            {
                var endMs = i < count ? ToMilliseconds(track.Timestamps[i]) : ToMilliseconds(track.Timestamps[count - 1]);
                var clampedStart = Math.Max(previousMs, _viewStartMilliseconds);
                var clampedEnd = Math.Min(endMs, _viewEndMilliseconds);
                var active = previousRaw != track.DigitalNormalState;
                if (active && clampedEnd > clampedStart)
                {
                    var x1 = XForTime(clampedStart, plot);
                    var x2 = XForTime(clampedEnd, plot);
                    dc.DrawRectangle(activeBrush, null, new Rect(x1, plot.Top + 5, Math.Max(1, x2 - x1), plot.Height - 10));
                }

                if (i >= count) break;
                previousRaw = track.Digital[i] != 0 ? 1 : 0;
                previousMs = endMs;
            }
        }

        var edges = track.DigitalEdges;
        if (edges is { Count: > 0 })
        {
            foreach (var edge in edges)
            {
                var ms = ToMilliseconds(edge.Timestamp);
                if (ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) continue;
                var x = XForTime(ms, plot);
                dc.DrawLine(pen, new Point(x, plot.Top + 4), new Point(x, plot.Bottom - 4));
                DrawText(dc, edge.AfterState != 0 ? "↑" : "↓", 10, body, track.StrokeColor, new Point(x + 2, plot.Top + 1), dpi);
            }
        }
        else
        {
            for (var i = 1; i < count; i++)
            {
                var before = track.Digital[i - 1] != 0;
                var after = track.Digital[i] != 0;
                if (before == after) continue;
                var ms = ToMilliseconds(track.Timestamps[i]);
                if (ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) continue;
                var x = XForTime(ms, plot);
                dc.DrawLine(pen, new Point(x, plot.Top + 4), new Point(x, plot.Bottom - 4));
                DrawText(dc, after ? "↑" : "↓", 10, body, track.StrokeColor, new Point(x + 2, plot.Top + 1), dpi);
            }
        }
    }

    private void DrawTrigger(DrawingContext dc, Rect plot, double dpi, Typeface semibold)
    {
        if (_triggerMilliseconds is not { } trigger || trigger < _viewStartMilliseconds || trigger > _viewEndMilliseconds)
            return;
        var x = XForTime(trigger, plot);
        dc.DrawLine(FrozenDashedPen(Color.FromRgb(217, 121, 41), 1.1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        DrawText(dc, "TRG 0", 8.5, semibold, Color.FromRgb(186, 99, 31), new Point(x + 3, plot.Top + 17), dpi);
    }

    private void DrawCursor(DrawingContext dc, Rect plot, double? time, string label, Color color, double dpi, Typeface semibold)
    {
        if (time is not { } ms || ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) return;
        var x = XForTime(ms, plot);
        var brush = new SolidColorBrush(color); brush.Freeze();
        dc.DrawLine(FrozenPen(color, 1.2), new Point(x, plot.Top + 15), new Point(x, plot.Bottom));
        var handle = new Rect(x - 13, plot.Top, 26, 15);
        dc.DrawRoundedRectangle(brush, null, handle, 3, 3);
        DrawText(dc, label, 8.3, semibold, Colors.White, new Point(x - 8, plot.Top + 1), dpi);
    }

    private void DrawTimeAxis(DrawingContext dc, Rect axis, double dpi, Typeface body, Typeface semibold)
    {
        var trigger = _triggerMilliseconds ?? 0.0;
        dc.DrawLine(FrozenPen(Color.FromRgb(185, 196, 210), 1), new Point(axis.Left, axis.Top), new Point(axis.Right, axis.Top));
        for (var i = 0; i <= 10; i++)
        {
            var fraction = i / 10.0;
            var x = axis.Left + axis.Width * fraction;
            var absolute = _viewStartMilliseconds + (_viewEndMilliseconds - _viewStartMilliseconds) * fraction;
            var relative = absolute - trigger;
            dc.DrawLine(FrozenPen(Color.FromRgb(196, 206, 218), 1), new Point(x, axis.Top), new Point(x, axis.Top + 4));
            var label = Math.Abs(relative) < 0.0005 ? "0" : relative.ToString("+0.###;-0.###", CultureInfo.CurrentCulture);
            DrawText(dc, label, 8.2, body, Color.FromRgb(111, 125, 143), new Point(x - 15, axis.Top + 7), dpi);
        }
        DrawText(dc, "ms relative to trigger", 8.4, semibold, Color.FromRgb(104, 120, 140), new Point(axis.Right - 102, axis.Top + 21), dpi);
    }

    private void SetView(double start, double end)
    {
        var fullSpan = _fullEndMilliseconds - _fullStartMilliseconds;
        if (fullSpan <= 0) return;
        var span = Math.Clamp(end - start, MinimumViewSpan(), fullSpan);
        if (start < _fullStartMilliseconds) start = _fullStartMilliseconds;
        if (start + span > _fullEndMilliseconds) start = _fullEndMilliseconds - span;
        _viewStartMilliseconds = start;
        _viewEndMilliseconds = start + span;
    }

    private double MinimumViewSpan() => Math.Max(0.001, (_fullEndMilliseconds - _fullStartMilliseconds) / 5000.0);
    private double TimeAtFraction(double fraction) => _viewStartMilliseconds + (_viewEndMilliseconds - _viewStartMilliseconds) * fraction;
    private double XForTime(double milliseconds, Rect plot) => plot.Left + plot.Width * (milliseconds - _viewStartMilliseconds) / Math.Max(1e-12, _viewEndMilliseconds - _viewStartMilliseconds);
    private double ToMilliseconds(uint rawTimestamp) => ComtradeTimeMath.ToMilliseconds(rawTimestamp, _timeMultiplier);
    private static double TrackHeight(ComtradeDisturbanceTrack track) => track.IsDigital ? DigitalTrackHeight : AnalogTrackHeight;

    private (double Start, double End) GetTimeBounds(IReadOnlyList<ComtradeDisturbanceTrack> tracks)
    {
        var start = double.PositiveInfinity;
        var end = double.NegativeInfinity;
        foreach (var track in tracks)
        {
            if (track.Timestamps.Length == 0) continue;
            start = Math.Min(start, ToMilliseconds(track.Timestamps[0]));
            end = Math.Max(end, ToMilliseconds(track.Timestamps[^1]));
        }
        if (!double.IsFinite(start) || !double.IsFinite(end) || end <= start) return (0, 1);
        return (start, end);
    }

    private void RaiseNavigationChanged()
    {
        var trigger = _triggerMilliseconds ?? 0.0;
        var parts = new List<string>
        {
            $"View {FormatRelative(_viewStartMilliseconds - trigger)} … {FormatRelative(_viewEndMilliseconds - trigger)}"
        };
        if (_cursor1Milliseconds is { } c1) parts.Add($"C1 {FormatRelative(c1 - trigger)}"); else parts.Add("C1 —");
        if (_cursor2Milliseconds is { } c2) parts.Add($"C2 {FormatRelative(c2 - trigger)}"); else parts.Add("C2 —");
        if (_cursor1Milliseconds is { } first && _cursor2Milliseconds is { } second) parts.Add($"Δt {Math.Abs(second - first):G6} ms");
        NavigationChanged?.Invoke(this, new ComtradeDisturbanceNavigationChangedEventArgs(string.Join("  |  ", parts)));
    }

    private static string FormatRelative(double value) => Math.Abs(value) < 0.0005 ? "0 ms" : $"{value:+0.###;-0.###} ms";

    private static Pen FrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        var pen = new Pen(brush, thickness); pen.Freeze();
        return pen;
    }

    private static Pen FrozenDashedPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        var pen = new Pen(brush, thickness) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    private static void DrawText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi, double maxWidth = double.PositiveInfinity)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight, typeface, size, brush, dpi)
        {
            MaxTextWidth = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : 10000,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted, point);
    }
}
