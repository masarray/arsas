using ArIED61850Tester.Services;
using ProcessBus.Core.Models;

namespace ArIED61850Tester.Views;

/// <summary>
/// ARIEC61850 decoded and coherent snapshot -> original DigSubAnalyzer WPF
/// visualization contracts. No second packet decoder, no synthesized samples.
/// Counters anchor the DISPLAY phase; all raw evidence is unchanged.
/// </summary>
public static class SmvDigSubPresentationAdapter
{
    public static WaveformSnapshot BuildWaveform(SmvSnapshotResult snapshot, SmvDisplayUnits profile = SmvDisplayUnits.RawCounts)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (!snapshot.IsCleanProof || snapshot.SamplesPerCycle < 4 ||
            snapshot.Channels.Any(c => c.Samples.Count != snapshot.TargetSamples))
            return new WaveformSnapshot { StatusText = "Waiting for coherent, trusted raw SV window." };

        profile = SmvEngineeringDisplay.Effective(snapshot, profile);
        var a = new List<WaveformSeriesModel>(4);
        var b = new List<WaveformSeriesModel>(4);
        foreach (var channel in snapshot.Channels.Take(8))
        {
            var aligned = PhaseAlign(channel.Samples, snapshot.FirstSampleCount,
                snapshot.SamplesPerCycle);
            var channelUnits = SmvEngineeringDisplay.Describe(channel.ChannelIndex, profile);
            var series = new WaveformSeriesModel
            {
                Name = $"Ch{channel.ChannelIndex}", // fixed color identity shared with RAW
                Unit = channelUnits.Unit,
                Freshness = ChannelFreshness.Live,
                Samples = channelUnits.Scale == 1 ? aligned : aligned.Select(v => v * channelUnits.Scale).ToArray(),
                ShapeSeverity = "Unknown",
                ShapeStatusText = "Raw decoded INT32; channel semantics unverified"
            };
            (channel.ChannelIndex <= 4 ? a : b).Add(series);
        }

        return new WaveformSnapshot
        {
            VoltageSeries = a,
            CurrentSeries = b,
            SamplesPerCycle = snapshot.SamplesPerCycle,
            SampleRateHz = snapshot.SamplesPerCycle * snapshot.NominalFrequencyHz,
            MeasuredFrequencyHz = 0, // never claim a measured frequency from nominal
            WindowDurationMilliseconds = 1000.0 * snapshot.CycleCount / snapshot.NominalFrequencyHz,
            StatusText = profile == SmvDisplayUnits.NineTwoLe
                ? "9-2LE engineering display · operator-selected I/U mapping; raw evidence unchanged"
                : "Coherent sample-counter-anchored raw SV; no engineering channel mapping",
            ShapeSeverity = "Unknown"
        };
    }

    /// <summary>
    /// Always start displayed cycles at smpCnt modulo samples-per-cycle = 0.
    /// Only permutes a CLOSED integer-cycle DISPLAY window: no filtering,
    /// interpolation, or sample synthesis. Packet evidence stays unmodified.
    /// </summary>
    public static double[] PhaseAlign(IReadOnlyList<double> samples,
        ushort firstCounter, int samplesPerCycle)
    {
        if (samplesPerCycle < 4 || samples.Count == 0 ||
            samples.Count % samplesPerCycle != 0)
            return samples.ToArray();
        var offset = (samplesPerCycle - firstCounter % samplesPerCycle) % samplesPerCycle;
        var output = new double[samples.Count];
        for (var i = 0; i < samples.Count; i++)
            output[i] = samples[(i + offset) % samples.Count];
        return output;
    }

    /// <summary>
    /// Display-relative raw phasors. The first live significant lane is
    /// the fixed 0° reference on every frame; never use absolute DFT phase
    /// from a rolling ring start, which creates false rotating phasors.
    /// These six names are RAW Ch indices, NOT guessed Ia/Ua identities.
    /// </summary>
    public static IReadOnlyList<PhasorDisplayItem> BuildPhasors(
        IReadOnlyList<SmvRawVectorMeasurement> vectors,
        SmvDisplayUnits profile = SmvDisplayUnits.RawCounts)
    {
        var reference = vectors.FirstOrDefault(v => v.ChannelIndex == 1 && v.HasFundamental)
            ?? vectors.FirstOrDefault(v => v.HasFundamental);
        if (reference is null)
            return [];

        var rows = new List<PhasorDisplayItem>(6);
        foreach (var vector in vectors)
        {
            if (vector.ChannelIndex is not (1 or 2 or 3 or 5 or 6 or 7))
                continue;
            var a = vector.HasFundamental
                ? Math.IEEERemainder(vector.PhaseDegrees - reference.PhaseDegrees, 360)
                : (double?)null;
            var units = SmvEngineeringDisplay.Describe(vector.ChannelIndex, profile);
            rows.Add(new PhasorDisplayItem
            {
                Name = units.Name,
                Family = units.IsVoltage ? PhasorFamily.Voltage : PhasorFamily.Current,
                Magnitude = vector.HasFundamental ? vector.FundamentalRms * units.Scale : null,
                AngleDegrees = a,
                Unit = units.Unit,
                Freshness = ChannelFreshness.Live
            });
        }
        return rows;
    }

    public static IReadOnlyList<SmvRawVectorMeasurement> RelativeMeasurements(
        IReadOnlyList<SmvRawVectorMeasurement> vectors)
    {
        var anchor = vectors.FirstOrDefault(v => v.ChannelIndex == 1 && v.HasFundamental)
            ?? vectors.FirstOrDefault(v => v.HasFundamental);
        if (anchor is null)
            return vectors;
        return vectors.Select(v => v.HasFundamental
            ? v with { PhaseDegrees = Math.IEEERemainder(v.PhaseDegrees - anchor.PhaseDegrees, 360) }
            : v).ToArray();
    }
}
