using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

/// <summary>
/// P1D.3 harmonics workstation: native ArdIrec remains the calculation authority; this control
/// only presents the returned spectrum with adaptive engineering density.
/// </summary>
public sealed class ComtradeHarmonicsWorkstationView : FrameworkElement
{
    private const double HeaderHeight = 62.0;
    private const double SummaryTop = 62.0;
    private const double SummaryHeight = 58.0;
    private const double DetailHeight = 64.0;

    private ComtradeHarmonicDisplaySpectrum? _spectrum;
    private string _title = "Harmonics";
    private string _subtitle = "Select an analog signal";
    private int _selectedOrder = 1;
    private Rect _barsRect;

    public ComtradeHarmonicsWorkstationView()
    {
        Cursor = Cursors.Arrow;
        ToolTip = "Click a harmonic bar to inspect RMS magnitude, percentage of fundamental and phase angle.";
    }

    internal void ShowSpectrum(string title, string subtitle, ComtradeHarmonicDisplaySpectrum spectrum)
    {
        _title = title;
        _subtitle = subtitle;
        _spectrum = spectrum;
        _selectedOrder = spectrum.DominantOrder > 1 && spectrum.Bins.Any(bin => bin.Order == spectrum.DominantOrder)
            ? spectrum.DominantOrder
            : spectrum.Bins.Count > 0 ? spectrum.Bins[0].Order : 1;
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _spectrum = null;
        _barsRect = Rect.Empty;
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 320 || bounds.Height < 240) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        DrawHeader(dc, bounds, dpi, body, semibold);

        if (_spectrum is null || _spectrum.Bins.Count == 0)
        {
            DrawText(dc, "No valid harmonic spectrum at the analysis reference.", 12, body,
                Color.FromRgb(126, 139, 156), new Point(22, 86), dpi);
            return;
        }

