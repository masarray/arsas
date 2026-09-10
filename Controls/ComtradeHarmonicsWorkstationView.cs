using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Controls;

internal sealed record ComtradeHarmonicOverviewSpectrum(
    string SignalName,
    string Units,
    double DcComponent,
    double FundamentalRms,
    double ThdPercent,
    int DominantOrder,
    double DominantRms,
    double DominantPercent,
    double EstimatedSampleRateHz,
    int MaximumResolvableOrder,
    IReadOnlyList<ComtradeHarmonicDisplayBin> Bins);

/// <summary>
/// P1D.4 engineering harmonic comparison view. ArdIrec remains the calculation authority; this
/// control only renders compact, aligned small multiples so several checked analog channels can be
/// compared at the same H cursor without switching channel-by-channel.
/// </summary>
public sealed class ComtradeHarmonicsWorkstationView : FrameworkElement
{
    private const double HeaderHeight = 46.0;
    private const double FooterHeight = 28.0;
    private const double LabelWidth = 82.0;
    private const double RightMargin = 12.0;
    private const double RowGap = 5.0;
    private const double MinimumRowHeight = 76.0;
    private const double MaximumRowHeight = 132.0;
    private const int MaximumDisplayedOrder = 10;

    private IReadOnlyList<ComtradeHarmonicOverviewSpectrum> _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private string _title = "Harmonics";
    private string _subtitle = "Select analog signals";
    private int _selectedOrder = 1;
    private readonly List<RowHitTarget> _rowTargets = new();

    public ComtradeHarmonicsWorkstationView()
    {
        Cursor = Cursors.Arrow;
        ToolTip = "Click any harmonic order to compare the same order across all visible analog channels.";
    }

    internal void ShowSpectrum(string title, string subtitle, ComtradeHarmonicDisplaySpectrum spectrum)
    {
        ShowSpectra(title, subtitle, new[]
        {
            new ComtradeHarmonicOverviewSpectrum(
                spectrum.SignalName,
                spectrum.Units,
                0.0,
                spectrum.FundamentalRms,
                spectrum.ThdPercent,
                spectrum.DominantOrder,
                spectrum.DominantRms,
                spectrum.DominantPercent,
                spectrum.EstimatedSampleRateHz,
                spectrum.MaximumResolvableOrder,
                spectrum.Bins)
        });
    }

    internal void ShowSpectra(
        string title,
        string subtitle,
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> spectra)
    {
        _title = title;
        _subtitle = subtitle;
        _spectra = spectra ?? Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        var maximum = ResolveMaximumDisplayedOrder(_spectra);
        _selectedOrder = Math.Clamp(_selectedOrder, 0, Math.Max(0, maximum));
        _rowTargets.Clear();
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title;
        _subtitle = message;
        _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _rowTargets.Clear();
        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);
        var bounds = new Rect(0, 0, Math.Max(0, ActualWidth), Math.Max(0, ActualHeight));
        dc.DrawRectangle(Brushes.White, null, bounds);
        if (bounds.Width < 420 || bounds.Height < 240) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var body = new Typeface("Segoe UI");
        var semibold = new Typeface("Segoe UI Semibold");
        DrawHeader(dc, bounds, dpi, body, semibold);

        _rowTargets.Clear();
        if (_spectra.Count == 0)
        {
            DrawText(dc, "No valid harmonic spectrum at the H reference.", 12, body,
                Color.FromRgb(126, 139, 156), new Point(22, 78), dpi);
            return;
        }

        var maximumOrder = ResolveMaximumDisplayedOrder(_spectra);
        if (maximumOrder < 0) return;

        var availableHeight = Math.Max(80.0, bounds.Height - HeaderHeight - FooterHeight - 8.0);
        var rawRowHeight = (availableHeight - RowGap * Math.Max(0, _spectra.Count - 1)) / Math.Max(1, _spectra.Count);
        var rowHeight = Math.Clamp(rawRowHeight, MinimumRowHeight, MaximumRowHeight);
        var rowsHeight = rowHeight * _spectra.Count + RowGap * Math.Max(0, _spectra.Count - 1);
        var top = HeaderHeight + Math.Max(4.0, (availableHeight - rowsHeight) * 0.04);

