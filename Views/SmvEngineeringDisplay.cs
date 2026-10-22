using System.Globalization;
using ArIED61850Tester.Services;

namespace ArIED61850Tester.Views;

/// <summary>
/// Operator-selected PRESENTATION profile, not a packet decoder or SCL proof.
/// 9-2LE fixes 4 current then 4 voltage INT32 samples (with separate q).
/// Counts are never overwritten; a custom IEC 61869-9 DataSet stays RAW
/// unless its actual typed channel metadata is separately verified.
/// </summary>
public enum SmvDisplayUnits { RawCounts, NineTwoLe }

public readonly record struct SmvChannelUnit(string Name, string Unit, double Scale, bool IsVoltage);

public static class SmvEngineeringDisplay
{
    private static readonly string[] CurrentNames = ["Ia", "Ib", "Ic", "In"];
    private static readonly string[] VoltageNames = ["Ua", "Ub", "Uc", "Un"];

    public static bool IsProfileApplicable(SmvSnapshotResult? snapshot)
        => snapshot is { IsCleanProof: true } &&
           snapshot.Channels.Count == 8 &&
           snapshot.Channels.Select(c => c.ChannelIndex)
               .OrderBy(i => i).SequenceEqual(Enumerable.Range(1, 8));

    public static SmvDisplayUnits Effective(SmvSnapshotResult? snapshot, SmvDisplayUnits selected)
        => selected == SmvDisplayUnits.NineTwoLe && IsProfileApplicable(snapshot)
            ? SmvDisplayUnits.NineTwoLe : SmvDisplayUnits.RawCounts;

    public static SmvChannelUnit Describe(int channelIndex, SmvDisplayUnits profile)
    {
        if (profile == SmvDisplayUnits.NineTwoLe && channelIndex is >= 1 and <= 4)
            return new SmvChannelUnit(CurrentNames[channelIndex - 1], "A", 0.001, false);
        if (profile == SmvDisplayUnits.NineTwoLe && channelIndex is >= 5 and <= 8)
            return new SmvChannelUnit(VoltageNames[channelIndex - 5], "V", 0.01, true);
        return new SmvChannelUnit($"Ch{channelIndex}", "counts", 1, channelIndex <= 4);
    }

    public static SmvDisplayMeasurement Present(SmvRawVectorMeasurement raw, SmvDisplayUnits profile)
    {
        var channel = Describe(raw.ChannelIndex, profile);
        return new SmvDisplayMeasurement(raw.ChannelIndex, channel.Name, channel.Unit,
            raw.Rms * channel.Scale, raw.FundamentalRms * channel.Scale,
            raw.PhaseDegrees, raw.HasFundamental);
    }
}

public sealed record SmvDisplayMeasurement(
    int ChannelIndex, string DisplayName, string Unit, double Rms,
    double FundamentalRms, double PhaseDegrees, bool HasFundamental)
{
    public string RmsText => Rms.ToString("0.###", CultureInfo.InvariantCulture);
    public string FundamentalRmsText => FundamentalRms.ToString("0.###", CultureInfo.InvariantCulture);
    public string AngleText => HasFundamental
        ? PhaseDegrees.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°"
        : "—";
}
