namespace ArIED61850Tester.Services;

internal sealed record ComtradeAnalogPlotSeries(double[] Values, uint[] Timestamps);
internal sealed record ComtradeDigitalPlotSeries(byte[] States, uint[] Timestamps, bool IsTruncated);

internal static class ComtradeDecimatedSeriesBuilder
{
    internal static ComtradeAnalogPlotSeries BuildAnalog(ComtradeAnalogEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        if (envelope.Buckets.Count == 0)
            return new ComtradeAnalogPlotSeries(Array.Empty<double>(), Array.Empty<uint>());

        var values = new List<double>(checked(envelope.Buckets.Count * 4));
        var timestamps = new List<uint>(checked(envelope.Buckets.Count * 4));

        foreach (var bucket in envelope.Buckets)
        {
            AddPoint(bucket.FirstTimestamp, bucket.FirstValue, values, timestamps);

            var minFinite = double.IsFinite(bucket.Minimum);
            var maxFinite = double.IsFinite(bucket.Maximum);
            if (minFinite && maxFinite && bucket.MinimumTimestamp <= bucket.MaximumTimestamp)
            {
                AddPoint(bucket.MinimumTimestamp, bucket.Minimum, values, timestamps);
                AddPoint(bucket.MaximumTimestamp, bucket.Maximum, values, timestamps);
            }
            else if (minFinite && maxFinite)
            {
                AddPoint(bucket.MaximumTimestamp, bucket.Maximum, values, timestamps);
                AddPoint(bucket.MinimumTimestamp, bucket.Minimum, values, timestamps);
            }
            else if (minFinite)
            {
                AddPoint(bucket.MinimumTimestamp, bucket.Minimum, values, timestamps);
            }
            else if (maxFinite)
            {
                AddPoint(bucket.MaximumTimestamp, bucket.Maximum, values, timestamps);
            }

            AddPoint(bucket.LastTimestamp, bucket.LastValue, values, timestamps);
        }

        return new ComtradeAnalogPlotSeries(values.ToArray(), timestamps.ToArray());
    }

    internal static ComtradeDigitalPlotSeries BuildDigital(ComtradeDigitalTransitionSet transitionSet)
    {
        ArgumentNullException.ThrowIfNull(transitionSet);
        if (transitionSet.Transitions.Count == 0)
            return new ComtradeDigitalPlotSeries(Array.Empty<byte>(), Array.Empty<uint>(), transitionSet.IsTruncated);

        var capacity = checked(transitionSet.Transitions.Count + 1);
        var states = new List<byte>(capacity);
        var timestamps = new List<uint>(capacity);

        foreach (var transition in transitionSet.Transitions)
        {
            states.Add(transition.State == 0 ? (byte)0 : (byte)1);
            timestamps.Add(transition.Timestamp);
        }

        var lastState = states[^1];
        var lastStoredTimestamp = timestamps[^1];
        if (transitionSet.LastTimestamp != lastStoredTimestamp)
        {
            states.Add(lastState);
            timestamps.Add(transitionSet.LastTimestamp);
        }
        else if (states.Count == 1)
        {
            // A one-frame/one-transition range still needs a second plotting point so the
            // existing timestamp-aware waveform navigation can show a deterministic span.
            states.Add(lastState);
            timestamps.Add(lastStoredTimestamp);
        }

        return new ComtradeDigitalPlotSeries(states.ToArray(), timestamps.ToArray(), transitionSet.IsTruncated);
    }

    private static void AddPoint(uint timestamp, double value, List<double> values, List<uint> timestamps)
    {
        if (!double.IsFinite(value))
            return;

        // Preserve monotonic timestamp ordering required by the existing binary-search mapping.
        // Equal timestamps are valid and keep a narrow spike visible as a vertical segment.
        if (timestamps.Count > 0 && timestamp < timestamps[^1])
            timestamp = timestamps[^1];

        if (timestamps.Count > 0 && timestamps[^1] == timestamp && values[^1] == value)
            return;

        timestamps.Add(timestamp);
        values.Add(value);
    }
}
