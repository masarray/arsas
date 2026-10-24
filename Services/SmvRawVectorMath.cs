using System.Globalization;

namespace ArIED61850Tester.Services;

/// <summary>
/// DFT of the decoded, bounded SV evidence window. This is a RAW observation,
/// NOT a calibrated voltage/current measurement or an SCL channel mapping.
/// </summary>
public static class SmvRawVectorMath
{
    public static IReadOnlyList<SmvRawVectorMeasurement> Analyze(SmvSnapshotResult snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsCleanProof ||
            snapshot.SamplesPerCycle < 4 ||
            snapshot.CycleCount < 1 ||
            snapshot.TargetSamples < 4 ||
            (long)snapshot.SamplesPerCycle * snapshot.CycleCount != snapshot.TargetSamples)
            return Array.Empty<SmvRawVectorMeasurement>();

        var count = snapshot.TargetSamples;
        var output = new List<SmvRawVectorMeasurement>(snapshot.Channels.Count);
        foreach (var channel in snapshot.Channels)
        {
            var samples = channel.Samples;
            if (samples.Count != count || samples.Any(value => !double.IsFinite(value)))
                return Array.Empty<SmvRawVectorMeasurement>();

            // Remove the DC mean from the fundamental only. Total raw RMS
            // intentionally includes DC and is labeled as such.
            double mean = 0;
            foreach (var sample in samples)
                mean += sample / count;
            double rmsSquared = 0, real = 0, imaginary = 0;
            for (var i = 0; i < count; i++)
            {
                var value = samples[i];
                rmsSquared += value * value / count;
                var centered = value - mean;
                var angle = 2 * Math.PI * i / snapshot.SamplesPerCycle;
                real += centered * Math.Cos(angle) / count;
                imaginary -= centered * Math.Sin(angle) / count;
            }

            var rms = Math.Sqrt(rmsSquared);
            var fundamental = Math.Sqrt(2) * Math.Sqrt(real * real + imaginary * imaginary);
            if (!double.IsFinite(rms) || !double.IsFinite(fundamental))
                return Array.Empty<SmvRawVectorMeasurement>();

            // Flat / DC-only lanes have no meaningful phase direction.
            var phaseDefined = fundamental > Math.Max(1e-9, rms * 1e-9);
            var degrees = phaseDefined ? Math.Atan2(imaginary, real) * 180 / Math.PI : 0;
            output.Add(new SmvRawVectorMeasurement(
                channel.ChannelIndex, channel.Label, rms,
                phaseDefined ? fundamental : 0, degrees, phaseDefined));
        }
        return output;
    }
}

public sealed record SmvRawVectorMeasurement(
    int ChannelIndex,
    string Label,
    double Rms,
    double FundamentalRms,
    double PhaseDegrees,
    bool HasFundamental)
{
    public string RmsText => Rms.ToString("0.##", CultureInfo.InvariantCulture);
    public string FundamentalRmsText => FundamentalRms.ToString("0.##", CultureInfo.InvariantCulture);
    public string AngleText => HasFundamental
        ? PhaseDegrees.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°"
        : "—";
}
