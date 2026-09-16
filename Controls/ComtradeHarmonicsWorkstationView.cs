using System.Diagnostics;
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
/// Render-path rule: native results update exact cached targets at analysis cadence. Static row
/// assets and reusable numeric buffers are rebuilt only on topology changes; composition frames
/// mutate only numeric presentation values and never rebuild brushes, labels or per-bin arrays.
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

    private const double PresentationTimeConstantMs = 92.0;
    private const double PresentationAnimationMaximumMs = 300.0;
    private bool _presentationRenderingHooked;
    private long _lastPresentationTimestamp;
    private long _presentationAnimationStartedTimestamp;
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
        Unloaded += (_, _) => StopPresentationAnimation();
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
        var targetSpectra = spectra ?? Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        var targetMaximumOrder = ResolveMaximumDisplayedOrder(targetSpectra);
        var topologyMatches = PreparedRowsMatchTopology(_preparedRows, targetSpectra, targetMaximumOrder);

        _maximumDisplayedOrder = targetMaximumOrder;
        _selectedOrder = Math.Clamp(_selectedOrder, 0, Math.Max(0, _maximumDisplayedOrder));

        if (!topologyMatches)
        {
            _preparedRows = PrepareRows(targetSpectra, _maximumDisplayedOrder);
            StopPresentationAnimation();
        }
        else
        {
            UpdatePreparedTargets(_preparedRows, targetSpectra, _maximumDisplayedOrder);
            if (PreparedRowsDifferFromTarget(_preparedRows))
                StartPresentationAnimation();
            else
                StopPresentationAnimation();
        }

        _rowTargets.Clear();
        InvalidateVisual();
    }

    internal void ShowMessage(string title, string message)
    {
        _title = title ?? string.Empty;
        _subtitle = message ?? string.Empty;
        StopPresentationAnimation();
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
        DrawText(dc, prepared.TargetThdLabel, 7.7, BodyTypeface,
            SecondaryTextBrush, new Point(18, row.Top + 22), dpi, LabelWidth - 22);

        var plot = new Rect(
            LabelWidth,
            row.Top + 8,
            Math.Max(120, row.Width - LabelWidth - RightMargin),
            Math.Max(38, row.Height - 27));
        _rowTargets.Add(new RowHitTarget(plot, maximumOrder));

        DrawMagnitudeGrid(dc, plot, prepared.AxisMaximum, prepared.TargetAxisTopLabel, dpi);

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
            DrawCenteredText(dc, prepared.TargetPercentLabels[order], 7.1, SemiboldTypeface,
                HarmonicLabelBrush, new Point(centerX, labelY), dpi);
            DrawCenteredText(dc, prepared.TargetMagnitudeLabels[order], 6.9, BodyTypeface,
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

    private void StartPresentationAnimation()
    {
        var now = Stopwatch.GetTimestamp();
        _presentationAnimationStartedTimestamp = now;
        if (_presentationRenderingHooked) return;
        _lastPresentationTimestamp = now;
        CompositionTarget.Rendering += PresentationCompositionFrame;
        _presentationRenderingHooked = true;
    }

    private void StopPresentationAnimation()
    {
        if (_presentationRenderingHooked)
        {
            CompositionTarget.Rendering -= PresentationCompositionFrame;
            _presentationRenderingHooked = false;
        }
        _lastPresentationTimestamp = 0;
        _presentationAnimationStartedTimestamp = 0;
    }

    private void PresentationCompositionFrame(object? sender, EventArgs e)
    {
        if (!_presentationRenderingHooked) return;

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = _lastPresentationTimestamp == 0
            ? 16.0
            : Math.Clamp(Stopwatch.GetElapsedTime(_lastPresentationTimestamp, now).TotalMilliseconds, 0.0, 50.0);
        _lastPresentationTimestamp = now;

        AdvancePreparedRows(_preparedRows, elapsedMilliseconds);

        var animationAgeMilliseconds = _presentationAnimationStartedTimestamp == 0
            ? PresentationAnimationMaximumMs
            : Stopwatch.GetElapsedTime(_presentationAnimationStartedTimestamp, now).TotalMilliseconds;
        if (animationAgeMilliseconds >= PresentationAnimationMaximumMs)
        {
            SnapPreparedRowsToTarget(_preparedRows);
            StopPresentationAnimation();
        }

        InvalidateVisual();
    }

    private static bool PreparedRowsMatchTopology(
        IReadOnlyList<PreparedSpectrumRow> rows,
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> target,
        int maximumOrder)
    {
        if (maximumOrder < 0)
            return rows.Count == 0 && target.Count == 0;
        if (rows.Count != target.Count)
            return false;

        for (var index = 0; index < target.Count; index++)
        {
            var row = rows[index];
            var spectrum = target[index];
            if (!string.Equals(row.SignalName, spectrum.SignalName, StringComparison.Ordinal) ||
                !string.Equals(row.Units, spectrum.Units, StringComparison.Ordinal) ||
                row.Bins.Length != maximumOrder + 1)
                return false;
        }

        return true;
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
            var signalColor = SignalColor(spectrum.SignalName);
            var unitSuffix = string.IsNullOrWhiteSpace(spectrum.Units) ? string.Empty : $"/{spectrum.Units}";
            var row = new PreparedSpectrumRow(
                spectrum.SignalName,
                spectrum.Units,
                new PlotBin[maximumOrder + 1],
                new double[maximumOrder + 1],
                new double[maximumOrder + 1],
                new string[maximumOrder + 1],
                new string[maximumOrder + 1],
                FreezeBrush(signalColor),
                FreezeBrush(WithAlpha(signalColor, 190)),
                $"{spectrum.SignalName}{unitSuffix}");
            rows[index] = row;
            UpdatePreparedTarget(row, spectrum, maximumOrder);
            SnapPreparedRowToTarget(row);
        }

        return rows;
    }

    private static void UpdatePreparedTargets(
        IReadOnlyList<PreparedSpectrumRow> rows,
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> target,
        int maximumOrder)
    {
        var count = Math.Min(rows.Count, target.Count);
        for (var index = 0; index < count; index++)
            UpdatePreparedTarget(rows[index], target[index], maximumOrder);
    }

    private static void UpdatePreparedTarget(
        PreparedSpectrumRow row,
        ComtradeHarmonicOverviewSpectrum target,
        int maximumOrder)
    {
        var targetFundamental = DisplayMagnitude(target.FundamentalRms);
        var maximumMagnitude = 0.0;

        for (var order = 0; order <= maximumOrder; order++)
        {
            ResolveTargetBin(target, order, targetFundamental, out var magnitude, out var angleDegrees);
            var percent = order == 1 && targetFundamental > 0.0
                ? 100.0
                : ComtradeHarmonicsOverviewMath.PercentOfFundamental(magnitude, targetFundamental);

            row.TargetMagnitudes[order] = magnitude;
            row.TargetAngles[order] = angleDegrees;
            row.TargetPercentLabels[order] = $"{percent:0.#}%";
            row.TargetMagnitudeLabels[order] = FormatEngineering(magnitude);
            maximumMagnitude = Math.Max(maximumMagnitude, magnitude);
        }

        row.AxisMaximum = ComtradeHarmonicsOverviewMath.NiceMagnitudeAxisMaximum(maximumMagnitude);
        var axisUnit = string.IsNullOrWhiteSpace(target.Units) ? string.Empty : $" {target.Units}";
        row.TargetAxisTopLabel = $"{FormatEngineering(row.AxisMaximum)}{axisUnit}";
        row.TargetThdLabel = $"THD {DisplayScalar(target.ThdPercent):G4}%";
        row.EstimatedSampleRateHz = target.EstimatedSampleRateHz;
        row.SampleRateLabel = target.EstimatedSampleRateHz > 0.0 && double.IsFinite(target.EstimatedSampleRateHz)
            ? $"{target.EstimatedSampleRateHz:G6} Hz"
            : string.Empty;
    }

    private static void ResolveTargetBin(
        ComtradeHarmonicOverviewSpectrum spectrum,
        int order,
        double fundamentalRms,
        out double magnitude,
        out double angleDegrees)
    {
        if (order == 0)
        {
            magnitude = Math.Abs(DisplayScalar(spectrum.DcComponent));
            angleDegrees = 0.0;
            return;
        }

        magnitude = order == 1 ? fundamentalRms : 0.0;
        angleDegrees = 0.0;
        for (var index = 0; index < spectrum.Bins.Count; index++)
        {
            var source = spectrum.Bins[index];
            if (source.Order != order)
                continue;
            if (order != 1)
                magnitude = DisplayMagnitude(source.MagnitudeRms);
            angleDegrees = double.IsFinite(source.AngleDegrees)
                ? PresentationEasingMath.NormalizeAngleDegrees(source.AngleDegrees)
                : 0.0;
            return;
        }
    }

    private static void AdvancePreparedRows(
        IReadOnlyList<PreparedSpectrumRow> rows,
        double elapsedMilliseconds)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var order = 0; order < row.Bins.Length; order++)
            {
                var before = row.Bins[order];
                row.Bins[order] = new PlotBin(
                    order,
                    PresentationEasingMath.Smooth(
                        before.MagnitudeRms,
                        row.TargetMagnitudes[order],
                        elapsedMilliseconds,
                        PresentationTimeConstantMs),
                    PresentationEasingMath.SmoothAngleDegrees(
                        before.AngleDegrees,
                        row.TargetAngles[order],
                        elapsedMilliseconds,
                        PresentationTimeConstantMs));
            }
        }
    }

    private static void SnapPreparedRowsToTarget(IReadOnlyList<PreparedSpectrumRow> rows)
    {
        for (var index = 0; index < rows.Count; index++)
            SnapPreparedRowToTarget(rows[index]);
    }

    private static void SnapPreparedRowToTarget(PreparedSpectrumRow row)
    {
        for (var order = 0; order < row.Bins.Length; order++)
            row.Bins[order] = new PlotBin(order, row.TargetMagnitudes[order], row.TargetAngles[order]);
    }

    private static bool PreparedRowsDifferFromTarget(IReadOnlyList<PreparedSpectrumRow> rows)
    {
        for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
        {
            var row = rows[rowIndex];
            for (var order = 0; order < row.Bins.Length; order++)
            {
                var bin = row.Bins[order];
                if (!bin.MagnitudeRms.Equals(row.TargetMagnitudes[order]) ||
                    !PresentationEasingMath.NormalizeAngleDegrees(bin.AngleDegrees)
                        .Equals(PresentationEasingMath.NormalizeAngleDegrees(row.TargetAngles[order])))
                    return true;
            }
        }
        return false;
    }

    private static double DisplayMagnitude(double value)
        => double.IsFinite(value) ? Math.Max(0.0, value) : 0.0;

    private static double DisplayScalar(double value)
        => double.IsFinite(value) ? value : 0.0;

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
        double AngleDegrees);

    private sealed class PreparedSpectrumRow
    {
        internal PreparedSpectrumRow(
            string signalName,
            string units,
            PlotBin[] bins,
            double[] targetMagnitudes,
            double[] targetAngles,
            string[] targetPercentLabels,
            string[] targetMagnitudeLabels,
            Brush signalBrush,
            Brush secondarySignalBrush,
            string signalLabel)
        {
            SignalName = signalName ?? string.Empty;
            Units = units ?? string.Empty;
            Bins = bins;
            TargetMagnitudes = targetMagnitudes;
            TargetAngles = targetAngles;
            TargetPercentLabels = targetPercentLabels;
            TargetMagnitudeLabels = targetMagnitudeLabels;
            SignalBrush = signalBrush;
            SecondarySignalBrush = secondarySignalBrush;
            SignalLabel = signalLabel;
        }

        internal string SignalName { get; }
        internal string Units { get; }
        internal PlotBin[] Bins { get; }
        internal double[] TargetMagnitudes { get; }
        internal double[] TargetAngles { get; }
        internal string[] TargetPercentLabels { get; }
        internal string[] TargetMagnitudeLabels { get; }
        internal Brush SignalBrush { get; }
        internal Brush SecondarySignalBrush { get; }
        internal string SignalLabel { get; }
        internal double AxisMaximum { get; set; } = 1.0;
        internal string TargetAxisTopLabel { get; set; } = string.Empty;
        internal string TargetThdLabel { get; set; } = string.Empty;
        internal double EstimatedSampleRateHz { get; set; }
        internal string SampleRateLabel { get; set; } = string.Empty;
    }

    private readonly record struct RowHitTarget(Rect Plot, int MaximumOrder);
}
