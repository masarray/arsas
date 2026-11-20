using System.Globalization;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Views;

/// <summary>
/// One coherent same-ASDU eight-channel U/I window. Engineering units are
/// operator-selected 9-2LE assumptions, never SCL/protection-calibration proof.
/// P = arithmetic mean(u*i), S = true U/I RMS product, PF = P/S.
/// Q1 uses *fundamental* DFT magnitude and phase displacement, and must not
/// be mislabeled as total/non-sinusoidal reactive power.
/// </summary>
public static class SmvPowerMeasurements
{
    public sealed record Phase(string Name, double P, double Q1, double S, double PF)
    {
        public string PText => Format(P, "W");
        public string QText => Format(Q1, "var");
        public string SText => Format(S, "VA");
        public string PfText => PF.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture);
    }

    public sealed record Result(IReadOnlyList<Phase> Phases, Phase Total);

    public static Result? Analyze(SmvSnapshotResult? snapshot,
        IReadOnlyList<SmvRawVectorMeasurement> vectors, SmvDisplayUnits selected)
    {
        if (snapshot is null || !snapshot.IsCleanProof || snapshot.TargetSamples < 8 ||
            SmvEngineeringDisplay.Effective(snapshot, selected) != SmvDisplayUnits.NineTwoLe)
            return null;

        var output = new List<Phase>(3);
        for (var phase = 0; phase < 3; phase++)
        {
            var iChannel = phase + 1;
            var uChannel = phase + 5;
            var i = snapshot.Channels.FirstOrDefault(x => x.ChannelIndex == iChannel);
            var u = snapshot.Channels.FirstOrDefault(x => x.ChannelIndex == uChannel);
            var iVector = vectors.FirstOrDefault(x => x.ChannelIndex == iChannel);
            var uVector = vectors.FirstOrDefault(x => x.ChannelIndex == uChannel);
            if (i is null || u is null || iVector is null || uVector is null ||
                i.Samples.Count != snapshot.TargetSamples || u.Samples.Count != snapshot.TargetSamples)
                return null;

            var p = 0.0;
            for (var sample = 0; sample < snapshot.TargetSamples; sample++)
                p += (u.Samples[sample] * 0.01) * (i.Samples[sample] * 0.001);
            p /= snapshot.TargetSamples;

            var s = uVector.Rms * 0.01 * iVector.Rms * 0.001;
            // A DFT with no fundamental cannot assert a signed displacement-Q.
            if (!uVector.HasFundamental || !iVector.HasFundamental ||
                !double.IsFinite(p) || !double.IsFinite(s))
                return null;
            var angle = (uVector.PhaseDegrees - iVector.PhaseDegrees) * Math.PI / 180.0;
            var q = uVector.FundamentalRms * 0.01 *
                    iVector.FundamentalRms * 0.001 * Math.Sin(angle);
            if (!double.IsFinite(q))
                return null;
            output.Add(new Phase(((char)('a' + phase)).ToString(),
                p, q, s, s > 1e-12 ? Math.Clamp(p / s, -1, 1) : 0));
        }

        var totalP = output.Sum(x => x.P);
        var totalQ = output.Sum(x => x.Q1);
        var totalS = output.Sum(x => x.S);
        var total = new Phase("Σ", totalP, totalQ, totalS,
            totalS > 1e-12 ? Math.Clamp(totalP / totalS, -1, 1) : 0);
        return new Result(output, total);
    }

    public static string Format(double value, string unit)
    {
        var scaled = Math.Abs(value) >= 1000;
        return (scaled ? value / 1000 : value)
            .ToString("0.###", CultureInfo.InvariantCulture) +
            (scaled ? " k" : " ") + unit;
    }
}
