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
///
/// Render-path rule: spectrum normalization, harmonic lookup, engineering-value formatting and
/// brush creation happen only when ShowSpectra receives a new immutable result. OnRender consumes
/// prepared rows and only performs geometry/text drawing required for the current element size.
/// </summary>
public sealed class ComtradeHarmonicsWorkstationView : FrameworkElement
{
    private const double HeaderHeight = 46.0;
    private const double FooterHeight = 28.0;
    private const double LabelWidth = 82.0;
    private const double RightMargin = 12.0;
    private const double RowGap = 5.0;
    private const double MinimumRowHeight = 70.0;
    private const double MaximumRowHeight = 132.0;
    private const int MaximumDisplayedOrder = 10;

    private static readonly Typeface BodyTypeface = new("Segoe UI");
    private static readonly Typeface SemiboldTypeface = new("Segoe UI Semibold");
    private static readonly Brush HeadingBrush = FreezeBrush(Color.FromRgb(29, 49, 73));
    private static readonly Brush SubtitleBrush = FreezeBrush(Color.FromRgb(103, 120, 141));
    private static readonly Brush EmptyTextBrush = FreezeBrush(Color.FromRgb(126, 139, 156));
    private static readonly Brush PrimaryTextBrush = FreezeBrush(Color.FromRgb(39, 57, 79));
    private static readonly Brush SecondaryTextBrush = FreezeBrush(Color.FromRgb(119, 133, 151));
    private static readonly Brush HarmonicLabelBrush = FreezeBrush(Color.FromRgb(55, 70, 88));
    private static readonly Brush MagnitudeLabelBrush = FreezeBrush(Color.FromRgb(79, 94, 113));
    private static readonly Brush AxisTextBrush = FreezeBrush(Color.FromRgb(135, 147, 163));
    private static readonly Brush OrderTextBrush = FreezeBrush(Color.FromRgb(95, 110, 129));
    private static readonly Brush SelectedOrderBrush = FreezeBrush(Color.FromRgb(189, 88, 22));
    private static readonly Brush FooterPrimaryBrush = FreezeBrush(Color.FromRgb(62, 82, 106));
    private static readonly Brush FooterSecondaryBrush = FreezeBrush(Color.FromRgb(124, 137, 153));
    private static readonly Brush FooterRateBrush = FreezeBrush(Color.FromRgb(130, 142, 157));
    private static readonly Brush EvenRowBrush = FreezeBrush(Color.FromRgb(253, 254, 255));
    private static readonly Brush OddRowBrush = FreezeBrush(Color.FromRgb(250, 252, 255));
    private static readonly Brush SelectionFillBrush = FreezeBrush(Color.FromArgb(20, 230, 123, 32));
    private static readonly Pen HeaderDividerPen = FreezePen(Color.FromRgb(229, 235, 242), 1);
    private static readonly Pen RowDividerPen = FreezePen(Color.FromRgb(229, 234, 241), 1);
    private static readonly Pen GridPen = FreezePen(Color.FromRgb(226, 232, 239), 1);
    private static readonly Pen PlotBorderPen = FreezePen(Color.FromRgb(190, 201, 214), 1);
    private static readonly Pen SelectionPen = FreezePen(Color.FromRgb(220, 111, 29), 1);
    private static readonly Pen FooterDividerPen = FreezePen(Color.FromRgb(233, 237, 243), 1);
    private static readonly string[] OrderLabels = CreateOrderLabels();

