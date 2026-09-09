using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ArIED61850Tester.Controls;

internal sealed record ComtradePhasorVector(
    string Label,
    string Phase,
    string Units,
    double MagnitudeRms,
    double AngleDegrees);

public sealed class ComtradePhasorView : FrameworkElement
{
    private IReadOnlyList<ComtradePhasorVector> _vectors = Array.Empty<ComtradePhasorVector>();
    private string _title = "Phasor";
    private string _subtitle = "Select an analog signal";

    internal void ShowPhasors(string title, string subtitle, IReadOnlyList<ComtradePhasorVector> vectors)
    {
        _title = title;
        _subtitle = subtitle;
        _vectors = vectors.Where(v => double.IsFinite(v.MagnitudeRms) && double.IsFinite(v.AngleDegrees)).ToArray();
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _vectors = Array.Empty<ComtradePhasorVector>();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 260 || bounds.Height < 220) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        DrawText(dc, _title, 15, semibold, Color.FromRgb(31, 50, 74), new Point(18, 14), dpi);
        DrawText(dc, _subtitle, 10.8, body, Color.FromRgb(103, 120, 141), new Point(18, 39), dpi);

        if (_vectors.Count == 0)
        {
            DrawText(dc, "No valid phasor at the analysis reference.", 12, body,
                Color.FromRgb(126, 139, 156), new Point(24, 82), dpi);
            return;
        }

        var legendWidth = Math.Clamp(bounds.Width * 0.29, 210, 300);
        var plotArea = new Rect(18, 65, Math.Max(120, bounds.Width - legendWidth - 48), Math.Max(120, bounds.Height - 88));
        var radius = Math.Max(45, Math.Min(plotArea.Width, plotArea.Height) * 0.43);
        var center = new Point(plotArea.Left + plotArea.Width * 0.5, plotArea.Top + plotArea.Height * 0.5);
        DrawPolarGrid(dc, center, radius, dpi, body);

        var maxMagnitude = _vectors.Max(v => Math.Abs(v.MagnitudeRms));
        if (!double.IsFinite(maxMagnitude) || maxMagnitude <= 1e-12) maxMagnitude = 1.0;
        foreach (var vector in _vectors)
            DrawVector(dc, center, radius, maxMagnitude, vector, dpi, semibold);

        DrawLegend(dc, new Point(bounds.Right - legendWidth - 14, 72), legendWidth, dpi, body, semibold);
    }

    private static void DrawPolarGrid(DrawingContext dc, Point center, double radius, double dpi, Typeface body)
    {
        var minorPen = FrozenPen(Color.FromRgb(229, 234, 241), 1.0);
        var axisPen = FrozenPen(Color.FromRgb(183, 195, 209), 1.05);
        for (var ring = 1; ring <= 4; ring++)
        {
            var r = radius * ring / 4.0;
            dc.DrawEllipse(null, ring == 4 ? axisPen : minorPen, center, r, r);
            DrawText(dc, $"{ring * 25}%", 8.8, body, Color.FromRgb(143, 154, 169),
                new Point(center.X + 4, center.Y - r + 2), dpi);
        }

        for (var degrees = 0; degrees < 360; degrees += 30)
        {
            var radians = degrees * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(radians) * radius, center.Y - Math.Sin(radians) * radius);
            dc.DrawLine(degrees % 90 == 0 ? axisPen : minorPen, center, end);
        }

        DrawText(dc, "0°", 9.2, body, Color.FromRgb(108, 122, 140), new Point(center.X + radius + 5, center.Y - 7), dpi);
        DrawText(dc, "+90°", 9.2, body, Color.FromRgb(108, 122, 140), new Point(center.X - 14, center.Y - radius - 17), dpi);
        DrawText(dc, "±180°", 9.2, body, Color.FromRgb(108, 122, 140), new Point(center.X - radius - 42, center.Y - 7), dpi);
        DrawText(dc, "−90°", 9.2, body, Color.FromRgb(108, 122, 140), new Point(center.X - 15, center.Y + radius + 5), dpi);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(83, 101, 122)), null, center, 2.5, 2.5);
    }

    private static void DrawVector(DrawingContext dc, Point center, double radius, double maxMagnitude,
        ComtradePhasorVector vector, double dpi, Typeface typeface)
    {
        var color = PhaseColor(vector.Phase);
        var fraction = Math.Clamp(Math.Abs(vector.MagnitudeRms) / maxMagnitude, 0.0, 1.0);
        var length = radius * fraction;
        var radians = vector.AngleDegrees * Math.PI / 180.0;
        var end = new Point(center.X + Math.Cos(radians) * length, center.Y - Math.Sin(radians) * length);
        var pen = FrozenPen(color, 2.0);
        dc.DrawLine(pen, center, end);

        if (length > 8)
        {
            var head = 8.0;
            var leftAngle = radians + Math.PI * 0.88;
            var rightAngle = radians - Math.PI * 0.88;
            dc.DrawLine(pen, end, new Point(end.X + Math.Cos(leftAngle) * head, end.Y - Math.Sin(leftAngle) * head));
            dc.DrawLine(pen, end, new Point(end.X + Math.Cos(rightAngle) * head, end.Y - Math.Sin(rightAngle) * head));
        }

        var labelPoint = new Point(end.X + (Math.Cos(radians) >= 0 ? 6 : -30), end.Y - 15);
        DrawText(dc, vector.Label, 9.2, typeface, color, labelPoint, dpi);
    }

    private void DrawLegend(DrawingContext dc, Point origin, double width, double dpi, Typeface body, Typeface semibold)
    {
        DrawText(dc, "RMS phasors", 11.5, semibold, Color.FromRgb(49, 70, 94), origin, dpi);
        var y = origin.Y + 25;
        foreach (var vector in _vectors.Take(10))
        {
            var color = PhaseColor(vector.Phase);
            dc.DrawEllipse(new SolidColorBrush(color), null, new Point(origin.X + 4, y + 6), 3.5, 3.5);
            DrawText(dc, vector.Label, 10.5, semibold, Color.FromRgb(48, 65, 86), new Point(origin.X + 15, y - 1), dpi);
            var unit = string.IsNullOrWhiteSpace(vector.Units) ? string.Empty : " " + vector.Units;
            DrawText(dc, $"{vector.MagnitudeRms:G6}{unit}  ∠ {vector.AngleDegrees:+0.##;-0.##;0}°",
                9.8, body, Color.FromRgb(103, 119, 139), new Point(origin.X + 15, y + 14), dpi);
            y += 43;
        }
    }

    internal static Color PhaseColor(string? phase)
    {
        var normalized = (phase ?? string.Empty).Trim().ToUpperInvariant();
        return normalized switch
        {
            "L1" or "A" => Color.FromRgb(211, 47, 47),
            "L2" or "B" => Color.FromRgb(214, 167, 0),
            "L3" or "C" => Color.FromRgb(25, 118, 210),
            "E" or "N" => Color.FromRgb(46, 139, 87),
            _ => Color.FromRgb(111, 119, 128)
        };
    }

    private static Pen FrozenPen(Color color, double thickness)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        var pen = new Pen(brush, thickness); pen.Freeze();
        return pen;
    }

    private static void DrawText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi)
    {
        var brush = new SolidColorBrush(color); brush.Freeze();
        dc.DrawText(new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush, dpi), point);
    }
}
