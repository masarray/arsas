namespace ArIED61850Tester.Services;

internal static class ComtradeDisturbanceTimelineMath
{
    internal static int NearestTimestampIndex(IReadOnlyList<uint> timestamps, double targetRawTimestamp)
    {
        ArgumentNullException.ThrowIfNull(timestamps);
        if (timestamps.Count == 0) return -1;
        if (timestamps.Count == 1) return 0;
        if (!double.IsFinite(targetRawTimestamp)) throw new ArgumentOutOfRangeException(nameof(targetRawTimestamp));

        var lo = 0;
        var hi = timestamps.Count - 1;
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
        return new[] { "TRIP", "PICK", "START", "OPER", "OPEN", "CLOSE", "BREAKER", "CB", "52", "87", "50", "51", "21" }
            .Any(token => value.Contains(token, StringComparison.Ordinal));
    }

    internal static string FormatRelativeTime(double milliseconds)
    {
        if (!double.IsFinite(milliseconds)) return "—";
        return Math.Abs(milliseconds) < 0.0005
            ? "0 ms"
            : $"{milliseconds:+0.###;-0.###} ms";
    }
}
