using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeDecimatedSeriesBuilderTests
{
    [Fact]
    public void AnalogSeries_PreservesExtremaInTimestampOrder()
    {
        var envelope = new ComtradeAnalogEnvelope(
            new ComtradeFrameRange(0, 20),
            new[]
            {
                new ComtradeAnalogEnvelopeBucket(0, 10, 0, 900, 700, 200, -5, 8),
                new ComtradeAnalogEnvelopeBucket(10, 20, 1000, 1900, 1100, 1800, -3, 12)
            });

        var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);

        Assert.Equal(new double[] { 8, -5, -3, 12 }, series.Values);
        Assert.Equal(new uint[] { 200, 700, 1100, 1800 }, series.Timestamps);
    }

    [Fact]
    public void AnalogSeries_KeepsEqualTimestampExtremaForVerticalSpike()
    {
        var envelope = new ComtradeAnalogEnvelope(
            new ComtradeFrameRange(0, 1),
            new[]
            {
                new ComtradeAnalogEnvelopeBucket(0, 1, 100, 100, 100, 100, -10, 10)
            });

        var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);

        Assert.Equal(new double[] { -10, 10 }, series.Values);
        Assert.Equal(new uint[] { 100, 100 }, series.Timestamps);
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
        Assert.False(series.IsTruncated);
    }

    [Fact]
    public void DigitalSeries_PreservesTransitionsAndTruncationFlag()
    {
        var transitions = new ComtradeDigitalTransitionSet(
            new ComtradeFrameRange(0, 100),
            51,
            0,
            50,
            new[]
            {
                new ComtradeDigitalTransition(0, 0, 0),
                new ComtradeDigitalTransition(20, 20, 1),
                new ComtradeDigitalTransition(40, 40, 0)
            },
            true);

        var series = ComtradeDecimatedSeriesBuilder.BuildDigital(transitions);

        Assert.Equal(new byte[] { 0, 1, 0, 0 }, series.States);
        Assert.Equal(new uint[] { 0, 20, 40, 50 }, series.Timestamps);
        Assert.True(series.IsTruncated);
    }
}
