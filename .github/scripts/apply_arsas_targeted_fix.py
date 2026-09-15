from pathlib import Path


def replace_exact(path: str, old: str, new: str) -> None:
    p = Path(path)
    text = p.read_text(encoding="utf-8")
    if old not in text:
        raise SystemExit(f"Expected source block not found in {path}")
    p.write_text(text.replace(old, new, 1), encoding="utf-8")


# 1) Strict IEC 61850 control/process separation: never inject command result into monitored stVal.
replace_exact(
    "Services/Iec61850MonitorRuntime.cs",
    '''        if (!request.TestMode && result.FeedbackConfirmed && !string.IsNullOrWhiteSpace(result.FeedbackValue) && result.FeedbackValue != "-")
            ApplyControlFeedbackToMonitor(session, request.Signal, result.FeedbackValue);

''',
    '''        // IMPORTANT: a successful/confirmed IEC 61850 control service is command-path evidence,
        // not process-image authority. Never synthesize or inject stVal from Operate/SBO feedback here.
        // The monitored state changes only through the independent acquisition path (RCB/report or
        // an explicit authoritative MMS read performed by the monitor), matching IED engineering tools.

''')

# 2) Command window must not display the command result as if it were the actual process position.
replace_exact(
    "ControlCommandWindow.xaml.cs",
    '''            CommandStage = result.Stage;
            CommandStatus = BuildCommandResultText(result);
            if (!string.IsNullOrWhiteSpace(result.FeedbackValue) && result.FeedbackValue != "-")
                CurrentValue = result.FeedbackValue;
            SetResultTone(result.IsSuccess ? "Success" : "Error");
''',
    '''            CommandStage = result.IsSuccess && !TestMode ? "Command accepted" : result.Stage;
            CommandStatus = BuildCommandResultText(result);
            if (result.IsSuccess && !TestMode)
            {
                CommandStatus += " Command accepted by the IEC 61850 control service. Waiting for independent IED process feedback; monitored stVal is not changed from the command path.";
            }
            SetResultTone(result.IsSuccess ? "Success" : "Error");
''')
replace_exact(
    "ControlCommandWindow.xaml.cs",
    '''        if (!string.IsNullOrWhiteSpace(result.FeedbackElapsedText) && result.FeedbackElapsedText != "-")
            details.Add($"Process feedback: {result.FeedbackElapsedText}.");
''',
    '''        if (!string.IsNullOrWhiteSpace(result.FeedbackElapsedText) && result.FeedbackElapsedText != "-")
            details.Add($"Control-side feedback verification: {result.FeedbackElapsedText}. This does not overwrite monitored stVal.");
''')

# 3) Shared time-based, allocation-free easing math.
Path("Services/PresentationEasingMath.cs").write_text(r'''namespace ArIED61850Tester.Services;

/// <summary>
/// Allocation-free presentation easing helpers. These functions are intentionally presentation-only:
/// raw COMTRADE/IEC 61850 engineering values remain untouched and authoritative.
/// </summary>
public static class PresentationEasingMath
{
    public static double ExponentialAlpha(double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(timeConstantMilliseconds) || timeConstantMilliseconds <= 0.0)
            return 1.0;
        if (double.IsPositiveInfinity(elapsedMilliseconds))
            return 1.0;
        if (!double.IsFinite(elapsedMilliseconds) || elapsedMilliseconds <= 0.0)
            return 0.0;

        var alpha = 1.0 - Math.Exp(-elapsedMilliseconds / timeConstantMilliseconds);
        return Math.Clamp(alpha, 0.0, 1.0);
    }

    public static double Smooth(double current, double target, double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(target)) return current;
        if (!double.IsFinite(current)) return target;
        var alpha = ExponentialAlpha(elapsedMilliseconds, timeConstantMilliseconds);
        return current + ((target - current) * alpha);
    }

    public static double ShortestAngleDeltaDegrees(double currentDegrees, double targetDegrees)
    {
        if (!double.IsFinite(currentDegrees) || !double.IsFinite(targetDegrees))
            return 0.0;
        var delta = (targetDegrees - currentDegrees) % 360.0;
        if (delta >= 180.0) delta -= 360.0;
        if (delta < -180.0) delta += 360.0;
        return delta;
    }

    public static double NormalizeAngleDegrees(double degrees)
    {
        if (!double.IsFinite(degrees)) return 0.0;
        var normalized = degrees % 360.0;
        if (normalized >= 180.0) normalized -= 360.0;
        if (normalized < -180.0) normalized += 360.0;
        return normalized;
    }

    public static double SmoothAngleDegrees(double currentDegrees, double targetDegrees, double elapsedMilliseconds, double timeConstantMilliseconds)
    {
        if (!double.IsFinite(targetDegrees)) return NormalizeAngleDegrees(currentDegrees);
        if (!double.IsFinite(currentDegrees)) return NormalizeAngleDegrees(targetDegrees);
        var alpha = ExponentialAlpha(elapsedMilliseconds, timeConstantMilliseconds);
        return NormalizeAngleDegrees(currentDegrees + (ShortestAngleDeltaDegrees(currentDegrees, targetDegrees) * alpha));
    }
}
''', encoding="utf-8")

