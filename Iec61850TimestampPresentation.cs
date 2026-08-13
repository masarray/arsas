using System.Globalization;

namespace ArIED61850Tester;

/// <summary>
/// Customer-facing IEC 61850 timestamp presentation helpers.
/// IEC 61850 timestamps retain their original full-resolution value in the model/evidence;
/// this class only rounds when a millisecond-resolution string is requested.
/// </summary>
public static class Iec61850TimestampPresentation
{
    private const long TicksPerMillisecond = TimeSpan.TicksPerMillisecond;
    private const long HalfMillisecondTicks = TicksPerMillisecond / 2;

    /// <summary>
    /// Rounds to the nearest millisecond. An exact half-millisecond is rounded forward
    /// so a value such as 31.2005000 is presented as 31.201 rather than truncated to 31.200.
    /// The original UTC offset is preserved.
    /// </summary>
    public static DateTimeOffset RoundToNearestMillisecond(DateTimeOffset value)
    {
        var delta = MillisecondDelta(value.Ticks);
        if (delta > 0 && value.UtcTicks > DateTime.MaxValue.Ticks - delta)
            delta = -(value.Ticks % TicksPerMillisecond);
        return value.AddTicks(delta);
    }

    public static DateTime RoundToNearestMillisecond(DateTime value)
    {
        var delta = MillisecondDelta(value.Ticks);
        return value.AddTicks(delta);
    }

    public static string FormatMilliseconds(DateTimeOffset value, string format)
        => RoundToNearestMillisecond(value).ToString(format, CultureInfo.InvariantCulture);

    public static string FormatMilliseconds(DateTimeOffset? value, string format, string missing = "—")
        => value.HasValue ? FormatMilliseconds(value.Value, format) : missing;

    public static string FormatMilliseconds(DateTime value, string format)
        => RoundToNearestMillisecond(value).ToString(format, CultureInfo.InvariantCulture);

    private static long MillisecondDelta(long ticks)
    {
        var remainder = ticks % TicksPerMillisecond;
        if (remainder == 0)
            return 0;

        var delta = remainder >= HalfMillisecondTicks
            ? TicksPerMillisecond - remainder
            : -remainder;

        if (delta > 0 && ticks > DateTime.MaxValue.Ticks - delta)
            return -remainder;

        return delta;
    }
}
