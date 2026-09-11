using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeRmsSeriesBuilderTests
{
    [Fact]
    public void BuildExact_ConstantSignal_ReturnsScaledMagnitude()
    {
        var samples = Enumerable.Repeat(-2.0, 100).ToArray();
        var timestamps = Enumerable.Range(0, 100).Select(index => checked((uint)(index * 1000))).ToArray();
        var frames = Enumerable.Range(0, 100).Select(index => checked((ulong)index)).ToArray();

        var result = ComtradeRmsSeriesBuilder.BuildExact(
            samples,
            timestamps,
            frames,
            timeMultiplier: 1.0,
            nominalFrequencyHz: 50.0,
            displayScale: 10.0);

        Assert.Equal(100, result.Values.Length);
        Assert.All(result.Values, value => Assert.InRange(value, 19.999999, 20.000001));
        Assert.Equal(timestamps, result.Timestamps);
        Assert.Equal(frames, result.SourceFrames);
    }

    [Fact]
    public void BuildExact_OneCycleSine_ConvergesToExpectedRms()
    {
        const int samplesPerCycle = 20;
        var count = samplesPerCycle * 4;
        var samples = Enumerable.Range(0, count)
            .Select(index => Math.Sqrt(2.0) * Math.Sin(2.0 * Math.PI * index / samplesPerCycle))
            .ToArray();
        var timestamps = Enumerable.Range(0, count).Select(index => checked((uint)(index * 1000))).ToArray();
        var frames = Enumerable.Range(0, count).Select(index => checked((ulong)index)).ToArray();

        var result = ComtradeRmsSeriesBuilder.BuildExact(
            samples,
            timestamps,
            frames,
            timeMultiplier: 1.0,
            nominalFrequencyHz: 50.0,
            displayScale: 1.0);

        Assert.InRange(result.Values[^1], 0.99, 1.01);
    }

    [Fact]
    public void BuildBounded_LargeRecord_StaysWithinBoundAndKeepsLastFrame()
    {
        var source = new FakeRangeSource(10_000);
        var result = ComtradeRmsSeriesBuilder.BuildBounded(
            source,
            channelIndex: 0,
            startFrame: 0,
            frameCount: 10_000,
            timeMultiplier: 1.0,
            nominalFrequencyHz: 50.0,
            displayScale: 1.0,
            maximumOutputPoints: 128,
            chunkFrames: 257);

        Assert.InRange(result.Values.Length, 2, 129);
        Assert.Equal(9_999UL, result.SourceFrames[^1]);
        Assert.Equal(9_999_000U, result.Timestamps[^1]);
    }

    private sealed class FakeRangeSource : IComtradeRangeSource
    {
        internal FakeRangeSource(ulong frameCount) => FrameCount = frameCount;
        public ulong FrameCount { get; }

        public double[] ReadAnalog(uint channelIndex, ulong startFrame, int frameCount)
        {
            var result = new double[frameCount];
            for (var index = 0; index < result.Length; index++)
                result[index] = Math.Sin((startFrame + checked((ulong)index)) * 0.01);
            return result;
        }

        public byte[] ReadStatus(uint channelIndex, ulong startFrame, int frameCount)
            => new byte[frameCount];

        public uint[] ReadRawTimestamps(ulong startFrame, int frameCount)
        {
            var result = new uint[frameCount];
            for (var index = 0; index < result.Length; index++)
                result[index] = checked((uint)((startFrame + checked((ulong)index)) * 1000UL));
            return result;
        }
    }
}
