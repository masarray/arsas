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
    private IReadOnlyList<ComtradePhasorVector> _voltageVectors = Array.Empty<ComtradePhasorVector>();
    private IReadOnlyList<ComtradePhasorVector> _currentVectors = Array.Empty<ComtradePhasorVector>();
    private string _referenceLabel = "C1";
    private string _referenceDetail = "Select a valid analysis reference";
    private string _message = string.Empty;

    internal void ShowPhasors(
        string referenceLabel,
        string referenceDetail,
        IReadOnlyList<ComtradePhasorVector> voltageVectors,
        IReadOnlyList<ComtradePhasorVector> currentVectors)
    {
        _referenceLabel = string.IsNullOrWhiteSpace(referenceLabel) ? "Reference" : referenceLabel;
        _referenceDetail = referenceDetail ?? string.Empty;
        _voltageVectors = Filter(voltageVectors);
        _currentVectors = Filter(currentVectors);
        _message = string.Empty;
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _referenceLabel = string.IsNullOrWhiteSpace(title) ? "Phasor" : title;
        _referenceDetail = message ?? string.Empty;
        _voltageVectors = Array.Empty<ComtradePhasorVector>();
        _currentVectors = Array.Empty<ComtradePhasorVector>();
        _message = message ?? string.Empty;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 320 || bounds.Height < 260) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        var header = new Rect(0, 0, bounds.Width, 58);
        dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(248, 250, 253)), null, header);
        dc.DrawLine(FrozenPen(Color.FromRgb(225, 232, 241), 1), new Point(0, header.Bottom), new Point(bounds.Right, header.Bottom));

        DrawText(dc, $"Fundamental phasors at {_referenceLabel}", 14.5, semibold,
            Color.FromRgb(37, 56, 79), new Point(16, 10), dpi);
        DrawText(dc, _referenceDetail, 10.2, body,
            Color.FromRgb(102, 119, 139), new Point(16, 34), dpi, Math.Max(100, bounds.Width - 32));

        if (_voltageVectors.Count == 0 && _currentVectors.Count == 0)
        {
            DrawText(dc,
                string.IsNullOrWhiteSpace(_message) ? "No valid voltage or current phasors at this reference." : _message,
                12, body, Color.FromRgb(126, 139, 156), new Point(24, 90), dpi,
                Math.Max(100, bounds.Width - 48));
            return;
        }

        const double outer = 10;
        const double gap = 8;
        var content = new Rect(outer, header.Bottom + outer, bounds.Width - outer * 2, bounds.Height - header.Bottom - outer * 2);
        if (bounds.Width >= 820)
        {
            var panelWidth = Math.Max(240, (content.Width - gap) / 2.0);
            DrawPanel(dc, new Rect(content.Left, content.Top, panelWidth, content.Height),
                "VOLTAGE PHASORS", _voltageVectors, dpi, body, semibold);
            DrawPanel(dc, new Rect(content.Left + panelWidth + gap, content.Top, content.Width - panelWidth - gap, content.Height),
                "CURRENT PHASORS", _currentVectors, dpi, body, semibold);
        }
        else
        {
            var panelHeight = Math.Max(190, (content.Height - gap) / 2.0);
            DrawPanel(dc, new Rect(content.Left, content.Top, content.Width, panelHeight),
                "VOLTAGE PHASORS", _voltageVectors, dpi, body, semibold);
            DrawPanel(dc, new Rect(content.Left, content.Top + panelHeight + gap, content.Width, content.Height - panelHeight - gap),
                "CURRENT PHASORS", _currentVectors, dpi, body, semibold);
        }
    }

    private static IReadOnlyList<ComtradePhasorVector> Filter(IReadOnlyList<ComtradePhasorVector>? vectors)
        => (vectors ?? Array.Empty<ComtradePhasorVector>())
            .Where(vector => double.IsFinite(vector.MagnitudeRms) && vector.MagnitudeRms >= 0 && double.IsFinite(vector.AngleDegrees))
            .ToArray();

    private static void DrawPanel(
        DrawingContext dc,
        Rect panel,
        string title,
        IReadOnlyList<ComtradePhasorVector> vectors,
        double dpi,
        Typeface body,
        Typeface semibold)
    {
        if (panel.Width <= 1 || panel.Height <= 1) return;
        var background = new SolidColorBrush(Color.FromRgb(252, 253, 255)); background.Freeze();
        dc.DrawRoundedRectangle(background, FrozenPen(Color.FromRgb(205, 216, 229), 1), panel, 5, 5);
        DrawText(dc, title, 10.2, semibold, Color.FromRgb(58, 71, 84), new Point(panel.Left + 11, panel.Top + 8), dpi);

        if (vectors.Count == 0)
        {
            DrawText(dc, "No mapped channels with a valid one-cycle phasor.", 10.5, body,
                Color.FromRgb(126, 139, 156), new Point(panel.Left + 14, panel.Top + 43), dpi,
                Math.Max(80, panel.Width - 28));
            return;
        }

        var distinctUnits = vectors.Select(vector => vector.Units?.Trim() ?? string.Empty)
            .Where(unit => unit.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var unitSummary = distinctUnits.Length == 1 ? distinctUnits[0] : distinctUnits.Length > 1 ? "mixed units" : string.Empty;
        DrawText(dc, $"{vectors.Count} vector{(vectors.Count == 1 ? string.Empty : "s")}" +
                     (string.IsNullOrWhiteSpace(unitSummary) ? string.Empty : $" • {unitSummary}"),
            8.8, body, Color.FromRgb(119, 132, 149), new Point(panel.Left + 11, panel.Top + 26), dpi,
            Math.Max(60, panel.Width - 22));

        var legendRows = Math.Min(4, vectors.Count);
        var legendHeight = Math.Clamp(legendRows * 25.0 + 12.0, 42.0, 112.0);
        var plotTop = panel.Top + 47;
        var plotBottom = panel.Bottom - legendHeight - 4;
        var plot = new Rect(panel.Left + 9, plotTop, Math.Max(60, panel.Width - 18), Math.Max(70, plotBottom - plotTop));
        var radius = Math.Max(30, Math.Min(plot.Width, plot.Height) * 0.39);
        var center = new Point(plot.Left + plot.Width * 0.5, plot.Top + plot.Height * 0.5);
        DrawPolarGrid(dc, center, radius, dpi, body);

        var maxMagnitude = vectors.Max(vector => Math.Abs(vector.MagnitudeRms));
        if (!double.IsFinite(maxMagnitude) || maxMagnitude <= 1e-12) maxMagnitude = 1.0;
        foreach (var vector in vectors)
            DrawVector(dc, center, radius, maxMagnitude, vector, dpi, semibold);

        var maxUnit = distinctUnits.Length == 1 ? $" {distinctUnits[0]}" : string.Empty;
        DrawText(dc, $"100% = {maxMagnitude:G6}{maxUnit}", 8.1, body, Color.FromRgb(135, 147, 162),
            new Point(plot.Left + 5, plot.Bottom - 15), dpi);
        DrawCompactLegend(dc,
            new Rect(panel.Left + 10, panel.Bottom - legendHeight + 2, panel.Width - 20, legendHeight - 7),
            vectors, dpi, body, semibold);
    }

    private static void DrawPolarGrid(DrawingContext dc, Point center, double radius, double dpi, Typeface body)
    {
        var minorPen = FrozenPen(Color.FromRgb(233, 237, 242), 0.9);
        var axisPen = FrozenPen(Color.FromRgb(181, 190, 200), 1.0);
        for (var ring = 1; ring <= 4; ring++)
        {
            var r = radius * ring / 4.0;
            dc.DrawEllipse(null, ring == 4 ? axisPen : minorPen, center, r, r);
        }

        for (var degrees = 0; degrees < 360; degrees += 30)
        {
            var radians = degrees * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(radians) * radius, center.Y - Math.Sin(radians) * radius);
            dc.DrawLine(degrees % 90 == 0 ? axisPen : minorPen, center, end);
        }

        DrawText(dc, "0°", 7.8, body, Color.FromRgb(119, 132, 149), new Point(center.X + radius + 3, center.Y - 6), dpi);
        DrawText(dc, "+90°", 7.8, body, Color.FromRgb(119, 132, 149), new Point(center.X - 13, center.Y - radius - 14), dpi);
        DrawText(dc, "±180°", 7.8, body, Color.FromRgb(119, 132, 149), new Point(center.X - radius - 34, center.Y - 6), dpi);
        DrawText(dc, "−90°", 7.8, body, Color.FromRgb(119, 132, 149), new Point(center.X - 13, center.Y + radius + 3), dpi);
        dc.DrawEllipse(new SolidColorBrush(Color.FromRgb(80, 95, 112)), null, center, 2.2, 2.2);
    }

    private static void DrawVector(
        DrawingContext dc,
        Point center,
        double radius,
        double maxMagnitude,
        ComtradePhasorVector vector,
        double dpi,
        Typeface typeface)
    {
        var color = PhaseColor(vector.Phase);
        var fraction = Math.Clamp(Math.Abs(vector.MagnitudeRms) / maxMagnitude, 0.0, 1.0);
        var length = radius * fraction;
        var radians = vector.AngleDegrees * Math.PI / 180.0;
        var end = new Point(center.X + Math.Cos(radians) * length, center.Y - Math.Sin(radians) * length);
        var pen = FrozenPen(color, vector.Phase is "E" or "N" ? 1.5 : 2.0);
        dc.DrawLine(pen, center, end);

        if (length > 8)
        {
            const double head = 7.5;
            var leftAngle = radians + Math.PI * 0.86;
            var rightAngle = radians - Math.PI * 0.86;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                context.BeginFigure(end, true, true);
                context.LineTo(new Point(end.X + Math.Cos(leftAngle) * head, end.Y - Math.Sin(leftAngle) * head), true, false);
                context.LineTo(new Point(end.X + Math.Cos(rightAngle) * head, end.Y - Math.Sin(rightAngle) * head), true, false);
            }
            geometry.Freeze();
            var brush = new SolidColorBrush(color); brush.Freeze();
            dc.DrawGeometry(brush, null, geometry);
        }

        if (length > radius * 0.22)
        {
            var labelPoint = new Point(end.X + (Math.Cos(radians) >= 0 ? 5 : -28), end.Y - 13);
            DrawText(dc, vector.Phase, 8.7, typeface, color, labelPoint, dpi, 32);
        }
    }

    private static void DrawCompactLegend(
        DrawingContext dc,
        Rect area,
        IReadOnlyList<ComtradePhasorVector> vectors,
        double dpi,
        Typeface body,
        Typeface semibold)
    {
        var columns = area.Width >= 420 ? 2 : 1;
        var rows = (int)Math.Ceiling(vectors.Count / (double)columns);
        rows = Math.Max(1, rows);
        var columnWidth = area.Width / columns;
        var rowHeight = Math.Max(21, area.Height / rows);
        for (var index = 0; index < vectors.Count; index++)
        {
            var column = index / rows;
            var row = index % rows;
            if (column >= columns) break;
            var vector = vectors[index];
            var x = area.Left + column * columnWidth;
            var y = area.Top + row * rowHeight;
            var color = PhaseColor(vector.Phase);
            dc.DrawLine(FrozenPen(color, 2.0), new Point(x, y + 8), new Point(x + 12, y + 8));
            DrawText(dc, vector.Label, 8.8, semibold, Color.FromRgb(65, 78, 94),
                new Point(x + 17, y), dpi, Math.Max(40, columnWidth * 0.42));
            var unit = string.IsNullOrWhiteSpace(vector.Units) ? string.Empty : " " + vector.Units;
            DrawText(dc, $"{vector.MagnitudeRms:G6}{unit}  ∠{vector.AngleDegrees:+0.##;-0.##;0}°",
                8.3, body, Color.FromRgb(103, 117, 135),
                new Point(x + Math.Max(86, columnWidth * 0.43), y), dpi,
                Math.Max(40, columnWidth - Math.Max(86, columnWidth * 0.43) - 4));
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
        var brush = new SolidColorBrush(color); brush.Freeze();
        var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush, dpi)
        {
            MaxTextWidth = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : 10000,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted, point);
    }
}
