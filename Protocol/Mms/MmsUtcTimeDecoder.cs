using System;
using System.Globalization;

namespace ArIED61850Tester.Protocol.Mms;

public static class MmsUtcTimeDecoder
{
    public static string Decode(ReadOnlySpan<byte> utcTime)
    {
        if (utcTime.Length < 8)
            return BitConverter.ToString(utcTime.ToArray());

        var seconds = ((uint)utcTime[0] << 24) | ((uint)utcTime[1] << 16) | ((uint)utcTime[2] << 8) | utcTime[3];
        var fraction = ((uint)utcTime[4] << 16) | ((uint)utcTime[5] << 8) | utcTime[6];
        var quality = utcTime[7];

        var epoch = DateTimeOffset.FromUnixTimeSeconds(seconds);
        var fractionSeconds = fraction / 16777216.0;
        var timestamp = epoch.AddTicks((long)Math.Round(fractionSeconds * TimeSpan.TicksPerSecond));

        var leapSecondKnown = (quality & 0x80) != 0;
        var clockFailure = (quality & 0x40) != 0;
        var clockNotSynchronized = (quality & 0x20) != 0;
        var accuracy = quality & 0x1F;

        var flags = $"accuracy={accuracy}";
        if (leapSecondKnown) flags += ", leapSecondKnown";
        if (clockFailure) flags += ", clockFailure";
        if (clockNotSynchronized) flags += ", notSynchronized";

        return $"{timestamp.UtcDateTime:yyyy-MM-dd HH:mm:ss.fff} UTC ({flags})";
    }
}
