using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

/// <summary>
/// Retained-mode COMTRADE workstation renderer. Static frame/data visuals are only rebuilt when
/// data, viewport or size changes. C1/C2 glyphs are retained DrawingVisuals whose X transforms are
/// updated synchronously from pointer input, so cursor feedback never waits for a composition
/// callback and never replays waveform geometry.
/// </summary>
public sealed class ComtradeDisturbanceViewP1D3 : FrameworkElement
{
    private const double LabelWidth = 150.0;
    private const double RightMargin = 18.0;
    private const double TopMargin = 14.0;
    private const double BottomAxisHeight = 34.0;
    private const double AnalogTrackHeight = 92.0;
    private const double DigitalTrackHeight = 38.0;
    private const double TrackGap = 5.0;
    private const double CursorHitRadius = 9.0;
    private const double PanActivationPixels = 4.0;
    private const double TransitionGlyphSpacing = 28.0;
    private static readonly long InteractiveNotifyIntervalTicks = Math.Max(1, Stopwatch.Frequency / 30);

    private enum PointerMode
    {
        None,
        PendingPan,
        Panning,
        Cursor1,
        Cursor2
    }

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _frameVisual = new();
    private readonly ContainerVisual _plotContainer = new();
    private readonly DrawingVisual _dataVisual = new();
    private readonly ContainerVisual _cursorContainer = new();
    private readonly DrawingVisual _cursor1Visual = new();
    private readonly DrawingVisual _cursor2Visual = new();
    private readonly TranslateTransform _dataPanTransform = new();
    private readonly TranslateTransform _cursorPanTransform = new();
    private readonly TranslateTransform _cursor1Transform = new();
    private readonly TranslateTransform _cursor2Transform = new();

    private IReadOnlyList<ComtradeDisturbanceTrack> _tracks = Array.Empty<ComtradeDisturbanceTrack>();
    private double[] _snapTimesMilliseconds = Array.Empty<double>();
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
    private Rect _lastTimelinePlot;
    private PointerMode _pointerMode;
    private Point _pointerStartPoint;
    private double _panStartMilliseconds;
    private double _panEndMilliseconds;
    private double _panPreviewPixels;
    private long _lastInteractiveNotifyTicks;
    private Size _cachedSize;
    private bool _frameDirty = true;
    private bool _dataDirty = true;
    private bool _cursorGlyphsDirty = true;
    private string? _analogRepresentationLabel;

    internal event EventHandler<ComtradeDisturbanceNavigationChangedEventArgs>? NavigationChanged;
    internal event EventHandler<ComtradeDisturbanceCursorChangedEventArgs>? CursorChanged;
    internal event EventHandler<ComtradeDisturbancePanRequestedEventArgs>? PanRequested;

    internal double? Cursor1Milliseconds => _cursor1Milliseconds;
    internal double? Cursor2Milliseconds => _cursor2Milliseconds;
    internal double? CursorAMilliseconds => _cursor1Milliseconds;
    internal double? CursorBMilliseconds => _cursor2Milliseconds;
    internal double ViewStartMilliseconds => _viewStartMilliseconds;
    internal double ViewEndMilliseconds => _viewEndMilliseconds;
    internal double FullStartMilliseconds => _fullStartMilliseconds;
    internal double FullEndMilliseconds => _fullEndMilliseconds;

    protected override int VisualChildrenCount => _visuals.Count;

    public ComtradeDisturbanceViewP1D3()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        ToolTip = "Wheel: scroll signals • Ctrl+wheel: zoom • Drag plot: pan • Drag C1/C2: move • Right-click: C2 • cursors snap to digital edges";

        _visuals = new VisualCollection(this);
        _visuals.Add(_frameVisual);
        _plotContainer.Children.Add(_dataVisual);
        _cursorContainer.Children.Add(_cursor1Visual);
        _cursorContainer.Children.Add(_cursor2Visual);
        _plotContainer.Children.Add(_cursorContainer);
        _visuals.Add(_plotContainer);