    private IReadOnlyList<ComtradeHarmonicOverviewSpectrum> _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private PreparedSpectrumRow[] _preparedRows = Array.Empty<PreparedSpectrumRow>();
    private int _maximumDisplayedOrder = -1;
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
        _title = title ?? string.Empty;
        _subtitle = subtitle ?? string.Empty;
        _spectra = spectra ?? Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _maximumDisplayedOrder = ResolveMaximumDisplayedOrder(_spectra);
        _selectedOrder = Math.Clamp(_selectedOrder, 0, Math.Max(0, _maximumDisplayedOrder));
        _preparedRows = PrepareRows(_spectra, _maximumDisplayedOrder);
        _rowTargets.Clear();
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title ?? string.Empty;
        _subtitle = message ?? string.Empty;
        _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _preparedRows = Array.Empty<PreparedSpectrumRow>();
        _maximumDisplayedOrder = -1;
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
        DrawHeader(dc, bounds, dpi);

        _rowTargets.Clear();
        if (_preparedRows.Length == 0)
        {
            DrawText(dc, "No valid harmonic spectrum at the H reference.", 12, BodyTypeface,
                EmptyTextBrush, new Point(22, 78), dpi);
            return;
        }

        if (_maximumDisplayedOrder < 0) return;

        var availableHeight = Math.Max(80.0, bounds.Height - HeaderHeight - FooterHeight - 8.0);
        var rawRowHeight = (availableHeight - RowGap * Math.Max(0, _preparedRows.Length - 1)) /
                           Math.Max(1, _preparedRows.Length);
        var rowHeight = Math.Clamp(rawRowHeight, MinimumRowHeight, MaximumRowHeight);
        var rowsHeight = rowHeight * _preparedRows.Length + RowGap * Math.Max(0, _preparedRows.Length - 1);
        var top = HeaderHeight + Math.Max(4.0, (availableHeight - rowsHeight) * 0.04);

        for (var index = 0; index < _preparedRows.Length; index++)
        {
            var row = new Rect(0, top + index * (rowHeight + RowGap), bounds.Width, rowHeight);
            DrawSpectrumRow(dc, row, _preparedRows[index], index, _maximumDisplayedOrder, dpi);
        }

