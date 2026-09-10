namespace ArIED61850Tester.Services;

/// <summary>
/// Bounded one-cycle RMS builder matching ArdIrec's workstation semantics. The calculation walks
/// native source samples once, keeps only one nominal cycle in memory, and emits a pixel-friendly
/// bounded series for large records. No FFT/DFT is duplicated in the presentation layer.
/// </summary>
internal static class ComtradeRmsSeriesBuilder
{
    internal const int DefaultMaximumOutputPoints = 8_192;

    internal static ComtradeRmsSeries BuildExact(
        IReadOnlyList<double> samples,
        IReadOnlyList<uint> timestamps,
        IReadOnlyList<ulong> sourceFrames,
        double timeMultiplier,
        double nominalFrequencyHz,
        double displayScale,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(samples);
        ArgumentNullException.ThrowIfNull(timestamps);
        ArgumentNullException.ThrowIfNull(sourceFrames);

        var count = Math.Min(samples.Count, Math.Min(timestamps.Count, sourceFrames.Count));
        if (count == 0)
            return ComtradeRmsSeries.Empty;

        var values = new double[count];
        var times = new uint[count];
        var frames = new ulong[count];
        var window = new Queue<RmsWindowPoint>(Math.Min(count, 4096));
        var sumSquares = 0.0;
        var finiteCount = 0;
        var frequency = NormalizeFrequency(nominalFrequencyHz);
        var periodSeconds = 1.0 / frequency;
        var multiplier = NormalizeTimeMultiplier(timeMultiplier);
        var scale = Math.Abs(double.IsFinite(displayScale) ? displayScale : 1.0);

        for (var index = 0; index < count; index++)
        {
            if ((index & 0x3ff) == 0)
                cancellationToken.ThrowIfCancellationRequested();

            var sample = samples[index];
            var seconds = timestamps[index] * multiplier / 1_000_000.0;
            var finite = double.IsFinite(sample);
            var square = finite ? sample * sample : 0.0;
            window.Enqueue(new RmsWindowPoint(seconds, square, finite));
            if (finite)
            {
                sumSquares += square;
                finiteCount++;
            }

            TrimWindow(window, seconds, periodSeconds, ref sumSquares, ref finiteCount);
            values[index] = finiteCount > 0 ? Math.Sqrt(Math.Max(0.0, sumSquares / finiteCount)) * scale : 0.0;
            times[index] = timestamps[index];
            frames[index] = sourceFrames[index];
        }

        return new ComtradeRmsSeries(values, times, frames);
    }

    internal static ComtradeRmsSeries BuildBounded(
        IComtradeRangeSource source,
        uint channelIndex,
        ulong startFrame,
        ulong frameCount,
        double timeMultiplier,
        double nominalFrequencyHz,
        double displayScale,
        int maximumOutputPoints = DefaultMaximumOutputPoints,
        int chunkFrames = ComtradeRangeDecimator.DefaultChunkFrames,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        var range = ComtradeRangeDecimator.NormalizeRange(source.FrameCount, startFrame, frameCount);
        if (range.FrameCount == 0)
            return ComtradeRmsSeries.Empty;

        maximumOutputPoints = Math.Clamp(maximumOutputPoints, 64, 65_536);
        chunkFrames = Math.Clamp(chunkFrames, 1, ComtradeRangeDecimator.MaximumChunkFrames);
        var outputStride = Math.Max(1UL, (range.FrameCount + (ulong)maximumOutputPoints - 1UL) / (ulong)maximumOutputPoints);
        var capacity = checked((int)Math.Min((ulong)maximumOutputPoints + 1UL, range.FrameCount));
        var values = new List<double>(capacity);
        var timestampsOut = new List<uint>(capacity);
        var framesOut = new List<ulong>(capacity);
        var window = new Queue<RmsWindowPoint>(4096);
        var sumSquares = 0.0;
        var finiteCount = 0;
        var periodSeconds = 1.0 / NormalizeFrequency(nominalFrequencyHz);
        var multiplier = NormalizeTimeMultiplier(timeMultiplier);
        var scale = Math.Abs(double.IsFinite(displayScale) ? displayScale : 1.0);
        ulong processed = 0;

        while (processed < range.FrameCount)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = checked((int)Math.Min((ulong)chunkFrames, range.FrameCount - processed));
            var absoluteStart = range.StartFrame + processed;
            var samples = source.ReadAnalog(channelIndex, absoluteStart, take);
            var timestamps = source.ReadRawTimestamps(absoluteStart, take);
            if (samples.Length != take || timestamps.Length != take)
                throw new InvalidOperationException("COMTRADE RMS source returned an incomplete native chunk.");

            for (var local = 0; local < take; local++)
            {
                if ((local & 0x3ff) == 0)
                    cancellationToken.ThrowIfCancellationRequested();

                var relative = processed + checked((ulong)local);
                var absoluteFrame = absoluteStart + checked((ulong)local);
                var sample = samples[local];
                var seconds = timestamps[local] * multiplier / 1_000_000.0;
                var finite = double.IsFinite(sample);
                var square = finite ? sample * sample : 0.0;
                window.Enqueue(new RmsWindowPoint(seconds, square, finite));
                if (finite)
                {
                    sumSquares += square;
                    finiteCount++;
                }
                TrimWindow(window, seconds, periodSeconds, ref sumSquares, ref finiteCount);

                if (relative % outputStride != 0 && relative + 1 != range.FrameCount)
                    continue;

                values.Add(finiteCount > 0 ? Math.Sqrt(Math.Max(0.0, sumSquares / finiteCount)) * scale : 0.0);
                timestampsOut.Add(timestamps[local]);
                framesOut.Add(absoluteFrame);
            }

            processed += checked((ulong)take);
        }

        return new ComtradeRmsSeries(values.ToArray(), timestampsOut.ToArray(), framesOut.ToArray());
    }

    internal static double[] ApplyScale(IReadOnlyList<double> values, double displayScale)
    {
        ArgumentNullException.ThrowIfNull(values);
        var scale = double.IsFinite(displayScale) ? displayScale : 1.0;
        if (Math.Abs(scale - 1.0) <= 1e-15 && values is double[] array)
            return array;

        var result = new double[values.Count];
        for (var index = 0; index < values.Count; index++)
            result[index] = values[index] * scale;
        return result;
    }

    private static void TrimWindow(
        Queue<RmsWindowPoint> window,
        double currentSeconds,
        double periodSeconds,
        ref double sumSquares,
        ref int finiteCount)
    {
        while (window.Count > 1 && currentSeconds - window.Peek().Seconds >= periodSeconds)
        {
            var removed = window.Dequeue();
            if (!removed.IsFinite)
                continue;
            sumSquares -= removed.Square;
            if (finiteCount > 0)
                finiteCount--;
        }
    }

    private static double NormalizeFrequency(double value)
        => double.IsFinite(value) && value > 1.0 ? value : 50.0;

    private static double NormalizeTimeMultiplier(double value)
        => double.IsFinite(value) && value > 0.0 ? value : 1.0;

    private readonly record struct RmsWindowPoint(double Seconds, double Square, bool IsFinite);
}

internal sealed record ComtradeRmsSeries(double[] Values, uint[] Timestamps, ulong[] SourceFrames)
{
    internal static ComtradeRmsSeries Empty { get; } = new(Array.Empty<double>(), Array.Empty<uint>(), Array.Empty<ulong>());
}
