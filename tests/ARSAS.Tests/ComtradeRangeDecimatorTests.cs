using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ComtradeRangeDecimatorTests
{
    [Fact]
    public void AnalogEnvelope_DecimatesFullRangeAcrossBoundedChunks()
    {
        var values = Enumerable.Range(0, 100).Select(value => (double)value).ToArray();
        var timestamps = Enumerable.Range(0, 100).Select(value => checked((uint)(value * 100))).ToArray();
        var source = new FakeRangeSource(values, new byte[100], timestamps);

        var envelope = ComtradeRangeDecimator.BuildAnalogEnvelope(
            source,
            channelIndex: 0,
            startFrame: 0,
            frameCount: 100,
            targetBuckets: 10,
            chunkFrames: 7);

        Assert.Equal(new ComtradeFrameRange(0, 100), envelope.Range);
        Assert.Equal(10, envelope.Buckets.Count);

        var first = envelope.Buckets[0];
        Assert.Equal((ulong)0, first.StartFrame);
        Assert.Equal((ulong)10, first.EndExclusive);
        Assert.Equal((uint)0, first.FirstTimestamp);
        Assert.Equal((uint)900, first.LastTimestamp);
        Assert.Equal(0.0, first.Minimum);
        Assert.Equal(9.0, first.Maximum);

        var last = envelope.Buckets[^1];
        Assert.Equal((ulong)90, last.StartFrame);
        Assert.Equal((ulong)100, last.EndExclusive);
        Assert.Equal(90.0, last.Minimum);
        Assert.Equal(99.0, last.Maximum);

        Assert.InRange(source.MaxAnalogRead, 1, 7);
        Assert.InRange(source.MaxTimestampRead, 1, 7);
        Assert.True(source.AnalogReadCalls > 1);
    }

    [Fact]
    public void AnalogEnvelope_ClampsRequestedRangeAtRecordTail()
    {
        var values = Enumerable.Range(0, 20).Select(value => (double)value).ToArray();
        var timestamps = Enumerable.Range(0, 20).Select(value => checked((uint)value)).ToArray();
        var source = new FakeRangeSource(values, new byte[20], timestamps);

        var envelope = ComtradeRangeDecimator.BuildAnalogEnvelope(
            source,
            channelIndex: 0,
            startFrame: 17,
            frameCount: 50,
            targetBuckets: 10,
            chunkFrames: 2);

        Assert.Equal(new ComtradeFrameRange(17, 3), envelope.Range);
        Assert.Equal(3, envelope.Buckets.Count);
        Assert.Equal(17.0, envelope.Buckets[0].Minimum);
        Assert.Equal(19.0, envelope.Buckets[^1].Maximum);
    }

    [Fact]
    public void DigitalTransitions_PreserveChangesAcrossChunkBoundaries()
    {
        var states = new byte[] { 0, 0, 1, 1, 1, 0, 0, 1 };
        var timestamps = Enumerable.Range(0, states.Length).Select(value => checked((uint)(value * 1000))).ToArray();
        var source = new FakeRangeSource(new double[states.Length], states, timestamps);

        var result = ComtradeRangeDecimator.BuildDigitalTransitions(
            source,
            channelIndex: 0,
            startFrame: 0,
            frameCount: checked((ulong)states.Length),
            maxTransitions: 100,
            chunkFrames: 3);

        Assert.False(result.IsTruncated);
        Assert.Equal((ulong)states.Length, result.ScannedFrameCount);
        Assert.Collection(
            result.Transitions,
            item => Assert.Equal(new ComtradeDigitalTransition(0, 0, 0), item),
            item => Assert.Equal(new ComtradeDigitalTransition(2, 2000, 1), item),
            item => Assert.Equal(new ComtradeDigitalTransition(5, 5000, 0), item),
            item => Assert.Equal(new ComtradeDigitalTransition(7, 7000, 1), item));
        Assert.InRange(source.MaxStatusRead, 1, 3);
    }

    [Fact]
    public void DigitalTransitions_StopsAtConfiguredMemoryBound()
    {
        var states = Enumerable.Range(0, 20).Select(value => (byte)(value % 2)).ToArray();
        var timestamps = Enumerable.Range(0, states.Length).Select(value => checked((uint)value)).ToArray();
        var source = new FakeRangeSource(new double[states.Length], states, timestamps);

        var result = ComtradeRangeDecimator.BuildDigitalTransitions(
            source,
            channelIndex: 0,
            startFrame: 0,
            frameCount: checked((ulong)states.Length),
            maxTransitions: 3,
            chunkFrames: 4);

        Assert.True(result.IsTruncated);
        Assert.Equal(3, result.Transitions.Count);
        Assert.Equal((ulong)3, result.ScannedFrameCount);
        Assert.Equal((ulong)0, result.Transitions[0].Frame);
        Assert.Equal((ulong)2, result.Transitions[^1].Frame);
    }

    [Fact]
    public void NormalizeRange_RejectsOutOfRecordStartWithoutOverflow()
    {
        Assert.Equal(new ComtradeFrameRange(100, 0),
            ComtradeRangeDecimator.NormalizeRange(100, 500, ulong.MaxValue));
        Assert.Equal(new ComtradeFrameRange(90, 10),
            ComtradeRangeDecimator.NormalizeRange(100, 90, ulong.MaxValue));
    }

    private sealed class FakeRangeSource : IComtradeRangeSource
    {
        private readonly double[] _analog;
        private readonly byte[] _status;
        private readonly uint[] _timestamps;

        internal FakeRangeSource(double[] analog, byte[] status, uint[] timestamps)
        {
            _analog = analog;
            _status = status;
            _timestamps = timestamps;
            if (_analog.Length != _status.Length || _analog.Length != _timestamps.Length)
                throw new ArgumentException("Fake COMTRADE source arrays must have identical lengths.");
        }

        public ulong FrameCount => checked((ulong)_timestamps.Length);
        internal int MaxAnalogRead { get; private set; }
        internal int MaxStatusRead { get; private set; }
        internal int MaxTimestampRead { get; private set; }
        internal int AnalogReadCalls { get; private set; }

        public double[] ReadAnalog(uint channelIndex, ulong startFrame, int frameCount)
        {
            AnalogReadCalls++;
            MaxAnalogRead = Math.Max(MaxAnalogRead, frameCount);
            return Slice(_analog, startFrame, frameCount);
        }

        public byte[] ReadStatus(uint channelIndex, ulong startFrame, int frameCount)
        {
            MaxStatusRead = Math.Max(MaxStatusRead, frameCount);
            return Slice(_status, startFrame, frameCount);
        }

        public uint[] ReadRawTimestamps(ulong startFrame, int frameCount)
        {
            MaxTimestampRead = Math.Max(MaxTimestampRead, frameCount);
            return Slice(_timestamps, startFrame, frameCount);
        }

        private static T[] Slice<T>(T[] source, ulong startFrame, int frameCount)
        {
            var start = checked((int)startFrame);
            var result = new T[frameCount];
            Array.Copy(source, start, result, 0, frameCount);
            return result;
        }
    }
}