# 4) Phasor smoothing happens only in the presentation layer, at incoming analysis-result cadence.
replace_exact(
    "Controls/ComtradePhasorView.cs",
    '''using System.Globalization;
using System.Windows;
using System.Windows.Media;
''',
    '''using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using ArIED61850Tester.Services;
''')
replace_exact(
    "Controls/ComtradePhasorView.cs",
    '''    private string _headerLabel = "Fundamental phasors at C1";
    private string _referenceDetail = "Select a valid analysis reference";
    private string _message = string.Empty;
''',
    '''    private const double PresentationTimeConstantMs = 78.0;
    private string _headerLabel = "Fundamental phasors at C1";
    private string _referenceDetail = "Select a valid analysis reference";
    private string _message = string.Empty;
    private ComtradePhasorVector[] _smoothedVoltageVectors = Array.Empty<ComtradePhasorVector>();
    private ComtradePhasorVector[] _smoothedCurrentVectors = Array.Empty<ComtradePhasorVector>();
    private long _lastPresentationTimestamp;
''')
replace_exact(
    "Controls/ComtradePhasorView.cs",
    '''        var resolvedReference = string.IsNullOrWhiteSpace(referenceLabel) ? "Reference" : referenceLabel;
        _headerLabel = $"Fundamental phasors at {resolvedReference}";
        _referenceDetail = referenceDetail ?? string.Empty;
        _voltagePanel = PreparePanel(voltageVectors);
        _currentPanel = PreparePanel(currentVectors);
        _message = string.Empty;
        InvalidateVisual();
''',
    '''        var resolvedReference = string.IsNullOrWhiteSpace(referenceLabel) ? "Reference" : referenceLabel;
        _headerLabel = $"Fundamental phasors at {resolvedReference}";
        _referenceDetail = referenceDetail ?? string.Empty;

        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = _lastPresentationTimestamp == 0
            ? double.PositiveInfinity
            : Stopwatch.GetElapsedTime(_lastPresentationTimestamp, now).TotalMilliseconds;
        _lastPresentationTimestamp = now;

        _smoothedVoltageVectors = SmoothVectors(_smoothedVoltageVectors, voltageVectors, elapsedMilliseconds);
        _smoothedCurrentVectors = SmoothVectors(_smoothedCurrentVectors, currentVectors, elapsedMilliseconds);
        _voltagePanel = PreparePanel(_smoothedVoltageVectors);
        _currentPanel = PreparePanel(_smoothedCurrentVectors);
        _message = string.Empty;
        InvalidateVisual();
''')
replace_exact(
    "Controls/ComtradePhasorView.cs",
    '''        _voltagePanel = PreparedPhasorPanel.Empty;
        _currentPanel = PreparedPhasorPanel.Empty;
        _message = message ?? string.Empty;
        InvalidateVisual();
''',
    '''        _voltagePanel = PreparedPhasorPanel.Empty;
        _currentPanel = PreparedPhasorPanel.Empty;
        _smoothedVoltageVectors = Array.Empty<ComtradePhasorVector>();
        _smoothedCurrentVectors = Array.Empty<ComtradePhasorVector>();
        _lastPresentationTimestamp = 0;
        _message = message ?? string.Empty;
        InvalidateVisual();
''')
replace_exact(
    "Controls/ComtradePhasorView.cs",
    '''    private static PreparedPhasorPanel PreparePanel(IReadOnlyList<ComtradePhasorVector>? source)
''',
    '''    private static ComtradePhasorVector[] SmoothVectors(
        IReadOnlyList<ComtradePhasorVector> previous,
        IReadOnlyList<ComtradePhasorVector>? target,
        double elapsedMilliseconds)
    {
        if (target is null || target.Count == 0)
            return Array.Empty<ComtradePhasorVector>();

        var topologyMatches = previous.Count == target.Count && previous.Count > 0;
        if (topologyMatches)
        {
            for (var index = 0; index < target.Count; index++)
            {
                if (!string.Equals(previous[index].Label, target[index].Label, StringComparison.Ordinal) ||
                    !string.Equals(previous[index].Phase, target[index].Phase, StringComparison.Ordinal) ||
                    !string.Equals(previous[index].Units, target[index].Units, StringComparison.Ordinal))
                {
                    topologyMatches = false;
                    break;
                }
            }
        }

        var output = new ComtradePhasorVector[target.Count];
        if (!topologyMatches)
        {
            for (var index = 0; index < target.Count; index++)
                output[index] = target[index];
            return output; // First sample/topology change snaps: no artificial ramp from zero.
        }

        for (var index = 0; index < target.Count; index++)
        {
            var before = previous[index];
            var next = target[index];
            output[index] = new ComtradePhasorVector(
                next.Label,
                next.Phase,
                next.Units,
                PresentationEasingMath.Smooth(before.MagnitudeRms, next.MagnitudeRms, elapsedMilliseconds, PresentationTimeConstantMs),
                PresentationEasingMath.SmoothAngleDegrees(before.AngleDegrees, next.AngleDegrees, elapsedMilliseconds, PresentationTimeConstantMs));
        }
        return output;
    }

    private static PreparedPhasorPanel PreparePanel(IReadOnlyList<ComtradePhasorVector>? source)
''')

