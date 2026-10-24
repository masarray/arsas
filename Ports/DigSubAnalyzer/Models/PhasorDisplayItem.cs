// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Models/PhasorDisplayItem.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Models;

public enum PhasorFamily
{
    Voltage,
    Current
}

public sealed class PhasorDisplayItem
{
    public required string Name { get; init; }
    public required PhasorFamily Family { get; init; }
    public double? Magnitude { get; init; }
    public double? AngleDegrees { get; init; }
    public string Unit { get; init; } = string.Empty;
    public ChannelFreshness Freshness { get; init; } = ChannelFreshness.Live;
    public bool HasValue => Magnitude.HasValue && AngleDegrees.HasValue;
    public string MagnitudeDisplayText => Magnitude.HasValue ? $"{Magnitude:0.###}" : "N/A";
    public string AngleDisplayText => AngleDegrees.HasValue ? $"{AngleDegrees:0.###} deg" : "N/A";
}