        DrawSummaryCards(dc, bounds, dpi, body, semibold);
        var chartTop = SummaryTop + SummaryHeight + 20;
        _barsRect = new Rect(64, chartTop, Math.Max(120, bounds.Width - 92),
            Math.Max(90, bounds.Height - chartTop - DetailHeight - 30));
        var axisMaximum = HarmonicAxisMaximum(_spectrum.Bins);
        DrawGrid(dc, _barsRect, axisMaximum, dpi, body, semibold);
        DrawBars(dc, _barsRect, axisMaximum, dpi, body, semibold);
        DrawSelectedDetail(dc, new Rect(18, bounds.Bottom - DetailHeight, bounds.Width - 36, DetailHeight - 8), dpi, body, semibold);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (_spectrum is null || _spectrum.Bins.Count == 0 || !_barsRect.Contains(e.GetPosition(this))) return;
        var point = e.GetPosition(this);
        var fraction = Math.Clamp((point.X - _barsRect.Left) / _barsRect.Width, 0.0, 0.999999);
        var index = Math.Clamp((int)(fraction * _spectrum.Bins.Count), 0, _spectrum.Bins.Count - 1);
        _selectedOrder = _spectrum.Bins[index].Order;
        InvalidateVisual();
        e.Handled = true;
    }

    private void DrawHeader(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        DrawText(dc, _title, 15.5, semibold, Color.FromRgb(29, 49, 73), new Point(18, 13), dpi,
            Math.Max(160, bounds.Width - 36));
        DrawText(dc, _subtitle, 10.4, body, Color.FromRgb(103, 120, 141), new Point(18, 39), dpi,
            Math.Max(160, bounds.Width - 36));
        dc.DrawLine(FrozenPen(Color.FromRgb(235, 239, 244), 1), new Point(18, HeaderHeight - 1), new Point(bounds.Right - 18, HeaderHeight - 1));
    }

    private void DrawSummaryCards(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var units = string.IsNullOrWhiteSpace(_spectrum.Units) ? string.Empty : " " + _spectrum.Units;
        var dominant = _spectrum.DominantOrder > 1
            ? $"H{_spectrum.DominantOrder}  {_spectrum.DominantPercent:G4}%"
            : "—";
        var nyquist = _spectrum.MaximumResolvableOrder > 0 ? $"H{_spectrum.MaximumResolvableOrder}" : "—";
        var items = new[]
        {
            ("FUNDAMENTAL RMS", $"{_spectrum.FundamentalRms:G6}{units}"),
            ("THD", $"{_spectrum.ThdPercent:G4}%"),
            ("DOMINANT", dominant),
            ("NYQUIST LIMIT", nyquist)
        };

        var left = 18.0;
        var gap = 8.0;
        var available = bounds.Width - 36 - gap * (items.Length - 1);
        var width = Math.Max(120, available / items.Length);
        for (var i = 0; i < items.Length; i++)
        {
            var rect = new Rect(left + i * (width + gap), SummaryTop + 2, width, SummaryHeight - 8);
            dc.DrawRoundedRectangle(FrozenBrush(Color.FromRgb(248, 250, 253)), FrozenPen(Color.FromRgb(226, 232, 240), 1), rect, 6, 6);
            DrawText(dc, items[i].Item1, 8.5, body, Color.FromRgb(129, 143, 159), new Point(rect.Left + 10, rect.Top + 7), dpi, rect.Width - 20);
            DrawText(dc, items[i].Item2, 11.2, semibold, Color.FromRgb(45, 65, 89), new Point(rect.Left + 10, rect.Top + 25), dpi, rect.Width - 20);
        }
    }

    private static double HarmonicAxisMaximum(IReadOnlyList<ComtradeHarmonicDisplayBin> bins)
    {
        var measured = bins.Count == 0 ? 100.0 : bins.Max(bin =>
            double.IsFinite(bin.PercentOfFundamental) ? Math.Max(0.0, bin.PercentOfFundamental) : 0.0);
        measured = Math.Max(100.0, measured);
        var roughStep = measured / 4.0;
        var exponent = Math.Pow(10.0, Math.Floor(Math.Log10(Math.Max(roughStep, 1e-9))));
        var normalized = roughStep / exponent;
        var step = normalized <= 1.0 ? 1.0 : normalized <= 2.0 ? 2.0 : normalized <= 5.0 ? 5.0 : 10.0;
        step *= exponent;
        return Math.Ceiling(measured / step) * step;
    }

    private static void DrawGrid(DrawingContext dc, Rect plot, double axisMaximum, double dpi, Typeface body, Typeface semibold)
    {
        var gridPen = FrozenPen(Color.FromRgb(233, 237, 242), 1);
        var borderPen = FrozenPen(Color.FromRgb(193, 203, 215), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = axisMaximum * i / 4.0;
            DrawRightAlignedText(dc, $"{value:G4}%", 8.5, body, Color.FromRgb(128, 141, 157), new Point(plot.Left - 8, y - 6), dpi);
        }

        if (axisMaximum >= 5.0)
        {
            var y5 = plot.Bottom - plot.Height * 5.0 / axisMaximum;
            dc.DrawLine(FrozenDashedPen(Color.FromRgb(203, 168, 91), 1), new Point(plot.Left, y5), new Point(plot.Right, y5));
            DrawText(dc, "5%", 8.0, semibold, Color.FromRgb(160, 126, 58), new Point(plot.Right - 25, y5 - 13), dpi);
        }
        dc.DrawRectangle(null, borderPen, plot);
    }

    private void DrawBars(DrawingContext dc, Rect plot, double axisMaximum, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var bins = _spectrum.Bins;
        var slot = plot.Width / Math.Max(1, bins.Count);
        var barWidth = Math.Clamp(slot * 0.62, 3.0, 24.0);
        var labelStride = ComtradeInteractionPerformanceMath.LabelStride(bins.Count, plot.Width, 42.0);

        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            var percent = double.IsFinite(bin.PercentOfFundamental) ? Math.Max(0, bin.PercentOfFundamental) : 0;
            var height = Math.Clamp(percent / axisMaximum, 0, 1) * plot.Height;
            var x = plot.Left + slot * (i + 0.5) - barWidth * 0.5;
            var selected = bin.Order == _selectedOrder;
            var dominant = _spectrum.DominantOrder > 1 && bin.Order == _spectrum.DominantOrder;
            var color = bin.Order == 1
                ? Color.FromRgb(42, 120, 223)
                : selected ? Color.FromRgb(217, 121, 41)
                : dominant ? Color.FromRgb(76, 139, 197)
                : Color.FromRgb(134, 172, 210);
            dc.DrawRoundedRectangle(FrozenBrush(color), null,
                new Rect(x, plot.Bottom - Math.Max(1.0, height), barWidth, Math.Max(1.0, height)), 2, 2);

            if (selected)
                dc.DrawRectangle(null, FrozenPen(Color.FromRgb(194, 97, 24), 1.2), new Rect(x - 2, plot.Top + 2, barWidth + 4, plot.Height - 4));

            var showLabel = bin.Order == 1 || selected || dominant || i % labelStride == 0;
            if (!showLabel) continue;
            var label = $"H{bin.Order}";
            DrawCenteredText(dc, label, selected ? 8.8 : 8.4, selected ? semibold : body,
                selected ? Color.FromRgb(181, 91, 28) : Color.FromRgb(103, 116, 132),
                new Point(x + barWidth * 0.5, plot.Bottom + 5), dpi);
        }
    }

    private void DrawSelectedDetail(DrawingContext dc, Rect rect, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var selected = _spectrum.Bins.FirstOrDefault(bin => bin.Order == _selectedOrder) ?? _spectrum.Bins[0];
        var unit = string.IsNullOrWhiteSpace(_spectrum.Units) ? string.Empty : " " + _spectrum.Units;
        dc.DrawRoundedRectangle(FrozenBrush(Color.FromRgb(248, 250, 253)), FrozenPen(Color.FromRgb(222, 230, 239), 1), rect, 6, 6);
        DrawText(dc, $"H{selected.Order}", 12, semibold, Color.FromRgb(44, 64, 87), new Point(rect.Left + 12, rect.Top + 8), dpi);
        DrawText(dc, $"{selected.MagnitudeRms:G6}{unit} RMS", 10.2, semibold, Color.FromRgb(70, 91, 116), new Point(rect.Left + 62, rect.Top + 8), dpi);
        DrawText(dc, $"{selected.PercentOfFundamental:G5}% of fundamental", 9.5, body, Color.FromRgb(102, 119, 139), new Point(rect.Left + 210, rect.Top + 9), dpi);
        DrawText(dc, $"Phase  ∠ {selected.AngleDegrees:+0.##;-0.##;0}°", 9.4, body, Color.FromRgb(111, 126, 145), new Point(rect.Left + 62, rect.Top + 29), dpi);
        if (_spectrum.EstimatedSampleRateHz > 0)
            DrawText(dc, $"Sample rate {_spectrum.EstimatedSampleRateHz:G6} Hz", 9.1, body, Color.FromRgb(130, 142, 157), new Point(rect.Right - 180, rect.Top + 29), dpi, 168);
    }

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

    private static FormattedText MakeText(string text, double size, Typeface typeface, Color color, double dpi, double maxWidth = 10000)
    {
        return new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, FrozenBrush(color), dpi)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            Trimming = TextTrimming.CharacterEllipsis
        };
    }

    private static void DrawText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi, double maxWidth = 10000)
        => dc.DrawText(MakeText(text, size, typeface, color, dpi, maxWidth), point);

    private static void DrawCenteredText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi)
    {
        var formatted = MakeText(text, size, typeface, color, dpi);
        formatted.TextAlignment = TextAlignment.Center;
        dc.DrawText(formatted, point);
    }

    private static void DrawRightAlignedText(DrawingContext dc, string text, double size, Typeface typeface, Color color, Point point, double dpi)
    {
        var formatted = MakeText(text, size, typeface, color, dpi);
        formatted.TextAlignment = TextAlignment.Right;
        dc.DrawText(formatted, point);
    }
}