# 5) Harmonics workstation smoothing, also display-only and time-based.
replace_exact(
    "Controls/ComtradeHarmonicsWorkstationView.cs",
    '''using System.Globalization;
using System.Windows;
''',
    '''using System.Diagnostics;
using System.Globalization;
using System.Windows;
''')
replace_exact(
    "Controls/ComtradeHarmonicsWorkstationView.cs",
    '''    private IReadOnlyList<ComtradeHarmonicOverviewSpectrum> _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private PreparedSpectrumRow[] _preparedRows = Array.Empty<PreparedSpectrumRow>();
''',
    '''    private const double PresentationTimeConstantMs = 92.0;
    private IReadOnlyList<ComtradeHarmonicOverviewSpectrum> _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private ComtradeHarmonicOverviewSpectrum[] _smoothedSpectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
    private long _lastPresentationTimestamp;
    private PreparedSpectrumRow[] _preparedRows = Array.Empty<PreparedSpectrumRow>();
''')
replace_exact(
    "Controls/ComtradeHarmonicsWorkstationView.cs",
    '''        _title = title ?? string.Empty;
        _subtitle = subtitle ?? string.Empty;
        _spectra = spectra ?? Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _maximumDisplayedOrder = ResolveMaximumDisplayedOrder(_spectra);
''',
    '''        _title = title ?? string.Empty;
        _subtitle = subtitle ?? string.Empty;
        var targetSpectra = spectra ?? Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        var now = Stopwatch.GetTimestamp();
        var elapsedMilliseconds = _lastPresentationTimestamp == 0
            ? double.PositiveInfinity
            : Stopwatch.GetElapsedTime(_lastPresentationTimestamp, now).TotalMilliseconds;
        _lastPresentationTimestamp = now;
        _smoothedSpectra = SmoothSpectra(_smoothedSpectra, targetSpectra, elapsedMilliseconds);
        _spectra = _smoothedSpectra;
        _maximumDisplayedOrder = ResolveMaximumDisplayedOrder(_spectra);
''')
replace_exact(
    "Controls/ComtradeHarmonicsWorkstationView.cs",
    '''        _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _preparedRows = Array.Empty<PreparedSpectrumRow>();
        _maximumDisplayedOrder = -1;
''',
    '''        _spectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _smoothedSpectra = Array.Empty<ComtradeHarmonicOverviewSpectrum>();
        _lastPresentationTimestamp = 0;
        _preparedRows = Array.Empty<PreparedSpectrumRow>();
        _maximumDisplayedOrder = -1;
''')
replace_exact(
    "Controls/ComtradeHarmonicsWorkstationView.cs",
    '''    private static PreparedSpectrumRow[] PrepareRows(
''',
    '''    private static ComtradeHarmonicOverviewSpectrum[] SmoothSpectra(
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> previous,
        IReadOnlyList<ComtradeHarmonicOverviewSpectrum> target,
        double elapsedMilliseconds)
    {
        if (target.Count == 0)
            return Array.Empty<ComtradeHarmonicOverviewSpectrum>();

        var topologyMatches = previous.Count == target.Count && previous.Count > 0;
        if (topologyMatches)
        {
            for (var spectrumIndex = 0; spectrumIndex < target.Count; spectrumIndex++)
            {
                var before = previous[spectrumIndex];
                var next = target[spectrumIndex];
                if (!string.Equals(before.SignalName, next.SignalName, StringComparison.Ordinal) ||
                    !string.Equals(before.Units, next.Units, StringComparison.Ordinal) ||
                    before.Bins.Count != next.Bins.Count)
                {
                    topologyMatches = false;
                    break;
                }
                for (var binIndex = 0; binIndex < next.Bins.Count; binIndex++)
                {
                    if (before.Bins[binIndex].Order != next.Bins[binIndex].Order)
                    {
                        topologyMatches = false;
                        break;
                    }
                }
                if (!topologyMatches) break;
            }
        }

        var result = new ComtradeHarmonicOverviewSpectrum[target.Count];
        for (var spectrumIndex = 0; spectrumIndex < target.Count; spectrumIndex++)
        {
            var next = target[spectrumIndex];
            if (!topologyMatches)
            {
                result[spectrumIndex] = next with { Bins = next.Bins.ToArray() };
                continue; // First sample/channel-set change snaps; never invent a ramp from zero.
            }

            var before = previous[spectrumIndex];
            var bins = new ComtradeHarmonicDisplayBin[next.Bins.Count];
            for (var binIndex = 0; binIndex < bins.Length; binIndex++)
            {
                var previousBin = before.Bins[binIndex];
                var targetBin = next.Bins[binIndex];
                bins[binIndex] = new ComtradeHarmonicDisplayBin(
                    targetBin.Order,
                    PresentationEasingMath.Smooth(previousBin.MagnitudeRms, targetBin.MagnitudeRms, elapsedMilliseconds, PresentationTimeConstantMs),
                    PresentationEasingMath.Smooth(previousBin.PercentOfFundamental, targetBin.PercentOfFundamental, elapsedMilliseconds, PresentationTimeConstantMs),
                    PresentationEasingMath.SmoothAngleDegrees(previousBin.AngleDegrees, targetBin.AngleDegrees, elapsedMilliseconds, PresentationTimeConstantMs));
            }

            result[spectrumIndex] = new ComtradeHarmonicOverviewSpectrum(
                next.SignalName,
                next.Units,
                PresentationEasingMath.Smooth(before.DcComponent, next.DcComponent, elapsedMilliseconds, PresentationTimeConstantMs),
                PresentationEasingMath.Smooth(before.FundamentalRms, next.FundamentalRms, elapsedMilliseconds, PresentationTimeConstantMs),
                PresentationEasingMath.Smooth(before.ThdPercent, next.ThdPercent, elapsedMilliseconds, PresentationTimeConstantMs),
                next.DominantOrder,
                PresentationEasingMath.Smooth(before.DominantRms, next.DominantRms, elapsedMilliseconds, PresentationTimeConstantMs),
                PresentationEasingMath.Smooth(before.DominantPercent, next.DominantPercent, elapsedMilliseconds, PresentationTimeConstantMs),
                next.EstimatedSampleRateHz,
                next.MaximumResolvableOrder,
                bins);
        }
        return result;
    }

    private static PreparedSpectrumRow[] PrepareRows(
''')

