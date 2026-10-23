using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SmvRawVectorMathTests
{
    [Fact]
    public void CoherentTwoCycleSine_UsesActualSamplesAndRetainsRelativePhase()
    {
        const int samplesPerCycle = 80;
        const int cycles = 2;
        var a = Enumerable.Range(0, samplesPerCycle * cycles)
            .Select(i => 1000.0 * Math.Cos(2 * Math.PI * i / samplesPerCycle)).ToArray();
        var b = Enumerable.Range(0, samplesPerCycle * cycles)
            .Select(i => 500.0 * Math.Cos(2 * Math.PI * i / samplesPerCycle - 2 * Math.PI / 3)).ToArray();
        var result = Build(a, b);
        var vectors = SmvRawVectorMath.Analyze(result);
        Assert.Equal(2, vectors.Count);
        Assert.InRange(vectors[0].Rms, 707.0, 707.2);
        Assert.InRange(vectors[0].FundamentalRms, 707.0, 707.2);
        Assert.InRange(vectors[0].PhaseDegrees, -0.1, 0.1);
        Assert.InRange(vectors[1].PhaseDegrees, -120.1, -119.9);
        Assert.InRange(vectors[1].FundamentalRms, 353.4, 353.7);
    }

    [Fact]
    public void ContinuityAnomalyMustNotProduceAValidPhasor()
    {
        var samples = Enumerable.Repeat(100.0, 160).ToArray();
        Assert.Empty(SmvRawVectorMath.Analyze(Build(samples) with { GapTransitions = 1 }));
        Assert.Empty(SmvRawVectorMath.Analyze(Build(samples) with { RestartTransitions = 1 }));
    }

    [Fact]
    public void PartialOrNonFiniteInputFailsClosed()
    {
        var values = Enumerable.Repeat(1.0, 160).ToArray();
        Assert.Empty(SmvRawVectorMath.Analyze(Build(values[..159])));
        values[5] = double.NaN;
        Assert.Empty(SmvRawVectorMath.Analyze(Build(values)));
    }

    [Fact]
    public void DcOnlyLaneHasRawRmsButNoFictionalPhase()
    {
        var measurement = Assert.Single(SmvRawVectorMath.Analyze(Build(Enumerable.Repeat(42.0, 160).ToArray())));
        Assert.Equal(42, measurement.Rms, 9);
        Assert.False(measurement.HasFundamental);
        Assert.Equal("—", measurement.AngleText);
    }

    [Fact]
    public void ViewerHasCompactThreePaneAndPreservesPassiveCapture()
    {
        var xaml = ReadRepoFile("SmvViewerWindow.xaml");
        Assert.Contains("Grid.Column=\"2\" BorderBrush", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"RawPhasorCanvas\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding RawVectorRows}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SnapshotChannels}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Capture 2 cycles", xaml, StringComparison.Ordinal);
        Assert.Contains("Received INT32 lanes", xaml, StringComparison.Ordinal);
    }

    private static SmvSnapshotResult Build(params double[][] lanes) => new()
    {
        SamplesPerCycle = 80,
        CycleCount = 2,
        TargetSamples = 160,
        CapturedSamples = 160,
        Channels = lanes.Select((samples, i) => new SmvSnapshotChannel
        {
            ChannelIndex = i,
            Label = $"raw {i}",
            Samples = samples
        }).ToArray()
    };

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
