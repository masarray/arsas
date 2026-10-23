using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeDecimatedSeriesBuilderTests
{
    [Fact]
    public void AnalogSeries_PreservesBoundariesExtremaAndSourceFramesInTimeOrder()
    {
        var envelope = new ComtradeAnalogEnvelope(
            new ComtradeFrameRange(0, 20),
            new[]
            {
                new ComtradeAnalogEnvelopeBucket(0, 10, 0, 900, 1, 2, 7, 2, 700, 200, -5, 8),
                new ComtradeAnalogEnvelopeBucket(10, 20, 1000, 1900, 3, 4, 11, 18, 1100, 1800, -3, 12)
            });

        var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);

        Assert.Equal(new double[] { 1, 8, -5, 2, 3, -3, 12, 4 }, series.Values);
        Assert.Equal(new uint[] { 0, 200, 700, 900, 1000, 1100, 1800, 1900 }, series.Timestamps);
        Assert.Equal(new ulong[] { 0, 2, 7, 9, 10, 11, 18, 19 }, series.SourceFrames);
    }

    [Fact]
    public void AnalogSeries_KeepsEqualTimestampExtremaForVerticalSpike()
    {
        var envelope = new ComtradeAnalogEnvelope(
            new ComtradeFrameRange(0, 1),
            new[]
            {
                new ComtradeAnalogEnvelopeBucket(0, 1, 100, 100, -10, 10, 0, 0, 100, 100, -10, 10)
            });

        var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);

        Assert.Equal(new double[] { -10, 10 }, series.Values);
        Assert.Equal(new uint[] { 100, 100 }, series.Timestamps);
        Assert.Equal(new ulong[] { 0, 0 }, series.SourceFrames);
    }

    [Fact]
    public void DigitalSeries_AppendsRangeEndpointWithoutExpandingSteadyFrames()
    {
        var transitions = new ComtradeDigitalTransitionSet(
            new ComtradeFrameRange(0, 1_000_000),
            1_000_000,
            0,
            999_999,
            new[] { new ComtradeDigitalTransition(0, 0, 1) },
            false);

        var series = ComtradeDecimatedSeriesBuilder.BuildDigital(transitions);

        Assert.Equal(new byte[] { 1, 1 }, series.States);
        Assert.Equal(new uint[] { 0, 999_999 }, series.Timestamps);
        Assert.Equal(new ulong[] { 0, 999_999 }, series.SourceFrames);
        Assert.False(series.IsTruncated);
    }

    [Fact]
    public void DigitalSeries_PreservesTransitionsEndpointAndSourceFrames()
    {
        var transitions = new ComtradeDigitalTransitionSet(
            new ComtradeFrameRange(0, 100),
            100,
            0,
            99,
            new[]
            {
                new ComtradeDigitalTransition(0, 0, 0),
                new ComtradeDigitalTransition(20, 20, 1),
                new ComtradeDigitalTransition(80, 80, 0)
            },
            true);

        var series = ComtradeDecimatedSeriesBuilder.BuildDigital(transitions);

        Assert.Equal(new byte[] { 0, 1, 0, 0 }, series.States);
        Assert.Equal(new uint[] { 0, 20, 80, 99 }, series.Timestamps);
        Assert.Equal(new ulong[] { 0, 20, 80, 99 }, series.SourceFrames);
        Assert.True(series.IsTruncated);
    }
}
