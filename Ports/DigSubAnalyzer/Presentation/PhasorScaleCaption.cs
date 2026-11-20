// DigSubAnalyzer presentation port. Original: src/ProcessBus.Core/Presentation/PhasorScaleCaption.cs
// Copyright original contributors; GPL-3.0-or-later.
using System.Globalization;

namespace ProcessBus.Core.Presentation;

/// <summary>
/// Compact engineering labels for the normalized U/I plot; never mutate or
/// quantize the underlying RMS values or phase-angle measurements.
/// </summary>
public static class PhasorScaleCaption
{
    public static string Format(string family, double rmsMaximum, string? unit)
    {
        if (!double.IsFinite(rmsMaximum) || rmsMaximum <= 0)
            return $"{family} N/A";

        var suffix = string.IsNullOrWhiteSpace(unit) ? "counts" : unit.Trim();
        var value = rmsMaximum;
        var prefix = string.Empty;
        if (value >= 1_000_000)
        {
            value /= 1_000_000;
            prefix = "M";
        }
        else if (value >= 10_000)
        {
            value /= 1_000;
            prefix = "k";
        }

        // Nonzero currents must never look like zero just because the legend
        // chose two decimal places. Keep 3 significant digits on small values.
        var format = value < 0.01 ? "G3" : "0.###";
        return $"{family} max {value.ToString(format, CultureInfo.InvariantCulture)} {prefix}{suffix}";
    }
}
