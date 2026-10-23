// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Models/WaveformAutoRangePolicy.cs
// Copyright original contributors; GPL-3.0-or-later.
namespace ProcessBus.Core.Models;

/// <summary>
/// Screen-only oscilloscope scale guard. A WPF panel is reused across SV streams
/// and raw-to-nominal unit transitions; old peaks cannot remain authoritative.
/// No decoded SV samples, phase, RMS, or packet diagnostics are altered.
/// </summary>
public static class WaveformAutoRangePolicy
{
    public static bool UnitsChanged(string? previousUnit, string? nextUnit) =>
        !string.Equals(previousUnit, nextUnit, StringComparison.OrdinalIgnoreCase);

    public static bool ShouldReleasePeakHold(double heldPeak, double incomingPeak)
    {
        if (!double.IsFinite(heldPeak) || heldPeak <= 0)
            return true;
        if (!double.IsFinite(incomingPeak) || incomingPeak <= 0)
            return false;
        return incomingPeak >= heldPeak || incomingPeak < heldPeak * 0.25;
    }
}
