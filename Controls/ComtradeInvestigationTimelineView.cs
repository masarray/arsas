using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

internal enum ComtradeInvestigationTimelineMode
{
    DualCursor,
    PhasorCursor,
    HarmonicCursor
}

internal enum ComtradeInvestigationTimelineCursor
{
    Cursor1,
    Cursor2,
    Phasor,
    Harmonic
}

internal sealed class ComtradeInvestigationTimelineCursorChangedEventArgs : EventArgs
{
    internal ComtradeInvestigationTimelineCursorChangedEventArgs(
        ComtradeInvestigationTimelineCursor cursor,
        double absoluteMilliseconds,
        double snapToleranceMilliseconds,
        bool isFinal)
    {
        Cursor = cursor;
        AbsoluteMilliseconds = absoluteMilliseconds;
        SnapToleranceMilliseconds = snapToleranceMilliseconds;
        IsFinal = isFinal;
    }

    internal ComtradeInvestigationTimelineCursor Cursor { get; }
    internal double AbsoluteMilliseconds { get; }
    internal double SnapToleranceMilliseconds { get; }
    internal bool IsFinal { get; }
}

/// <summary>
/// Persistent investigation ruler with retained cursor glyphs. Static ticks/background are rebuilt
/// only for viewport/geometry changes. Cursor lines are prebuilt DrawingVisuals and follow pointer
/// input through synchronous TranslateTransform updates; numeric readout text is coalesced to the
/// WPF composition cadence so text formatting can never hold the pointer behind the mouse.
/// </summary>
public sealed class ComtradeInvestigationTimelineView : FrameworkElement
{
    private const double DefaultLeftInset = 150.0;
    private const double DefaultRightInset = 18.0;
    private const double CursorHitRadius = 10.0;

    private readonly VisualCollection _visuals;
    private readonly DrawingVisual _staticVisual = new();
    private readonly DrawingVisual _readoutVisual = new();
    private readonly DrawingVisual _cursor1Visual = new();
    private readonly DrawingVisual _cursor2Visual = new();
    private readonly DrawingVisual _phasorVisual = new();
    private readonly DrawingVisual _harmonicVisual = new();
    private readonly TranslateTransform _cursor1Transform = new();
    private readonly TranslateTransform _cursor2Transform = new();
    private readonly TranslateTransform _phasorTransform = new();
    private readonly TranslateTransform _harmonicTransform = new();

    private ComtradeInvestigationTimelineMode _mode = ComtradeInvestigationTimelineMode.DualCursor;
    private ComtradeInvestigationTimelineCursor _dragCursor;
    private bool _dragging;
    private double _fullStartMilliseconds;
    private double _fullEndMilliseconds = 1.0;
    private double _viewStartMilliseconds;
    private double _viewEndMilliseconds = 1.0;
    private double? _triggerMilliseconds;
    private double? _cursor1Milliseconds;
    private double? _cursor2Milliseconds;
    private double? _phasorCursorMilliseconds;
    private double? _harmonicCursorMilliseconds;
    private double _plotLeftInset = DefaultLeftInset;
    private double _plotRightInset = DefaultRightInset;
    private double _plotContentWidth;
    private Rect _rulerRect;
    private Size _cachedSize;
    private bool _staticDirty = true;
    private bool _glyphsDirty = true;
    private bool _readoutDirty = true;
    private bool _readoutRenderingHooked;

    internal event EventHandler<ComtradeInvestigationTimelineCursorChangedEventArgs>? CursorChanged;

    internal ComtradeInvestigationTimelineMode Mode => _mode;
    internal double? Cursor1Milliseconds => _cursor1Milliseconds;
    internal double? Cursor2Milliseconds => _cursor2Milliseconds;
    internal double? PhasorCursorMilliseconds => _phasorCursorMilliseconds;
    internal double? HarmonicCursorMilliseconds => _harmonicCursorMilliseconds;

