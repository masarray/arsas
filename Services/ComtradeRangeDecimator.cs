namespace ArIED61850Tester.Services;

internal interface IComtradeRangeSource
{
    ulong FrameCount { get; }
    double[] ReadAnalog(uint channelIndex, ulong startFrame, int frameCount);
    byte[] ReadStatus(uint channelIndex, ulong startFrame, int frameCount);
    uint[] ReadRawTimestamps(ulong startFrame, int frameCount);
}

internal sealed class ArdIrecRangeSource : IComtradeRangeSource
{
    private readonly ArdIrecNativeRecord _record;

    internal ArdIrecRangeSource(ArdIrecNativeRecord record)
    {
        _record = record ?? throw new ArgumentNullException(nameof(record));
    }

    public ulong FrameCount => _record.Info.FrameCount;

    public double[] ReadAnalog(uint channelIndex, ulong startFrame, int frameCount)
        => _record.ReadAnalog(channelIndex, startFrame, frameCount);

    public byte[] ReadStatus(uint channelIndex, ulong startFrame, int frameCount)
        => _record.ReadStatus(channelIndex, startFrame, frameCount);

    public uint[] ReadRawTimestamps(ulong startFrame, int frameCount)
        => _record.ReadRawTimestamps(startFrame, frameCount);
}

internal readonly record struct ComtradeFrameRange(ulong StartFrame, ulong FrameCount)
{
    public ulong EndExclusive => StartFrame + FrameCount;
}

internal readonly record struct ComtradeAnalogEnvelopeBucket(
    ulong StartFrame,
    ulong EndExclusive,
    uint FirstTimestamp,
    uint LastTimestamp,
    double FirstValue,
    double LastValue,
    ulong MinimumFrame,
    ulong MaximumFrame,
    uint MinimumTimestamp,
    uint MaximumTimestamp,
    double Minimum,
    double Maximum);

internal sealed record ComtradeAnalogEnvelope(
    ComtradeFrameRange Range,
    IReadOnlyList<ComtradeAnalogEnvelopeBucket> Buckets);

internal readonly record struct ComtradeDigitalTransition(
    ulong Frame,
    uint Timestamp,
    byte State);

internal sealed record ComtradeDigitalTransitionSet(
    ComtradeFrameRange Range,
    ulong ScannedFrameCount,
    uint FirstTimestamp,
    uint LastTimestamp,
    IReadOnlyList<ComtradeDigitalTransition> Transitions,
    bool IsTruncated);

internal static class ComtradeRangeDecimator
{
    internal const int DefaultChunkFrames = 65_536;
    internal const int MaximumChunkFrames = 1_048_576;

