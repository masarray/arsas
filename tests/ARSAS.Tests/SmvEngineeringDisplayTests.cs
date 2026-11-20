using ArIED61850Tester.Services;
using ArIED61850Tester.Views;
using ProcessBus.Core.Models;

namespace ARSAS.Tests;

public sealed class SmvEngineeringDisplayTests
{
    [Fact]
    public void Default_IsRawAndCannotInventUnitsWithoutProfileSelection()
    {
        var snapshot = Full9_2LeShape();
        var plotted = SmvDigSubPresentationAdapter.BuildWaveform(snapshot);
        Assert.All(plotted.VoltageSeries.Concat(plotted.CurrentSeries),
            series => Assert.Equal("counts", series.Unit));
        Assert.Equal("Ch1", plotted.VoltageSeries[0].Name);
        Assert.Equal("Ch5", plotted.CurrentSeries[0].Name);
        var raw = SmvEngineeringDisplay.Present(
            new SmvRawVectorMeasurement(5, "Raw Ch5", 5000, 4700, 120, true),
            SmvDisplayUnits.RawCounts);
        Assert.Equal("counts", raw.Unit);
        Assert.Equal(5000, raw.Rms);
        Assert.Equal("Ch5", raw.DisplayName);
    }

    [Fact]
    public void Explicit9_2Le_UsesCurrentFirstVoltageSecondAndSameUnitsInAllPlots()
    {
        var snapshot = Full9_2LeShape();
        Assert.True(SmvEngineeringDisplay.IsProfileApplicable(snapshot));
        var plotted = SmvDigSubPresentationAdapter.BuildWaveform(snapshot, SmvDisplayUnits.NineTwoLe);
        Assert.Equal("A", plotted.VoltageSeries[0].Unit);
        Assert.Equal("V", plotted.CurrentSeries[0].Unit);
        Assert.Equal("Ia", plotted.VoltageSeries[0].Name);
        Assert.Equal("Ua", plotted.CurrentSeries[0].Name);
        Assert.Equal(1, plotted.VoltageSeries[0].Samples[0], 8);
        Assert.Equal(50, plotted.CurrentSeries[0].Samples[0], 8);
        Assert.Equal("Ia", SmvEngineeringDisplay.Describe(1, SmvDisplayUnits.NineTwoLe).Name);
        Assert.Equal("In", SmvEngineeringDisplay.Describe(4, SmvDisplayUnits.NineTwoLe).Name);
        Assert.Equal("Ua", SmvEngineeringDisplay.Describe(5, SmvDisplayUnits.NineTwoLe).Name);
        Assert.Equal("Un", SmvEngineeringDisplay.Describe(8, SmvDisplayUnits.NineTwoLe).Name);

        var vector = new SmvRawVectorMeasurement(5, "Ch5", 5000, 4700, -120, true);
        var measurement = SmvEngineeringDisplay.Present(vector, SmvDisplayUnits.NineTwoLe);
        Assert.Equal("Ua", measurement.DisplayName);
        Assert.Equal(50, measurement.Rms, 8);
        Assert.Equal("Wire channel 5 · Ua", measurement.Label);
        Assert.Equal(47, measurement.FundamentalRms, 8);
        Assert.Equal("V", measurement.Unit);
        var phasors = SmvDigSubPresentationAdapter.BuildPhasors(
        [
            new SmvRawVectorMeasurement(1, "Ch1", 1000, 1000, 0, true),
            vector
        ], SmvDisplayUnits.NineTwoLe);
        Assert.Equal("Ia", phasors[0].Name);
        Assert.Equal(PhasorFamily.Current, phasors[0].Family);
        Assert.Equal(1, phasors[0].Magnitude!.Value, 8);
        Assert.Equal("Ua", phasors[1].Name);
        Assert.Equal(PhasorFamily.Voltage, phasors[1].Family);
        Assert.Equal(47, phasors[1].Magnitude!.Value, 8);
    }

    [Fact]
    public void AngleZero_NeverShowsNegativeZeroOrMisleadingSignedReference()
    {
        var value = new SmvDisplayMeasurement(5, "Ua", "V", 50, 50, -0.001, true);
        Assert.Equal("∠0.0°", value.AngleText);
        Assert.Equal(-0.001, value.PhaseDegrees);
    }

    [Fact]
    public void EightChannelShapeRequired_NoSilentScalingOfUnverifiedCustomStream()
    {
        var twoChannels = Full9_2LeShape() with
        {
            Channels = Full9_2LeShape().Channels.Take(2).ToArray()
        };
        Assert.False(SmvEngineeringDisplay.IsProfileApplicable(twoChannels));
        Assert.Equal(SmvDisplayUnits.RawCounts,
            SmvEngineeringDisplay.Effective(twoChannels, SmvDisplayUnits.NineTwoLe));
        var rawFallback = SmvDigSubPresentationAdapter.BuildWaveform(twoChannels,
            SmvDisplayUnits.NineTwoLe);
        Assert.All(rawFallback.VoltageSeries, s => Assert.Equal("counts", s.Unit));
    }

    private static SmvSnapshotResult Full9_2LeShape() => new()
    {
        FirstSampleCount = 0,
        SamplesPerCycle = 80,
        CycleCount = 2,
        TargetSamples = 160,
        CapturedSamples = 160,
        NominalFrequencyHz = 50,
        Channels = Enumerable.Range(1, 8).Select(index => new SmvSnapshotChannel
        {
            ChannelIndex = index,
            Label = $"Raw Ch{index}",
            Samples = Enumerable.Repeat(index <= 4 ? 1000.0 : 5000.0, 160).ToArray()
        }).ToArray()
    };
}