    protected override int VisualChildrenCount => _visuals.Count;

    public ComtradeInvestigationTimelineView()
    {
        Height = 66;
        MinHeight = 66;
        Focusable = true;
        Cursor = Cursors.Arrow;
        ToolTipService.SetInitialShowDelay(this, 1200);
        ToolTip = "Time Signals: C1/C2. Phasor: P. Harmonics: H. Cursor position and digital-edge snap are shared with the waveform timebase.";

        _visuals = new VisualCollection(this);
        _visuals.Add(_staticVisual);
        _visuals.Add(_readoutVisual);
        _visuals.Add(_cursor1Visual);
        _visuals.Add(_cursor2Visual);
        _visuals.Add(_phasorVisual);
        _visuals.Add(_harmonicVisual);
        _cursor1Visual.Transform = _cursor1Transform;
        _cursor2Visual.Transform = _cursor2Transform;
        _phasorVisual.Transform = _phasorTransform;
        _harmonicVisual.Transform = _harmonicTransform;
        HideAllCursorVisuals();
        Unloaded += (_, _) => StopReadoutRenderingPump();
    }

    protected override Visual GetVisualChild(int index) => _visuals[index];

    internal void SetPlotGeometry(double leftInset, double rightInset, double contentWidth)
    {
        var nextLeft = double.IsFinite(leftInset) && leftInset >= 0 ? leftInset : DefaultLeftInset;
        var nextRight = double.IsFinite(rightInset) && rightInset >= 0 ? rightInset : DefaultRightInset;
        var nextWidth = double.IsFinite(contentWidth) && contentWidth > 0 ? contentWidth : 0.0;
        if (Math.Abs(nextLeft - _plotLeftInset) < 0.01 &&
            Math.Abs(nextRight - _plotRightInset) < 0.01 &&
            Math.Abs(nextWidth - _plotContentWidth) < 0.01)
            return;
        _plotLeftInset = nextLeft;
        _plotRightInset = nextRight;
        _plotContentWidth = nextWidth;
        MarkRetainedLayersDirty();
        InvalidateVisual();
    }

    internal void SetMode(ComtradeInvestigationTimelineMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
        _glyphsDirty = true;
        _readoutDirty = true;
        InvalidateVisual();
    }

    internal void SetContext(
        double fullStartMilliseconds,
        double fullEndMilliseconds,
        double viewStartMilliseconds,
        double viewEndMilliseconds,
        double? triggerMilliseconds,
        double? cursor1Milliseconds,
        double? cursor2Milliseconds,
        double? phasorCursorMilliseconds,
        double? harmonicCursorMilliseconds)
    {
        _fullStartMilliseconds = double.IsFinite(fullStartMilliseconds) ? fullStartMilliseconds : 0.0;
        _fullEndMilliseconds = double.IsFinite(fullEndMilliseconds) && fullEndMilliseconds > _fullStartMilliseconds
            ? fullEndMilliseconds
            : _fullStartMilliseconds + 1.0;
        _viewStartMilliseconds = double.IsFinite(viewStartMilliseconds) ? viewStartMilliseconds : _fullStartMilliseconds;
        _viewEndMilliseconds = double.IsFinite(viewEndMilliseconds) && viewEndMilliseconds > _viewStartMilliseconds
            ? viewEndMilliseconds
            : _fullEndMilliseconds;
        _triggerMilliseconds = Finite(triggerMilliseconds);
        _cursor1Milliseconds = Finite(cursor1Milliseconds);
        _cursor2Milliseconds = Finite(cursor2Milliseconds);
        _phasorCursorMilliseconds = Finite(phasorCursorMilliseconds);
        _harmonicCursorMilliseconds = Finite(harmonicCursorMilliseconds);
        MarkRetainedLayersDirty();
        InvalidateVisual();
    }

