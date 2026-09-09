using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

public sealed class ComtradeWaveformView : FrameworkElement
{
    private double[]? _analog;
    private byte[]? _status;
    private uint[]? _timestamps;
    private double _timeMultiplier = 1.0;
    private string _title = "Select a signal";
    private string _subtitle = "Native ArdIrec core • ARSAS WPF";
    private string _units = string.Empty;
    private ComtradeFrameWindow _window;
    private int? _cursorA;
    private int? _cursorB;
    private double? _triggerMilliseconds;
    private Rect _lastPlot;
    private bool _isPanning;
    private bool _preserveAnalogPoints;
    private Point _panStartPoint;
    private ComtradeFrameWindow _panStartWindow;

    internal event EventHandler<ComtradeNavigationChangedEventArgs>? NavigationChanged;

    public ComtradeWaveformView()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        ToolTip = "Wheel: zoom • Shift+wheel: pan • Alt+drag/middle-drag: pan • Click: Cursor A • Ctrl+click/right-click: Cursor B • Double-click: reset";
    }

    internal void ShowAnalog(
        string title,
        string subtitle,
        string units,
        double[] values,
        uint[] timestamps,
        double timeMultiplier,
        bool preserveAllPoints = false)
    {
        _title = title;
        _subtitle = subtitle;
        _units = units;
        _analog = values;
        _status = null;
        _timestamps = timestamps;
        _timeMultiplier = NormalizeTimeMultiplier(timeMultiplier);
        _preserveAnalogPoints = preserveAllPoints;
        ResetNavigationCore();
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ShowStatus(string title, string subtitle, byte[] values, uint[] timestamps, double timeMultiplier)
    {
        _title = title;
        _subtitle = subtitle;
        _units = "state";
        _analog = null;
        _status = values;
        _timestamps = timestamps;
        _timeMultiplier = NormalizeTimeMultiplier(timeMultiplier);
        _preserveAnalogPoints = false;
        ResetNavigationCore();
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _units = string.Empty;
        _analog = null;
        _status = null;
        _timestamps = null;
        _timeMultiplier = 1.0;
        _preserveAnalogPoints = false;
        _window = default;
        _cursorA = null;
        _cursorB = null;
        InvalidateVisual();
    }

    internal void SetTriggerOffsetMilliseconds(double? milliseconds)
    {
        _triggerMilliseconds = milliseconds is { } value && double.IsFinite(value) ? value : null;
        InvalidateVisual();
    }

    internal void ResetNavigation()
    {
        if (DataLength <= 0)
            return;

        ResetNavigationCore();
        InvalidateVisual();
        RaiseNavigationChanged();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 120 || bounds.Height < 100)
            return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var titleTypeface = new Typeface("Segoe UI Semibold");
        var bodyTypeface = new Typeface("Segoe UI");
        dc.DrawText(new FormattedText(_title, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            titleTypeface, 14, new SolidColorBrush(Color.FromRgb(31, 50, 74)), dpi), new Point(18, 14));
        dc.DrawText(new FormattedText(_subtitle, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            bodyTypeface, 11, new SolidColorBrush(Color.FromRgb(101, 119, 142)), dpi), new Point(18, 37));

        var plot = new Rect(62, 66, bounds.Width - 82, bounds.Height - 100);
        _lastPlot = plot;
        if (plot.Width <= 20 || plot.Height <= 20)
            return;

        DrawGrid(dc, plot);

        if (_analog is { Length: > 0 })
            DrawAnalog(dc, plot, dpi, bodyTypeface);
        else if (_status is { Length: > 0 })
            DrawStatus(dc, plot, dpi, bodyTypeface);
        else
            dc.DrawText(new FormattedText("No signal data loaded", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, bodyTypeface, 12,
                new SolidColorBrush(Color.FromRgb(126, 139, 156)), dpi),
                new Point(plot.Left + 16, plot.Top + 16));

        DrawTrigger(dc, plot, dpi, bodyTypeface);
        DrawCursor(dc, plot, _cursorA, "A", Color.FromRgb(214, 93, 55), dpi, bodyTypeface);
        DrawCursor(dc, plot, _cursorB, "B", Color.FromRgb(123, 82, 181), dpi, bodyTypeface);
        DrawTimeAxis(dc, plot, dpi, bodyTypeface);
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!CanNavigate || !_lastPlot.Contains(e.GetPosition(this)))
            return;

        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var step = Math.Max(1, _window.Count / 10);
            _window = ComtradeNavigationMath.Pan(_window, DataLength, e.Delta > 0 ? -step : step);
        }
        else
        {
            var plotFraction = FractionAtPoint(e.GetPosition(this));
            var anchorFrame = FrameAtPlotFraction(plotFraction);
            var frameFraction = anchorFrame < 0 || _window.Count <= 1
                ? plotFraction
                : (anchorFrame - _window.Start) / (double)(_window.Count - 1);
            _window = ComtradeNavigationMath.Zoom(_window, DataLength, frameFraction, e.Delta > 0 ? 0.8 : 1.25);
        }

        InvalidateVisual();
        RaiseNavigationChanged();
        e.Handled = true;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (!CanNavigate || !_lastPlot.Contains(e.GetPosition(this)))
            return;

        Focus();
        if (e.ClickCount >= 2 && e.ChangedButton == MouseButton.Left)
        {
            ResetNavigation();
            e.Handled = true;
            return;
        }

        var panGesture = e.ChangedButton == MouseButton.Middle ||
                         (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        if (panGesture)
        {
            _isPanning = true;
            _panStartPoint = e.GetPosition(this);
            _panStartWindow = _window;
            CaptureMouse();
            Cursor = Cursors.SizeWE;
            e.Handled = true;
            return;
        }

        if (e.ChangedButton is MouseButton.Left or MouseButton.Right)
        {
            var index = FrameAtPlotFraction(FractionAtPoint(e.GetPosition(this)));
            if (index >= 0)
            {
                if (e.ChangedButton == MouseButton.Right || (Keyboard.Modifiers & ModifierKeys.Control) != 0)
                    _cursorB = index;
                else
                    _cursorA = index;

                InvalidateVisual();
                RaiseNavigationChanged();
                e.Handled = true;
            }
        }
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (!_isPanning || !IsMouseCaptured || _lastPlot.Width <= 0)
            return;

        var deltaPixels = e.GetPosition(this).X - _panStartPoint.X;
        var deltaFrames = -(int)Math.Round(deltaPixels / _lastPlot.Width * _panStartWindow.Count, MidpointRounding.AwayFromZero);
        var next = ComtradeNavigationMath.Pan(_panStartWindow, DataLength, deltaFrames);
        if (next == _window)
            return;

        _window = next;
        InvalidateVisual();
        RaiseNavigationChanged();
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (!_isPanning)
            return;

        _isPanning = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        Cursor = Cursors.Cross;
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isPanning = false;
        Cursor = Cursors.Cross;
    }

    private int DataLength
    {
        get
        {
            var signalLength = _analog?.Length ?? _status?.Length ?? 0;
            return _timestamps is null ? signalLength : Math.Min(signalLength, _timestamps.Length);
        }
    }

    private bool CanNavigate => DataLength > 1 && _window.Count > 1;

    private void ResetNavigationCore()
    {
        _window = ComtradeNavigationMath.Full(DataLength);
        _cursorA = null;
        _cursorB = null;
    }

    private static double NormalizeTimeMultiplier(double value)
        => value > 0 && double.IsFinite(value) ? value : 1.0;

    private double FractionAtPoint(Point point)
    {
        if (_lastPlot.Width <= 0)
            return 0;
        return Math.Clamp((point.X - _lastPlot.Left) / _lastPlot.Width, 0.0, 1.0);
    }

    private int FrameAtPlotFraction(double fraction)
    {
        if (_timestamps is { Length: > 0 })
            return ComtradeTimeMath.FrameAtFraction(_timestamps, _window, fraction);
        return ComtradeNavigationMath.FrameAtFraction(_window, DataLength, fraction);
    }

    private double FractionForFrame(int frameIndex)
    {
        if (_timestamps is { Length: > 0 })
            return ComtradeTimeMath.FractionForFrame(_timestamps, _window, frameIndex);
        return _window.Count <= 1 ? 0.0 : (frameIndex - _window.Start) / (double)(_window.Count - 1);
    }

    private void DrawGrid(DrawingContext dc, Rect plot)
    {
        var gridPen = new Pen(new SolidColorBrush(Color.FromRgb(229, 234, 241)), 1);
        var axisPen = new Pen(new SolidColorBrush(Color.FromRgb(188, 198, 211)), 1);
        for (var i = 0; i <= 10; i++)
        {
            var x = plot.Left + plot.Width * i / 10.0;
            dc.DrawLine(gridPen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        }
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Top + plot.Height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
        }
        dc.DrawRectangle(null, axisPen, plot);
    }

    private void DrawAnalog(DrawingContext dc, Rect plot, double dpi, Typeface bodyTypeface)
    {
        if (_analog is null || _window.Count <= 0)
            return;

        var start = _window.Start;
        var end = Math.Min(_window.EndExclusive, _analog.Length);
        var min = double.PositiveInfinity;
        var max = double.NegativeInfinity;
        for (var i = start; i < end; i++)
        {
            var value = _analog[i];
            if (!double.IsFinite(value))
                continue;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }

        if (!double.IsFinite(min) || !double.IsFinite(max))
            return;
        if (Math.Abs(max - min) < 1e-12)
        {
            var pad = Math.Max(1.0, Math.Abs(max) * 0.1);
            min -= pad;
            max += pad;
        }

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(32, 113, 224)), 1.25);
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            // Raw series can still be thinned to the available pixel density. A P1B.2 envelope
            // is already the final bounded representation and must keep every boundary/extrema
            // point; applying a second stride could hide a narrow fault spike.
            var maxPoints = Math.Max(64, (int)Math.Ceiling(plot.Width * 2));
            var stride = _preserveAnalogPoints ? 1 : Math.Max(1, _window.Count / maxPoints);
            var started = false;
            for (var i = start; i < end; i += stride)
            {
                var value = _analog[i];
                if (!double.IsFinite(value))
                    continue;
                var x = plot.Left + plot.Width * FractionForFrame(i);
                var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
                var point = new Point(x, y);
                if (!started)
                {
                    context.BeginFigure(point, false, false);
                    started = true;
                }
                else
                {
                    context.LineTo(point, true, false);
                }
            }

            if (end - 1 >= start && (end - 1 - start) % stride != 0)
            {
                var i = end - 1;
                var value = _analog[i];
                if (double.IsFinite(value))
                {
                    var x = plot.Left + plot.Width * FractionForFrame(i);
                    var y = plot.Bottom - (value - min) / (max - min) * plot.Height;
                    if (!started)
                        context.BeginFigure(new Point(x, y), false, false);
                    else
                        context.LineTo(new Point(x, y), true, false);
                }
            }
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        DrawValueLabel(dc, plot, max, plot.Top - 7, dpi, bodyTypeface);
        DrawValueLabel(dc, plot, min, plot.Bottom - 7, dpi, bodyTypeface);
    }

    private void DrawStatus(DrawingContext dc, Rect plot, double dpi, Typeface bodyTypeface)
    {
        if (_status is null || _window.Count <= 0)
            return;

        var start = _window.Start;
        var end = Math.Min(_window.EndExclusive, _status.Length);
        if (end <= start)
            return;

        var pen = new Pen(new SolidColorBrush(Color.FromRgb(29, 139, 91)), 1.5);
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var y0 = plot.Bottom - plot.Height * 0.25;
            var y1 = plot.Top + plot.Height * 0.25;
            var previousState = _status[start] != 0;
            var previousY = previousState ? y1 : y0;
            context.BeginFigure(new Point(plot.Left, previousY), false, false);

            // Digital traces are transition-driven: a 500k-frame record with a steady state
            // produces one horizontal segment instead of half a million line commands.
            for (var i = start + 1; i < end; i++)
            {
                var currentState = _status[i] != 0;
                if (currentState == previousState)
                    continue;

                var x = plot.Left + plot.Width * FractionForFrame(i);
                var currentY = currentState ? y1 : y0;
                context.LineTo(new Point(x, previousY), true, false);
                context.LineTo(new Point(x, currentY), true, false);
                previousState = currentState;
                previousY = currentY;
            }
            context.LineTo(new Point(plot.Right, previousY), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        dc.DrawText(new FormattedText("1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bodyTypeface, 10, Brushes.Gray, dpi), new Point(plot.Left - 20, plot.Top + plot.Height * 0.25 - 7));
        dc.DrawText(new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bodyTypeface, 10, Brushes.Gray, dpi), new Point(plot.Left - 20, plot.Bottom - plot.Height * 0.25 - 7));
    }

    private void DrawTrigger(DrawingContext dc, Rect plot, double dpi, Typeface typeface)
    {
        if (_triggerMilliseconds is not { } trigger || _timestamps is not { Length: > 1 } || _window.Count <= 1)
            return;

        var firstMs = TimeMillisecondsAt(_window.Start);
        var lastMs = TimeMillisecondsAt(_window.EndExclusive - 1);
        if (lastMs <= firstMs || trigger < firstMs || trigger > lastMs)
            return;

        var fraction = (trigger - firstMs) / (lastMs - firstMs);
        var x = plot.Left + plot.Width * fraction;
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(218, 147, 46)), 1.1) { DashStyle = DashStyles.Dash };
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        DrawMarkerLabel(dc, "TRG", x, plot.Top + 3, Color.FromRgb(181, 112, 19), dpi, typeface);
    }

    private void DrawCursor(DrawingContext dc, Rect plot, int? index, string label, Color color, double dpi, Typeface typeface)
    {
        if (index is not { } frame || frame < _window.Start || frame >= _window.EndExclusive || _window.Count <= 1)
            return;

        var x = plot.Left + plot.Width * FractionForFrame(frame);
        var pen = new Pen(new SolidColorBrush(color), 1.15);
        pen.Freeze();
        dc.DrawLine(pen, new Point(x, plot.Top), new Point(x, plot.Bottom));
        DrawMarkerLabel(dc, label, x, plot.Top + 18, color, dpi, typeface);
    }

    private static void DrawMarkerLabel(DrawingContext dc, string text, double x, double y, Color color, double dpi, Typeface typeface)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 9.5, new SolidColorBrush(color), dpi)
        {
            TextAlignment = TextAlignment.Center
        };
        dc.DrawText(formatted, new Point(x, y));
    }

    private void DrawValueLabel(DrawingContext dc, Rect plot, double value, double y, double dpi, Typeface typeface)
    {
        var text = value.ToString("G5", CultureInfo.InvariantCulture);
        if (!string.IsNullOrWhiteSpace(_units)) text += " " + _units;
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 9.5, new SolidColorBrush(Color.FromRgb(105, 117, 132)), dpi)
        {
            TextAlignment = TextAlignment.Right
        };
        dc.DrawText(formatted, new Point(plot.Left - 8, y));
    }

    private void DrawTimeAxis(DrawingContext dc, Rect plot, double dpi, Typeface typeface)
    {
        if (_timestamps is not { Length: > 1 } || _window.Count <= 0)
            return;

        var firstMs = TimeMillisecondsAt(_window.Start);
        var lastMs = TimeMillisecondsAt(_window.EndExclusive - 1);
        var middleMs = (firstMs + lastMs) * 0.5;
        DrawTimeLabel(dc, $"{firstMs:G7} ms", plot.Left, TextAlignment.Left, plot.Bottom + 8, dpi, typeface);
        DrawTimeLabel(dc, $"{middleMs:G7} ms", plot.Left + plot.Width * 0.5, TextAlignment.Center, plot.Bottom + 8, dpi, typeface);
        DrawTimeLabel(dc, $"{lastMs:G7} ms", plot.Right, TextAlignment.Right, plot.Bottom + 8, dpi, typeface);
    }

    private static void DrawTimeLabel(DrawingContext dc, string text, double x, TextAlignment alignment, double y, double dpi, Typeface typeface)
    {
        var formatted = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 9.5, Brushes.Gray, dpi) { TextAlignment = alignment };
        dc.DrawText(formatted, new Point(x, y));
    }

    private double TimeMillisecondsAt(int index)
    {
        if (_timestamps is null || _timestamps.Length == 0)
            return 0;
        index = Math.Clamp(index, 0, _timestamps.Length - 1);
        return ComtradeTimeMath.ToMilliseconds(_timestamps[index], _timeMultiplier);
    }

    private string CursorText(string name, int? index)
    {
        if (index is not { } frame || frame < 0 || frame >= DataLength)
            return $"{name}: —";

        var time = TimeMillisecondsAt(frame);
        if (_analog is not null && frame < _analog.Length)
        {
            var value = _analog[frame];
            var unitSuffix = string.IsNullOrWhiteSpace(_units) ? string.Empty : $" {_units}";
            return $"{name}: {time:G7} ms • {value:G7}{unitSuffix}";
        }
        if (_status is not null && frame < _status.Length)
            return $"{name}: {time:G7} ms • {(_status[frame] == 0 ? 0 : 1)}";
        return $"{name}: {time:G7} ms";
    }

    private void RaiseNavigationChanged()
    {
        if (DataLength <= 0 || _window.Count <= 0)
            return;

        var firstMs = TimeMillisecondsAt(_window.Start);
        var lastMs = TimeMillisecondsAt(_window.EndExclusive - 1);
        var parts = new List<string>
        {
            $"View {firstMs:G7}…{lastMs:G7} ms",
            CursorText("A", _cursorA),
            CursorText("B", _cursorB)
        };

        if (_cursorA is { } a && _cursorB is { } b)
        {
            var deltaMs = TimeMillisecondsAt(b) - TimeMillisecondsAt(a);
            var delta = $"Δt {deltaMs:G7} ms";
            if (_analog is not null && a < _analog.Length && b < _analog.Length)
                delta += $" • Δ {_analog[b] - _analog[a]:G7}{(string.IsNullOrWhiteSpace(_units) ? string.Empty : " " + _units)}";
            parts.Add(delta);
        }

        NavigationChanged?.Invoke(this, new ComtradeNavigationChangedEventArgs(string.Join("  |  ", parts)));
    }
}

internal sealed class ComtradeNavigationChangedEventArgs(string summary) : EventArgs
{
    public string Summary { get; } = summary;
}
