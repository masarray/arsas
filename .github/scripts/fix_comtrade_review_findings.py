from pathlib import Path


def cut(text: str, start_marker: str, end_marker: str, replacement: str, label: str) -> str:
    start = text.find(start_marker)
    end = text.find(end_marker, start + 1) if start >= 0 else -1
    if start < 0 or end < 0:
        raise SystemExit(f"{label}: markers missing")
    return text[:start] + replacement + text[end:]


def replace_once(text: str, old: str, new: str, label: str) -> str:
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected one match, got {count}")
    return text.replace(old, new, 1)


# P1: preserve composition-frame elapsed time when a fresh native target arrives
# while the animation pump is already active. A retarget restarts only the settle
# deadline, not the previous-frame clock.
phasor_path = Path("Controls/ComtradePhasorView.cs")
phasor = phasor_path.read_text(encoding="utf-8")
phasor_start = """    private void StartPresentationAnimation()
    {
        var now = Stopwatch.GetTimestamp();
        _presentationAnimationStartedTimestamp = now;
        if (_presentationRenderingHooked) return;
        _lastPresentationTimestamp = now;
        CompositionTarget.Rendering += PresentationCompositionFrame;
        _presentationRenderingHooked = true;
    }

"""
phasor = cut(
    phasor,
    "    private void StartPresentationAnimation()",
    "private void StopPresentationAnimation()",
    phasor_start,
    "phasor StartPresentationAnimation")
phasor_path.write_text(phasor, encoding="utf-8", newline="\n")


# P2: harmonics must not rebuild arrays, spectrum/bin records, brushes or labels
# at composition cadence. Native results update exact target buffers only when a
# new analysis result arrives; rendering frames mutate only reusable numeric bins.
harmonic_path = Path("Controls/ComtradeHarmonicsWorkstationView.cs")
harmonic = harmonic_path.read_text(encoding="utf-8")

harmonic = replace_once(
    harmonic,
    """    private const double PresentationTimeConstantMs = 92.0;
    private const double PresentationAnimationMaximumMs = 300.0;
    private IReadOnlyList<ComtradeHarmonicOverviewSpectrum> _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private ComtradeHarmonicOverviewSpectrum[] _targetSpectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private ComtradeHarmonicOverviewSpectrum[] _smoothedSpectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private bool _presentationRenderingHooked;
    private long _lastPresentationTimestamp;
    private long _presentationAnimationStartedTimestamp;
""",
    """    private const double PresentationTimeConstantMs = 92.0;
    private const double PresentationAnimationMaximumMs = 300.0;
    private bool _presentationRenderingHooked;
    private long _lastPresentationTimestamp;
    private long _presentationAnimationStartedTimestamp;
""",
    "harmonic fields")

new_show = """    internal void ShowSpectra(
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

"""
harmonic = cut(
    harmonic,
    "    internal void ShowSpectra(",
    "    internal void ShowMessage(",
    new_show,
    "harmonic ShowSpectra")

new_message = """    internal void ShowMessage(string title, string message)
    {
        _title = title ?? string.Empty;
        _subtitle = message ?? string.Empty;
        StopPresentationAnimation();
        _preparedRows = Array.Empty<PreparedSpectrumRow>();
        _maximumDisplayedOrder = -1;
        _rowTargets.Clear();
        InvalidateVisual();
    }

"""
harmonic = cut(
    harmonic,
    "    internal void ShowMessage(",
    "    protected override void OnRender(",
    new_message,
    "harmonic ShowMessage")

# Draw exact target labels cached at native-result cadence while only geometry eases.
harmonic = replace_once(harmonic, "prepared.ThdLabel", "prepared.TargetThdLabel", "THD draw")
harmonic = replace_once(
    harmonic,
    "prepared.AxisMaximum, prepared.AxisTopLabel, dpi",
    "prepared.AxisMaximum, prepared.TargetAxisTopLabel, dpi",
    "axis label draw")
harmonic = replace_once(harmonic, "bin.PercentLabel", "prepared.TargetPercentLabels[order]", "percent draw")
harmonic = replace_once(harmonic, "bin.MagnitudeLabel", "prepared.TargetMagnitudeLabels[order]", "magnitude draw")

helpers = """    private void StartPresentationAnimation()
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

"""
harmonic = cut(
    harmonic,
    "    private void StartPresentationAnimation()",
    "    private static int ResolveMaximumDisplayedOrder(",
    helpers,
    "harmonic presentation helper block")

