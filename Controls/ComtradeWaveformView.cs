using System.Globalization;
using System.Windows;
using System.Windows.Media;

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

    internal void ShowAnalog(string title, string subtitle, string units, double[] values, uint[] timestamps, double timeMultiplier)
    {
        _title = title;
        _subtitle = subtitle;
        _units = units;
        _analog = values;
        _status = null;
        _timestamps = timestamps;
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        InvalidateVisual();
    }

    internal void ShowStatus(string title, string subtitle, byte[] values, uint[] timestamps, double timeMultiplier)
    {
        _title = title;
        _subtitle = subtitle;
        _units = "state";
        _analog = null;
        _status = values;
        _timestamps = timestamps;
        _timeMultiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        InvalidateVisual();
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
        InvalidateVisual();
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
        if (plot.Width <= 20 || plot.Height <= 20)
            return;

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

        if (_analog is { Length: > 0 })
            DrawAnalog(dc, plot, _analog, dpi, bodyTypeface);
        else if (_status is { Length: > 0 })
            DrawStatus(dc, plot, _status, dpi, bodyTypeface);
        else
            dc.DrawText(new FormattedText("No signal data loaded", CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, bodyTypeface, 12,
                new SolidColorBrush(Color.FromRgb(126, 139, 156)), dpi),
                new Point(plot.Left + 16, plot.Top + 16));

        DrawTimeAxis(dc, plot, dpi, bodyTypeface);
    }

    private void DrawAnalog(DrawingContext dc, Rect plot, double[] values, double dpi, Typeface bodyTypeface)
    {
        var min = values.Min();
        var max = values.Max();
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
            var maxPoints = Math.Max(64, (int)Math.Ceiling(plot.Width * 2));
            var stride = Math.Max(1, values.Length / maxPoints);
            var started = false;
            for (var i = 0; i < values.Length; i += stride)
            {
                var value = values[i];
                if (!double.IsFinite(value)) continue;
                var x = plot.Left + plot.Width * i / Math.Max(1, values.Length - 1.0);
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
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        DrawValueLabel(dc, plot, max, plot.Top - 7, dpi, bodyTypeface);
        DrawValueLabel(dc, plot, min, plot.Bottom - 7, dpi, bodyTypeface);
    }

    private void DrawStatus(DrawingContext dc, Rect plot, byte[] values, double dpi, Typeface bodyTypeface)
    {
        var pen = new Pen(new SolidColorBrush(Color.FromRgb(29, 139, 91)), 1.5);
        pen.Freeze();
        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            var y0 = plot.Bottom - plot.Height * 0.25;
            var y1 = plot.Top + plot.Height * 0.25;
            var previous = values[0] == 0 ? y0 : y1;
            context.BeginFigure(new Point(plot.Left, previous), false, false);
            for (var i = 1; i < values.Length; i++)
            {
                var x = plot.Left + plot.Width * i / Math.Max(1, values.Length - 1.0);
                var current = values[i] == 0 ? y0 : y1;
                context.LineTo(new Point(x, previous), true, false);
                if (Math.Abs(current - previous) > 0.1)
                    context.LineTo(new Point(x, current), true, false);
                previous = current;
            }
            context.LineTo(new Point(plot.Right, previous), true, false);
        }
        geometry.Freeze();
        dc.DrawGeometry(null, pen, geometry);

        dc.DrawText(new FormattedText("1", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bodyTypeface, 10, Brushes.Gray, dpi), new Point(plot.Left - 20, plot.Top + plot.Height * 0.25 - 7));
        dc.DrawText(new FormattedText("0", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            bodyTypeface, 10, Brushes.Gray, dpi), new Point(plot.Left - 20, plot.Bottom - plot.Height * 0.25 - 7));
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
        if (_timestamps is not { Length: > 1 })
            return;

        // COMTRADE timestamps are in microseconds and are scaled by TIMEMULT.
        var firstMs = _timestamps[0] * _timeMultiplier / 1000.0;
        var lastMs = _timestamps[^1] * _timeMultiplier / 1000.0;
        var firstText = $"{firstMs:G6} ms";
        var lastText = $"{lastMs:G6} ms";
        dc.DrawText(new FormattedText(firstText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 9.5, Brushes.Gray, dpi), new Point(plot.Left, plot.Bottom + 8));
        var formattedLast = new FormattedText(lastText, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, 9.5, Brushes.Gray, dpi) { TextAlignment = TextAlignment.Right };
        dc.DrawText(formattedLast, new Point(plot.Right, plot.Bottom + 8));
    }
}
