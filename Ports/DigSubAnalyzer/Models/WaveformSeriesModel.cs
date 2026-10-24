// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Models/WaveformSeriesModel.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Models;

public sealed class WaveformSeriesModel
{
    public required string Name { get; init; }
    public string Unit { get; init; } = string.Empty;
    public ChannelFreshness Freshness { get; init; } = ChannelFreshness.Live;
    public IReadOnlyList<double> Samples { get; init; } = Array.Empty<double>();
    public string ShapeSeverity { get; init; } = "Unknown";
    public string ShapeStatusText { get; init; } = "Shape pending";
    public double ShapeResidualPercent { get; init; }
    public double CrestFactor { get; init; }
    public bool HasShapeDistortion { get; init; }
}
