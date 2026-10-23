// Original DigSubAnalyzer WaveformPanelControl; GPL-3.0-or-later
// Local changes: raw-lane naming only (no guessed engineering U/I mapping),
// higher maximum render samples to preserve trace fidelity at narrow widths.
using System.Globalization;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ProcessBus.Core.Models;

namespace ProcessBus.App.Wpf.Controls;

public sealed class WaveformPanelControl : FrameworkElement
{
    // ARSAS light workstation presentation; render geometry and DSP unchanged.
    private static readonly Brush PanelBrush = CreateBrush("#FFFFFF");
    private static readonly Brush PlotBackgroundBrush = CreateBrush("#F8FBFF");
    private static readonly Brush LaneHeaderBrush = CreateBrush("#EFF5FC");
    private static readonly Brush TitleBrush = CreateBrush("#2865C7");
    private static readonly Brush LabelBrush = CreateBrush("#667085");
    private static readonly Brush MutedBrush = CreateBrush("#7C8DA4");
    private static readonly Brush ShapeOkBrush = CreateBrush("#15803D");
    private static readonly Brush ShapeWarningBrush = CreateBrush("#B77905");
    private static readonly Brush ShapeAlertBrush = CreateBrush("#DC2626");
    private static readonly Brush ShapeUnknownBrush = CreateBrush("#7C8DA4");
    private static readonly Brush UaBrush = CreateBrush("#E33455");
    private static readonly Brush UbBrush = CreateBrush("#D99A0B");
    private static readonly Brush UcBrush = CreateBrush("#257ADE");
    private static readonly Brush NeutralBrush = CreateBrush("#8A9DB3");
    private static readonly Pen BorderPen = CreatePen("#DCE6F2", 1.0);
    private static readonly Pen GridPen = CreatePen("#E4EDF7", 1.05, [2.0, 6.0]);
    private static readonly Pen MajorGridPen = CreatePen("#D7E4F2", 1.15, [4.0, 6.0]);
    private static readonly Pen ZeroPen = CreatePen("#A1B4CA", 1.35);
    private static readonly Pen HighlightPen = CreatePen("#EDF3FA", 1.0);
    private static readonly Brush AxisTextBrush = CreateBrush("#7C8DA4");
    // Soft vertical guide; phase-specific leader arrows carry the information.
    // All drawing resources are frozen and shared between render frames.
    private static readonly Pen CursorPen = CreatePen("#8AA0BA", 1.05, [3.0, 4.0]);
    private static readonly Brush CursorLabelSurface = CreateBrush("#F8FBFF");
    private static readonly Pen CursorLabelFrame = CreatePen("#D0DCEB", 0.9);
    private static readonly Pen CursorRedLeader = CreateFrozenSeriesPen(UaBrush, 1.4);
    private static readonly Pen CursorGoldLeader = CreateFrozenSeriesPen(UbBrush, 1.4);
    private static readonly Pen CursorBlueLeader = CreateFrozenSeriesPen(UcBrush, 1.4);
    private static readonly Pen CursorNeutralLeader = CreateFrozenSeriesPen(NeutralBrush, 1.4);
    private static readonly Dictionary<string, Pen> SeriesPenCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Typeface TitleTypeface = new((FontFamily)System.Windows.Application.Current.FindResource("AppFontFamily"), FontStyles.Normal, FontWeights.SemiBold, FontStretches.Normal);
    private static readonly Typeface LabelTypeface = new((FontFamily)System.Windows.Application.Current.FindResource("AppFontFamily"), FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private double _cursorFraction = 0.5;
    private bool _isDraggingCursor;
    private bool _cursorVisible;
    private int _cursorLane = -1;
    private int _cursorSampleIndex = -1;
    private double _voltageAutoPeak;
    private double _currentAutoPeak;
    private DateTime _voltagePeakUpdatedUtc = DateTime.MinValue;
    private DateTime _currentPeakUpdatedUtc = DateTime.MinValue;
    private string? _voltageUnit;
    private string? _currentUnit;
    private const double AutoScaleHeadroom = 1.28;
    private const double AutoScaleSmoothing = 0.18;
    // Steady-state deadband: the observed window peak jitters a little every cycle, and an
    // EMA with no deadband keeps micro-rescaling the axis, which reads as vertical
    // "breathing" of the whole trace. Inside the deadband the axis is frozen.
    private const double AutoScaleDeadband = 0.02;
    private const double PeakHoldSeconds = 6.0;
    private const double SamplesPerPixel = 2.0;

    public WaveformPanelControl()
    {
        Focusable = true;
        Cursor = Cursors.Cross;
        // Render only the latest immutable waveform snapshot; never tween real SV samples.
    }

    public static readonly DependencyProperty SnapshotProperty =
        DependencyProperty.Register(
            nameof(Snapshot),
            typeof(WaveformSnapshot),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    // A reused WPF control must never carry a raw-count peak range into a
    // different SV publisher. Stream identity is cheap state, not packet work.
    public static readonly DependencyProperty StreamKeyProperty =
        DependencyProperty.Register(
            nameof(StreamKey), typeof(string), typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata(string.Empty,
                FrameworkPropertyMetadataOptions.AffectsRender,
                static (d, _) => ((WaveformPanelControl)d).ResetScaleState()));

    public static readonly DependencyProperty ShowModeProperty =
        DependencyProperty.Register(
            nameof(ShowMode),
            typeof(string),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata("Both", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LayoutModeProperty =
        DependencyProperty.Register(
            nameof(LayoutMode),
            typeof(string),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata("Overlay", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TimebaseModeProperty =
        DependencyProperty.Register(
            nameof(TimebaseMode),
            typeof(string),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata("4 cycles", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty MarkersEnabledProperty =
        DependencyProperty.Register(
            nameof(MarkersEnabled),
            typeof(bool),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty VoltageScaleProperty =
        DependencyProperty.Register(
            nameof(VoltageScale),
            typeof(string),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata("Auto", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CurrentScaleProperty =
        DependencyProperty.Register(
            nameof(CurrentScale),
            typeof(string),
            typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata("Auto", FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty IsStreamLiveProperty =
        DependencyProperty.Register(
            nameof(IsStreamLive), typeof(bool), typeof(WaveformPanelControl),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));

    public bool IsStreamLive
    {
        get => (bool)GetValue(IsStreamLiveProperty);
        set => SetValue(IsStreamLiveProperty, value);
    }

    public WaveformSnapshot? Snapshot
    {
        get => (WaveformSnapshot?)GetValue(SnapshotProperty);
        set => SetValue(SnapshotProperty, value);
    }

    public string StreamKey
    {
        get => (string)GetValue(StreamKeyProperty);
        set => SetValue(StreamKeyProperty, value);
    }

    private void ResetScaleState()
    {
        _voltageAutoPeak = 0;
        _currentAutoPeak = 0;
        _voltagePeakUpdatedUtc = DateTime.MinValue;
        _currentPeakUpdatedUtc = DateTime.MinValue;
        _voltageUnit = null;
        _currentUnit = null;
    }

    private void ResetLaneScaleWhenUnitsChange(bool isVoltage, string unit)
    {
        if (isVoltage)
        {
            if (!WaveformAutoRangePolicy.UnitsChanged(_voltageUnit, unit))
                return;
            _voltageUnit = unit;
            _voltageAutoPeak = 0;
            _voltagePeakUpdatedUtc = DateTime.MinValue;
        }
        else
        {
            if (!WaveformAutoRangePolicy.UnitsChanged(_currentUnit, unit))
                return;
            _currentUnit = unit;
            _currentAutoPeak = 0;
            _currentPeakUpdatedUtc = DateTime.MinValue;
        }
    }

    public string ShowMode
    {
        get => (string)GetValue(ShowModeProperty);
        set => SetValue(ShowModeProperty, value);
    }

    public string LayoutMode
    {
        get => (string)GetValue(LayoutModeProperty);
        set => SetValue(LayoutModeProperty, value);
    }

    public string TimebaseMode
    {
        get => (string)GetValue(TimebaseModeProperty);
        set => SetValue(TimebaseModeProperty, value);
    }

    public bool MarkersEnabled
    {
        get => (bool)GetValue(MarkersEnabledProperty);
        set => SetValue(MarkersEnabledProperty, value);
    }

    public string VoltageScale
    {
        get => (string)GetValue(VoltageScaleProperty);
        set => SetValue(VoltageScaleProperty, value);
    }

    public string CurrentScale
    {
        get => (string)GetValue(CurrentScaleProperty);
        set => SetValue(CurrentScaleProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsInfinity(availableSize.Width) ? 900 : availableSize.Width;
        var height = double.IsInfinity(availableSize.Height) ? 430 : availableSize.Height;
        return new Size(Math.Max(480, width), Math.Max(320, height));
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var bounds = new Rect(new Point(0, 0), RenderSize);
        dc.DrawRectangle(PanelBrush, null, bounds);

        var snapshot = Snapshot;
        if (snapshot is null || (snapshot.VoltageSeries.Count == 0 && snapshot.CurrentSeries.Count == 0))
        {
            DrawCenteredMessage(dc, bounds, "Waveform data pending");
            return;
        }

        var showVoltage = !string.Equals(ShowMode, "Current", StringComparison.OrdinalIgnoreCase);
        var showCurrent = !string.Equals(ShowMode, "Voltage", StringComparison.OrdinalIgnoreCase);

        var visibleLanes = new List<LaneDefinition>(2);
        if (showVoltage)
            visibleLanes.Add(new LaneDefinition(snapshot.VoltageSeries.FirstOrDefault()?.Unit == "A" ? "CURRENT · Ch1–4" : "RAW Ch1–4", snapshot.VoltageSeries.FirstOrDefault()?.Unit ?? "counts", snapshot.VoltageSeries));
        if (showCurrent)
            visibleLanes.Add(new LaneDefinition(snapshot.CurrentSeries.FirstOrDefault()?.Unit == "V" ? "VOLTAGE · Ch5–8" : "RAW Ch5–8", snapshot.CurrentSeries.FirstOrDefault()?.Unit ?? "counts", snapshot.CurrentSeries));

        if (visibleLanes.Count == 0)
        {
            DrawCenteredMessage(dc, bounds, "Waveform lanes hidden");
            return;
        }

        const double outerPadding = 10;
        const double laneGap = 12;
        var laneHeight = Math.Max(110, (bounds.Height - (outerPadding * 2) - (laneGap * (visibleLanes.Count - 1))) / visibleLanes.Count);
        var laneWidth = Math.Max(120, bounds.Width - (outerPadding * 2));
        var top = outerPadding;

        for (var laneIndex = 0; laneIndex < visibleLanes.Count; laneIndex++)
        {
            var laneRect = new Rect(outerPadding, top, laneWidth, laneHeight);
            DrawLane(dc, laneRect, visibleLanes[laneIndex], snapshot, laneIndex);
            top += laneHeight + laneGap;
        }
    }

    private void DrawLane(DrawingContext dc, Rect rect, LaneDefinition lane, WaveformSnapshot snapshot, int laneIndex)
    {
        dc.DrawRoundedRectangle(PlotBackgroundBrush, BorderPen, rect, 10, 10);

        var headerRect = new Rect(rect.Left, rect.Top, rect.Width, 30);
        dc.DrawRoundedRectangle(LaneHeaderBrush, null, headerRect, 10, 10);
        dc.DrawRectangle(LaneHeaderBrush, null, new Rect(rect.Left, rect.Top + 15, rect.Width, 15));

        var innerRect = new Rect(rect.Left + 12, rect.Top + 38, rect.Width - 24, rect.Height - 50);
        if (innerRect.Width <= 20 || innerRect.Height <= 20)
            return;

        var laneSeries = BuildVisibleSeries(lane.Series, snapshot, innerRect.Width);
        var amplitudeMax = ResolveLaneAmplitudeMax(lane, laneSeries);

        dc.DrawText(CreateText(lane.Title, 13, TitleBrush, TitleTypeface), new Point(rect.Left + 12, rect.Top + 7));
        // Stable header legend, not badges covering the trace. Samples remain
        // geometrically unmodified and every phase retains its color identity.
        var legendX = rect.Left + (lane.Title.StartsWith("RAW", StringComparison.Ordinal) ? 115 : 188);
        foreach (var visible in laneSeries)
        {
            var suffix = ChannelFreshnessDisplay.Suffix(visible.Freshness);
            var title = suffix.Length == 0 ? visible.Name : visible.Name + " " + suffix;
            var legend = CreateText(title, 10.5, LabelBrush, LabelTypeface);
            dc.PushOpacity(TraceOpacity(IsStreamLive, visible.Freshness));
            if (legendX + legend.Width + 9 > rect.Right - 12)
            {
                dc.Pop();
                break;
            }
            dc.DrawEllipse(GetSeriesBrush(visible.Name), null,
                new Point(legendX, rect.Top + 15), 3.5, 3.5);
            dc.DrawText(legend, new Point(legendX + 7, rect.Top + 7));
            dc.Pop();
            legendX += legend.Width + 22;
        }

        if (!IsStreamLive)
        {
            var staleText = CreateText("STALE · last valid samples", 10, ShapeWarningBrush, TitleTypeface);
            dc.DrawText(staleText,
                new Point(rect.Right - staleText.Width - 12, rect.Top + 8));
        }
        else if (laneSeries.Any(item => item.Freshness != ChannelFreshness.Live))
        {
            var held = CreateText("PARTIAL · quality held", 10, ShapeWarningBrush, TitleTypeface);
            if (legendX + 14 < rect.Right - held.Width - 12)
                dc.DrawText(held, new Point(rect.Right - held.Width - 12, rect.Top + 8));
        }

        var stacked = string.Equals(LayoutMode, "Stacked", StringComparison.OrdinalIgnoreCase);
        // Neutral at exactly zero is not a fourth *phase waveform*; retain its
        // legend and measurements, but do not waste a Stacked band on a flat line.
        var plotSeries = laneSeries
            .Where(item => item.Samples.Count >= 2 && !IsSilentZeroTrace(item.Samples))
            .ToArray();
        DrawGrid(dc, innerRect, stacked, plotSeries.Length);
        DrawYAxisScale(dc, innerRect, amplitudeMax, lane.Unit, stacked, plotSeries.Length);

        if (laneSeries.Count == 0 || laneSeries.All(item => item.Samples.Count < 2))
        {
            DrawCenteredMessage(dc, innerRect, $"{lane.Title} samples pending");
            return;
        }

        for (var index = 0; index < plotSeries.Length; index++)
        {
            var trace = plotSeries[index];
            dc.PushOpacity(TraceOpacity(IsStreamLive, trace.Freshness));
            DrawSeries(dc, innerRect, trace, amplitudeMax, stacked, index, plotSeries.Length);
            dc.Pop();
        }

        if (MarkersEnabled && _cursorVisible && _cursorLane == laneIndex)
            DrawCursor(dc, innerRect, plotSeries, amplitudeMax, stacked, snapshot);

        DrawTimebaseLabel(dc, innerRect, snapshot);
    }

    private IReadOnlyList<WaveformSeriesModel> BuildVisibleSeries(
        IReadOnlyList<WaveformSeriesModel> source,
        WaveformSnapshot snapshot,
        double plotWidth)
    {
        var visibleSamples = CalculateVisibleSamples(snapshot);

        if (visibleSamples <= 0)
            visibleSamples = source.Select(item => item.Samples.Count).DefaultIfEmpty(0).Max();

        var maxDisplaySamples = Math.Max(64, (int)Math.Ceiling(plotWidth * SamplesPerPixel));

        return source
            .Select(item =>
            {
                var samples = BuildDisplaySamples(item.Samples, visibleSamples, maxDisplaySamples);

                return new WaveformSeriesModel
                {
                    Name = item.Name,
                    Unit = item.Unit,
                    Samples = samples,
                    ShapeSeverity = item.ShapeSeverity,
                    ShapeStatusText = item.ShapeStatusText,
                    ShapeResidualPercent = item.ShapeResidualPercent,
                    CrestFactor = item.CrestFactor,
                    HasShapeDistortion = item.HasShapeDistortion,
                    Freshness = item.Freshness
                };
            })
            // Keep missing/held identities for honest per-phase legends; only
            // trusted valid points are rendered as bright, live waveform.
            .Where(item => item.Samples.Count >= 2 || item.Freshness != ChannelFreshness.Live)
            .ToArray();
    }

    private static double[] BuildDisplaySamples(IReadOnlyList<double> source, int visibleSamples, int maxDisplaySamples)
    {
        var sourceCount = Math.Min(source.Count, visibleSamples);
        if (sourceCount <= 0)
            return Array.Empty<double>();

        // Show the newest part of the scope window. The old implementation always started at index 0,
        // so short timebase modes could display stale samples after an SV magnitude change.
        var sourceOffset = Math.Max(0, source.Count - sourceCount);

        if (sourceCount <= maxDisplaySamples)
        {
            var direct = new double[sourceCount];
            for (var i = 0; i < sourceCount; i++)
                direct[i] = source[sourceOffset + i];

            return direct;
        }

        var bucketCount = Math.Max(2, maxDisplaySamples / 2);
        var result = new List<double>(bucketCount * 2);
        var step = sourceCount / (double)bucketCount;

        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            var start = (int)Math.Floor(bucket * step);
            var end = (int)Math.Floor((bucket + 1) * step);
            start = Math.Clamp(start, 0, sourceCount - 1);
            end = Math.Clamp(Math.Max(start + 1, end), start + 1, sourceCount);

            var min = double.PositiveInfinity;
            var max = double.NegativeInfinity;
            var minIndex = start;
            var maxIndex = start;

            for (var i = start; i < end; i++)
            {
                var absoluteIndex = sourceOffset + i;
                var sample = source[absoluteIndex];
                if (double.IsNaN(sample) || double.IsInfinity(sample))
                    sample = 0.0;

                if (sample < min)
                {
                    min = sample;
                    minIndex = i;
                }

                if (sample > max)
                {
                    max = sample;
                    maxIndex = i;
                }
            }

            if (minIndex <= maxIndex)
            {
                result.Add(min);
                result.Add(max);
            }
            else
            {
                result.Add(max);
                result.Add(min);
            }
        }

        return result.ToArray();
    }

    private double ResolveLaneAmplitudeMax(LaneDefinition lane, IReadOnlyList<WaveformSeriesModel> series)
    {
        // Engineering units select the correct independent scale families.
        // RAW retains existing per-lane auto-range behavior.
        var isVoltage = string.Equals(lane.Unit, "V", StringComparison.OrdinalIgnoreCase) ||
                        (string.Equals(lane.Unit, "counts", StringComparison.OrdinalIgnoreCase) &&
                         lane.Title.StartsWith("RAW Ch1", StringComparison.OrdinalIgnoreCase));
        ResetLaneScaleWhenUnitsChange(isVoltage, lane.Unit);
        var hasPhysicalUnits = string.Equals(lane.Unit, isVoltage ? "V" : "A", StringComparison.OrdinalIgnoreCase);
        var scaleText = isVoltage ? VoltageScale : CurrentScale;

        if (hasPhysicalUnits && isVoltage && TryParseVoltageScalePeak(scaleText, out var fixedVoltagePeak))
            return Math.Max(fixedVoltagePeak, 1.0);

        if (hasPhysicalUnits && !isVoltage && TryParseCurrentScalePeak(scaleText, out var fixedCurrentPeak))
            return Math.Max(fixedCurrentPeak, 1.0);

        // Held channels must not drive the fresh auto-range. If the entire
        // publisher stopped, keep its last range: no false auto-zoom on hold.
        var savedPeak = isVoltage ? _voltageAutoPeak : _currentAutoPeak;
        if (!IsStreamLive && savedPeak > 0)
            return savedPeak;

        var trusted = series.Where(item => item.Freshness == ChannelFreshness.Live).ToArray();
        if (trusted.Length == 0 && savedPeak > 0)
            return savedPeak;
        var amplitudeSeries = trusted.Length > 0 ? trusted : series;
        var observedPeak = ResolveRobustObservedPeak(amplitudeSeries);

        observedPeak = Math.Max(observedPeak, 1.0);

        var rmsPeakEstimate = ResolveRmsBasedPeak(amplitudeSeries);
        var targetPeak = Math.Max(observedPeak * 1.08, rmsPeakEstimate * AutoScaleHeadroom);
        targetPeak = Math.Max(targetPeak, 1.0);

        if (string.Equals(scaleText, "Auto Peak Hold", StringComparison.OrdinalIgnoreCase))
            return ResolvePeakHoldScale(isVoltage, targetPeak);

        return ResolveSmoothedAutoScale(isVoltage, targetPeak);
    }

    private static double ResolveRmsBasedPeak(IReadOnlyList<WaveformSeriesModel> series)
    {
        var maxRms = 1.0;

        foreach (var item in series)
        {
            var count = 0;
            var sumSquare = 0.0;

            foreach (var sample in item.Samples)
            {
                if (double.IsNaN(sample) || double.IsInfinity(sample))
                    continue;

                sumSquare += sample * sample;
                count++;
            }

            if (count > 0)
                maxRms = Math.Max(maxRms, Math.Sqrt(sumSquare / count));
        }

        return maxRms * Math.Sqrt(2.0);
    }

    private static double ResolveRobustObservedPeak(IReadOnlyList<WaveformSeriesModel> series)
    {
        var maxPeak = 1.0;

        foreach (var item in series)
        {
            foreach (var sample in item.Samples)
            {
                if (double.IsNaN(sample) || double.IsInfinity(sample))
                    continue;

                maxPeak = Math.Max(maxPeak, Math.Abs(sample));
            }
        }

        return maxPeak;
    }

    private double ResolveSmoothedAutoScale(bool isVoltage, double targetPeak)
    {
        if (isVoltage)
        {
            if (_voltageAutoPeak <= 0)
                _voltageAutoPeak = targetPeak;
            else if (Math.Abs(targetPeak - _voltageAutoPeak) / Math.Max(_voltageAutoPeak, 1e-9) >= AutoScaleDeadband)
                _voltageAutoPeak = (_voltageAutoPeak * (1.0 - AutoScaleSmoothing)) + (targetPeak * AutoScaleSmoothing);

            return Math.Max(_voltageAutoPeak, 1.0);
        }

        if (_currentAutoPeak <= 0)
            _currentAutoPeak = targetPeak;
        else if (Math.Abs(targetPeak - _currentAutoPeak) / Math.Max(_currentAutoPeak, 1e-9) >= AutoScaleDeadband)
            _currentAutoPeak = (_currentAutoPeak * (1.0 - AutoScaleSmoothing)) + (targetPeak * AutoScaleSmoothing);

        return Math.Max(_currentAutoPeak, 1.0);
    }

    private double ResolvePeakHoldScale(bool isVoltage, double targetPeak)
    {
        var now = DateTime.UtcNow;

        if (isVoltage)
        {
            // A 1000× counts-to-V conversion (or sudden real magnitude change)
            // must not be hidden as a flat line for the entire hold timeout.
            if (WaveformAutoRangePolicy.ShouldReleasePeakHold(_voltageAutoPeak, targetPeak) ||
                (now - _voltagePeakUpdatedUtc).TotalSeconds > PeakHoldSeconds)
            {
                _voltageAutoPeak = targetPeak;
                _voltagePeakUpdatedUtc = now;
            }
            // Peak-hold must not decay a little on every render; that slow decay is
            // perceived as vertical waveform movement after an injection change. Hold the
            // axis steady until the hold timeout expires, then jump to the current target.

            return Math.Max(_voltageAutoPeak, 1.0);
        }

        if (WaveformAutoRangePolicy.ShouldReleasePeakHold(_currentAutoPeak, targetPeak) ||
            (now - _currentPeakUpdatedUtc).TotalSeconds > PeakHoldSeconds)
        {
            _currentAutoPeak = targetPeak;
            _currentPeakUpdatedUtc = now;
        }

        // Peak-hold must not decay a little on every render; that slow decay is
        // perceived as vertical waveform movement after an injection change. Hold the
        // axis steady until the hold timeout expires, then jump to the current target.
        return Math.Max(_currentAutoPeak, 1.0);
    }

    private static bool TryParseVoltageScalePeak(string? scaleText, out double peak)
    {
        peak = 0.0;

        if (string.IsNullOrWhiteSpace(scaleText) ||
            string.Equals(scaleText, "Auto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scaleText, "Auto Peak Hold", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = scaleText.Trim();

        if (normalized.EndsWith("kV", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace("kV", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var kv))
                return false;

            peak = kv * 1000.0 * Math.Sqrt(2.0) * 1.10;
            return true;
        }

        if (normalized.EndsWith("V", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace("V", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var v))
                return false;

            peak = v * Math.Sqrt(2.0) * 1.10;
            return true;
        }

        return false;
    }

    private static bool TryParseCurrentScalePeak(string? scaleText, out double peak)
    {
        peak = 0.0;

        if (string.IsNullOrWhiteSpace(scaleText) ||
            string.Equals(scaleText, "Auto", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(scaleText, "Auto Peak Hold", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = scaleText.Trim();

        if (normalized.EndsWith("kA", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace("kA", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var ka))
                return false;

            peak = ka * 1000.0 * Math.Sqrt(2.0) * 1.10;
            return true;
        }

        if (normalized.EndsWith("A", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized.Replace("A", string.Empty, StringComparison.OrdinalIgnoreCase).Trim();

            if (!double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out var ampere))
                return false;

            peak = ampere * Math.Sqrt(2.0) * 1.10;
            return true;
        }

        return false;
    }

    private static string FormatAxisLabel(double peak, string unit)
    {
        if (string.Equals(unit, "V", StringComparison.OrdinalIgnoreCase) && Math.Abs(peak) >= 1000.0)
            return $"+/- {peak / 1000.0:0.##} kVpk";

        if (string.Equals(unit, "A", StringComparison.OrdinalIgnoreCase) && Math.Abs(peak) >= 1000.0)
            return $"+/- {peak / 1000.0:0.##} kApk";

        return $"+/- {peak:0.#} {unit}pk";
    }

    private static double[] RepairDisplayGlitches(double[] samples)
    {
        if (samples.Length < 5)
            return samples;

        var repaired = samples
            .Select(static sample => double.IsNaN(sample) || double.IsInfinity(sample) ? 0.0 : sample)
            .ToArray();

        var amplitude = ResolveRobustAmplitude(repaired);
        var hardLimit = Math.Max(8.0, amplitude * 3.5);
        var jumpLimit = Math.Max(8.0, amplitude * 0.24);

        for (var pass = 0; pass < 2; pass++)
        {
            for (var i = 1; i < repaired.Length - 1; i++)
            {
                var previous = repaired[i - 1];
                var current = repaired[i];
                var next = repaired[i + 1];

                if (Math.Abs(current) > hardLimit ||
                    (Math.Abs(current - previous) > jumpLimit && Math.Abs(next - current) > jumpLimit))
                    repaired[i] = (previous + next) / 2.0;
            }
        }

        return repaired;
    }

    private static double ResolveRobustAmplitude(double[] samples)
    {
        var maxPeak = 1.0;

        foreach (var sample in samples)
            maxPeak = Math.Max(maxPeak, Math.Abs(sample));

        return maxPeak;
    }

    private int CalculateVisibleSamples(WaveformSnapshot snapshot)
    {
        var cycles = TimebaseMode switch
        {
            "1 cycle" => 1,
            "2 cycles" => 2,
            "8 cycles" => 8,
            _ => 4
        };

        var samplesPerCycle = snapshot.SamplesPerCycle > 0
            ? snapshot.SamplesPerCycle
            : snapshot.SampleRateHz > 0 && snapshot.MeasuredFrequencyHz > 0
                ? Math.Max(1, (int)Math.Round(snapshot.SampleRateHz / snapshot.MeasuredFrequencyHz))
                : 80;

        return samplesPerCycle * cycles;
    }

    private static void DrawGrid(DrawingContext dc, Rect rect, bool stacked, int traces)
    {
        // Frozen pens and lightweight subdivisions; never remove the actual 0 axis.
        for (var i = 1; i < 10; i++)
        {
            var x = rect.Left + (rect.Width * i / 10.0);
            dc.DrawLine(i == 5 ? MajorGridPen : GridPen,
                new Point(x, rect.Top), new Point(x, rect.Bottom));
        }

        // Stacked gets one mathematically correct baseline per *visible phase*.
        // Overlay gets exactly one shared baseline at Y=0.
        if (stacked && traces > 0)
        {
            var band = rect.Height / traces;
            var zeroLabel = CreateText("0", 9.6, AxisTextBrush, LabelTypeface);
            for (var i = 0; i < traces; i++)
            {
                var y = rect.Top + (i + 0.5) * band;
                dc.DrawLine(ZeroPen, new Point(rect.Left, y), new Point(rect.Right, y));
                dc.DrawText(zeroLabel, new Point(rect.Left + 3, y - 13));
            }
            for (var i = 1; i < traces; i++)
            {
                var y = rect.Top + i * band;
                dc.DrawLine(GridPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
        }
        else
        {
            for (var i = 1; i < 4; i += 2)
            {
                var y = rect.Top + rect.Height * i / 4.0;
                dc.DrawLine(GridPen, new Point(rect.Left, y), new Point(rect.Right, y));
            }
            var zeroY = rect.Top + rect.Height / 2.0;
            dc.DrawLine(ZeroPen, new Point(rect.Left, zeroY), new Point(rect.Right, zeroY));
            dc.DrawText(CreateText("0", 9.6, AxisTextBrush, LabelTypeface),
                new Point(rect.Left + 3, zeroY - 13));
        }

        dc.DrawLine(HighlightPen, new Point(rect.Left, rect.Top + 1), new Point(rect.Right, rect.Top + 1));
        dc.DrawLine(HighlightPen, new Point(rect.Left, rect.Bottom - 1), new Point(rect.Right, rect.Bottom - 1));
    }

    private static void DrawYAxisScale(DrawingContext dc, Rect rect, double peak, string unit, bool stacked, int traces)
    {
        if (peak <= 0 || stacked)
            return; // stacked uses per-phase zero axes; global ±peak labels are misleading.

        var topText = FormatAxisLabelSigned(peak, unit, positive: true);
        var bottomText = FormatAxisLabelSigned(peak, unit, positive: false);
        var top = CreateText(topText, 10.2, AxisTextBrush, LabelTypeface);
        var bottom = CreateText(bottomText, 10.2, AxisTextBrush, LabelTypeface);
        var x = rect.Left + 4;
        dc.DrawText(top, new Point(x, rect.Top + 4));
        dc.DrawText(bottom, new Point(x, rect.Bottom - bottom.Height - 4));
    }

    private static string FormatAxisLabelSigned(double peak, string unit, bool positive)
    {
        var sign = positive ? "+" : "-";
        var absPeak = Math.Abs(peak);

        if (string.Equals(unit, "V", StringComparison.OrdinalIgnoreCase) && absPeak >= 1000.0)
            return $"{sign}{absPeak / 1000.0:0.##} kVpk";

        if (string.Equals(unit, "A", StringComparison.OrdinalIgnoreCase) && absPeak >= 1000.0)
            return $"{sign}{absPeak / 1000.0:0.##} kApk";

        return $"{sign}{absPeak:0.#} {unit}pk";
    }
    private void DrawSeries(DrawingContext dc, Rect rect, WaveformSeriesModel series, double amplitudeMax, bool stacked, int index, int totalSeries)
    {
        if (series.Samples.Count < 2)
            return;

        // Zero neutral/disabled inputs should not impersonate a permanently
        // enabled 0-axis when phase traces are overlaid. Keep the channel in
        // measurements/cursor evidence; only the redundant flat ink is omitted.
        if (!stacked && IsSilentZeroTrace(series.Samples))
            return;

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();

        var verticalBand = stacked ? rect.Height / Math.Max(1, totalSeries) : rect.Height;
        var bandTop = stacked ? rect.Top + (verticalBand * index) : rect.Top;
        var bandCenter = bandTop + (verticalBand / 2.0);
        var bandScale = (stacked ? verticalBand : rect.Height) * 0.37;

        for (var i = 0; i < series.Samples.Count; i++)
        {
            var x = rect.Left + (rect.Width * i / (series.Samples.Count - 1.0));
            var normalized = amplitudeMax <= 0 ? 0 : series.Samples[i] / amplitudeMax;
            var y = bandCenter - (normalized * bandScale);
            var point = new Point(x, y);

            if (i == 0)
                ctx.BeginFigure(point, false, false);
            else
                ctx.LineTo(point, true, false);
        }

        geometry.Freeze();

        // One crisp antialiased vector stroke. The original dark-theme
        // Glow and Shadow layers wash out fine lines on ARSAS light surfaces.
        // Geometry remains the exact coherent input; no smoothing or interpolation.
        dc.DrawGeometry(null, GetSeriesPen(series.Name), geometry);

    }

    private static bool IsSilentZeroTrace(IReadOnlyList<double> samples)
    {
        // Bounded one-pass numeric check. No allocations and no tolerance that
        // could erase a physically small but nonzero engineering signal.
        for (var i = 0; i < samples.Count; i++)
            if (samples[i] != 0.0)
                return false;
        return true;
    }

    private static void DrawLaneShapeBadge(DrawingContext dc, Rect rect, IReadOnlyList<WaveformSeriesModel> series)
    {
        var quality = ResolveLaneShape(series);
        var brush = ResolveShapeBrush(quality.Severity);
        var text = CreateText(quality.Text, 10.2, brush, LabelTypeface);
        var badge = new Rect(rect.Left + 74, rect.Top + 6, Math.Min(230, text.Width + 18), 18);
        dc.DrawRoundedRectangle(CloneBrushOpacity(brush, 0.13), null, badge, 8, 8);
        dc.DrawText(text, new Point(badge.Left + 9, badge.Top + 2));
    }

    private static (string Severity, string Text) ResolveLaneShape(IReadOnlyList<WaveformSeriesModel> series)
    {
        if (series.Count == 0)
            return ("Unknown", "Shape pending");

        var ranked = series
            .Select(item => (Item: item, Rank: ShapeRank(item.ShapeSeverity)))
            .OrderByDescending(item => item.Rank)
            .ThenByDescending(item => item.Item.ShapeResidualPercent)
            .First();

        var selected = ranked.Item;
        return selected.ShapeSeverity switch
        {
            "Distorted" => ("Distorted", $"Distorted · {selected.Name} {selected.ShapeResidualPercent:0.#}%"),
            "Warning" => ("Warning", $"Shape watch · {selected.Name} {selected.ShapeResidualPercent:0.#}%"),
            "OK" => ("OK", "Shape OK"),
            "Low" => ("Low", "Low signal"),
            _ => ("Unknown", "Shape pending")
        };
    }

    private static int ShapeRank(string? severity)
    {
        return severity switch
        {
            "Distorted" => 4,
            "Warning" => 3,
            "Low" => 1,
            "OK" => 0,
            _ => 2
        };
    }

    private static Brush ResolveShapeBrush(string? severity)
    {
        return severity switch
        {
            "OK" => ShapeOkBrush,
            "Warning" => ShapeWarningBrush,
            "Distorted" => ShapeAlertBrush,
            _ => ShapeUnknownBrush
        };
    }

    private static SolidColorBrush CloneBrushOpacity(Brush brush, double opacity)
    {
        var color = brush is SolidColorBrush solid ? solid.Color : Colors.White;
        var clone = new SolidColorBrush(color) { Opacity = opacity };
        clone.Freeze();
        return clone;
    }

    private static Pen GetSeriesPen(string name)
    {
        if (SeriesPenCache.TryGetValue(name, out var pen))
            return pen;
        // Cache one frozen pen per channel, without glow or halo allocations.
        pen = CreateFrozenSeriesPen(GetSeriesBrush(name), 2.7);
        SeriesPenCache[name] = pen;
        return pen;
    }

    private static Pen CreateFrozenSeriesPen(Brush brush, double thickness)
    {
        var pen = new Pen(brush, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        pen.Freeze();
        return pen;
    }

    private static void DrawMarkers(DrawingContext dc, Rect rect, WaveformSeriesModel series, double amplitudeMax, bool stacked, int index, int totalSeries, Brush brush)
    {
        if (series.Samples.Count < 2)
            return;

        var markerIndices = new[] { 0, series.Samples.Count / 2, series.Samples.Count - 1 }.Distinct();
        foreach (var sampleIndex in markerIndices)
        {
            var point = GetPoint(rect, series.Samples, amplitudeMax, stacked, index, totalSeries, sampleIndex);
            dc.DrawEllipse(brush, null, point, 2.2, 2.2);
        }
    }

    private static Point GetPoint(Rect rect, IReadOnlyList<double> samples, double amplitudeMax, bool stacked, int index, int totalSeries, int sampleIndex)
    {
        var verticalBand = stacked ? rect.Height / Math.Max(1, totalSeries) : rect.Height;
        var bandTop = stacked ? rect.Top + (verticalBand * index) : rect.Top;
        var bandCenter = bandTop + (verticalBand / 2.0);
        var bandScale = (stacked ? verticalBand : rect.Height) * 0.34;
        var x = rect.Left + (rect.Width * sampleIndex / (samples.Count - 1.0));
        var normalized = amplitudeMax <= 0 ? 0 : samples[sampleIndex] / amplitudeMax;
        var y = bandCenter - (normalized * bandScale);
        return new Point(x, y);
    }

    private static double GetLastY(Rect rect, IReadOnlyList<double> samples, double amplitudeMax, bool stacked, int index, int totalSeries)
    {
        return GetPoint(rect, samples, amplitudeMax, stacked, index, totalSeries, samples.Count - 1).Y;
    }

    private static Pen GetCursorLeaderPen(string name) => name switch
    {
        "Ua" or "Ia" or "Ch1" or "Ch5" => CursorRedLeader,
        "Ub" or "Ib" or "Ch2" or "Ch6" => CursorGoldLeader,
        "Uc" or "Ic" or "Ch3" or "Ch7" => CursorBlueLeader,
        _ => CursorNeutralLeader
    };

    private void DrawCursor(
        DrawingContext dc,
        Rect rect,
        IReadOnlyList<WaveformSeriesModel> series,
        double amplitudeMax,
        bool stacked,
        WaveformSnapshot snapshot)
    {
        if (series.Count == 0)
            return;

        // The cursor is snapped to the visible plot position, not to a
        // separately sampled or fabricated set of amplitudes.
        var count = 0;
        foreach (var item in series)
            count = Math.Max(count, item.Samples.Count);
        if (count < 2)
            return;

        var sampleIndex = (int)Math.Round(_cursorFraction * (count - 1));
        var x = rect.Left + rect.Width * sampleIndex / (count - 1.0);
        dc.DrawLine(CursorPen, new Point(x, rect.Top), new Point(x, rect.Bottom));

        // Compute the *exact line-segment intersection* for each plotted phase.
        // The same 0.37 vertical transform is used by DrawSeries. This is not
        // a new DSP sample; the displayed value is explicitly a linear
        // interpolation between adjacent plotted vertices at the cursor.
        var anchors = new List<WaveformCursorCalloutLayout.Anchor>(series.Count);
        var texts = new List<FormattedText>(series.Count);
        var colors = new List<Pen>(series.Count);
        for (var index = 0; index < series.Count; index++)
        {
            var item = series[index];
            if (item.Samples.Count < 2)
                continue;

            var samplePosition = sampleIndex / (count - 1.0) * (item.Samples.Count - 1);
            var a = (int)Math.Floor(samplePosition);
            var b = Math.Min(a + 1, item.Samples.Count - 1);
            var t = samplePosition - a;
            var value = item.Samples[a] + (item.Samples[b] - item.Samples[a]) * t;
            if (!double.IsFinite(value))
                continue;

            var bandHeight = stacked ? rect.Height / series.Count : rect.Height;
            var bandCenter = rect.Top + (stacked ? (index + 0.5) * bandHeight : rect.Height / 2.0);
            var y = bandCenter - (amplitudeMax > 0 ? value / amplitudeMax : 0.0)
                * bandHeight * 0.37;
            y = Math.Clamp(y, rect.Top, rect.Bottom);

            var color = GetSeriesBrush(item.Name);
            var formatted = CreateText(
                item.Name + "  " + FormatEngineeringValue(value, item.Unit),
                10.5, color, LabelTypeface);
            anchors.Add(new WaveformCursorCalloutLayout.Anchor(texts.Count, y, formatted.Width + 17));
            texts.Add(formatted);
            colors.Add(GetCursorLeaderPen(item.Name));
        }

        var placements = WaveformCursorCalloutLayout.Arrange(rect, x, anchors);
        // First paint leader lines, then the compact labels on top. The arrowhead
        // tip sits precisely on the vertical cursor / phase-wave intersection.
        foreach (var placement in placements)
        {
            var tip = new Point(x, anchors[placement.Index].Y);
            var start = new Point(placement.LabelRight ? placement.Box.Left : placement.Box.Right,
                placement.Box.Top + placement.Box.Height / 2.0);
            DrawCalloutArrow(dc, colors[placement.Index], start, tip);
        }

        foreach (var placement in placements)
        {
            var box = placement.Box;
            dc.DrawRoundedRectangle(CursorLabelSurface, CursorLabelFrame, box, 5, 5);
            dc.DrawText(texts[placement.Index],
                new Point(box.Left + 8, box.Top + (box.Height - texts[placement.Index].Height) / 2.0));
        }

        // One small time caption, never a row of chips over the phase legend.
        if (snapshot.SampleRateHz > 0 && double.IsFinite(snapshot.SampleRateHz))
        {
            var windowSamples = Math.Min(CalculateVisibleSamples(snapshot),
                Math.Max(2, snapshot.VoltageSeries.Concat(snapshot.CurrentSeries)
                    .Select(item => item.Samples.Count).DefaultIfEmpty(2).Max()));
            var timeMs = _cursorFraction * Math.Max(0, windowSamples - 1) *
                1000.0 / snapshot.SampleRateHz;
            var caption = CreateText("t  " + timeMs.ToString("0.00", CultureInfo.InvariantCulture) + " ms",
                10.1, LabelBrush, LabelTypeface);
            var width = caption.Width + 12;
            var left = Math.Clamp(x - width / 2, rect.Left + 4,
                Math.Max(rect.Left + 4, rect.Right - width - 4));
            var badge = new Rect(left, rect.Bottom - 23, width, 20);
            dc.DrawRoundedRectangle(CursorLabelSurface, CursorLabelFrame, badge, 4, 4);
            dc.DrawText(caption, new Point(badge.Left + 6, badge.Top + 2));
        }
    }

    private static void DrawCalloutArrow(DrawingContext dc, Pen pen, Point start, Point tip)
    {
        var dx = tip.X - start.X;
        var dy = tip.Y - start.Y;
        var length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1.0)
            return;
        var ux = dx / length;
        var uy = dy / length;
        dc.DrawLine(pen, start, tip);
        // Directional open-arrow tips (no marker dot) terminate on the signal.
        const double arrowLength = 6.5;
        const double arrowHalfWidth = 3.0;
        var back = new Point(tip.X - ux * arrowLength, tip.Y - uy * arrowLength);
        dc.DrawLine(pen, tip, new Point(back.X - uy * arrowHalfWidth, back.Y + ux * arrowHalfWidth));
        dc.DrawLine(pen, tip, new Point(back.X + uy * arrowHalfWidth, back.Y - ux * arrowHalfWidth));
    }

    private static string FormatEngineeringValue(double value, string unit)
    {
        // Cursor precision is diagnostic, but concise: small engineering
        // signals must not disappear when rounded to one decimal place.
        if (string.Equals(unit, "V", StringComparison.OrdinalIgnoreCase) && Math.Abs(value) >= 1000.0)
            return $"{value / 1000.0:0.###} kV";

        if (string.Equals(unit, "A", StringComparison.OrdinalIgnoreCase) && Math.Abs(value) >= 1000.0)
            return $"{value / 1000.0:0.###} kA";

        return $"{value:0.###} {unit}";
    }

    private static void DrawTimebaseLabel(DrawingContext dc, Rect rect, WaveformSnapshot snapshot)
    {
        var sampleRateText = snapshot.SampleRateHz > 0 ? $"{snapshot.SampleRateHz:0.#} Hz sample" : "Sample rate pending";
        var frequencyText = snapshot.MeasuredFrequencyHz > 0 ? $"{snapshot.MeasuredFrequencyHz:0.###} Hz signal" : "Signal frequency pending";
        dc.DrawText(CreateText(sampleRateText, 10.5, MutedBrush, LabelTypeface), new Point(rect.Left, rect.Bottom + 3));
        dc.DrawText(CreateText(frequencyText, 10.5, MutedBrush, LabelTypeface), new Point(rect.Right - 106, rect.Bottom + 3));
    }

    // Presentation only: no fade animations/timers or per-sample state.
    public static double TraceOpacity(bool streamLive, ChannelFreshness freshness) =>
        streamLive ? ChannelFreshnessDisplay.Opacity(freshness) : 0.38;

    private static Brush GetSeriesBrush(string name)
    {
        return name switch
        {
            "Ua" or "Ia" or "Ch1" or "Ch5" => UaBrush,
            "Ub" or "Ib" or "Ch2" or "Ch6" => UbBrush,
            "Uc" or "Ic" or "Ch3" or "Ch7" => UcBrush,
            _ => NeutralBrush
        };
    }

    private static double GetSeriesThickness(string name)
    {
        return name.StartsWith("Ch", StringComparison.OrdinalIgnoreCase) ? 2.4 : 2.0;
    }

    private static void DrawCenteredMessage(DrawingContext dc, Rect bounds, string text)
    {
        var formatted = CreateText(text, 18, LabelBrush, TitleTypeface);
        dc.DrawText(formatted, new Point(bounds.Left + ((bounds.Width - formatted.Width) / 2.0), bounds.Top + ((bounds.Height - formatted.Height) / 2.0)));
    }

    private static FormattedText CreateText(string text, double size, Brush brush, Typeface typeface)
    {
        text = NormalizeDisplayText(text);
        return new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, size, brush, 1.0);
    }

    private static string NormalizeDisplayText(string text)
    {
        const string DoubleEncodedPlusMinus = "\u00C3\u201A\u00C2\u00B1";
        const string EncodedPlusMinus = "\u00C2\u00B1";
        const string PlusMinus = "\u00B1";

        if (!text.StartsWith("??", StringComparison.Ordinal) &&
            !text.StartsWith("+/-", StringComparison.Ordinal) &&
            !text.StartsWith(DoubleEncodedPlusMinus, StringComparison.Ordinal) &&
            !text.StartsWith(EncodedPlusMinus, StringComparison.Ordinal) &&
            !text.StartsWith(PlusMinus, StringComparison.Ordinal))
            return text;

        var normalized = text.StartsWith("??", StringComparison.Ordinal)
            ? text[2..].Trim()
            : text.StartsWith(DoubleEncodedPlusMinus, StringComparison.Ordinal)
                ? text[DoubleEncodedPlusMinus.Length..].Trim()
                : text.StartsWith(EncodedPlusMinus, StringComparison.Ordinal)
                    ? text[EncodedPlusMinus.Length..].Trim()
                    : text.StartsWith(PlusMinus, StringComparison.Ordinal)
                        ? text[PlusMinus.Length..].Trim()
                        : text[3..].Trim();
        var parts = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var peak))
            return text.Replace("??", "+/-", StringComparison.Ordinal);

        return FormatAxisLabel(peak, parts[1]);
    }

    private static Brush CreateBrush(string hex)
    {
        var brush = (SolidColorBrush)new BrushConverter().ConvertFromString(hex)!;
        brush.Freeze();
        return brush;
    }

    private static Pen CreatePen(string hex, double thickness, double[]? dash = null)
    {
        var pen = new Pen(CreateBrush(hex), thickness);
        if (dash is not null)
            pen.DashStyle = new DashStyle(dash, 0);
        pen.Freeze();
        return pen;
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Left || !MarkersEnabled)
            return;
        Focus();
        _isDraggingCursor = true;
        CaptureMouse();
        UpdateCursorPosition(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        // Passive hover costs nothing until the nearest *source sample* changes.
        // The control has at most two lanes and already owns this drawing surface.
        if (MarkersEnabled)
            UpdateCursorPosition(e.GetPosition(this));
    }

    protected override void OnMouseLeave(MouseEventArgs e)
    {
        base.OnMouseLeave(e);
        if (!_isDraggingCursor)
            HideCursor();
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Left)
            return;
        _isDraggingCursor = false;
        if (IsMouseCaptured)
            ReleaseMouseCapture();
        UpdateCursorPosition(e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        _isDraggingCursor = false;
        if (!IsMouseOver)
            HideCursor();
    }

    private void HideCursor()
    {
        if (!_cursorVisible) return;
        _cursorVisible = false;
        _cursorLane = -1;
        _cursorSampleIndex = -1;
        InvalidateVisual();
    }

    private void UpdateCursorPosition(Point mouse)
    {
        var snapshot = Snapshot;
        if (snapshot is null)
        {
            HideCursor();
            return;
        }

        const double outerPadding = 10;
        const double laneGap = 12;
        const double plotInsetX = 12;
        var availableWidth = ActualWidth - 2 * (outerPadding + plotInsetX);
        var showBoth = string.Equals(ShowMode, "Both", StringComparison.OrdinalIgnoreCase);
        var lanes = showBoth ? 2 : 1;
        var laneHeight = Math.Max(110, (ActualHeight - 2 * outerPadding - laneGap * (lanes - 1)) / lanes);

        if (availableWidth <= 20 || mouse.X < outerPadding + plotInsetX ||
            mouse.X > ActualWidth - outerPadding - plotInsetX)
        {
            HideCursor();
            return;
        }
        var lane = -1;
        for (var i = 0; i < lanes; i++)
        {
            var laneTop = outerPadding + i * (laneHeight + laneGap);
            if (mouse.Y >= laneTop + 38 && mouse.Y <= laneTop + laneHeight - 12)
            {
                lane = i;
                break;
            }
        }
        if (lane < 0)
        {
            HideCursor();
            return;
        }

        var fraction = Math.Clamp((mouse.X - outerPadding - plotInsetX) / availableWidth, 0.0, 1.0);
        // The pointer hot path must not create LINQ enumerators or temporary
        // arrays; received data is already bounded and read-only.
        var count = 2;
        foreach (var series in snapshot.VoltageSeries)
            count = Math.Max(count, series.Samples.Count);
        foreach (var series in snapshot.CurrentSeries)
            count = Math.Max(count, series.Samples.Count);
        var sampleIndex = (int)Math.Round(fraction * (count - 1));
        if (_cursorVisible && _cursorLane == lane && _cursorSampleIndex == sampleIndex)
            return; // sample quantization avoids per-pixel redraw on high-rate mouse moves.

        _cursorFraction = fraction;
        _cursorVisible = true;
        _cursorLane = lane;
        _cursorSampleIndex = sampleIndex;
        InvalidateVisual();
    }

    private sealed record LaneDefinition(string Title, string Unit, IReadOnlyList<WaveformSeriesModel> Series);

}
