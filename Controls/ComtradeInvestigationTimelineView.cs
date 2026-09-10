using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

internal enum ComtradeInvestigationTimelineMode
{
    DualCursor,
    HarmonicCursor
}

internal enum ComtradeInvestigationTimelineCursor
{
    Cursor1,
    Cursor2,
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
/// Persistent investigation ruler shared by Time Signals, Phasor and Harmonics. It intentionally
/// owns only lightweight cursor interaction; waveform rendering and native analysis remain in their
/// dedicated views. This mirrors the workstation model where one timeline context drives all views.
/// </summary>
public sealed class ComtradeInvestigationTimelineView : FrameworkElement
{
    private const double LeftInset = 150.0;
    private const double RightInset = 18.0;
    private const double CursorHitRadius = 10.0;
    private static readonly long InteractiveNotifyTicks = Math.Max(1, Stopwatch.Frequency / 30);

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
    private double? _harmonicCursorMilliseconds;
    private Rect _rulerRect;
    private long _lastInteractiveNotify;

    internal event EventHandler<ComtradeInvestigationTimelineCursorChangedEventArgs>? CursorChanged;

    internal ComtradeInvestigationTimelineMode Mode => _mode;
    internal double? Cursor1Milliseconds => _cursor1Milliseconds;
    internal double? Cursor2Milliseconds => _cursor2Milliseconds;
    internal double? HarmonicCursorMilliseconds => _harmonicCursorMilliseconds;

    public ComtradeInvestigationTimelineView()
    {
        Height = 66;
        MinHeight = 66;
        Focusable = true;
        Cursor = Cursors.Arrow;
        ToolTipService.SetInitialShowDelay(this, 1200);
        ToolTip = "Drag C1/C2 to investigate. Harmonics uses one H cursor.";
    }

    internal void SetMode(ComtradeInvestigationTimelineMode mode)
    {
        if (_mode == mode) return;
        _mode = mode;
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
        _triggerMilliseconds = triggerMilliseconds is { } trigger && double.IsFinite(trigger) ? trigger : null;
        _cursor1Milliseconds = cursor1Milliseconds is { } c1 && double.IsFinite(c1) ? c1 : null;
        _cursor2Milliseconds = cursor2Milliseconds is { } c2 && double.IsFinite(c2) ? c2 : null;
        _harmonicCursorMilliseconds = harmonicCursorMilliseconds is { } harmonic && double.IsFinite(harmonic) ? harmonic : null;
        InvalidateVisual();
    }

