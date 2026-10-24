using ProcessBus.Core.Models;

namespace ProcessBus.App.Wpf.Controls;

/// <summary>
/// Presentation time axis only. In mapped IEC 9-2LE the first rising Ua
/// zero crossing within the first cycle is t=0; sample order and sample
/// counter evidence are NOT re-timed or modified. In RAW/nonmapped input,
/// t=0 is the first visible sample, NOT a fabricated voltage phase.
/// </summary>
public static class SmvWaveformTimeAxis
{
    public static int AvailableSamples(WaveformSnapshot snapshot)
    {
        var count = 0;
        foreach (var row in snapshot.VoltageSeries)
            count = Math.Max(count, row.Samples.Count);
        foreach (var row in snapshot.CurrentSeries)
            count = Math.Max(count, row.Samples.Count);
        return count;
    }

    public static double ReferenceSample(WaveformSnapshot snapshot, int visibleSamples)
    {
        if (visibleSamples < 2)
            return 0;
        // CurrentSeries in this legacy visualization contract contains wire
        // Ch5-8 (voltage in 9-2LE). Prefer semantic name, not container name.
        WaveformSeriesModel? ua = null;
        foreach (var row in snapshot.CurrentSeries)
            if (row.Name == "Ua") { ua = row; break; }
        if (ua is null)
            foreach (var row in snapshot.VoltageSeries)
                if (row.Name == "Ua") { ua = row; break; }
        if (ua is null || ua.Samples.Count < 2)
            return 0;

        var count = Math.Min(visibleSamples, ua.Samples.Count);
        var offset = ua.Samples.Count - count;
        var span = Math.Min(count, snapshot.SamplesPerCycle > 1 ? snapshot.SamplesPerCycle : count);
        for (var i = 1; i < span; i++)
        {
            var before = ua.Samples[offset + i - 1];
            var after = ua.Samples[offset + i];
            if (!double.IsFinite(before) || !double.IsFinite(after))
                continue;
            if (before <= 0 && after > 0)
                return i - 1 + (-before / (after - before));
        }
        return 0; // No visible Ua rising crossing: frame-relative axis, no claim.
    }
}