# 6) Regression tests.
g1 = Path("tests/ARSAS.Tests/G1ControlCorrectnessRegressionTests.cs")
g1_text = g1.read_text(encoding="utf-8")
anchor = '''    [Fact]
    public void CdcDerivation_MapsRepresentativeControlModelClasses()
'''
if anchor not in g1_text:
    raise SystemExit("G1 test insertion anchor not found")
test_block = r'''    [Fact]
    public void OperateSuccess_DoesNotInjectCommandFeedbackIntoMonitoredProcessState()
    {
        var runtimeSource = File.ReadAllText(Path.GetFullPath("../../../../Services/Iec61850MonitorRuntime.cs"));
        var commandWindowSource = File.ReadAllText(Path.GetFullPath("../../../../ControlCommandWindow.xaml.cs"));

        Assert.DoesNotContain("ApplyControlFeedbackToMonitor(session, request.Signal, result.FeedbackValue)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentValue = result.FeedbackValue", commandWindowSource, StringComparison.Ordinal);
        Assert.Contains("Waiting for independent IED process feedback", commandWindowSource, StringComparison.Ordinal);
        Assert.Contains("not process-image authority", runtimeSource, StringComparison.Ordinal);
    }

'''
g1.write_text(g1_text.replace(anchor, test_block + anchor, 1), encoding="utf-8")