    internal static ComtradeAnalogEnvelope BuildAnalogEnvelope(
        IComtradeRangeSource source,
        uint channelIndex,
        ulong startFrame,
        ulong frameCount,
        int targetBuckets,
        int chunkFrames = DefaultChunkFrames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var range = NormalizeRange(source.FrameCount, startFrame, frameCount);
        if (range.FrameCount == 0)
            return new ComtradeAnalogEnvelope(range, Array.Empty<ComtradeAnalogEnvelopeBucket>());

        targetBuckets = Math.Max(1, targetBuckets);
        chunkFrames = NormalizeChunkFrames(chunkFrames);
        var bucketCount = checked((int)Math.Min((ulong)targetBuckets, range.FrameCount));

        var minimum = Enumerable.Repeat(double.PositiveInfinity, bucketCount).ToArray();
        var maximum = Enumerable.Repeat(double.NegativeInfinity, bucketCount).ToArray();
        var bucketStarts = new ulong[bucketCount];
        var bucketEnds = new ulong[bucketCount];
        var firstTimestamp = new uint[bucketCount];
        var lastTimestamp = new uint[bucketCount];
        var firstValue = new double[bucketCount];
        var lastValue = new double[bucketCount];
        var minimumFrame = new ulong[bucketCount];
        var maximumFrame = new ulong[bucketCount];
        var minimumTimestamp = new uint[bucketCount];
        var maximumTimestamp = new uint[bucketCount];
        var initialized = new bool[bucketCount];

        ulong processed = 0;
        while (processed < range.FrameCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = checked((int)Math.Min((ulong)chunkFrames, range.FrameCount - processed));
            var absoluteStart = range.StartFrame + processed;
            var values = source.ReadAnalog(channelIndex, absoluteStart, take);
            var timestamps = source.ReadRawTimestamps(absoluteStart, take);
            ValidateChunkLengths(take, values.Length, timestamps.Length, "analog");

            for (var i = 0; i < take; i++)
            {
                var relativeFrame = processed + checked((ulong)i);
                var absoluteFrame = absoluteStart + checked((ulong)i);
                var bucket = BucketIndex(relativeFrame, range.FrameCount, bucketCount);
                var value = values[i];

                if (!initialized[bucket])
                {
                    initialized[bucket] = true;
                    bucketStarts[bucket] = absoluteFrame;
                    firstTimestamp[bucket] = timestamps[i];
                    firstValue[bucket] = value;
                }

                bucketEnds[bucket] = absoluteFrame + 1;
                lastTimestamp[bucket] = timestamps[i];
                lastValue[bucket] = value;

                if (!double.IsFinite(value))
                    continue;

                if (value < minimum[bucket])
                {
                    minimum[bucket] = value;
                    minimumFrame[bucket] = absoluteFrame;
                    minimumTimestamp[bucket] = timestamps[i];
                }
                if (value > maximum[bucket])
                {
                    maximum[bucket] = value;
                    maximumFrame[bucket] = absoluteFrame;
                    maximumTimestamp[bucket] = timestamps[i];
                }
            }

            processed += checked((ulong)take);
        }

        var buckets = new List<ComtradeAnalogEnvelopeBucket>(bucketCount);
        for (var bucket = 0; bucket < bucketCount; bucket++)
        {
            if (!initialized[bucket])
                continue;

            var min = double.IsPositiveInfinity(minimum[bucket]) ? double.NaN : minimum[bucket];
            var max = double.IsNegativeInfinity(maximum[bucket]) ? double.NaN : maximum[bucket];
            buckets.Add(new ComtradeAnalogEnvelopeBucket(
                bucketStarts[bucket],
                bucketEnds[bucket],
                firstTimestamp[bucket],
                lastTimestamp[bucket],
                firstValue[bucket],
                lastValue[bucket],
                minimumFrame[bucket],
                maximumFrame[bucket],
                minimumTimestamp[bucket],
                maximumTimestamp[bucket],
                min,
                max));
        }

        return new ComtradeAnalogEnvelope(range, buckets);
    }

