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
    private static readonly Typeface BodyTypeface = new("Segoe UI");
    private static readonly Typeface SemiboldTypeface = new("Segoe UI Semibold");
    private static readonly Brush HeaderBackgroundBrush = FreezeBrush(Color.FromRgb(248, 250, 253));
    private static readonly Brush PanelBackgroundBrush = FreezeBrush(Color.FromRgb(252, 253, 255));
    private static readonly Brush HeaderTextBrush = FreezeBrush(Color.FromRgb(37, 56, 79));
    private static readonly Brush HeaderDetailBrush = FreezeBrush(Color.FromRgb(102, 119, 139));
    private static readonly Brush EmptyTextBrush = FreezeBrush(Color.FromRgb(126, 139, 156));
    private static readonly Brush PanelTitleBrush = FreezeBrush(Color.FromRgb(58, 71, 84));
    private static readonly Brush PanelSummaryBrush = FreezeBrush(Color.FromRgb(119, 132, 149));
    private static readonly Brush GridLabelBrush = FreezeBrush(Color.FromRgb(119, 132, 149));
    private static readonly Brush CenterBrush = FreezeBrush(Color.FromRgb(80, 95, 112));
    private static readonly Brush LegendLabelBrush = FreezeBrush(Color.FromRgb(65, 78, 94));
    private static readonly Brush LegendValueBrush = FreezeBrush(Color.FromRgb(103, 117, 135));
    private static readonly Brush ScaleBrush = FreezeBrush(Color.FromRgb(135, 147, 162));
    private static readonly Pen HeaderDividerPen = FreezePen(Color.FromRgb(225, 232, 241), 1);
    private static readonly Pen PanelBorderPen = FreezePen(Color.FromRgb(205, 216, 229), 1);
    private static readonly Pen MinorGridPen = FreezePen(Color.FromRgb(233, 237, 242), 0.9);
    private static readonly Pen AxisGridPen = FreezePen(Color.FromRgb(181, 190, 200), 1.0);

    private PreparedPhasorPanel _voltagePanel = PreparedPhasorPanel.Empty;
    private PreparedPhasorPanel _currentPanel = PreparedPhasorPanel.Empty;
    private string _headerLabel = "Fundamental phasors at C1";
    private string _referenceDetail = "Select a valid analysis reference";
    private string _message = string.Empty;

    internal void ShowPhasors(
        string referenceLabel,
        string referenceDetail,
        IReadOnlyList<ComtradePhasorVector> voltageVectors,
        IReadOnlyList<ComtradePhasorVector> currentVectors)
    {
        var resolvedReference = string.IsNullOrWhiteSpace(referenceLabel) ? "Reference" : referenceLabel;
        _headerLabel = $"Fundamental phasors at {resolvedReference}";
        _referenceDetail = referenceDetail ?? string.Empty;
        _voltagePanel = PreparePanel(voltageVectors);
        _currentPanel = PreparePanel(currentVectors);
        _message = string.Empty;
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        var resolvedTitle = string.IsNullOrWhiteSpace(title) ? "Phasor" : title;
        _headerLabel = $"Fundamental phasors at {resolvedTitle}";
        _referenceDetail = message ?? string.Empty;
        _voltagePanel = PreparedPhasorPanel.Empty;
        _currentPanel = PreparedPhasorPanel.Empty;
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
        var header = new Rect(0, 0, bounds.Width, 58);
        dc.DrawRectangle(HeaderBackgroundBrush, null, header);
        dc.DrawLine(HeaderDividerPen, new Point(0, header.Bottom), new Point(bounds.Right, header.Bottom));

        DrawText(dc, _headerLabel, 14.5, SemiboldTypeface,
            HeaderTextBrush, new Point(16, 10), dpi);
        DrawText(dc, _referenceDetail, 10.2, BodyTypeface,
            HeaderDetailBrush, new Point(16, 34), dpi, Math.Max(100, bounds.Width - 32));

        if (_voltagePanel.Vectors.Length == 0 && _currentPanel.Vectors.Length == 0)
        {
            DrawText(dc,
                string.IsNullOrWhiteSpace(_message) ? "No valid voltage or current phasors at this reference." : _message,
                12, BodyTypeface, EmptyTextBrush, new Point(24, 90), dpi,
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
                "VOLTAGE PHASORS", _voltagePanel, dpi);
            DrawPanel(dc, new Rect(content.Left + panelWidth + gap, content.Top, content.Width - panelWidth - gap, content.Height),
                "CURRENT PHASORS", _currentPanel, dpi);
        }
        else
        {
            var panelHeight = Math.Max(190, (content.Height - gap) / 2.0);
            DrawPanel(dc, new Rect(content.Left, content.Top, content.Width, panelHeight),
                "VOLTAGE PHASORS", _voltagePanel, dpi);
            DrawPanel(dc, new Rect(content.Left, content.Top + panelHeight + gap, content.Width, content.Height - panelHeight - gap),
                "CURRENT PHASORS", _currentPanel, dpi);
        }
    }

    private static PreparedPhasorPanel PreparePanel(IReadOnlyList<ComtradePhasorVector>? source)
    {
        if (source is null || source.Count == 0)
            return PreparedPhasorPanel.Empty;

        var validCount = 0;
        for (var index = 0; index < source.Count; index++)
        {
            var vector = source[index];
            if (double.IsFinite(vector.MagnitudeRms) && vector.MagnitudeRms >= 0 && double.IsFinite(vector.AngleDegrees))
                validCount++;
        }
        if (validCount == 0)
            return PreparedPhasorPanel.Empty;

        var vectors = new PreparedPhasorVector[validCount];
        var write = 0;
        var maximumMagnitude = 0.0;
        string? singleUnit = null;
        var mixedUnits = false;

        for (var index = 0; index < source.Count; index++)
        {
            var vector = source[index];
            if (!double.IsFinite(vector.MagnitudeRms) || vector.MagnitudeRms < 0 || !double.IsFinite(vector.AngleDegrees))
                continue;

            var units = vector.Units?.Trim() ?? string.Empty;
            if (units.Length > 0)
            {
                if (singleUnit is null)
                    singleUnit = units;
                else if (!singleUnit.Equals(units, StringComparison.OrdinalIgnoreCase))
                    mixedUnits = true;
            }

            maximumMagnitude = Math.Max(maximumMagnitude, Math.Abs(vector.MagnitudeRms));
            var color = PhaseColor(vector.Phase);
            var brush = FreezeBrush(color);
            var pen = FreezePen(brush, vector.Phase is "E" or "N" ? 1.5 : 2.0);
            var unitSuffix = units.Length == 0 ? string.Empty : " " + units;
            vectors[write++] = new PreparedPhasorVector(
                vector.Label ?? string.Empty,
                vector.Phase ?? string.Empty,
                vector.MagnitudeRms,
                vector.AngleDegrees,
                brush,
                pen,
                $"{vector.MagnitudeRms:G6}{unitSuffix}  ∠{vector.AngleDegrees:+0.##;-0.##;0}°");
        }

        if (!double.IsFinite(maximumMagnitude) || maximumMagnitude <= 1e-12)
            maximumMagnitude = 1.0;

        var unitSummary = mixedUnits ? "mixed units" : singleUnit ?? string.Empty;
        var countLabel = $"{validCount} vector{(validCount == 1 ? string.Empty : "s")}" +
                         (unitSummary.Length == 0 ? string.Empty : $" • {unitSummary}");
        var scaleUnit = !mixedUnits && singleUnit is { Length: > 0 } ? $" {singleUnit}" : string.Empty;
        return new PreparedPhasorPanel(
            vectors,
            maximumMagnitude,
            countLabel,
            $"100% = {maximumMagnitude:G6}{scaleUnit}");
    }

    private static void DrawPanel(
        DrawingContext dc,
        Rect panel,
        string title,
        PreparedPhasorPanel prepared,
        double dpi)
    {
        if (panel.Width <= 1 || panel.Height <= 1) return;
        dc.DrawRoundedRectangle(PanelBackgroundBrush, PanelBorderPen, panel, 5, 5);
        DrawText(dc, title, 10.2, SemiboldTypeface, PanelTitleBrush, new Point(panel.Left + 11, panel.Top + 8), dpi);

        var vectors = prepared.Vectors;
        if (vectors.Length == 0)
        {
            DrawText(dc, "No mapped channels with a valid one-cycle phasor.", 10.5, BodyTypeface,
                EmptyTextBrush, new Point(panel.Left + 14, panel.Top + 43), dpi,
                Math.Max(80, panel.Width - 28));
            return;
        }

        DrawText(dc, prepared.CountLabel, 8.8, BodyTypeface, PanelSummaryBrush,
            new Point(panel.Left + 11, panel.Top + 26), dpi, Math.Max(60, panel.Width - 22));

        var legendRows = Math.Min(4, vectors.Length);
        var legendHeight = Math.Clamp(legendRows * 25.0 + 12.0, 42.0, 112.0);
        var plotTop = panel.Top + 47;
        var plotBottom = panel.Bottom - legendHeight - 4;
        var plot = new Rect(panel.Left + 9, plotTop, Math.Max(60, panel.Width - 18), Math.Max(70, plotBottom - plotTop));
        var radius = Math.Max(30, Math.Min(plot.Width, plot.Height) * 0.39);
        var center = new Point(plot.Left + plot.Width * 0.5, plot.Top + plot.Height * 0.5);
        DrawPolarGrid(dc, center, radius, dpi);

        for (var index = 0; index < vectors.Length; index++)
            DrawVector(dc, center, radius, prepared.MaximumMagnitude, vectors[index], dpi);

        DrawText(dc, prepared.ScaleLabel, 8.1, BodyTypeface, ScaleBrush,
            new Point(plot.Left + 5, plot.Bottom - 15), dpi);
        DrawCompactLegend(dc,
            new Rect(panel.Left + 10, panel.Bottom - legendHeight + 2, panel.Width - 20, legendHeight - 7),
            vectors, dpi);
    }

    private static void DrawPolarGrid(DrawingContext dc, Point center, double radius, double dpi)
    {
        for (var ring = 1; ring <= 4; ring++)
        {
            var r = radius * ring / 4.0;
            dc.DrawEllipse(null, ring == 4 ? AxisGridPen : MinorGridPen, center, r, r);
        }

        for (var degrees = 0; degrees < 360; degrees += 30)
        {
            var radians = degrees * Math.PI / 180.0;
            var end = new Point(center.X + Math.Cos(radians) * radius, center.Y - Math.Sin(radians) * radius);
            dc.DrawLine(degrees % 90 == 0 ? AxisGridPen : MinorGridPen, center, end);
        }

        DrawText(dc, "0°", 7.8, BodyTypeface, GridLabelBrush, new Point(center.X + radius + 3, center.Y - 6), dpi);
        DrawText(dc, "+90°", 7.8, BodyTypeface, GridLabelBrush, new Point(center.X - 13, center.Y - radius - 14), dpi);
        DrawText(dc, "±180°", 7.8, BodyTypeface, GridLabelBrush, new Point(center.X - radius - 34, center.Y - 6), dpi);
        DrawText(dc, "−90°", 7.8, BodyTypeface, GridLabelBrush, new Point(center.X - 13, center.Y + radius + 3), dpi);
        dc.DrawEllipse(CenterBrush, null, center, 2.2, 2.2);
    }

    private static void DrawVector(
        DrawingContext dc,
        Point center,
        double radius,
        double maxMagnitude,
        PreparedPhasorVector vector,
        double dpi)
    {
        var fraction = Math.Clamp(Math.Abs(vector.MagnitudeRms) / maxMagnitude, 0.0, 1.0);
        var length = radius * fraction;
        var radians = vector.AngleDegrees * Math.PI / 180.0;
        var end = new Point(center.X + Math.Cos(radians) * length, center.Y - Math.Sin(radians) * length);
        dc.DrawLine(vector.Pen, center, end);

        if (length > 8)
        {
            const double head = 7.5;
            var leftAngle = radians + Math.PI * 0.86;
            var rightAngle = radians - Math.PI * 0.86;
            var left = new Point(end.X + Math.Cos(leftAngle) * head, end.Y - Math.Sin(leftAngle) * head);
            var right = new Point(end.X + Math.Cos(rightAngle) * head, end.Y - Math.Sin(rightAngle) * head);
            dc.DrawLine(vector.Pen, end, left);
            dc.DrawLine(vector.Pen, end, right);
        }

        if (length > radius * 0.22)
        {
            var labelPoint = new Point(end.X + (Math.Cos(radians) >= 0 ? 5 : -28), end.Y - 13);
            DrawText(dc, vector.Phase, 8.7, SemiboldTypeface, vector.Brush, labelPoint, dpi, 32);
        }
    }

    private static void DrawCompactLegend(
        DrawingContext dc,
        Rect area,
        PreparedPhasorVector[] vectors,
        double dpi)
    {
        var columns = area.Width >= 420 ? 2 : 1;
        var rows = (int)Math.Ceiling(vectors.Length / (double)columns);
        rows = Math.Max(1, rows);
        var columnWidth = area.Width / columns;
        var rowHeight = Math.Max(21, area.Height / rows);
        for (var index = 0; index < vectors.Length; index++)
        {
            var column = index / rows;
            var row = index % rows;
            if (column >= columns) break;
            var vector = vectors[index];
            var x = area.Left + column * columnWidth;
            var y = area.Top + row * rowHeight;
            dc.DrawLine(vector.Pen, new Point(x, y + 8), new Point(x + 12, y + 8));
            DrawText(dc, vector.Label, 8.8, SemiboldTypeface, LegendLabelBrush,
                new Point(x + 17, y), dpi, Math.Max(40, columnWidth * 0.42));
            DrawText(dc, vector.ValueLabel,
                8.3, BodyTypeface, LegendValueBrush,
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

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Color color, double thickness)
        => FreezePen(FreezeBrush(color), thickness);

    private static Pen FreezePen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness);
        pen.Freeze();
        return pen;
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Brush brush,
        Point point,
        double dpi,
        double maxWidth = double.PositiveInfinity)
    {
        var formatted = new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, brush, dpi)
        {
            MaxTextWidth = double.IsFinite(maxWidth) ? Math.Max(1, maxWidth) : 10000,
            Trimming = TextTrimming.CharacterEllipsis
        };
        dc.DrawText(formatted, point);
    }

    private readonly record struct PreparedPhasorVector(
        string Label,
        string Phase,
        double MagnitudeRms,
        double AngleDegrees,
        Brush Brush,
        Pen Pen,
        string ValueLabel);

    private readonly record struct PreparedPhasorPanel(
        PreparedPhasorVector[] Vectors,
        double MaximumMagnitude,
        string CountLabel,
        string ScaleLabel)
    {
        internal static PreparedPhasorPanel Empty { get; } = new(
            Array.Empty<PreparedPhasorVector>(),
            1.0,
            string.Empty,
            string.Empty);
    }
}
