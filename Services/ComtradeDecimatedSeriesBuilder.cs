namespace ArIED61850Tester.Services;

internal sealed record ComtradeAnalogPlotSeries(double[] Values, uint[] Timestamps, ulong[] SourceFrames);
internal sealed record ComtradeDigitalPlotSeries(byte[] States, uint[] Timestamps, ulong[] SourceFrames, bool IsTruncated);

internal static class ComtradeDecimatedSeriesBuilder
{
    internal static ComtradeAnalogPlotSeries BuildAnalog(ComtradeAnalogEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Buckets.Count == 0)
            return new ComtradeAnalogPlotSeries(Array.Empty<double>(), Array.Empty<uint>(), Array.Empty<ulong>());

        var values = new List<double>(checked(envelope.Buckets.Count * 4));
        var timestamps = new List<uint>(checked(envelope.Buckets.Count * 4));
        var sourceFrames = new List<ulong>(checked(envelope.Buckets.Count * 4));

        foreach (var bucket in envelope.Buckets)
        {
            AddPoint(bucket.StartFrame, bucket.FirstTimestamp, bucket.FirstValue, values, timestamps, sourceFrames);

            var minFinite = double.IsFinite(bucket.Minimum);
            var maxFinite = double.IsFinite(bucket.Maximum);
            if (minFinite && maxFinite && bucket.MinimumFrame <= bucket.MaximumFrame)
            {
                AddPoint(bucket.MinimumFrame, bucket.MinimumTimestamp, bucket.Minimum, values, timestamps, sourceFrames);
                AddPoint(bucket.MaximumFrame, bucket.MaximumTimestamp, bucket.Maximum, values, timestamps, sourceFrames);
            }
            else if (minFinite && maxFinite)
            {
                AddPoint(bucket.MaximumFrame, bucket.MaximumTimestamp, bucket.Maximum, values, timestamps, sourceFrames);
                AddPoint(bucket.MinimumFrame, bucket.MinimumTimestamp, bucket.Minimum, values, timestamps, sourceFrames);
            }
            else if (minFinite)
            {
                AddPoint(bucket.MinimumFrame, bucket.MinimumTimestamp, bucket.Minimum, values, timestamps, sourceFrames);
            }
            else if (maxFinite)
            {
                AddPoint(bucket.MaximumFrame, bucket.MaximumTimestamp, bucket.Maximum, values, timestamps, sourceFrames);
            }

            var lastFrame = bucket.EndExclusive == 0 ? bucket.StartFrame : bucket.EndExclusive - 1;
            AddPoint(lastFrame, bucket.LastTimestamp, bucket.LastValue, values, timestamps, sourceFrames);
        }

        return new ComtradeAnalogPlotSeries(values.ToArray(), timestamps.ToArray(), sourceFrames.ToArray());
    }

    internal static ComtradeDigitalPlotSeries BuildDigital(ComtradeDigitalTransitionSet transitionSet)
    {
        ArgumentNullException.ThrowIfNull(transitionSet);
        if (transitionSet.Transitions.Count == 0)
        {
            return new ComtradeDigitalPlotSeries(
                Array.Empty<byte>(), Array.Empty<uint>(), Array.Empty<ulong>(), transitionSet.IsTruncated);
        }

        var capacity = checked(transitionSet.Transitions.Count + 1);
        var states = new List<byte>(capacity);
        var timestamps = new List<uint>(capacity);
        var sourceFrames = new List<ulong>(capacity);

        foreach (var transition in transitionSet.Transitions)
        {
            states.Add(transition.State == 0 ? (byte)0 : (byte)1);
            timestamps.Add(transition.Timestamp);
            sourceFrames.Add(transition.Frame);
        }

        var lastState = states[^1];
        var lastStoredTimestamp = timestamps[^1];
        var rangeLastFrame = transitionSet.Range.FrameCount == 0
            ? transitionSet.Range.StartFrame
            : transitionSet.Range.EndExclusive - 1;
        if (transitionSet.LastTimestamp != lastStoredTimestamp || sourceFrames[^1] != rangeLastFrame)
        {
            states.Add(lastState);
            timestamps.Add(transitionSet.LastTimestamp);
            sourceFrames.Add(rangeLastFrame);
        }
        else if (states.Count == 1)
        {
            states.Add(lastState);
            timestamps.Add(lastStoredTimestamp);
            sourceFrames.Add(sourceFrames[^1]);
        }

        return new ComtradeDigitalPlotSeries(
            states.ToArray(), timestamps.ToArray(), sourceFrames.ToArray(), transitionSet.IsTruncated);
    }

    private static void AddPoint(
        ulong sourceFrame,
        uint timestamp,
        double value,
        List<double> values,
        List<uint> timestamps,
        List<ulong> sourceFrames)
    {
        if (!double.IsFinite(value))
            return;

        if (sourceFrames.Count > 0 && sourceFrame < sourceFrames[^1])
            throw new InvalidOperationException("Decimated COMTRADE plot points must be source-frame monotonic.");

        // Raw timestamps should also be monotonic. Keep the plot searchable even for a malformed
        // recorder timestamp while source-frame ordering remains authoritative for native reloads.
        if (timestamps.Count > 0 && timestamp < timestamps[^1])
            timestamp = timestamps[^1];

        if (sourceFrames.Count > 0 &&
            sourceFrames[^1] == sourceFrame &&
            timestamps[^1] == timestamp &&
            values[^1] == value)
            return;

        sourceFrames.Add(sourceFrame);
        timestamps.Add(timestamp);
        values.Add(value);
    }
}
