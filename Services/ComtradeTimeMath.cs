using System.Globalization;

namespace ArIED61850Tester.Services;

internal static class ComtradeTimeMath
{
    private static readonly string[] TimestampFormats =
    [
        "dd/MM/yyyy,HH:mm:ss.fffffff",
        "dd/MM/yyyy,HH:mm:ss.ffffff",
        "dd/MM/yyyy,HH:mm:ss.fffff",
        "dd/MM/yyyy,HH:mm:ss.ffff",
        "dd/MM/yyyy,HH:mm:ss.fff",
        "dd/MM/yyyy,HH:mm:ss.ff",
        "dd/MM/yyyy,HH:mm:ss.f",
        "dd/MM/yyyy,HH:mm:ss",
        "MM/dd/yyyy,HH:mm:ss.fffffff",
        "MM/dd/yyyy,HH:mm:ss.ffffff",
        "MM/dd/yyyy,HH:mm:ss.fff",
        "MM/dd/yyyy,HH:mm:ss"
    ];

    internal static double ToMilliseconds(uint rawTimestamp, double timeMultiplier)
    {
        var multiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        return rawTimestamp * multiplier / 1000.0;
    }

    /// <summary>
    /// Converts a COMTRADE DAT timestamp to elapsed milliseconds from the first recorded sample.
    /// Some legacy/third-party records start DAT timestamps at one sample period rather than zero;
    /// StartTime still describes the first recorded sample. Subtracting the first raw timestamp is
    /// therefore required before comparing sample time with the CFG trigger offset.
    /// </summary>
    internal static double ToRecordMilliseconds(uint rawTimestamp, uint firstRawTimestamp, double timeMultiplier)
    {
        var multiplier = timeMultiplier > 0 && double.IsFinite(timeMultiplier) ? timeMultiplier : 1.0;
        return (rawTimestamp - (double)firstRawTimestamp) * multiplier / 1000.0;
    }

    internal static bool TryGetTriggerOffsetMilliseconds(string startText, string triggerText, out double offsetMilliseconds)
    {
        offsetMilliseconds = 0;
        if (!TryParseTimestamp(startText, out var start) || !TryParseTimestamp(triggerText, out var trigger))
            return false;

        offsetMilliseconds = (trigger - start).TotalMilliseconds;
        return double.IsFinite(offsetMilliseconds);
    }

    internal static double FractionForFrame(uint[] timestamps, ComtradeFrameWindow window, int frameIndex)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        window = ComtradeNavigationMath.Normalize(window, timestamps.Length);
        if (window.Count <= 1)
            return 0;

        frameIndex = Math.Clamp(frameIndex, window.Start, window.EndExclusive - 1);
        var first = timestamps[window.Start];
        var last = timestamps[window.EndExclusive - 1];
        if (last > first)
            return Math.Clamp((timestamps[frameIndex] - (double)first) / (last - (double)first), 0.0, 1.0);

        return (frameIndex - window.Start) / (double)(window.Count - 1);
    }

    internal static int FrameAtFraction(uint[] timestamps, ComtradeFrameWindow window, double fraction)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        window = ComtradeNavigationMath.Normalize(window, timestamps.Length);
        if (window.Count <= 0)
            return -1;
        if (window.Count == 1)
            return window.Start;
        if (!double.IsFinite(fraction))
            fraction = 0;
        fraction = Math.Clamp(fraction, 0.0, 1.0);

        var first = timestamps[window.Start];
        var last = timestamps[window.EndExclusive - 1];
        if (last <= first)
            return ComtradeNavigationMath.FrameAtFraction(window, timestamps.Length, fraction);

        var target = first + (last - (double)first) * fraction;
        var low = window.Start;
        var high = window.EndExclusive - 1;
        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (timestamps[middle] < target)
                low = middle + 1;
            else
                high = middle;
        }

        var upper = low;
        var lower = Math.Max(window.Start, upper - 1);
        return Math.Abs(timestamps[lower] - target) <= Math.Abs(timestamps[upper] - target)
            ? lower
            : upper;
    }

    private static bool TryParseTimestamp(string value, out DateTime timestamp)
    {
        timestamp = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        if (DateTime.TryParseExact(
                text,
                TimestampFormats,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AllowWhiteSpaces,
                out timestamp))
            return true;

        return DateTime.TryParse(
            text,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces,
            out timestamp);
    }
}