        _dataVisual.Transform = _dataPanTransform;
        _cursorContainer.Transform = _cursorPanTransform;
        _cursor1Visual.Transform = _cursor1Transform;
        _cursor2Visual.Transform = _cursor2Transform;
        _cursor1Visual.Opacity = 0.0;
        _cursor2Visual.Opacity = 0.0;
    }

    protected override Visual GetVisualChild(int index) => _visuals[index];

    internal void ShowTracks(
        IReadOnlyList<ComtradeDisturbanceTrack> tracks,
        double timeMultiplier,
        double? triggerMilliseconds,
        bool preserveCursor = true)
    {
        _tracks = tracks ?? Array.Empty<ComtradeDisturbanceTrack>();
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        _triggerMilliseconds = triggerMilliseconds is { } trigger && double.IsFinite(trigger) ? trigger : null;
        _snapTimesMilliseconds = BuildSnapIndex(_tracks);

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
        ResetPanPreview();
        MarkStaticDirty();
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ShowMessage(string message)
    {
        _tracks = Array.Empty<ComtradeDisturbanceTrack>();
        _snapTimesMilliseconds = Array.Empty<double>();
        _fullStartMilliseconds = 0;
        _fullEndMilliseconds = 0;
        _viewStartMilliseconds = 0;
        _viewEndMilliseconds = 0;
        _cursor1Milliseconds = null;
        _cursor2Milliseconds = null;
        Height = 330;
        ToolTip = message;
        ResetPanPreview();
        MarkStaticDirty();
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
        MarkStaticDirty();
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
        if (!double.IsFinite(milliseconds)) return;
        if (cursor == ComtradeDisturbanceCursor.Cursor1)
            _cursor1Milliseconds = milliseconds;
        else
            _cursor2Milliseconds = milliseconds;

        // Pointer feedback is presentation-only and synchronous. NavigationChanged intentionally
        // remains reserved for viewport changes so this path allocates no status strings.
        UpdateCursorTransforms();
    }

    internal void SetAnalogRepresentationLabel(string representation)
    {
        var normalized = string.IsNullOrWhiteSpace(representation) ? null : representation.Trim();
        if (string.Equals(_analogRepresentationLabel, normalized, StringComparison.Ordinal))
            return;
        _analogRepresentationLabel = normalized;
        _frameDirty = true;
        InvalidateVisual();
    }

    internal void ResetNavigation()
    {
        if (_fullEndMilliseconds <= _fullStartMilliseconds) return;
        _viewStartMilliseconds = _fullStartMilliseconds;
        _viewEndMilliseconds = _fullEndMilliseconds;
        MarkStaticDirty();
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void SetViewWindow(double startMilliseconds, double endMilliseconds)
    {
        SetView(startMilliseconds, endMilliseconds);
        MarkStaticDirty();
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
        // Cheap transparent hit target. Heavy waveform content lives in retained child visuals.
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        EnsureRetainedLayers(bounds);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MarkStaticDirty();
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) return;
        if (!_lastPlot.Contains(e.GetPosition(this)) || _viewEndMilliseconds <= _viewStartMilliseconds) return;

        var span = _viewEndMilliseconds - _viewStartMilliseconds;
        var fraction = PlotFractionAt(e.GetPosition(this).X);
        var anchor = _viewStartMilliseconds + span * fraction;
        var factor = e.Delta > 0 ? 0.72 : 1.38;
        var nextSpan = Math.Clamp(span * factor, MinimumViewSpan(), Math.Max(MinimumViewSpan(), _fullEndMilliseconds - _fullStartMilliseconds));
        var start = anchor - nextSpan * fraction;
        SetView(start, start + nextSpan);
        MarkStaticDirty();
        InvalidateVisual();
        RaiseNavigationChanged();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        if (!_lastPlot.Contains(point) || _viewEndMilliseconds <= _viewStartMilliseconds) return;

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
            _pointerMode = IsNearCursor(point.X, _cursor1Milliseconds)
                ? PointerMode.Cursor1
                : IsNearCursor(point.X, _cursor2Milliseconds)
                    ? PointerMode.Cursor2
                    : PointerMode.PendingPan;
            _panStartMilliseconds = _viewStartMilliseconds;
            _panEndMilliseconds = _viewEndMilliseconds;
            ResetPanPreview();
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
            ResetPanPreview();
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
        if (_pointerMode != PointerMode.Panning) return;

        SetPanPreview(deltaPixels);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (_pointerMode == PointerMode.None) return;

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
            var deltaPixels = point.X - _pointerStartPoint.X;
            var span = _panEndMilliseconds - _panStartMilliseconds;
            var delta = -deltaPixels / _lastPlot.Width * span;
            SetView(_panStartMilliseconds + delta, _panEndMilliseconds + delta);
            ResetPanPreview();
            MarkStaticDirty();
            InvalidateVisual();
            RaiseNavigationChanged();

            var deltaFraction = -deltaPixels / _lastPlot.Width;
            if (Math.Abs(deltaFraction) > 1e-6)
                PanRequested?.Invoke(this, new ComtradeDisturbancePanRequestedEventArgs(deltaFraction));
        }

        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        ResetPanPreview();
        _pointerMode = PointerMode.None;
        Cursor = Cursors.Cross;
    }

    private void EnsureRetainedLayers(Rect bounds)
    {
        var size = new Size(bounds.Width, bounds.Height);
        if (_cachedSize != size)
        {
            _cachedSize = size;
            _frameDirty = true;
            _dataDirty = true;
            _cursorGlyphsDirty = true;
        }

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        var plotWidth = Math.Max(80, bounds.Width - LabelWidth - RightMargin);
        _lastPlot = new Rect(LabelWidth, TopMargin, plotWidth, Math.Max(80, bounds.Height - TopMargin - BottomAxisHeight));

        if (bounds.Width < 320 || bounds.Height < 160)
        {
            using (var frameDc = _frameVisual.RenderOpen())
                frameDc.DrawRectangle(Brushes.White, null, bounds);
            using (_dataVisual.RenderOpen()) { }
            ClearCursorGlyphs();
            _plotContainer.Clip = null;
            _frameDirty = false;
            _dataDirty = false;
            _cursorGlyphsDirty = false;
            return;
        }

        if (_tracks.Count == 0 || _viewEndMilliseconds <= _viewStartMilliseconds)
        {
            if (_frameDirty)
            {
                using var frameDc = _frameVisual.RenderOpen();
                frameDc.DrawRectangle(Brushes.White, null, bounds);
                DrawText(frameDc, "Select signals from the left panel to build a synchronized disturbance timeline.", 12,
                    body, Color.FromRgb(119, 133, 151), new Point(LabelWidth + 18, 42), dpi);
            }
            if (_dataDirty)
            {
                using var dataDc = _dataVisual.RenderOpen();
            }
            ClearCursorGlyphs();
            _plotContainer.Clip = null;
            _frameDirty = false;
            _dataDirty = false;
            _cursorGlyphsDirty = false;
            return;
        }

        var y = TopMargin;
        foreach (var track in _tracks)
            y += TrackHeight(track) + TrackGap;
        var tracksBottom = Math.Min(bounds.Height - BottomAxisHeight, y - TrackGap);
        _lastTimelinePlot = new Rect(LabelWidth, TopMargin, plotWidth, Math.Max(1, tracksBottom - TopMargin));
        var clip = new RectangleGeometry(_lastTimelinePlot);
        clip.Freeze();
        _plotContainer.Clip = clip;

        if (_frameDirty)
        {
            using var frameDc = _frameVisual.RenderOpen();
            frameDc.DrawRectangle(Brushes.White, null, bounds);
            y = TopMargin;
            foreach (var track in _tracks)
            {
                var height = TrackHeight(track);
                var row = new Rect(0, y, bounds.Width, height);
                var plot = new Rect(LabelWidth, y, plotWidth, height);
                DrawTrackBackground(frameDc, row, plot);
                DrawTrackLabel(frameDc, track, row, dpi, body, semibold);
                y += height + TrackGap;
            }
            DrawTimeAxis(frameDc, new Rect(LabelWidth, tracksBottom, plotWidth, BottomAxisHeight), dpi, body, semibold);
            _frameDirty = false;
        }

        if (_dataDirty)
        {
            using var dataDc = _dataVisual.RenderOpen();
            y = TopMargin;
            foreach (var track in _tracks)
            {
                var height = TrackHeight(track);
                var plot = new Rect(LabelWidth, y, plotWidth, height);
                if (track.IsDigital)
                    DrawDigitalTrack(dataDc, track, plot, dpi, body);
                else
                    DrawAnalogTrack(dataDc, track, plot);
                y += height + TrackGap;
            }
            DrawTrigger(dataDc, _lastTimelinePlot, dpi, semibold);
            _dataDirty = false;
        }

        if (_cursorGlyphsDirty)
            RebuildCursorGlyphs(dpi, semibold);
        else
            UpdateCursorTransforms();
    }

    private void ClearCursorGlyphs()
    {
        using (_cursor1Visual.RenderOpen()) { }
        using (_cursor2Visual.RenderOpen()) { }
        _cursor1Visual.Opacity = 0.0;
        _cursor2Visual.Opacity = 0.0;
    }

    private void RebuildCursorGlyphs(double dpi, Typeface semibold)
    {
        DrawCursorGlyph(_cursor1Visual, "C1", Color.FromRgb(221, 142, 32), dpi, semibold);
        DrawCursorGlyph(_cursor2Visual, "C2", Color.FromRgb(36, 172, 211), dpi, semibold);
        _cursorGlyphsDirty = false;
        UpdateCursorTransforms();
    }

    private void DrawCursorGlyph(DrawingVisual visual, string label, Color color, double dpi, Typeface semibold)
    {
        using var dc = visual.RenderOpen();
        var brush = FrozenBrush(color);
        dc.DrawLine(FrozenPen(color, 1.2), new Point(0, _lastTimelinePlot.Top + 15), new Point(0, _lastTimelinePlot.Bottom));
        dc.DrawRoundedRectangle(brush, null, new Rect(-13, _lastTimelinePlot.Top, 26, 15), 3, 3);
        DrawText(dc, label, 8.2, semibold, Colors.White, new Point(-8, _lastTimelinePlot.Top + 1), dpi);
    }

    private void UpdateCursorTransforms()
    {
        UpdateCursorTransform(_cursor1Visual, _cursor1Transform, _cursor1Milliseconds);
        UpdateCursorTransform(_cursor2Visual, _cursor2Transform, _cursor2Milliseconds);
    }

    private void UpdateCursorTransform(DrawingVisual visual, TranslateTransform transform, double? time)
    {
        if (_lastTimelinePlot.Width <= 0 || _viewEndMilliseconds <= _viewStartMilliseconds ||
            time is not { } ms || !double.IsFinite(ms) || ms < _viewStartMilliseconds || ms > _viewEndMilliseconds)
        {
            visual.Opacity = 0.0;
            return;
        }

        transform.X = XForTime(ms, _lastTimelinePlot);
        visual.Opacity = 1.0;
    }

    private void SetPanPreview(double pixels)
    {
        _panPreviewPixels = pixels;
        _dataPanTransform.X = pixels;
        _cursorPanTransform.X = pixels;
    }

    private void ResetPanPreview()
    {
        _panPreviewPixels = 0;
        _dataPanTransform.X = 0;
        _cursorPanTransform.X = 0;
    }

    private void PlaceCursor(ComtradeDisturbanceCursor cursor, double requestedMilliseconds, bool isFinal)
    {
        if (!double.IsFinite(requestedMilliseconds)) return;
        requestedMilliseconds = Math.Clamp(requestedMilliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        var tolerance = ComtradeTimeSignalsNavigationMath.SnapToleranceMilliseconds(
            _viewEndMilliseconds - _viewStartMilliseconds,
            Math.Max(1.0, _lastPlot.Width));
        var snapped = ComtradeInteractionPerformanceMath.TrySnapSorted(
            _snapTimesMilliseconds,
            requestedMilliseconds,
            tolerance,
            out var snappedMilliseconds);
        var value = snapped ? snappedMilliseconds : requestedMilliseconds;

        if (cursor == ComtradeDisturbanceCursor.Cursor1)
            _cursor1Milliseconds = value;
        else
            _cursor2Milliseconds = value;

        UpdateCursorTransforms();
        if (isFinal || ShouldNotifyInteractive())
        {
            CursorChanged?.Invoke(this, new ComtradeDisturbanceCursorChangedEventArgs(cursor, value, tolerance, isFinal, snapped));
        }
    }

    private bool ShouldNotifyInteractive()
    {
        var now = Stopwatch.GetTimestamp();
        if (now - _lastInteractiveNotifyTicks < InteractiveNotifyIntervalTicks) return false;
        _lastInteractiveNotifyTicks = now;
        return true;
    }

    private double[] BuildSnapIndex(IReadOnlyList<ComtradeDisturbanceTrack> tracks)
    {
        var times = new List<double>();
        foreach (var track in tracks)
        {
            if (!track.IsDigital) continue;
            if (track.DigitalEdges is { Count: > 0 })
            {
                times.AddRange(track.DigitalEdges.Select(edge => ToMilliseconds(edge.Timestamp)));
                continue;
            }

            if (track.Digital is null) continue;
            var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
            for (var i = 1; i < count; i++)
            {
                if ((track.Digital[i - 1] != 0) != (track.Digital[i] != 0))
                    times.Add(ToMilliseconds(track.Timestamps[i]));
            }
        }

        if (times.Count == 0) return Array.Empty<double>();
        times.Sort();
        var unique = new List<double>(times.Count) { times[0] };
        for (var i = 1; i < times.Count; i++)
        {
            if (Math.Abs(times[i] - unique[^1]) > 1e-9)
                unique.Add(times[i]);
        }
        return unique.ToArray();
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

    private void DrawTrackBackground(DrawingContext dc, Rect row, Rect plot)
    {
        dc.DrawRectangle(FrozenBrush(Color.FromRgb(252, 253, 255)), null, row);
        dc.DrawLine(FrozenPen(Color.FromRgb(226, 232, 240), 1), new Point(0, row.Bottom), new Point(row.Right, row.Bottom));
        var gridPen = FrozenPen(Color.FromRgb(238, 242, 247), 1);
        for (var i = 0; i <= 10; i++)
        {
            var x = plot.Left + plot.Width * i / 10.0;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
    }

    private void DrawTrackLabel(DrawingContext dc, ComtradeDisturbanceTrack track, Rect row, double dpi, Typeface body, Typeface semibold)
    {
        dc.DrawRoundedRectangle(FrozenBrush(track.StrokeColor), null, new Rect(10, row.Top + 9, 4, Math.Max(14, row.Height - 18)), 2, 2);
        DrawText(dc, track.Title, 10.5, semibold, Color.FromRgb(43, 61, 82), new Point(22, row.Top + 7), dpi, LabelWidth - 30);
        if (track.IsDigital) return;

        var trackSubtitle = ApplyRepresentationLabel(track.Subtitle);
        var subtitle = string.IsNullOrWhiteSpace(track.Units)
            ? trackSubtitle
            : string.IsNullOrWhiteSpace(trackSubtitle) ? track.Units : $"{trackSubtitle} • {track.Units}";
        if (!string.IsNullOrWhiteSpace(subtitle))
            DrawText(dc, subtitle, 8.6, body, Color.FromRgb(119, 132, 149), new Point(22, row.Top + 26), dpi, LabelWidth - 30);
    }

    private string ApplyRepresentationLabel(string subtitle)
    {
        if (string.IsNullOrWhiteSpace(subtitle) || string.IsNullOrWhiteSpace(_analogRepresentationLabel))
            return subtitle;
        return subtitle
            .Replace("Secondary", _analogRepresentationLabel, StringComparison.Ordinal)
            .Replace("Primary", _analogRepresentationLabel, StringComparison.Ordinal);
    }

    private void DrawAnalogTrack(DrawingContext dc, ComtradeDisturbanceTrack track, Rect plot)
    {
        if (track.Analog is null || track.Timestamps.Length == 0) return;
        var count = Math.Min(track.Analog.Length, track.Timestamps.Length);
        if (count <= 0) return;

        var range = VisibleRange(track.Timestamps, count);
        if (range.IsEmpty) return;

        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var i = range.StartIndex; i < range.EndExclusive; i++)
        {
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
            dc.DrawLine(FrozenPen(Color.FromRgb(205, 214, 224), 1), new Point(plot.Left, zeroY), new Point(plot.Right, zeroY));
        }

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var started = false;
            var lastEmitted = -1;
            if (ComtradeScreenSpaceRenderPolicy.UseEnvelope(range.Count, plot.Width))
            {
                DrawAnalogEnvelope(context, track, plot, range, min, max, ref started, ref lastEmitted);
            }
            else
            {
                var stride = ComtradeScreenSpaceRenderPolicy.SparseStride(range.Count, plot.Width, track.PreserveAllPoints);
                for (var i = range.StartIndex; i < range.EndExclusive; i += stride)
                    AppendAnalogPoint(context, track, plot, i, min, max, ref started, ref lastEmitted);
                AppendAnalogPoint(context, track, plot, range.EndExclusive - 1, min, max, ref started, ref lastEmitted);
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, FrozenPen(track.StrokeColor, 1.15), geometry);
    }

    private void DrawAnalogEnvelope(
        StreamGeometryContext context,
        ComtradeDisturbanceTrack track,
        Rect plot,
        ComtradeVisibleSampleRange range,
        double min,
        double max,
        ref bool started,
        ref int lastEmitted)
    {
        if (track.Analog is null || range.IsEmpty) return;
        var bucketCount = ComtradeScreenSpaceRenderPolicy.PixelBucketCount(plot.Width);
        var span = Math.Max(1e-12, _viewEndMilliseconds - _viewStartMilliseconds);
        var currentBucket = -1;
        var firstIndex = -1;
        var lastIndex = -1;
        var minIndex = -1;
        var maxIndex = -1;
        var minValue = double.PositiveInfinity;
        var maxValue = double.NegativeInfinity;

        for (var index = range.StartIndex; index < range.EndExclusive; index++)
        {
            var value = track.Analog[index];
            if (!double.IsFinite(value)) continue;
            var ms = ToMilliseconds(track.Timestamps[index]);
            var bucket = Math.Clamp((int)Math.Floor((ms - _viewStartMilliseconds) / span * bucketCount), 0, bucketCount - 1);
            if (currentBucket >= 0 && bucket != currentBucket)
            {
                EmitEnvelopeBucket(context, track, plot, firstIndex, minIndex, maxIndex, lastIndex, min, max, ref started, ref lastEmitted);
                firstIndex = lastIndex = minIndex = maxIndex = -1;
                minValue = double.PositiveInfinity;
                maxValue = double.NegativeInfinity;
            }

            currentBucket = bucket;
            if (firstIndex < 0) firstIndex = index;
            lastIndex = index;
            if (value < minValue) { minValue = value; minIndex = index; }
            if (value > maxValue) { maxValue = value; maxIndex = index; }
        }

        if (currentBucket >= 0)
            EmitEnvelopeBucket(context, track, plot, firstIndex, minIndex, maxIndex, lastIndex, min, max, ref started, ref lastEmitted);
    }

    private void EmitEnvelopeBucket(
        StreamGeometryContext context,
        ComtradeDisturbanceTrack track,
        Rect plot,
        int firstIndex,
        int minIndex,
        int maxIndex,
        int lastIndex,
        double min,
        double max,
        ref bool started,
        ref int lastEmitted)
    {
        AppendAnalogPoint(context, track, plot, firstIndex, min, max, ref started, ref lastEmitted);
        if (minIndex <= maxIndex)
        {
            AppendAnalogPoint(context, track, plot, minIndex, min, max, ref started, ref lastEmitted);
            AppendAnalogPoint(context, track, plot, maxIndex, min, max, ref started, ref lastEmitted);
        }
        else
        {
            AppendAnalogPoint(context, track, plot, maxIndex, min, max, ref started, ref lastEmitted);
            AppendAnalogPoint(context, track, plot, minIndex, min, max, ref started, ref lastEmitted);
        }
        AppendAnalogPoint(context, track, plot, lastIndex, min, max, ref started, ref lastEmitted);
    }

    private void AppendAnalogPoint(
        StreamGeometryContext context,
        ComtradeDisturbanceTrack track,
        Rect plot,
        int index,
        double min,
        double max,
        ref bool started,
        ref int lastEmitted)
    {
        if (track.Analog is null || index < 0 || index >= track.Analog.Length || index >= track.Timestamps.Length || index == lastEmitted)
            return;
        var value = track.Analog[index];
        if (!double.IsFinite(value)) return;
        var ms = ToMilliseconds(track.Timestamps[index]);
        var x = XForTime(ms, plot);
        var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
        if (!started)
        {
            context.BeginFigure(new Point(x, y), false, false);
            started = true;
        }
        else
        {
            context.LineTo(new Point(x, y), true, false);
        }
        lastEmitted = index;
    }

    private void DrawDigitalTrack(DrawingContext dc, ComtradeDisturbanceTrack track, Rect plot, double dpi, Typeface body)
    {
        if (track.Digital is null || track.Timestamps.Length == 0) return;
        var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
        if (count <= 0) return;
        var range = VisibleRange(track.Timestamps, count);
        if (range.IsEmpty) return;

        var mid = plot.Top + plot.Height * 0.5;
        dc.DrawLine(FrozenPen(Color.FromRgb(216, 224, 233), 1), new Point(plot.Left, mid), new Point(plot.Right, mid));

        var activeBrush = FrozenBrush(Color.FromArgb(42, track.StrokeColor.R, track.StrokeColor.G, track.StrokeColor.B));
        var pen = FrozenPen(track.StrokeColor, 1.25);
        if (!track.DigitalIsLossy)
        {
            var startIndex = range.StartIndex;
            var previousRaw = track.Digital[startIndex] != 0 ? 1 : 0;
            var previousMs = ToMilliseconds(track.Timestamps[startIndex]);
            for (var i = startIndex + 1; i <= range.EndExclusive; i++)
            {
                var endMs = i < range.EndExclusive
                    ? ToMilliseconds(track.Timestamps[i])
                    : Math.Min(_viewEndMilliseconds, ToMilliseconds(track.Timestamps[range.EndExclusive - 1]));
                var clampedStart = Math.Max(previousMs, _viewStartMilliseconds);
                var clampedEnd = Math.Min(endMs, _viewEndMilliseconds);
                if (previousRaw != track.DigitalNormalState && clampedEnd > clampedStart)
                {
                    var x1 = XForTime(clampedStart, plot);
                    var x2 = XForTime(clampedEnd, plot);
                    dc.DrawRectangle(activeBrush, null, new Rect(x1, plot.Top + 4, Math.Max(1, x2 - x1), plot.Height - 8));
                }
                if (i >= range.EndExclusive) break;
                previousRaw = track.Digital[i] != 0 ? 1 : 0;
                previousMs = endMs;
            }
        }

        var transitions = new List<(double Milliseconds, bool Rising)>();
        if (track.DigitalEdges is { Count: > 0 } edges)
        {
            foreach (var edge in edges)
            {
                var ms = ToMilliseconds(edge.Timestamp);
                if (ms >= _viewStartMilliseconds && ms <= _viewEndMilliseconds)
                    transitions.Add((ms, edge.AfterState != 0));
            }
        }
        else
        {
            var start = Math.Max(1, range.StartIndex);
            for (var i = start; i < range.EndExclusive; i++)
            {
                var before = track.Digital[i - 1] != 0;
                var after = track.Digital[i] != 0;
                if (before == after) continue;
                var ms = ToMilliseconds(track.Timestamps[i]);
                if (ms >= _viewStartMilliseconds && ms <= _viewEndMilliseconds)
                    transitions.Add((ms, after));
            }
        }

        var pixelColumns = new HashSet<int>();
        var sparseEnoughForGlyphs = transitions.Count <= Math.Max(2, (int)(plot.Width / TransitionGlyphSpacing));
        var lastGlyphX = double.NegativeInfinity;
        foreach (var transition in transitions)
        {
            var x = XForTime(transition.Milliseconds, plot);
            var pixel = (int)Math.Round(x);
            if (pixelColumns.Add(pixel))
                dc.DrawLine(pen, new Point(x, plot.Top + 3), new Point(x, plot.Bottom - 3));

            if (!sparseEnoughForGlyphs ||
                !ComtradeInteractionPerformanceMath.ShouldDrawTransitionGlyph(x, lastGlyphX, TransitionGlyphSpacing))
                continue;
            DrawText(dc, transition.Rising ? "↑" : "↓", 9.5, body, track.StrokeColor, new Point(x + 2, plot.Top), dpi);
            lastGlyphX = x;
        }
    }

    private ComtradeVisibleSampleRange VisibleRange(uint[] timestamps, int count)
    {
        var divisor = Math.Max(1e-12, _timeMultiplier);
        var startRaw = _viewStartMilliseconds * 1000.0 / divisor;
        var endRaw = _viewEndMilliseconds * 1000.0 / divisor;
        return ComtradeScreenSpaceRenderPolicy.FindVisibleRange(timestamps, count, startRaw, endRaw);
    }

    private void DrawTrigger(DrawingContext dc, Rect plot, double dpi, Typeface semibold)
    {
        if (_triggerMilliseconds is not { } trigger || trigger < _viewStartMilliseconds || trigger > _viewEndMilliseconds) return;
        var x = XForTime(trigger, plot);
        dc.DrawLine(FrozenDashedPen(Color.FromRgb(217, 121, 41), 1.1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        DrawText(dc, "TRG", 8.2, semibold, Color.FromRgb(186, 99, 31), new Point(x + 3, plot.Top + 17), dpi);
    }

    private void DrawTimeAxis(DrawingContext dc, Rect axis, double dpi, Typeface body, Typeface semibold)
    {
        var trigger = _triggerMilliseconds ?? 0.0;
        dc.DrawLine(FrozenPen(Color.FromRgb(185, 196, 210), 1), new Point(axis.Left, axis.Top), new Point(axis.Right, axis.Top));
        var tickCount = Math.Clamp((int)Math.Round(axis.Width / 120.0), 5, 10);
        for (var i = 0; i <= tickCount; i++)
        {
            var fraction = i / (double)tickCount;
            var x = axis.Left + axis.Width * fraction;
            var absolute = _viewStartMilliseconds + (_viewEndMilliseconds - _viewStartMilliseconds) * fraction;
            var relative = absolute - trigger;
            dc.DrawLine(FrozenPen(Color.FromRgb(196, 206, 218), 1), new Point(x, axis.Top), new Point(x, axis.Top + 4));
            var label = Math.Abs(relative) < 0.0005 ? "0" : relative.ToString("+0.###;-0.###", CultureInfo.CurrentCulture);
            DrawText(dc, label, 8.1, body, Color.FromRgb(111, 125, 143), new Point(x - 15, axis.Top + 7), dpi);
        }
        DrawText(dc, "ms relative to trigger", 8.2, semibold, Color.FromRgb(104, 120, 140), new Point(axis.Right - 102, axis.Top + 21), dpi);
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

    private void MarkStaticDirty()
    {
        _frameDirty = true;
        _dataDirty = true;
        _cursorGlyphsDirty = true;
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
        parts.Add(_cursor1Milliseconds is { } c1 ? $"C1 {FormatRelative(c1 - trigger)}" : "C1 —");
        parts.Add(_cursor2Milliseconds is { } c2 ? $"C2 {FormatRelative(c2 - trigger)}" : "C2 —");
        if (_cursor1Milliseconds is { } first && _cursor2Milliseconds is { } second)
            parts.Add($"Δt {Math.Abs(second - first):G6} ms");
        NavigationChanged?.Invoke(this, new ComtradeDisturbanceNavigationChangedEventArgs(string.Join("  |  ", parts)));
    }

    private static string FormatRelative(double value) => Math.Abs(value) < 0.0005 ? "0 ms" : $"{value:+0.###;-0.###} ms";

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static Pen FrozenDashedPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        return pen;
    }

    private static void DrawText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi, double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, FrozenBrush(color), dpi)
        {
            MaxTextWidth = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : 10000,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted, point);
    }
}