record_block = """    private readonly record struct PlotBin(
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

"""
harmonic = cut(
    harmonic,
    "    private readonly record struct PlotBin(",
    "    private readonly record struct RowHitTarget(",
    record_block,
    "harmonic prepared records")

old_summary = """/// Render-path rule: spectrum normalization, harmonic lookup, engineering-value formatting and
/// brush creation happen only when ShowSpectra receives a new immutable result. OnRender consumes
/// prepared rows and only performs geometry/text drawing required for the current element size.
"""
new_summary = """/// Render-path rule: native results update exact cached targets at analysis cadence. Static row
/// assets and reusable numeric buffers are rebuilt only on topology changes; composition frames
/// mutate only numeric presentation values and never rebuild brushes, labels or per-bin arrays.
"""
harmonic = replace_once(harmonic, old_summary, new_summary, "harmonic summary")
harmonic_path.write_text(harmonic, encoding="utf-8", newline="\n")


# Regression locks P1 frame-clock ordering, P2 reusable harmonic buffers, and
# the existing latest-wins single-worker native scheduler.
test_path = Path("tests/ARSAS.Tests/ComtradePresentationAnimationRegressionTests.cs")
test_path.write_text(
    """namespace ARSAS.Tests;

public sealed class ComtradePresentationAnimationRegressionTests
{
    [Fact]
    public void PresentationViews_PreserveFrameClock_AndReuseHarmonicBuffers()
    {
        var phasor = File.ReadAllText(FindRepoFile("Controls/ComtradePhasorView.cs"));
        var harmonic = File.ReadAllText(FindRepoFile("Controls/ComtradeHarmonicsWorkstationView.cs"));

        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", phasor, StringComparison.Ordinal);
        Assert.Contains("_presentationAnimationStartedTimestamp = now;", phasor, StringComparison.Ordinal);
        Assert.Contains("if (_presentationRenderingHooked) return;", phasor, StringComparison.Ordinal);

        Assert.Contains("CompositionTarget.Rendering += PresentationCompositionFrame", harmonic, StringComparison.Ordinal);
        Assert.Contains("AdvancePreparedRows(_preparedRows, elapsedMilliseconds)", harmonic, StringComparison.Ordinal);
        Assert.Contains("TargetMagnitudes", harmonic, StringComparison.Ordinal);
        Assert.Contains("TargetMagnitudeLabels", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshPreparedSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("SmoothSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("_smoothedSpectra", harmonic, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", phasor, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", harmonic, StringComparison.Ordinal);
    }

    [Fact]
    public void NativeScrub_RemainsLatestWinsSingleWorker()
    {
        var source = File.ReadAllText(FindRepoFile("ComtradeWorkspaceWindow.P1D4LiveScrub.cs"));
        Assert.Contains("if (_p1d4ScrubWorkerRunning || !_p1d4ScrubDirty)", source, StringComparison.Ordinal);
        Assert.Contains("_p1d4ScrubWorkerRunning = true", source, StringComparison.Ordinal);
        Assert.Contains("if (_p1d4ScrubDirty && _analysisMode != AnalysisMode.Waveform)", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
""",
    encoding="utf-8",
    newline="\n")


# Fail closed if the intended hot-path architecture did not land.
for path in (phasor_path, harmonic_path):
    source = path.read_text(encoding="utf-8")
    start = source.index("private void StartPresentationAnimation()")
    stop = source.index("private void StopPresentationAnimation()", start)
    block = source[start:stop]
    deadline = block.index("_presentationAnimationStartedTimestamp = now;")
    hooked = block.index("if (_presentationRenderingHooked) return;")
    frame_clock = block.index("_lastPresentationTimestamp = now;")
    if not deadline < hooked < frame_clock:
        raise SystemExit(f"{path}: retarget/frame-clock ordering regression")
    if "DispatcherTimer" in source:
        raise SystemExit(f"{path}: DispatcherTimer forbidden")

harmonic_source = harmonic_path.read_text(encoding="utf-8")
for forbidden in ("RefreshPreparedSpectra", "SmoothSpectra", "_smoothedSpectra"):
    if forbidden in harmonic_source:
        raise SystemExit(f"harmonic hot-path allocation helper still present: {forbidden}")

required = (
    "AdvancePreparedRows(_preparedRows, elapsedMilliseconds)",
    "TargetMagnitudes",
    "TargetAngles",
    "TargetPercentLabels",
    "TargetMagnitudeLabels",
)
for token in required:
    if token not in harmonic_source:
        raise SystemExit(f"harmonic reusable-buffer contract missing: {token}")
