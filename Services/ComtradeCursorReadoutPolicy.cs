using System.Globalization;

namespace ArIED61850Tester.Services;

/// <summary>
/// Allocation-free policy helpers for the COMTRADE cursor-readout hot path. The worker may only
/// present the exact latest revision; invalid/native-unavailable samples remain explicit in the UI
/// instead of silently turning the readout into an empty string.
/// </summary>
internal static class ComtradeCursorReadoutPolicy
{
    internal static bool IsCurrent(long requestRevision, long currentRevision, bool cancelled)
        => !cancelled && requestRevision == currentRevision;

    internal static string FormatValue(
        string cursorLabel,
        bool rms,
        double? value,
        IFormatProvider? provider = null)
    {
        var mode = rms ? "RMS" : "Inst";
        if (value is not { } finite || !double.IsFinite(finite))
            return $"{cursorLabel} {mode} —";

        return $"{cursorLabel} {mode} {finite.ToString("G6", provider ?? CultureInfo.CurrentCulture)}";
    }
}