        for (var index = 0; index < _spectra.Count; index++)
        {
            var row = new Rect(0, top + index * (rowHeight + RowGap), bounds.Width, rowHeight);
            DrawSpectrumRow(dc, row, _spectra[index], index, maximumOrder, dpi, body, semibold);
        }

        DrawFooter(dc, bounds, dpi, body, semibold);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        foreach (var target in _rowTargets)
        {
            if (!target.Plot.Contains(point)) continue;
            var slotCount = target.MaximumOrder + 1;
            if (slotCount <= 0) return;
            var fraction = Math.Clamp((point.X - target.Plot.Left) / target.Plot.Width, 0.0, 0.999999);
            _selectedOrder = Math.Clamp((int)(fraction * slotCount), 0, target.MaximumOrder);
            InvalidateVisual();
            e.Handled = true;
            return;
        }
    }

    private void DrawHeader(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        DrawText(dc, _title, 14.2, semibold, Color.FromRgb(29, 49, 73), new Point(16, 8), dpi,
            Math.Max(160, bounds.Width - 32));
        DrawText(dc, _subtitle, 9.4, body, Color.FromRgb(103, 120, 141), new Point(16, 28), dpi,
            Math.Max(160, bounds.Width - 32));
        dc.DrawLine(FrozenPen(Color.FromRgb(229, 235, 242), 1),
            new Point(12, HeaderHeight - 1), new Point(bounds.Right - 12, HeaderHeight - 1));
    }

    private void DrawSpectrumRow(
        DrawingContext dc,
        Rect row,
        ComtradeHarmonicOverviewSpectrum spectrum,
        int rowIndex,
        int maximumOrder,
        double dpi,
        Typeface body,
        Typeface semibold)
    {
        var rowBackground = rowIndex % 2 == 0 ? Color.FromRgb(253, 254, 255) : Color.FromRgb(250, 252, 255);
        dc.DrawRectangle(FrozenBrush(rowBackground), null, row);
        dc.DrawLine(FrozenPen(Color.FromRgb(229, 234, 241), 1),
            new Point(0, row.Bottom), new Point(row.Right, row.Bottom));

        var signalColor = SignalColor(spectrum.SignalName);
        dc.DrawRoundedRectangle(FrozenBrush(signalColor), null,
            new Rect(9, row.Top + 10, 4, Math.Max(18, row.Height - 24)), 2, 2);

        var unitSuffix = string.IsNullOrWhiteSpace(spectrum.Units) ? string.Empty : $"/{spectrum.Units}";
        DrawText(dc, $"{spectrum.SignalName}{unitSuffix}", 9.5, semibold,
            Color.FromRgb(39, 57, 79), new Point(18, row.Top + 5), dpi, LabelWidth - 22);
        DrawText(dc, $"THD {spectrum.ThdPercent:G4}%", 7.7, body,
            Color.FromRgb(119, 133, 151), new Point(18, row.Top + 22), dpi, LabelWidth - 22);

        var plot = new Rect(
            LabelWidth,
            row.Top + 8,
            Math.Max(120, row.Width - LabelWidth - RightMargin),
            Math.Max(38, row.Height - 27));
        _rowTargets.Add(new RowHitTarget(plot, maximumOrder));

        var bins = BuildPlotBins(spectrum, maximumOrder);
        var axisMaximum = ComtradeHarmonicsOverviewMath.NiceMagnitudeAxisMaximum(
            bins.Select(bin => bin.MagnitudeRms));
        DrawMagnitudeGrid(dc, plot, axisMaximum, spectrum.Units, dpi, body);

        var slot = plot.Width / Math.Max(1, maximumOrder + 1);
        var barWidth = Math.Clamp(slot * 0.64, 4.0, 38.0);
        for (var order = 0; order <= maximumOrder; order++)
        {
            var bin = bins[order];
            var centerX = plot.Left + slot * (order + 0.5);
            var selected = order == _selectedOrder;
            var height = Math.Clamp(bin.MagnitudeRms / Math.Max(axisMaximum, 1e-12), 0.0, 1.0) * plot.Height;
            var barRect = new Rect(
                centerX - barWidth * 0.5,
                plot.Bottom - Math.Max(1.0, height),
                barWidth,
                Math.Max(1.0, height));

            var fill = order == 1
                ? signalColor
                : WithAlpha(signalColor, 190);
            dc.DrawRectangle(FrozenBrush(fill), null, barRect);

            if (selected)
            {
                var highlight = new Rect(
                    centerX - slot * 0.46,
                    plot.Top,
                    slot * 0.92,
                    plot.Height);
                dc.DrawRectangle(FrozenBrush(Color.FromArgb(20, 230, 123, 32)),
                    FrozenPen(Color.FromRgb(220, 111, 29), 1), highlight);
            }

            DrawCenteredText(dc, order.ToString(CultureInfo.InvariantCulture), 7.7,
                selected ? semibold : body,
                selected ? Color.FromRgb(189, 88, 22) : Color.FromRgb(95, 110, 129),
                new Point(centerX, plot.Bottom + 3), dpi);

            if (bin.MagnitudeRms <= 0 && order != 1) continue;
            var labelY = Math.Max(plot.Top + 1, barRect.Top - 23);
            DrawCenteredText(dc, $"{bin.PercentOfFundamental:0.#}%", 7.1, semibold,
                Color.FromRgb(55, 70, 88), new Point(centerX, labelY), dpi);
            DrawCenteredText(dc, FormatEngineering(bin.MagnitudeRms), 6.9, body,
                Color.FromRgb(79, 94, 113), new Point(centerX, labelY + 10), dpi);
        }
    }

    private void DrawMagnitudeGrid(
        DrawingContext dc,
        Rect plot,
        double axisMaximum,
        string units,
        double dpi,
        Typeface body)
    {
        var gridPen = FrozenPen(Color.FromRgb(226, 232, 239), 1);
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = plot.Bottom - plot.Height * tick / 4.0;
            dc.DrawLine(gridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (tick is not (0 or 4)) continue;
            var value = axisMaximum * tick / 4.0;
            var suffix = tick == 4 && !string.IsNullOrWhiteSpace(units) ? $" {units}" : string.Empty;
            DrawRightAlignedText(dc, $"{FormatEngineering(value)}{suffix}", 6.8, body,
                Color.FromRgb(135, 147, 163), new Point(plot.Left - 4, y - 5), dpi);
        }
        dc.DrawRectangle(null, FrozenPen(Color.FromRgb(190, 201, 214), 1), plot);
    }

    private void DrawFooter(DrawingContext dc, Rect bounds, double dpi, Typeface body, Typeface semibold)
    {
        var y = bounds.Bottom - FooterHeight + 5;
        dc.DrawLine(FrozenPen(Color.FromRgb(233, 237, 243), 1),
            new Point(12, bounds.Bottom - FooterHeight), new Point(bounds.Right - 12, bounds.Bottom - FooterHeight));
        DrawText(dc, $"Selected H{_selectedOrder}", 8.7, semibold, Color.FromRgb(62, 82, 106),
            new Point(16, y), dpi);
        DrawText(dc, "click any order to compare the same harmonic across all channels", 8.2, body,
            Color.FromRgb(124, 137, 153), new Point(92, y), dpi, Math.Max(120, bounds.Width - 210));

        if (_spectra.FirstOrDefault() is { EstimatedSampleRateHz: > 0 } first)
            DrawRightAlignedText(dc, $"{first.EstimatedSampleRateHz:G6} Hz", 8.0, body,
                Color.FromRgb(130, 142, 157), new Point(bounds.Right - 16, y), dpi);
    }

    private static IReadOnlyList<PlotBin> BuildPlotBins(ComtradeHarmonicOverviewSpectrum spectrum, int maximumOrder)
    {
        var result = new PlotBin[maximumOrder + 1];
        var dcMagnitude = Math.Abs(double.IsFinite(spectrum.DcComponent) ? spectrum.DcComponent : 0.0);
        result[0] = new PlotBin(
            0,
            dcMagnitude,
            ComtradeHarmonicsOverviewMath.PercentOfFundamental(dcMagnitude, spectrum.FundamentalRms),
            0.0);

        var byOrder = spectrum.Bins
            .Where(bin => bin.Order >= 1 && bin.Order <= maximumOrder)
            .GroupBy(bin => bin.Order)
            .ToDictionary(group => group.Key, group => group.First());

        for (var order = 1; order <= maximumOrder; order++)
        {
            if (byOrder.TryGetValue(order, out var bin))
            {
                var magnitude = order == 1 && spectrum.FundamentalRms > 0
                    ? spectrum.FundamentalRms
                    : Math.Max(0.0, double.IsFinite(bin.MagnitudeRms) ? bin.MagnitudeRms : 0.0);
                var percent = order == 1
                    ? 100.0
                    : ComtradeHarmonicsOverviewMath.PercentOfFundamental(magnitude, spectrum.FundamentalRms);
                result[order] = new PlotBin(order, magnitude, percent, bin.AngleDegrees);
            }
            else
            {
                result[order] = new PlotBin(order, 0.0, order == 1 && spectrum.FundamentalRms > 0 ? 100.0 : 0.0, 0.0);
            }
        }
        return result;
    }

    private static int ResolveMaximumDisplayedOrder(IReadOnlyList<ComtradeHarmonicOverviewSpectrum> spectra)
    {
        if (spectra.Count == 0) return -1;
        var available = spectra
            .Select(spectrum =>
            {
                var binMaximum = spectrum.Bins.Count == 0 ? 0 : spectrum.Bins.Max(bin => bin.Order);
                return spectrum.MaximumResolvableOrder > 0
                    ? Math.Min(spectrum.MaximumResolvableOrder, binMaximum > 0 ? binMaximum : spectrum.MaximumResolvableOrder)
                    : binMaximum;
            })
            .DefaultIfEmpty(0)
            .Max();
        return ComtradeHarmonicsOverviewMath.ClampDisplayOrder(available, MaximumDisplayedOrder);
    }

    private static string FormatEngineering(double value)
    {
        if (!double.IsFinite(value)) return "—";
        var magnitude = Math.Abs(value);
        if (magnitude >= 1000) return value.ToString("0", CultureInfo.CurrentCulture);
        if (magnitude >= 100) return value.ToString("0.#", CultureInfo.CurrentCulture);
        if (magnitude >= 10) return value.ToString("0.##", CultureInfo.CurrentCulture);
        if (magnitude >= 1) return value.ToString("0.###", CultureInfo.CurrentCulture);
        return value.ToString("0.####", CultureInfo.CurrentCulture);
    }

    private static Color SignalColor(string signalName)
    {
        var name = (signalName ?? string.Empty).ToUpperInvariant();
        if (name.Contains("L1") || name.Contains("IA") || name.Contains("UA") || name.Contains("VA"))
            return Color.FromRgb(232, 121, 24);
        if (name.Contains("L2") || name.Contains("IB") || name.Contains("UB") || name.Contains("VB"))
            return Color.FromRgb(45, 111, 210);
        if (name.Contains("L3") || name.Contains("IC") || name.Contains("UC") || name.Contains("VC"))
            return Color.FromRgb(29, 154, 132);
        return Color.FromRgb(92, 137, 181);
    }

    private static Color WithAlpha(Color color, byte alpha)
        => Color.FromArgb(alpha, color.R, color.G, color.B);

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

    private static FormattedText MakeText(
        string text,
        double size,
        Typeface typeface,
        Color color,
        double dpi,
        double maxWidth = 10000)
    {
        return new FormattedText(text ?? string.Empty, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
            typeface, size, FrozenBrush(color), dpi)
        {
            MaxTextWidth = Math.Max(1, maxWidth),
            Trimming = TextTrimming.CharacterEllipsis
        };
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Color color,
        Point point,
        double dpi,
        double maxWidth = 10000)
        => dc.DrawText(MakeText(text, size, typeface, color, dpi, maxWidth), point);

    private static void DrawCenteredText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Color color,
        Point point,
        double dpi)
    {
        var formatted = MakeText(text, size, typeface, color, dpi);
        formatted.TextAlignment = TextAlignment.Center;
        dc.DrawText(formatted, point);
    }

    private static void DrawRightAlignedText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Color color,
        Point point,
        double dpi)
    {
        var formatted = MakeText(text, size, typeface, color, dpi);
        formatted.TextAlignment = TextAlignment.Right;
        dc.DrawText(formatted, point);
    }

    private sealed record PlotBin(int Order, double MagnitudeRms, double PercentOfFundamental, double AngleDegrees);
    private readonly record struct RowHitTarget(Rect Plot, int MaximumOrder);
}