        DrawFooter(dc, bounds, dpi);
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        var point = e.GetPosition(this);
        for (var index = 0; index < _rowTargets.Count; index++)
        {
            var target = _rowTargets[index];
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

    private void DrawHeader(DrawingContext dc, Rect bounds, double dpi)
    {
        DrawText(dc, _title, 14.2, SemiboldTypeface, HeadingBrush, new Point(16, 8), dpi,
            Math.Max(160, bounds.Width - 32));
        DrawText(dc, _subtitle, 9.4, BodyTypeface, SubtitleBrush, new Point(16, 28), dpi,
            Math.Max(160, bounds.Width - 32));
        dc.DrawLine(HeaderDividerPen,
            new Point(12, HeaderHeight - 1), new Point(bounds.Right - 12, HeaderHeight - 1));
    }

    private void DrawSpectrumRow(
        DrawingContext dc,
        Rect row,
        PreparedSpectrumRow prepared,
        int rowIndex,
        int maximumOrder,
        double dpi)
    {
        dc.DrawRectangle(rowIndex % 2 == 0 ? EvenRowBrush : OddRowBrush, null, row);
        dc.DrawLine(RowDividerPen, new Point(0, row.Bottom), new Point(row.Right, row.Bottom));

        dc.DrawRoundedRectangle(prepared.SignalBrush, null,
            new Rect(9, row.Top + 10, 4, Math.Max(18, row.Height - 24)), 2, 2);

        DrawText(dc, prepared.SignalLabel, 9.5, SemiboldTypeface,
            PrimaryTextBrush, new Point(18, row.Top + 5), dpi, LabelWidth - 22);
        DrawText(dc, prepared.ThdLabel, 7.7, BodyTypeface,
            SecondaryTextBrush, new Point(18, row.Top + 22), dpi, LabelWidth - 22);

        var plot = new Rect(
            LabelWidth,
            row.Top + 8,
            Math.Max(120, row.Width - LabelWidth - RightMargin),
            Math.Max(38, row.Height - 27));
        _rowTargets.Add(new RowHitTarget(plot, maximumOrder));

        DrawMagnitudeGrid(dc, plot, prepared.AxisMaximum, prepared.AxisTopLabel, dpi);

        var slot = plot.Width / Math.Max(1, maximumOrder + 1);
        var barWidth = Math.Clamp(slot * 0.64, 4.0, 38.0);
        for (var order = 0; order <= maximumOrder; order++)
        {
            var bin = prepared.Bins[order];
            var centerX = plot.Left + slot * (order + 0.5);
            var selected = order == _selectedOrder;
            var height = Math.Clamp(bin.MagnitudeRms / Math.Max(prepared.AxisMaximum, 1e-12), 0.0, 1.0) * plot.Height;
            var barRect = new Rect(
                centerX - barWidth * 0.5,
                plot.Bottom - Math.Max(1.0, height),
                barWidth,
                Math.Max(1.0, height));

            dc.DrawRectangle(order == 1 ? prepared.SignalBrush : prepared.SecondarySignalBrush, null, barRect);

            if (selected)
            {
                var highlight = new Rect(
                    centerX - slot * 0.46,
                    plot.Top,
                    slot * 0.92,
                    plot.Height);
                dc.DrawRectangle(SelectionFillBrush, SelectionPen, highlight);
            }

            DrawCenteredText(dc, OrderLabels[order], 7.7,
                selected ? SemiboldTypeface : BodyTypeface,
                selected ? SelectedOrderBrush : OrderTextBrush,
                new Point(centerX, plot.Bottom + 3), dpi);

            if (bin.MagnitudeRms <= 0 && order != 1) continue;
            var labelY = Math.Max(plot.Top + 1, barRect.Top - 23);
            DrawCenteredText(dc, bin.PercentLabel, 7.1, SemiboldTypeface,
                HarmonicLabelBrush, new Point(centerX, labelY), dpi);
            DrawCenteredText(dc, bin.MagnitudeLabel, 6.9, BodyTypeface,
                MagnitudeLabelBrush, new Point(centerX, labelY + 10), dpi);
        }
    }

    private static void DrawMagnitudeGrid(
        DrawingContext dc,
        Rect plot,
        double axisMaximum,
        string axisTopLabel,
        double dpi)
    {
        for (var tick = 0; tick <= 4; tick++)
        {
            var y = plot.Bottom - plot.Height * tick / 4.0;
            dc.DrawLine(GridPen, new Point(plot.Left, y), new Point(plot.Right, y));
            if (tick == 0)
            {
                DrawRightAlignedText(dc, "0", 6.8, BodyTypeface,
                    AxisTextBrush, new Point(plot.Left - 4, y - 5), dpi);
            }
            else if (tick == 4)
            {
                DrawRightAlignedText(dc, axisTopLabel, 6.8, BodyTypeface,
                    AxisTextBrush, new Point(plot.Left - 4, y - 5), dpi);
            }
        }
        dc.DrawRectangle(null, PlotBorderPen, plot);
    }

    private void DrawFooter(DrawingContext dc, Rect bounds, double dpi)
    {
        var y = bounds.Bottom - FooterHeight + 5;
        dc.DrawLine(FooterDividerPen,
            new Point(12, bounds.Bottom - FooterHeight), new Point(bounds.Right - 12, bounds.Bottom - FooterHeight));
        DrawText(dc, $"Selected H{_selectedOrder}", 8.7, SemiboldTypeface, FooterPrimaryBrush,
            new Point(16, y), dpi);
        DrawText(dc, "click any order to compare the same harmonic across all channels", 8.2, BodyTypeface,
            FooterSecondaryBrush, new Point(92, y), dpi, Math.Max(120, bounds.Width - 210));

        if (_preparedRows.Length > 0 && _preparedRows[0].EstimatedSampleRateHz > 0)
            DrawRightAlignedText(dc, _preparedRows[0].SampleRateLabel, 8.0, BodyTypeface,
                FooterRateBrush, new Point(bounds.Right - 16, y), dpi);
    }

    private static PreparedSpectrumRow[] PrepareRows(
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> spectra,
        int maximumOrder)
    {
        if (spectra.Count == 0 || maximumOrder < 0)
            return Array.Empty<PreparedSpectrumRow>();

        var rows = new PreparedSpectrumRow[spectra.Count];
        for (var index = 0; index < spectra.Count; index++)
        {
            var spectrum = spectra[index];
            var bins = BuildPlotBins(spectrum, maximumOrder, out var maximumMagnitude);
            var axisMaximum = ComtradeHarmonicsOverviewMath.NiceMagnitudeAxisMaximum(maximumMagnitude);
            var signalColor = SignalColor(spectrum.SignalName);
            var signalBrush = FreezeBrush(signalColor);
            var secondarySignalBrush = FreezeBrush(WithAlpha(signalColor, 190));
            var unitSuffix = string.IsNullOrWhiteSpace(spectrum.Units) ? string.Empty : $"/{spectrum.Units}";
            var axisUnit = string.IsNullOrWhiteSpace(spectrum.Units) ? string.Empty : $" {spectrum.Units}";
            rows[index] = new PreparedSpectrumRow(
                bins,
                axisMaximum,
                signalBrush,
                secondarySignalBrush,
                $"{spectrum.SignalName}{unitSuffix}",
                $"THD {spectrum.ThdPercent:G4}%",
                $"{FormatEngineering(axisMaximum)}{axisUnit}",
                spectrum.EstimatedSampleRateHz,
                spectrum.EstimatedSampleRateHz > 0 ? $"{spectrum.EstimatedSampleRateHz:G6} Hz" : string.Empty);
        }
        return rows;
    }

    private static PlotBin[] BuildPlotBins(
        ComtradeHarmonicOverviewSpectrum spectrum,
        int maximumOrder,
        out double maximumMagnitude)
    {
        var result = new PlotBin[maximumOrder + 1];
        maximumMagnitude = 0.0;

        var dcMagnitude = Math.Abs(double.IsFinite(spectrum.DcComponent) ? spectrum.DcComponent : 0.0);
        result[0] = CreatePlotBin(
            0,
            dcMagnitude,
            ComtradeHarmonicsOverviewMath.PercentOfFundamental(dcMagnitude, spectrum.FundamentalRms),
            0.0);
        maximumMagnitude = dcMagnitude;

        // MaximumDisplayedOrder is 10, so an integer bit mask is a cheaper first-bin-wins index
        // than allocating GroupBy/Dictionary structures on every redraw.
        var populatedMask = 1u;
        for (var index = 0; index < spectrum.Bins.Count; index++)
        {
            var source = spectrum.Bins[index];
            var order = source.Order;
            if (order < 1 || order > maximumOrder)
                continue;
            var bit = 1u << order;
            if ((populatedMask & bit) != 0)
                continue;
            populatedMask |= bit;

            var magnitude = order == 1 && spectrum.FundamentalRms > 0
                ? spectrum.FundamentalRms
                : Math.Max(0.0, double.IsFinite(source.MagnitudeRms) ? source.MagnitudeRms : 0.0);
            var percent = order == 1
                ? 100.0
                : ComtradeHarmonicsOverviewMath.PercentOfFundamental(magnitude, spectrum.FundamentalRms);
            result[order] = CreatePlotBin(order, magnitude, percent, source.AngleDegrees);
            maximumMagnitude = Math.Max(maximumMagnitude, magnitude);
        }

        for (var order = 1; order <= maximumOrder; order++)
        {
            var bit = 1u << order;
            if ((populatedMask & bit) != 0)
                continue;
            var percent = order == 1 && spectrum.FundamentalRms > 0 ? 100.0 : 0.0;
            var magnitude = order == 1 && spectrum.FundamentalRms > 0 ? spectrum.FundamentalRms : 0.0;
            result[order] = CreatePlotBin(order, magnitude, percent, 0.0);
            maximumMagnitude = Math.Max(maximumMagnitude, magnitude);
        }

        return result;
    }

    private static PlotBin CreatePlotBin(int order, double magnitude, double percent, double angleDegrees)
        => new(
            order,
            magnitude,
            percent,
            angleDegrees,
            $"{percent:0.#}%",
            FormatEngineering(magnitude));

    private static int ResolveMaximumDisplayedOrder(IReadOnlyList<ComtradeHarmonicOverviewSpectrum> spectra)
    {
        if (spectra.Count == 0) return -1;

        var available = 0;
        for (var spectrumIndex = 0; spectrumIndex < spectra.Count; spectrumIndex++)
        {
            var spectrum = spectra[spectrumIndex];
            var binMaximum = 0;
            for (var binIndex = 0; binIndex < spectrum.Bins.Count; binIndex++)
                binMaximum = Math.Max(binMaximum, spectrum.Bins[binIndex].Order);

            var spectrumMaximum = spectrum.MaximumResolvableOrder > 0
                ? Math.Min(
                    spectrum.MaximumResolvableOrder,
                    binMaximum > 0 ? binMaximum : spectrum.MaximumResolvableOrder)
                : binMaximum;
            available = Math.Max(available, spectrumMaximum);
        }
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

    private static SolidColorBrush FreezeBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private static Pen FreezePen(Color color, double thickness)
    {
        var pen = new Pen(FreezeBrush(color), thickness);
        pen.Freeze();
        return pen;
    }

    private static string[] CreateOrderLabels()
    {
        var labels = new string[MaximumDisplayedOrder + 1];
        for (var order = 0; order <= MaximumDisplayedOrder; order++)
            labels[order] = order.ToString(CultureInfo.InvariantCulture);
        return labels;
    }

    private static FormattedText MakeText(
        string text,
        double size,
        Typeface typeface,
        Brush brush,
        double dpi,
        double? maxWidth = null)
    {
        var formatted = new FormattedText(
            text ?? string.Empty,
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            typeface,
            size,
            brush,
            dpi);
        if (maxWidth is { } width)
        {
            formatted.MaxTextWidth = Math.Max(1, width);
            formatted.Trimming = TextTrimming.CharacterEllipsis;
        }
        return formatted;
    }

    private static void DrawText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Brush brush,
        Point point,
        double dpi,
        double maxWidth = 10000)
        => dc.DrawText(MakeText(text, size, typeface, brush, dpi, maxWidth), point);

    private static void DrawCenteredText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Brush brush,
        Point point,
        double dpi)
    {
        var formatted = MakeText(text, size, typeface, brush, dpi);
        dc.DrawText(formatted, new Point(
            point.X - formatted.WidthIncludingTrailingWhitespace * 0.5,
            point.Y));
    }

    private static void DrawRightAlignedText(
        DrawingContext dc,
        string text,
        double size,
        Typeface typeface,
        Brush brush,
        Point point,
        double dpi)
    {
        var formatted = MakeText(text, size, typeface, brush, dpi);
        dc.DrawText(formatted, new Point(
            point.X - formatted.WidthIncludingTrailingWhitespace,
            point.Y));
    }

    private readonly record struct PlotBin(
        int Order,
        double MagnitudeRms,
        double PercentOfFundamental,
        double AngleDegrees,
        string PercentLabel,
        string MagnitudeLabel);

    private readonly record struct PreparedSpectrumRow(
        PlotBin[] Bins,
        double AxisMaximum,
        Brush SignalBrush,
        Brush SecondarySignalBrush,
        string SignalLabel,
        string ThdLabel,
        string AxisTopLabel,
        double EstimatedSampleRateHz,
        string SampleRateLabel);

    private readonly record struct RowHitTarget(Rect Plot, int MaximumOrder);
}
