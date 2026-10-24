using ArIED61850Tester.Services;
using ArIED61850Tester.Views;
using ProcessBus.Core.Models;

namespace ARSAS.Tests;

public sealed class SmvDigSubPortTests
{
    [Fact]
    public void PhaseAlignedWaveform_HasSameScreenSamplesForDifferentRingOffsets()
    {
        var first = Snapshot(17);
        var second = Snapshot(31);
        var a = SmvDigSubPresentationAdapter.BuildWaveform(first);
        var b = SmvDigSubPresentationAdapter.BuildWaveform(second);
        Assert.Equal(2, a.VoltageSeries.Count);
        Assert.Empty(a.CurrentSeries);
        Assert.Equal(a.VoltageSeries[0].Samples.Count, b.VoltageSeries[0].Samples.Count);
        for (var i = 0; i < a.VoltageSeries[0].Samples.Count; i++)
            Assert.InRange(Math.Abs(a.VoltageSeries[0].Samples[i] -
                b.VoltageSeries[0].Samples[i]), 0, 1e-8);
        Assert.Equal("counts", a.VoltageSeries[0].Unit);
        Assert.Equal(0, a.MeasuredFrequencyHz); // nominal is never a measured frequency
    }

    [Fact]
    public void RelativePhasors_AreStableAgainstCommonRotatingDftOffset()
    {
        var first = new[]
        {
            new SmvRawVectorMeasurement(1,"Ch1",200,100,19,true),
            new SmvRawVectorMeasurement(2,"Ch2",200,100,-101,true),
            new SmvRawVectorMeasurement(3,"Ch3",200,100,139,true),
            new SmvRawVectorMeasurement(5,"Ch5",200,100,29,true)
        };
        var shifted = first.Select(v => v with { PhaseDegrees = v.PhaseDegrees + 57 }).ToArray();
        var p = SmvDigSubPresentationAdapter.BuildPhasors(first);
        var q = SmvDigSubPresentationAdapter.BuildPhasors(shifted);
        Assert.Equal(new[] { "Ch1", "Ch2", "Ch3", "Ch5" }, p.Select(x => x.Name));
        Assert.Equal(new[] { 0.0, -120.0, 120.0, 10.0 }, p.Select(x => x.AngleDegrees!.Value));
        for (var i = 0; i < p.Count; i++)
            Assert.InRange(Math.Abs(p[i].AngleDegrees!.Value - q[i].AngleDegrees!.Value),0,1e-8);
        Assert.All(p, v => Assert.Equal("counts", v.Unit));
        var rows = SmvDigSubPresentationAdapter.RelativeMeasurements(first);
        Assert.Equal(0, rows[0].PhaseDegrees);
        Assert.Equal(-120, rows[1].PhaseDegrees);
    }

    [Fact]
    public void IncompleteOrDiscontinuousEvidence_IsNotPlotted()
    {
        var bad = Snapshot(17) with { GapTransitions = 1 };
        var plot = SmvDigSubPresentationAdapter.BuildWaveform(bad);
        Assert.Empty(plot.VoltageSeries);
        Assert.Empty(plot.CurrentSeries);
        Assert.Empty(SmvDigSubPresentationAdapter.BuildPhasors([]));
    }

    [Fact]
    public void WpfTab_PortsActualDigSubRendering_NotAnotherHomemadeCanvas()
    {
        var xaml = Read("Views/SmvInlineViewer.xaml");
        var wave = Read("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        var phasor = Read("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        Assert.Contains("<dig:WaveformPanelControl", xaml);
        Assert.Contains("<dig:PhasorDiagramControl", xaml);
        Assert.DoesNotContain("WaveformCanvas", xaml);
        Assert.DoesNotContain("PhasorCanvas", xaml);
        Assert.Contains("BuildDisplaySamples", wave);
        Assert.Contains("ResolvePeakHoldScale", wave);
        Assert.Contains("PhasorLabelLayout.Arrange", phasor);
        Assert.Contains("PhasorCalloutRouting.Route", phasor);
    }

    private static SmvSnapshotResult Snapshot(int start)
    {
        const int spc = 80;
        var channels = Enumerable.Range(1, 2).Select(ch => new SmvSnapshotChannel
        {
            ChannelIndex = ch,
            Label = $"Raw Ch{ch}",
            Samples = Enumerable.Range(0,160)
                .Select(i => 1000*Math.Sin((start+i)*2*Math.PI/spc + (ch-1)*2*Math.PI/3))
                .ToArray()
        }).ToArray();
        return new SmvSnapshotResult
        {
            FirstSampleCount = (ushort)start,
            SamplesPerCycle = spc,
            CycleCount = 2,
            NominalFrequencyHz = 50,
            TargetSamples = 160,
            CapturedSamples = 160,
            Channels = channels
        };
    }

    private static string Read(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        throw new FileNotFoundException(relative);
    }
}