    internal void SetCursorFromHost(ComtradeInvestigationTimelineCursor cursor, double milliseconds)
    {
        if (!double.IsFinite(milliseconds)) return;
        milliseconds = ComtradeInvestigationTimelineMath.ClampToRecord(milliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        switch (cursor)
        {
            case ComtradeInvestigationTimelineCursor.Cursor1:
                _cursor1Milliseconds = milliseconds;
                break;
            case ComtradeInvestigationTimelineCursor.Cursor2:
                _cursor2Milliseconds = milliseconds;
                break;
            case ComtradeInvestigationTimelineCursor.Harmonic:
                _harmonicCursorMilliseconds = milliseconds;
                break;
        }
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var width = Math.Max(0.0, ActualWidth);
        var height = Math.Max(0.0, ActualHeight);
        if (width < 260 || height < 48) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        dc.DrawRectangle(FrozenBrush(Color.FromRgb(250, 252, 255)), null, new Rect(0, 0, width, height));
        dc.DrawLine(FrozenPen(Color.FromRgb(226, 233, 242), 1), new Point(0, height - 1), new Point(width, height - 1));

        var left = Math.Min(LeftInset, Math.Max(18, width * 0.22));
        var right = Math.Max(left + 80, width - RightInset);
        _rulerRect = new Rect(left, 28, Math.Max(80, right - left), 30);

        DrawReadout(dc, dpi, body, semibold);
        DrawRuler(dc, dpi, body, semibold);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_rulerRect.Width <= 0 || !_rulerRect.Contains(e.GetPosition(this))) return;

        Focus();
        var point = e.GetPosition(this);
        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
        {
            _dragCursor = ComtradeInvestigationTimelineCursor.Harmonic;
        }
        else if (e.ChangedButton == MouseButton.Right)
        {
            _dragCursor = ComtradeInvestigationTimelineCursor.Cursor2;
        }
        else if (IsNear(point.X, _cursor2Milliseconds))
        {
            _dragCursor = ComtradeInvestigationTimelineCursor.Cursor2;
        }
        else
        {
            _dragCursor = ComtradeInvestigationTimelineCursor.Cursor1;
        }

        if (e.ChangedButton is not (MouseButton.Left or MouseButton.Right)) return;
        _dragging = true;
        CaptureMouse();
        Cursor = Cursors.SizeWE;
        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: false, forceNotify: true);
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

        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: false, forceNotify: false);
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
        PlaceCursor(_dragCursor, TimeAtX(point.X), isFinal: true, forceNotify: true);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _dragging = false;
        Cursor = Cursors.Arrow;
    }

    private void PlaceCursor(
        ComtradeInvestigationTimelineCursor cursor,
        double milliseconds,
        bool isFinal,
        bool forceNotify)
    {
        milliseconds = ComtradeInvestigationTimelineMath.ClampToRecord(milliseconds, _fullStartMilliseconds, _fullEndMilliseconds);
        SetCursorFromHost(cursor, milliseconds);

        var now = Stopwatch.GetTimestamp();
        if (!forceNotify && !isFinal && now - _lastInteractiveNotify < InteractiveNotifyTicks)
            return;
        _lastInteractiveNotify = now;
        var tolerance = (_viewEndMilliseconds - _viewStartMilliseconds) * 10.0 / Math.Max(1.0, _rulerRect.Width);
        CursorChanged?.Invoke(this, new ComtradeInvestigationTimelineCursorChangedEventArgs(
            cursor,
            milliseconds,
            Math.Max(0.0, tolerance),
            isFinal));
    }

    private void DrawReadout(DrawingContext dc, double dpi, Typeface body, Typeface semibold)
    {
        var trigger = _triggerMilliseconds ?? 0.0;
        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
        {
            DrawText(dc, "HARMONIC CURSOR", 8.6, semibold, Color.FromRgb(86, 102, 122), new Point(10, 6), dpi);
            DrawText(dc,
                _harmonicCursorMilliseconds is { } harmonic ? $"H  {FormatRelative(harmonic - trigger)}" : "H  —",
                9.6, semibold, Color.FromRgb(220, 127, 35), new Point(112, 4), dpi);
            DrawText(dc, "single analysis reference", 8.2, body, Color.FromRgb(132, 144, 159), new Point(220, 6), dpi);
            return;
        }

        DrawText(dc, "INVESTIGATION CURSORS", 8.6, semibold, Color.FromRgb(86, 102, 122), new Point(10, 6), dpi);
        DrawText(dc,
            _cursor1Milliseconds is { } c1 ? $"C1  {FormatRelative(c1 - trigger)}" : "C1  —",
            9.2, semibold, Color.FromRgb(205, 128, 24), new Point(132, 4), dpi);
        DrawText(dc,
            _cursor2Milliseconds is { } c2 ? $"C2  {FormatRelative(c2 - trigger)}" : "C2  —",
            9.2, semibold, Color.FromRgb(26, 145, 184), new Point(232, 4), dpi);
        if (_cursor1Milliseconds is { } first && _cursor2Milliseconds is { } second)
            DrawText(dc, $"Δt  {Math.Abs(second - first):0.###} ms", 9.0, body,
                Color.FromRgb(91, 107, 127), new Point(334, 4), dpi);
    }

    private void DrawRuler(DrawingContext dc, double dpi, Typeface body, Typeface semibold)
    {
        var y = _rulerRect.Top + 14;
        dc.DrawLine(FrozenPen(Color.FromRgb(174, 186, 201), 1), new Point(_rulerRect.Left, y), new Point(_rulerRect.Right, y));

        var approximateTicks = Math.Clamp((int)Math.Round(_rulerRect.Width / 125.0), 4, 10);
        var ticks = ComtradeInvestigationTimelineMath.BuildTriggerAnchoredTicks(
            _viewStartMilliseconds,
            _viewEndMilliseconds,
            _triggerMilliseconds,
            approximateTicks);
        foreach (var tick in ticks)
        {
            var x = XForTime(tick.AbsoluteMilliseconds);
            var isTriggerTick = Math.Abs(tick.RelativeMilliseconds) < 1e-9 && _triggerMilliseconds.HasValue;
            var pen = isTriggerTick
                ? FrozenPen(Color.FromRgb(70, 78, 89), 1.4)
                : FrozenPen(Color.FromRgb(202, 211, 222), 1);
            dc.DrawLine(pen, new Point(x, y - (isTriggerTick ? 12 : 4)), new Point(x, y + 5));
            var label = isTriggerTick ? "0" : tick.RelativeMilliseconds.ToString("+0.###;-0.###", CultureInfo.CurrentCulture);
            DrawText(dc, label, 7.8, body,
                isTriggerTick ? Color.FromRgb(58, 65, 74) : Color.FromRgb(117, 130, 146),
                new Point(x - 12, y + 7), dpi, 32);
        }

        if (_triggerMilliseconds is { } trigger && trigger >= _viewStartMilliseconds && trigger <= _viewEndMilliseconds)
        {
            var x = XForTime(trigger);
            dc.DrawLine(FrozenPen(Color.FromRgb(55, 62, 72), 1.4), new Point(x, _rulerRect.Top), new Point(x, _rulerRect.Bottom - 1));
            DrawText(dc, "TRG", 7.6, semibold, Color.FromRgb(55, 62, 72), new Point(x + 4, _rulerRect.Top), dpi);
        }

        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
        {
            DrawCursorMarker(dc, _harmonicCursorMilliseconds, "H", Color.FromRgb(226, 132, 38), dpi, semibold);
            return;
        }

        DrawCursorMarker(dc, _cursor1Milliseconds, "C1", Color.FromRgb(221, 142, 32), dpi, semibold);
        DrawCursorMarker(dc, _cursor2Milliseconds, "C2", Color.FromRgb(36, 172, 211), dpi, semibold);
    }

    private void DrawCursorMarker(DrawingContext dc, double? milliseconds, string label, Color color, double dpi, Typeface semibold)
    {
        if (milliseconds is not { } ms || ms < _viewStartMilliseconds || ms > _viewEndMilliseconds) return;
        var x = XForTime(ms);
        var brush = FrozenBrush(color);
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(new Point(x, _rulerRect.Top + 2), true, true);
            context.LineTo(new Point(x - 5, _rulerRect.Top - 4), true, false);
            context.LineTo(new Point(x, _rulerRect.Top - 10), true, false);
            context.LineTo(new Point(x + 5, _rulerRect.Top - 4), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(brush, null, geometry);
        dc.DrawLine(FrozenPen(color, 1.1), new Point(x, _rulerRect.Top + 2), new Point(x, _rulerRect.Bottom - 1));
        DrawText(dc, label, 7.2, semibold, color, new Point(x + 5, _rulerRect.Top - 12), dpi, 22);
    }

    private Cursor HoverCursor(double x)
    {
        if (_rulerRect.Width <= 0 || x < _rulerRect.Left || x > _rulerRect.Right)
            return Cursors.Arrow;
        if (_mode == ComtradeInvestigationTimelineMode.HarmonicCursor)
            return Cursors.SizeWE;
        return IsNear(x, _cursor1Milliseconds) || IsNear(x, _cursor2Milliseconds)
            ? Cursors.SizeWE
            : Cursors.Cross;
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
        => _rulerRect.Left + _rulerRect.Width *
           (milliseconds - _viewStartMilliseconds) / Math.Max(1e-12, _viewEndMilliseconds - _viewStartMilliseconds);

    private static string FormatRelative(double value)
        => Math.Abs(value) < 0.0005 ? "0 ms" : $"{value:+0.###;-0.###} ms";

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

    private static void DrawText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Color color,
        Point point,
        double dpi,
        double maxWidth = double.PositiveInfinity)
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