Path("tests/ARSAS.Tests/PresentationEasingMathTests.cs").write_text(r'''using ArIED61850Tester.Services;
using Xunit;

namespace ARSAS.Tests;

public sealed class PresentationEasingMathTests
{
    [Fact]
    public void ExponentialSmoothing_IsTimeBased_NotFrameCountBased()
    {
        const double tau = 80.0;
        var halfStep = PresentationEasingMath.Smooth(0.0, 1.0, 16.67, tau);
        var twoSteps = PresentationEasingMath.Smooth(halfStep, 1.0, 16.67, tau);
        var oneStep = PresentationEasingMath.Smooth(0.0, 1.0, 33.34, tau);
        Assert.Equal(oneStep, twoSteps, 12);
    }

    [Fact]
    public void AngleSmoothing_UsesShortestPathAcrossPlusMinus180()
    {
        Assert.Equal(2.0, PresentationEasingMath.ShortestAngleDeltaDegrees(179.0, -179.0), 10);
        Assert.Equal(-2.0, PresentationEasingMath.ShortestAngleDeltaDegrees(-179.0, 179.0), 10);
    }

    [Fact]
    public void InfiniteElapsedTime_SnapsToTarget()
    {
        Assert.Equal(1.0, PresentationEasingMath.ExponentialAlpha(double.PositiveInfinity, 80.0));
        Assert.Equal(42.0, PresentationEasingMath.Smooth(10.0, 42.0, double.PositiveInfinity, 80.0), 10);
    }
}
''', encoding="utf-8")
