using ArIED61850Tester.Services;
using ArIED61850Tester.Views;
using ProcessBus.App.Wpf.Controls;
using ProcessBus.Core.Models;

namespace ARSAS.Tests;

public sealed class SmvP3EngineeringRegressionTests
{
    [Theory]
    [InlineData(0, "Sync None")]
    [InlineData(1, "Local Sync")]
    [InlineData(2, "Global Sync")]
    [InlineData(3, "Sync Unknown")]
    public void PublisherReportedSmpSynch_DoesNotClaimVerifiedPTP(byte code, string expected)
    {
        var snapshot = new SmvSnapshotResult
        {
            CapturedSamples = 160, TargetSamples = 160, SampleSynchronization = code
        };
        Assert.Equal(expected, SmvSyncPresentation.Label(snapshot));
        if (code <= 2)
            Assert.Contains("Does NOT verify PTP", SmvSyncPresentation.Detail(snapshot));
        Assert.Equal("Sync Unknown", SmvSyncPresentation.Label(null));
        Assert.Equal("Sync Unknown", SmvSyncPresentation.Label(snapshot with { GapTransitions = 1 }));
    }

    [Fact]
    public void Power_TruePAndFundamentalQ1_UAndILagThirtyDegrees()
    {
        const int count = 160;
        const int spc = 80;
        const double angle = Math.PI / 6;
        var chans = new List<SmvSnapshotChannel>();
        var vectors = new List<SmvRawVectorMeasurement>();
        for (var index = 1; index <= 8; index++)
        {
            var phase = index <= 4 ? index - 1 : index - 5;
            var isNeutral = phase == 3;
            var baseAngle = -phase * 2.0 * Math.PI / 3.0;
            var isCurrent = index <= 4;
            var shift = isCurrent ? angle : 0.0;
            var rawRms = isNeutral ? 0 : (isCurrent ? 1000 : 10000);
            var samples = Enumerable.Range(0, count).Select(t =>
                isNeutral ? 0.0 : rawRms * Math.Sqrt(2) *
                Math.Cos(2 * Math.PI * t / spc + baseAngle - shift)).ToArray();
            chans.Add(new SmvSnapshotChannel { ChannelIndex = index, Samples = samples });
            vectors.Add(new SmvRawVectorMeasurement(index, $"Ch{index}",
                rawRms, rawRms, (baseAngle - shift) * 180.0 / Math.PI,
                !isNeutral));
        }
        var snapshot = new SmvSnapshotResult
        {
            Channels = chans, TargetSamples = count, CapturedSamples = count,
            SamplesPerCycle = spc, NominalFrequencyHz = 50, CycleCount = 2
        };
        var result = Assert.IsType<SmvPowerMeasurements.Result>(
            SmvPowerMeasurements.Analyze(snapshot, vectors, SmvDisplayUnits.NineTwoLe));
        Assert.Equal(3, result.Phases.Count);
        Assert.InRange(result.Total.P, 259.807, 259.808);
        Assert.InRange(result.Total.Q1, 149.999, 150.001);
        Assert.InRange(result.Total.S, 299.999, 300.001);
        Assert.InRange(result.Total.PF, 0.866, 0.8661);
        Assert.Null(SmvPowerMeasurements.Analyze(snapshot, vectors, SmvDisplayUnits.RawCounts));
        Assert.Null(SmvPowerMeasurements.Analyze(snapshot with { GapTransitions = 1 },
            vectors, SmvDisplayUnits.NineTwoLe));
    }

    [Fact]
    public void DistortedPower_TrueRmsApparentNotEqualFundamentalReactiveMagnitude()
    {
        const int n = 160, spc = 80;
        var channels = new List<SmvSnapshotChannel>();
        var vectors = new List<SmvRawVectorMeasurement>();
        for (var c = 1; c <= 8; c++)
        {
            var active = c is 1 or 5;
            var rmsFund = c == 1 ? 1000.0 : 10000.0;
            var value = Enumerable.Range(0, n).Select(t => !active ? 0.0 :
                Math.Sqrt(2) * rmsFund * (Math.Cos(2 * Math.PI * t / spc) +
                0.20 * Math.Cos(6 * Math.PI * t / spc))).ToArray();
            var totalRms = active ? rmsFund * Math.Sqrt(1.04) : 0.0;
            channels.Add(new SmvSnapshotChannel { ChannelIndex = c, Samples = value });
            vectors.Add(new SmvRawVectorMeasurement(c, "Ch" + c, totalRms,
                active ? rmsFund : 0, 0, active));
        }
        var snapshot = new SmvSnapshotResult
        {
            Channels = channels, TargetSamples = n, CapturedSamples = n,
            SamplesPerCycle = spc, NominalFrequencyHz = 50
        };
        // Absent fundamental phase for inactive phases must *not* produce
        // synthetic Q/PF values. All three phase channels must be qualified.
        Assert.Null(SmvPowerMeasurements.Analyze(snapshot, vectors, SmvDisplayUnits.NineTwoLe));
    }

    [Fact]
    public void MillisecondsAxis_UsesUaRisingCrossingOnlyWhenActualUaExists()
    {
        var ua = Enumerable.Range(0, 160)
            .Select(i => Math.Sin(2 * Math.PI * (i - 20) / 80)).ToArray();
        var snapshot = new WaveformSnapshot
        {
            CurrentSeries = [new WaveformSeriesModel
            { Name = "Ua", Unit = "V", Samples = ua }],
            SamplesPerCycle = 80, SampleRateHz = 4000
        };
        Assert.Equal(160, SmvWaveformTimeAxis.AvailableSamples(snapshot));
        Assert.InRange(SmvWaveformTimeAxis.ReferenceSample(snapshot, 160), 19.999, 20.001);
        var raw = snapshot with { CurrentSeries =
            [new WaveformSeriesModel { Name = "Ch5", Unit = "counts", Samples = ua }] };
        Assert.Equal(0, SmvWaveformTimeAxis.ReferenceSample(raw, 160));
    }

    [Fact]
    public void PhasorCalloutDisplaysExplicitAngleAndNeverNegativeZero()
    {
        var item = new PhasorDisplayItem
        {
            Name = "Ub", Family = PhasorFamily.Voltage,
            Magnitude = 100, AngleDegrees = -120, Unit = "V"
        };
        Assert.Contains("∠-120.0°", PhasorDiagramControl.FormatPhasorLabel(item));
        Assert.Contains("∠0.0°", PhasorDiagramControl.FormatPhasorLabel(
            new PhasorDisplayItem { Name = "Ua", Family = PhasorFamily.Voltage,
                Magnitude = 100, AngleDegrees = -0.001, Unit = "V" }));
    }
}
