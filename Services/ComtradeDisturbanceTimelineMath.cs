namespace ArIED61850Tester.Services;

internal static class ComtradeDisturbanceTimelineMath
{
    internal static int NearestTimestampIndex(IReadOnlyList<uint> timestamps, double targetRawTimestamp)
        => NearestTimestampIndex(timestamps, timestamps?.Count ?? 0, targetRawTimestamp);

    internal static int NearestTimestampIndex(IReadOnlyList<uint> timestamps, int count, double targetRawTimestamp)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        count = Math.Clamp(count, 0, timestamps.Count);
        if (count == 0) return -1;
        if (count == 1) return 0;
        if (!double.IsFinite(targetRawTimestamp)) throw new ArgumentOutOfRangeException(nameof(targetRawTimestamp));

        var lo = 0;
        var hi = count - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (timestamps[mid] < targetRawTimestamp) lo = mid + 1;
            else hi = mid;
        }

        if (lo == 0) return 0;
        var before = lo - 1;
        return Math.Abs(timestamps[lo] - targetRawTimestamp) < Math.Abs(timestamps[before] - targetRawTimestamp)
            ? lo
            : before;
    }

    internal static string DescribeDigitalEvent(string? signalTitle, bool asserted)
    {
        var value = (signalTitle ?? string.Empty).ToUpperInvariant();
        if (value.Contains("TRIP", StringComparison.Ordinal)) return asserted ? "Trip" : "Trip reset";
        if (value.Contains("PICK", StringComparison.Ordinal) || value.Contains("START", StringComparison.Ordinal))
            return asserted ? "Pickup" : "Dropoff";
        if (value.Contains("OPEN", StringComparison.Ordinal)) return asserted ? "Open" : "Open reset";
        if (value.Contains("CLOSE", StringComparison.Ordinal)) return asserted ? "Close" : "Close reset";
        return asserted ? "Assert" : "Deassert";
    }

    internal static bool IsUsefulProtectionDigital(string? signalTitle)
    {
        var value = (signalTitle ?? string.Empty).ToUpperInvariant();
        return value.Contains("TRIP", StringComparison.Ordinal) ||
               value.Contains("PICK", StringComparison.Ordinal) ||
               value.Contains("START", StringComparison.Ordinal) ||
               value.Contains("OPER", StringComparison.Ordinal) ||
               value.Contains("OPEN", StringComparison.Ordinal) ||
               value.Contains("CLOSE", StringComparison.Ordinal) ||
               value.Contains("BREAKER", StringComparison.Ordinal) ||
               value.Contains("CB", StringComparison.Ordinal) ||
               value.Contains("52", StringComparison.Ordinal) ||
               value.Contains("87", StringComparison.Ordinal) ||
               value.Contains("50", StringComparison.Ordinal) ||
               value.Contains("51", StringComparison.Ordinal) ||
               value.Contains("21", StringComparison.Ordinal);
    }

    internal static string FormatRelativeTime(double milliseconds)
    {
        if (!double.IsFinite(milliseconds)) return "—";
        return Math.Abs(milliseconds) < 0.0005
            ? "0 ms"
            : $"{milliseconds:+0.###;-0.###} ms";
    }
}
