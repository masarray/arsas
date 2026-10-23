using System.IO;
using ArIED61850Tester.Services;
using ArIED61850Tester.Views;

namespace ARSAS.Tests;

/// <summary>
/// Physical FAT expectations: the SV receiver must measure wire samples, not
/// rewrite results to match a publisher's configured fundamental setpoint.
/// </summary>
public sealed class SmvMeasurementFidelityAndCursorRegressionTests
{
    [Theory]
    [InlineData(0.493, "A", "0.493 A")]
    [InlineData(999.999, "V", "999.999 V")]
    [InlineData(1000, "V", "1 kV")]
    [InlineData(5735.6, "V", "5.736 kV")]
    [InlineData(1000, "A", "1 kA")]
    [InlineData(1600, "A", "1.6 kA")]
    [InlineData(5999.377, "counts", "5999.377")]
    public void RmsFormatting_UsesExplicitVerifiedUnitsWithoutChangingNumericSamples(
        double value, string unit, string expected)
    {
        var row = new SmvDisplayMeasurement(1, "Ia", unit, value, value, 0, true);
        Assert.Equal(expected, row.RmsText);
        Assert.Equal(value, row.Rms);
        Assert.StartsWith("∠", row.AngleText);
        Assert.Contains("Total RMS", row.RmsDetail);
        Assert.Contains("fundamental", row.RmsDetail);
    }

    [Fact]
    public void PhaseReference_UsesUaFor9_2LeAndCh1ForRaw()
    {
        var vectors = new[]
        {
            new SmvRawVectorMeasurement(1, "Ch1", 1000, 1000, 15, true),
            new SmvRawVectorMeasurement(2, "Ch2", 1000, 1000, -140, true),
            new SmvRawVectorMeasurement(3, "Ch3", 1000, 1000, 150, true),
            new SmvRawVectorMeasurement(5, "Ch5", 1000, 1000, 0, true),
            new SmvRawVectorMeasurement(6, "Ch6", 1000, 1000, -160, true),
            new SmvRawVectorMeasurement(7, "Ch7", 1000, 1000, 100, true)
        };
        var raw = SmvDigSubPresentationAdapter.RelativeMeasurements(vectors);
        Assert.Equal(0, raw[0].PhaseDegrees, 8);
        Assert.Equal(-155, raw[1].PhaseDegrees, 8);
        Assert.Equal(-15, raw[3].PhaseDegrees, 8);

        var mapped = SmvDigSubPresentationAdapter.RelativeMeasurements(raw, SmvDisplayUnits.NineTwoLe);
        Assert.Equal(15, mapped[0].PhaseDegrees, 8);
        Assert.Equal(-140, mapped[1].PhaseDegrees, 8);
        Assert.Equal(150, mapped[2].PhaseDegrees, 8);
        Assert.Equal(0, mapped[3].PhaseDegrees, 8);
        Assert.Equal(-160, mapped[4].PhaseDegrees, 8);
        Assert.Equal(100, mapped[5].PhaseDegrees, 8);

        var phasors = SmvDigSubPresentationAdapter.BuildPhasors(vectors, SmvDisplayUnits.NineTwoLe);
        Assert.Equal(new[] { 15d, -140d, 150d, 0d, -160d, 100d },
            phasors.Select(v => v.AngleDegrees!.Value).ToArray());
    }

    [Fact]
    public void NonSinusoidalWaveform_HasDifferentTotalAndFundamentalRms_ByMathematicalDefinition()
    {
        const int count = 160;
        const int spc = 80;
        const double fundamentalRms = 40;
        const double thirdHarmonicRms = 4;
        var signal = Enumerable.Range(0, count)
            .Select(i => Math.Sqrt(2) * (
                fundamentalRms * Math.Cos(2 * Math.PI * i / spc) +
                thirdHarmonicRms * Math.Cos(6 * Math.PI * i / spc)))
            .ToArray();
        var snapshot = new SmvSnapshotResult
        {
            SamplesPerCycle = spc,
            CycleCount = 2,
            TargetSamples = count,
            CapturedSamples = count,
            Channels = [new SmvSnapshotChannel { ChannelIndex = 5, Label = "Ch5", Samples = signal }]
        };
        var measured = Assert.Single(SmvRawVectorMath.Analyze(snapshot));
        Assert.InRange(measured.Rms, Math.Sqrt(1616) - 0.000001, Math.Sqrt(1616) + 0.000001);
        Assert.InRange(measured.FundamentalRms, 39.999999, 40.000001);
        Assert.True(measured.Rms > measured.FundamentalRms);
        var displayed = SmvEngineeringDisplay.Present(measured, SmvDisplayUnits.NineTwoLe);
        Assert.Contains("fundamental", displayed.RmsDetail);
        Assert.Equal(measured.Rms * .01, displayed.Rms, 9);
    }

    [Fact]
    public void HoverCursorAndStackedAxes_AreBoundedToWaveRenderer_WithoutTimersOrProtocolChanges()
    {
        var wave = Source("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        Assert.Contains("UpdateCursorPosition(e.GetPosition(this))", wave);
        Assert.Contains("protected override void OnMouseLeave(", wave);
        Assert.Contains("_cursorSampleIndex == sampleIndex", wave);
        Assert.Contains("_cursorLane == laneIndex", wave);
        Assert.Contains("if (stacked && traces > 0)", wave);
        Assert.Contains("if (!stacked && IsSilentZeroTrace(series.Samples))", wave);
        Assert.Contains("if (samples[i] != 0.0)", wave);
        Assert.Contains("var y = rect.Top + (i + 0.5) * band", wave);
        Assert.DoesNotContain("var pen = i == 2 ? ZeroPen", wave);
        Assert.DoesNotContain("DispatcherTimer", wave);
    }

    [Fact]
    public void SharedCommandDock_DefaultsClosedOnlyInPassiveSmvWorkspace_ButIsManuallyExpandable()
    {
        var code = Source("MainWindow.PersistentWorkbench.cs");
        Assert.Contains("public bool SmvDockExpanded { get; set; } = false;", code);
        Assert.Contains("workspaceIndex == EngineeringNavigationRoute.SmvWorkspaceIndex", code);
        Assert.Contains("state.SmvDockExpanded = expanded;", code);
        Assert.Contains("state.DockExpanded = expanded;", code);
        Assert.Contains("state.ApplyingDockState = true;", code);
        Assert.DoesNotContain("RefreshControlValuesAsync(SelectedDevice", code);
    }

    private static string Source(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
