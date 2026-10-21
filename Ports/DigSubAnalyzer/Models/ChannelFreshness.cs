// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Models/ChannelFreshness.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Models;

/// <summary>
/// Evidence-based status of a single decoded SV channel; never infer stale
/// from a legitimate flat/zero signal. Stream timeout is an independent
/// display override, shared by all channels from the selected publisher.
/// </summary>
public enum ChannelFreshness
{
    Live = 0,
    Held = 1,
    Missing = 2
}

public static class ChannelFreshnessDisplay
{
    public static double Opacity(ChannelFreshness status) => status switch
    {
        ChannelFreshness.Live => 1.0,
        ChannelFreshness.Held => 0.38,
        _ => 0.22
    };

    public static string Suffix(ChannelFreshness status) => status switch
    {
        ChannelFreshness.Held => "HELD",
        ChannelFreshness.Missing => "MISSING",
        _ => string.Empty
    };
}
