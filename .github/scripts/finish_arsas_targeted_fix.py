from pathlib import Path

runtime = Path("Services/Iec61850MonitorRuntime.cs").read_text(encoding="utf-8")
command_ui = Path("ControlCommandWindow.xaml.cs").read_text(encoding="utf-8")
phasor = Path("Controls/ComtradePhasorView.cs").read_text(encoding="utf-8")
harmonics = Path("Controls/ComtradeHarmonicsWorkstationView.cs").read_text(encoding="utf-8")

required = [
    ("runtime command/process separation", "ApplyControlFeedbackToMonitor(session, request.Signal, result.FeedbackValue)" not in runtime),
    ("runtime authority comment", "not process-image authority" in runtime),
    ("command UI does not overwrite CurrentValue", "CurrentValue = result.FeedbackValue" not in command_ui),
    ("command UI waits for IED process feedback", "Waiting for independent IED process feedback" in command_ui),
    ("phasor smoothing", "SmoothVectors(" in phasor and "SmoothAngleDegrees" in phasor),
    ("harmonic smoothing", "SmoothSpectra(" in harmonics and "PresentationTimeConstantMs = 92.0" in harmonics),
    ("shared easing helper", Path("Services/PresentationEasingMath.cs").exists()),
]
for label, ok in required:
    if not ok:
        raise SystemExit(f"Targeted patch validation failed: {label}")

g1 = Path("tests/ARSAS.Tests/G1ControlCorrectnessRegressionTests.cs")
g1_text = g1.read_text(encoding="utf-8")
if "OperateSuccess_DoesNotInjectCommandFeedbackIntoMonitoredProcessState" not in g1_text:
    anchor = "public sealed class G1ControlCorrectnessRegressionTests\n{\n"
    if anchor not in g1_text:
        raise SystemExit("G1 class insertion anchor not found")
    test_block = r'''    [Fact]
    public void OperateSuccess_DoesNotInjectCommandFeedbackIntoMonitoredProcessState()
    {
        var root = RepoRoot();
        var runtimeSource = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));
        var commandWindowSource = File.ReadAllText(Path.Combine(root, "ControlCommandWindow.xaml.cs"));

        Assert.DoesNotContain("ApplyControlFeedbackToMonitor(session, request.Signal, result.FeedbackValue)", runtimeSource, StringComparison.Ordinal);
        Assert.DoesNotContain("CurrentValue = result.FeedbackValue", commandWindowSource, StringComparison.Ordinal);
        Assert.Contains("Waiting for independent IED process feedback", commandWindowSource, StringComparison.Ordinal);
        Assert.Contains("not process-image authority", runtimeSource, StringComparison.Ordinal);
    }

'''
    g1_text = g1_text.replace(anchor, anchor + test_block, 1)
    g1.write_text(g1_text, encoding="utf-8")

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