    internal void SetCursorFromHost(ComtradeInvestigationTimelineCursor cursor, double milliseconds)
    {
        if (!double.IsFinite(milliseconds)) return;
        milliseconds = ComtradeInvestigationTimelineMath.ClampToRecord(milliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        switch (cursor)
        {
            case ComtradeInvestigationTimelineCursor.Cursor1: _cursor1Milliseconds = milliseconds; break;
            case ComtradeInvestigationTimelineCursor.Cursor2: _cursor2Milliseconds = milliseconds; break;
            case ComtradeInvestigationTimelineCursor.Phasor: _phasorCursorMilliseconds = milliseconds; break;
            case ComtradeInvestigationTimelineCursor.Harmonic: _harmonicCursorMilliseconds = milliseconds; break;
        }

        UpdateCursorTransforms();
        RequestReadoutRedraw();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0.0, ActualWidth), Math.Max(0.0, ActualHeight));
        dc.DrawRectangle(Brushes.Transparent, null, bounds);
        EnsureRetainedLayers(bounds);
    }

    protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
    {
        base.OnRenderSizeChanged(sizeInfo);
        MarkRetainedLayersDirty();
        InvalidateVisual();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        if (_rulerRect.Width <= 0 || !_rulerRect.Contains(point)) return;
        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;

        Focus();
        _dragCursor = ResolveCursorForPointer(point.X, e.ChangedButton);
        _dragging = true;
        CaptureMouse();
        Cursor = Cursors.SizeWE;
        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: false);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var point = e.GetPosition(this);
        if (!_dragging || !IsMouseCaptured)
        {
            Cursor = HoverCursor(point.X);
            return;
        }
        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: false);
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_dragging) return;
        var point = e.GetPosition(this);
        _dragging = false;
        if (IsMouseCaptured) ReleaseMouseCapture();
        Cursor = Cursors.Arrow;
        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: true);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        Cursor = Cursors.Arrow;
    }

    private void EnsureRetainedLayers(Rect bounds)
    {
        var size = new Size(bounds.Width, bounds.Height);
        if (_cachedSize != size)
        {
            _cachedSize = size;
            MarkRetainedLayersDirty();
        }

        if (bounds.Width < 260 || bounds.Height < 48)
        {
            using (_staticVisual.RenderOpen()) { }
            using (_readoutVisual.RenderOpen()) { }
            ClearCursorGlyphs();
            _staticDirty = false;
            _glyphsDirty = false;
            _readoutDirty = false;
            return;
        }

        var coordinateWidth = _plotContentWidth > 0 ? Math.Min(bounds.Width, _plotContentWidth) : bounds.Width;
        var left = Math.Min(_plotLeftInset, Math.Max(18, coordinateWidth * 0.45));
        var right = Math.Max(left + 80, coordinateWidth - _plotRightInset);
        _rulerRect = new Rect(left, 28, Math.Max(80, right - left), 30);

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");

        if (_staticDirty)
        {
            using var dc = _staticVisual.RenderOpen();
            DrawStaticRuler(dc, bounds, dpi, body, semibold);
            _staticDirty = false;
        }

        if (_glyphsDirty)
        {
            DrawCursorGlyph(_cursor1Visual, "C1", Color.FromRgb(221, 142, 32), dpi, semibold);
            DrawCursorGlyph(_cursor2Visual, "C2", Color.FromRgb(36, 172, 211), dpi, semibold);
            DrawCursorGlyph(_phasorVisual, "P", Color.FromRgb(34, 132, 184), dpi, semibold);
            DrawCursorGlyph(_harmonicVisual, "H", Color.FromRgb(226, 132, 38), dpi, semibold);
            _glyphsDirty = false;
        }

        UpdateCursorTransforms();
        if (_readoutDirty)
            RedrawReadout(dpi, body, semibold);
    }

    private void DrawStaticRuler(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        dc.DrawRectangle(FrozenBrush(Color.FromRgb(250, 252, 255)), null, bounds);
        dc.DrawLine(FrozenPen(Color.FromRgb(226, 233, 242), 1), new Point(0, bounds.Height - 1), new Point(bounds.Width, bounds.Height - 1));

        var y = _rulerRect.Top + 14;
        dc.DrawLine(FrozenPen(Color.FromRgb(174, 186, 201), 1), new Point(_rulerRect.Left, y), new Point(_rulerRect.Right, y));
        var approximateTicks = Math.Clamp((int)Math.Round(_rulerRect.Width / 125.0), 4, 10);
        foreach (var tick in ComtradeInvestigationTimelineMath.BuildTriggerAnchoredTicks(
                     _viewStartMilliseconds, _viewEndMilliseconds, _triggerMilliseconds, approximateTicks))
        {
            var x = XForTime(tick.AbsoluteMilliseconds);
            var isTriggerTick = Math.Abs(tick.RelativeMilliseconds) < 1e-9 && _triggerMilliseconds.HasValue;
            dc.DrawLine(isTriggerTick ? FrozenPen(Color.FromRgb(70, 78, 89), 1.4) : FrozenPen(Color.FromRgb(202, 211, 222), 1),
                new Point(x, y - (isTriggerTick ? 12 : 4)), new Point(x, y + 5));
            var label = isTriggerTick ? "0" : tick.RelativeMilliseconds.ToString("+0.###;-0.###", CultureInfo.CurrentCulture);
            DrawText(dc, label, 7.8, body, isTriggerTick ? Color.FromRgb(58, 65, 74) : Color.FromRgb(117, 130, 146),
                new Point(x - 12, y + 7), dpi, 32);
        }

        if (_triggerMilliseconds is { } trigger && trigger >= _viewStartMilliseconds && trigger <= _viewEndMilliseconds)
        {
            var x = XForTime(trigger);
            dc.DrawLine(FrozenPen(Color.FromRgb(55, 62, 72), 1.4), new Point(x, _rulerRect.Top), new Point(x, _rulerRect.Bottom - 1));
            DrawText(dc, "TRG", 7.6, semibold, Color.FromRgb(55, 62, 72), new Point(x + 4, _rulerRect.Top), dpi);
        }
    }

    private void DrawCursorGlyph(DrawingVisual visual, string label, Color color, double dpi, Typeface semibold)
    {
        using var dc = visual.RenderOpen();
        var brush = FrozenBrush(color);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(0, _rulerRect.Top + 2), true, true);
            context.LineTo(new Point(-5, _rulerRect.Top - 4), true, false);
            context.LineTo(new Point(0, _rulerRect.Top - 10), true, false);
            context.LineTo(new Point(5, _rulerRect.Top - 4), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
        dc.DrawLine(FrozenPen(color, 1.2), new Point(0, _rulerRect.Top + 2), new Point(0, ActualHeight));
        DrawText(dc, label, 7.2, semibold, color, new Point(5, _rulerRect.Top - 12), dpi, 22);
    }

    private void UpdateCursorTransforms()
    {
        if (_rulerRect.Width <= 0 || _viewEndMilliseconds <= _viewStartMilliseconds)
        {
            HideAllCursorVisuals();
            return;
        }

        SetCursorVisual(_cursor1Visual, _cursor1Transform, _mode == ComtradeInvestigationTimelineMode.DualCursor, _cursor1Milliseconds);
        SetCursorVisual(_cursor2Visual, _cursor2Transform, _mode == ComtradeInvestigationTimelineMode.DualCursor, _cursor2Milliseconds);
        SetCursorVisual(_phasorVisual, _phasorTransform, _mode == ComtradeInvestigationTimelineMode.PhasorCursor, _phasorCursorMilliseconds);
        SetCursorVisual(_harmonicVisual, _harmonicTransform, _mode == ComtradeInvestigationTimelineMode.HarmonicCursor, _harmonicCursorMilliseconds);
    }

    private void SetCursorVisual(DrawingVisual visual, TranslateTransform transform, bool modeVisible, double? milliseconds)
    {
        if (!modeVisible || milliseconds is not { } ms || !double.IsFinite(ms) ||
            ms < _viewStartMilliseconds || ms > _viewEndMilliseconds)
        {
            visual.Opacity = 0.0;
            return;
        }
        transform.X = XForTime(ms);
        visual.Opacity = 1.0;
    }

    private void HideAllCursorVisuals()
    {
        _cursor1Visual.Opacity = 0.0;
        _cursor2Visual.Opacity = 0.0;
        _phasorVisual.Opacity = 0.0;
        _harmonicVisual.Opacity = 0.0;
    }

    private void ClearCursorGlyphs()
    {
        using (_cursor1Visual.RenderOpen()) { }
        using (_cursor2Visual.RenderOpen()) { }
        using (_phasorVisual.RenderOpen()) { }
        using (_harmonicVisual.RenderOpen()) { }
        HideAllCursorVisuals();
    }

    private void RequestReadoutRedraw()
    {
        _readoutDirty = true;
        if (_readoutRenderingHooked)
            return;
        CompositionTarget.Rendering += ReadoutCompositionFrame;
        _readoutRenderingHooked = true;
    }

    private void ReadoutCompositionFrame(object? sender, EventArgs e)
    {
        if (!_readoutDirty)
        {
            StopReadoutRenderingPump();
            return;
        }

        if (ActualWidth >= 260 && ActualHeight >= 48)
        {
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            RedrawReadout(dpi, new Typeface("Segoe UI"), new Typeface("Segoe UI Semibold"));
        }
        else
        {
            using var dc = _readoutVisual.RenderOpen();
            _readoutDirty = false;
        }
        StopReadoutRenderingPump();
    }

    private void StopReadoutRenderingPump()
    {
        if (!_readoutRenderingHooked)
            return;
        CompositionTarget.Rendering -= ReadoutCompositionFrame;
        _readoutRenderingHooked = false;
    }

    private void RedrawReadout(double dpi, Typeface body, Typeface semibold)
    {
        using var dc = _readoutVisual.RenderOpen();
        DrawReadout(dc, dpi, body, semibold);
        _readoutDirty = false;
    }

    private void MarkRetainedLayersDirty()
    {
        _staticDirty = true;
        _glyphsDirty = true;
        _readoutDirty = true;
    }

    private ComtradeInvestigationTimelineCursor ResolveCursorForPointer(double x, MouseButton button)
    {
        if (_mode == ComtradeInvestigationTimelineMode.PhasorCursor)
            return ComtradeInvestigationTimelineCursor.Phasor;
        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
            return ComtradeInvestigationTimelineCursor.Harmonic;
        if (button == MouseButton.Right || IsNear(x, _cursor2Milliseconds))
            return ComtradeInvestigationTimelineCursor.Cursor2;
        return ComtradeInvestigationTimelineCursor.Cursor1;
    }

    private void PlaceCursor(ComtradeInvestigationTimelineCursor cursor, double milliseconds, bool isFinal)
    {
        milliseconds = ComtradeInvestigationTimelineMath.ClampToRecord(milliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        var tolerance = ComtradeTimeSignalsNavigationMath.SnapToleranceMilliseconds(
            _viewEndMilliseconds - _viewStartMilliseconds,
            Math.Max(1.0, _rulerRect.Width));

        var handler = CursorChanged;
        if (handler is null)
        {
            SetCursorFromHost(cursor, milliseconds);
            return;
        }

        handler.Invoke(this, new ComtradeInvestigationTimelineCursorChangedEventArgs(
            cursor, milliseconds, tolerance, isFinal));
    }

    private void DrawReadout(DrawingContext dc, double dpi, Typeface body, Typeface semibold)
    {
        var trigger = _triggerMilliseconds ?? 0.0;
        if (_mode == ComtradeInvestigationTimelineMode.PhasorCursor)
        {
            DrawText(dc, "PHASOR CURSOR", 8.6, semibold, Color.FromRgb(86, 102, 122), new Point(10, 6), dpi);
            DrawText(dc, _phasorCursorMilliseconds is { } p ? $"P  {FormatRelative(p - trigger)}" : "P  —",
                9.6, semibold, Color.FromRgb(34, 132, 184), new Point(104, 4), dpi);
            DrawText(dc, "single analysis reference", 8.2, body, Color.FromRgb(132, 144, 159), new Point(215, 6), dpi);
            return;
        }
        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
        {
            DrawText(dc, "HARMONIC CURSOR", 8.6, semibold, Color.FromRgb(86, 102, 122), new Point(10, 6), dpi);
            DrawText(dc, _harmonicCursorMilliseconds is { } h ? $"H  {FormatRelative(h - trigger)}" : "H  —",
                9.6, semibold, Color.FromRgb(220, 127, 35), new Point(112, 4), dpi);
            DrawText(dc, "single analysis reference", 8.2, body, Color.FromRgb(132, 144, 159), new Point(220, 6), dpi);
            return;
        }

        DrawText(dc, "INVESTIGATION CURSORS", 8.6, semibold, Color.FromRgb(86, 102, 122), new Point(10, 6), dpi);
        DrawText(dc, _cursor1Milliseconds is { } c1 ? $"C1  {FormatRelative(c1 - trigger)}" : "C1  —",
            9.2, semibold, Color.FromRgb(205, 128, 24), new Point(132, 4), dpi);
        DrawText(dc, _cursor2Milliseconds is { } c2 ? $"C2  {FormatRelative(c2 - trigger)}" : "C2  —",
            9.2, semibold, Color.FromRgb(26, 145, 184), new Point(232, 4), dpi);
        if (_cursor1Milliseconds is { } first && _cursor2Milliseconds is { } second)
            DrawText(dc, $"Δt  {Math.Abs(second - first):0.###} ms", 9.0, body,
                Color.FromRgb(91, 107, 127), new Point(334, 4), dpi);
    }

    private Cursor HoverCursor(double x)
    {
        if (_rulerRect.Width <= 0 || x < _rulerRect.Left || x > _rulerRect.Right)
            return Cursors.Arrow;
        if (_mode is ComtradeInvestigationTimelineMode.PhasorCursor or ComtradeInvestigationTimelineMode.HarmonicCursor)
            return Cursors.SizeWE;
        return IsNear(x, _cursor1Milliseconds) || IsNear(x, _cursor2Milliseconds) ? Cursors.SizeWE : Cursors.Cross;
    }

    private bool IsNear(double x, double? milliseconds)
        => milliseconds is { } ms && ms >= _viewStartMilliseconds && ms <= _viewEndMilliseconds &&
           Math.Abs(x - XForTime(ms)) <= CursorHitRadius;

    private double TimeAtX(double x)
    {
        var fraction = Math.Clamp((x - _rulerRect.Left) / Math.Max(1.0, _rulerRect.Width), 0.0, 1.0);
        return _viewStartMilliseconds + (_viewEndMilliseconds - _viewStartMilliseconds) * fraction;
    }

    private double XForTime(double milliseconds)
        => _rulerRect.Left + _rulerRect.Width * (milliseconds - _viewStartMilliseconds) /
           Math.Max(1e-12, _viewEndMilliseconds - _viewStartMilliseconds);

    private static double? Finite(double? value) => value is { } v && double.IsFinite(v) ? v : null;
    private static string FormatRelative(double value) => Math.Abs(value) < 0.0005 ? "0 ms" : $"{value:+0.###;-0.###} ms";

    private static SolidColorBrush FrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color); brush.Freeze(); return brush;
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var pen = new Pen(FrozenBrush(color), thickness); pen.Freeze(); return pen;
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
