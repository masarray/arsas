using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ArIED61850Tester.Controls;

internal sealed record ComtradeHarmonicDisplayBin(
    int Order,
    double MagnitudeRms,
    double PercentOfFundamental,
    double AngleDegrees);

internal sealed record ComtradeHarmonicDisplaySpectrum(
    string SignalName,
    string Units,
    double FundamentalRms,
    double ThdPercent,
    int DominantOrder,
    double DominantRms,
    double DominantPercent,
    double EstimatedSampleRateHz,
    int MaximumResolvableOrder,
    IReadOnlyList<ComtradeHarmonicDisplayBin> Bins);

public sealed class ComtradeHarmonicsView : FrameworkElement
{
    private ComtradeHarmonicDisplaySpectrum? _spectrum;
    private string _title = "Harmonics";
    private string _subtitle = "Select an analog signal";
    private int _selectedOrder = 1;
    private Rect _barsRect;

    public ComtradeHarmonicsView()
    {
        Cursor = Cursors.Arrow;
        ToolTip = "Click a harmonic bar to inspect magnitude, percentage and phase angle.";
    }

    internal void ShowSpectrum(string title, string subtitle, ComtradeHarmonicDisplaySpectrum spectrum)
    {
        _title = title;
        _subtitle = subtitle;
        _spectrum = spectrum;
        _selectedOrder = spectrum.Bins.Count > 0 ? Math.Max(1, spectrum.Bins[0].Order) : 1;
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _spectrum = null;
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
        DrawText(dc, _title, 15, semibold, Color.FromRgb(31, 50, 74), new Point(18, 14), dpi);
        DrawText(dc, _subtitle, 10.8, body, Color.FromRgb(103, 120, 141), new Point(18, 39), dpi);

        if (_spectrum is null || _spectrum.Bins.Count == 0)
        {
            DrawText(dc, "No valid harmonic spectrum at the analysis reference.", 12, body,
                Color.FromRgb(126, 139, 156), new Point(24, 82), dpi);
            return;
        }

        DrawSummary(dc, bounds, dpi, body, semibold);
        var chartTop = 126.0;
        var detailHeight = 62.0;
        _barsRect = new Rect(58, chartTop, Math.Max(100, bounds.Width - 82), Math.Max(80, bounds.Height - chartTop - detailHeight - 26));
        var axisMaximum = HarmonicAxisMaximum(_spectrum.Bins);
        DrawGrid(dc, _barsRect, axisMaximum, dpi, body);
        DrawBars(dc, _barsRect, axisMaximum, dpi, body, semibold);
        DrawSelectedDetail(dc, new Rect(18, bounds.Bottom - detailHeight, bounds.Width - 36, detailHeight - 8), dpi, body, semibold);
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

    private void DrawSummary(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var units = string.IsNullOrWhiteSpace(_spectrum.Units) ? string.Empty : " " + _spectrum.Units;
        var items = new[]
        {
            ("Fundamental", $"{_spectrum.FundamentalRms:G6}{units}"),
            ("THD", $"{_spectrum.ThdPercent:G4}%"),
            ("Dominant", _spectrum.DominantOrder > 1 ? $"H{_spectrum.DominantOrder} • {_spectrum.DominantPercent:G4}%" : "—"),
            ("Sample rate", _spectrum.EstimatedSampleRateHz > 0 ? $"{_spectrum.EstimatedSampleRateHz:G6} Hz" : "—")
        };
        var available = Math.Max(400, bounds.Width - 36);
        var cellWidth = available / items.Length;
        for (var i = 0; i < items.Length; i++)
        {
            var x = 18 + i * cellWidth;
            DrawText(dc, items[i].Item1, 9.5, body, Color.FromRgb(132, 145, 162), new Point(x, 69), dpi);
            DrawText(dc, items[i].Item2, 11.3, semibold, Color.FromRgb(48, 67, 90), new Point(x, 87), dpi);
        }
        DrawText(dc, $"Nyquist limit: H{_spectrum.MaximumResolvableOrder}", 9.2, body,
            Color.FromRgb(139, 150, 164), new Point(18, 108), dpi);
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

    private static void DrawGrid(DrawingContext dc, Rect plot, double axisMaximum, double dpi, Typeface body)
    {
        var gridPen = FrozenPen(Color.FromRgb(231, 236, 242), 1);
        var borderPen = FrozenPen(Color.FromRgb(190, 200, 213), 1);
        for (var i = 0; i <= 4; i++)
        {
            var y = plot.Bottom - plot.Height * i / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            var value = axisMaximum * i / 4.0;
            var text = new FormattedText($"{value:G4}%", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                body, 8.8, new SolidColorBrush(Color.FromRgb(132, 144, 158)), dpi)
            { TextAlignment = TextAlignment.Right };
            dc.DrawText(text, new Point(plot.Left - 8, y - 6));
        }
        dc.DrawRectangle(null, borderPen, plot);
    }

    private void DrawBars(DrawingContext dc, Rect plot, double axisMaximum, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var bins = _spectrum.Bins;
        var slot = plot.Width / Math.Max(1, bins.Count);
        var barWidth = Math.Clamp(slot * 0.62, 3.0, 24.0);

        for (var i = 0; i < bins.Count; i++)
        {
            var bin = bins[i];
            var percent = double.IsFinite(bin.PercentOfFundamental) ? Math.Max(0, bin.PercentOfFundamental) : 0;
            var height = Math.Clamp(percent / axisMaximum, 0, 1) * plot.Height;
            var x = plot.Left + slot * (i + 0.5) - barWidth * 0.5;
            var selected = bin.Order == _selectedOrder;
            var color = bin.Order == 1
                ? Color.FromRgb(42, 120, 223)
                : selected ? Color.FromRgb(217, 121, 41) : Color.FromRgb(106, 155, 205);
            var brush = new SolidColorBrush(color); brush.Freeze();
            dc.DrawRoundedRectangle(brush, null, new Rect(x, plot.Bottom - height, barWidth, height), 2, 2);

            if (selected)
            {
                var pen = FrozenPen(Color.FromRgb(194, 97, 24), 1.2);
                dc.DrawRectangle(null, pen, new Rect(x - 2, plot.Top + 2, barWidth + 4, plot.Height - 4));
            }

            if (bins.Count <= 20 || bin.Order == 1 || bin.Order % 2 == 1)
            {
                var label = new FormattedText($"H{bin.Order}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    selected ? semibold : body, 8.7, new SolidColorBrush(Color.FromRgb(103, 116, 132)), dpi)
                { TextAlignment = TextAlignment.Center };
                dc.DrawText(label, new Point(x + barWidth * 0.5, plot.Bottom + 5));
            }
        }
    }

    private void DrawSelectedDetail(DrawingContext dc, Rect rect, double dpi, Typeface body, Typeface semibold)
    {
        if (_spectrum is null) return;
        var selected = _spectrum.Bins.FirstOrDefault(b => b.Order == _selectedOrder) ?? _spectrum.Bins[0];
        var unit = string.IsNullOrWhiteSpace(_spectrum.Units) ? string.Empty : " " + _spectrum.Units;
        var bg = new SolidColorBrush(Color.FromRgb(248, 250, 253)); bg.Freeze();
        var border = FrozenPen(Color.FromRgb(222, 230, 239), 1);
        dc.DrawRoundedRectangle(bg, border, rect, 6, 6);
        DrawText(dc, $"H{selected.Order}", 11.5, semibold, Color.FromRgb(46, 65, 88), new Point(rect.Left + 12, rect.Top + 7), dpi);
        DrawText(dc, $"{selected.MagnitudeRms:G6}{unit} RMS  •  {selected.PercentOfFundamental:G5}% of fundamental",
            9.9, body, Color.FromRgb(87, 105, 126), new Point(rect.Left + 58, rect.Top + 8), dpi);
        DrawText(dc, $"Phase ∠ {selected.AngleDegrees:+0.##;-0.##;0}°",
            9.6, body, Color.FromRgb(111, 126, 145), new Point(rect.Left + 58, rect.Top + 27), dpi);
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