    internal static ComtradeDigitalTransitionSet BuildDigitalTransitions(
        IComtradeRangeSource source,
        uint channelIndex,
        ulong startFrame,
        ulong frameCount,
        int maxTransitions = 100_000,
        int chunkFrames = DefaultChunkFrames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var range = NormalizeRange(source.FrameCount, startFrame, frameCount);
        if (range.FrameCount == 0)
            return new ComtradeDigitalTransitionSet(range, 0, 0, 0, Array.Empty<ComtradeDigitalTransition>(), false);

        maxTransitions = Math.Max(2, maxTransitions);
        chunkFrames = NormalizeChunkFrames(chunkFrames);

        var sampled = new List<SampledDigitalTransition>(Math.Min(maxTransitions, 4096));
        var havePrevious = false;
        byte previousState = 0;
        ulong processed = 0;
        ulong changeOrdinal = 0;
        ulong samplingStride = 1;
        uint firstTimestamp = 0;
        uint lastTimestamp = 0;
        ComtradeDigitalTransition? lastActualTransition = null;

        while (processed < range.FrameCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = checked((int)Math.Min((ulong)chunkFrames, range.FrameCount - processed));
            var absoluteStart = range.StartFrame + processed;
            var states = source.ReadStatus(channelIndex, absoluteStart, take);
            var timestamps = source.ReadRawTimestamps(absoluteStart, take);
            ValidateChunkLengths(take, states.Length, timestamps.Length, "digital");

            for (var i = 0; i < take; i++)
            {
                var state = states[i] == 0 ? (byte)0 : (byte)1;
                var absoluteFrame = absoluteStart + checked((ulong)i);
                if (!havePrevious)
                    firstTimestamp = timestamps[i];
                lastTimestamp = timestamps[i];

                if (!havePrevious)
                {
                    var initial = new ComtradeDigitalTransition(absoluteFrame, timestamps[i], state);
                    sampled.Add(new SampledDigitalTransition(0, initial));
                    lastActualTransition = initial;
                    havePrevious = true;
                    previousState = state;
                    continue;
                }

                if (state == previousState)
                    continue;

                changeOrdinal++;
                var transition = new ComtradeDigitalTransition(absoluteFrame, timestamps[i], state);
                lastActualTransition = transition;
                previousState = state;

                if (changeOrdinal % samplingStride != 0)
                    continue;

                sampled.Add(new SampledDigitalTransition(changeOrdinal, transition));
                while (sampled.Count >= maxTransitions && samplingStride <= ulong.MaxValue / 2)
                {
                    samplingStride *= 2;
                    CompactTransitions(sampled, samplingStride);
                }
            }

            processed += checked((ulong)take);
        }

        if (lastActualTransition is { } lastActual &&
            sampled[^1].Transition.Frame != lastActual.Frame)
        {
            if (sampled.Count >= maxTransitions)
                sampled.RemoveAt(sampled.Count - 1);
            sampled.Add(new SampledDigitalTransition(changeOrdinal, lastActual));
        }

        var transitions = sampled
            .Select(item => item.Transition)
            .OrderBy(item => item.Frame)
            .ToArray();
        var exactTransitionCount = changeOrdinal + 1;
        var isTruncated = checked((ulong)transitions.Length) < exactTransitionCount;

        return new ComtradeDigitalTransitionSet(
            range,
            processed,
            firstTimestamp,
            lastTimestamp,
            transitions,
            isTruncated);
    }

    internal static ComtradeFrameRange NormalizeRange(ulong totalFrames, ulong startFrame, ulong frameCount)
    {
        if (totalFrames == 0 || frameCount == 0 || startFrame >= totalFrames)
            return new ComtradeFrameRange(Math.Min(startFrame, totalFrames), 0);

        var available = totalFrames - startFrame;
        return new ComtradeFrameRange(startFrame, Math.Min(frameCount, available));
    }

    private static void CompactTransitions(List<SampledDigitalTransition> sampled, ulong stride)
    {
        if (sampled.Count <= 1)
            return;

        var write = 1;
        for (var read = 1; read < sampled.Count; read++)
        {
            if (sampled[read].ChangeOrdinal % stride != 0)
                continue;
            sampled[write++] = sampled[read];
        }
        if (write < sampled.Count)
            sampled.RemoveRange(write, sampled.Count - write);
    }

    private static int NormalizeChunkFrames(int chunkFrames)
        => Math.Clamp(chunkFrames, 1, MaximumChunkFrames);

    private static int BucketIndex(ulong relativeFrame, ulong frameCount, int bucketCount)
    {
        if (bucketCount <= 1 || frameCount <= 1)
            return 0;

        var fraction = relativeFrame / (double)frameCount;
        return Math.Clamp((int)(fraction * bucketCount), 0, bucketCount - 1);
    }

    private static void ValidateChunkLengths(int expected, int signalLength, int timestampLength, string kind)
    {
        if (signalLength != expected || timestampLength != expected)
        {
            throw new InvalidOperationException(
                $"COMTRADE {kind} range source returned an incomplete chunk. " +
                $"Expected {expected} frames, signal={signalLength}, timestamps={timestampLength}.");
        }
    }

    private readonly record struct SampledDigitalTransition(
        ulong ChangeOrdinal,
        ComtradeDigitalTransition Transition);
}
