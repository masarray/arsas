// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Models/WaveformSnapshot.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Models;

public sealed class WaveformSnapshot
{
    public IReadOnlyList<WaveformSeriesModel> VoltageSeries { get; init; } = Array.Empty<WaveformSeriesModel>();
    public IReadOnlyList<WaveformSeriesModel> CurrentSeries { get; init; } = Array.Empty<WaveformSeriesModel>();
    public double SampleRateHz { get; init; }
    public double MeasuredFrequencyHz { get; init; }
    public int SamplesPerCycle { get; init; }
    public double WindowDurationMilliseconds { get; init; }
    public string StatusText { get; init; } = "Waiting for SV samples.";
    public string ShapeSeverity { get; init; } = "Unknown";
    public string ShapeStatusText { get; init; } = "Waveform shape pending.";
    public bool HasShapeWarning { get; init; }
    public bool IsReconstructed { get; init; }
}
