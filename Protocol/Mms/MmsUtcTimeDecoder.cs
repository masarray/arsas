using System;
using System.Globalization;

namespace ArIED61850Tester.Protocol.Mms;

/// <summary>
/// IEC 61850 UTC-Time quality octet, proven only from an exact 8-byte
/// MMS UTC-Time payload or the renderer's unambiguous canonical encoding.
/// The 5-bit time-accuracy code is not silently converted to seconds.
/// </summary>
public sealed record Iec61850UtcTimeEvidence(
    bool LeapSecondsKnown, bool ClockFailure, bool ClockNotSynchronized, byte TimeAccuracy)
{
    public string FormatField(string field) => field switch
    {
        "LeapSecondsKnown" => LeapSecondsKnown ? "true" : "false",
        "ClockFailure" => ClockFailure ? "true" : "false",
        "ClockNotSynchronized" => ClockNotSynchronized ? "true" : "false",
        "TimeAccuracy" => TimeAccuracy == 31 ? "Unknown (31)" :
            TimeAccuracy.ToString(CultureInfo.InvariantCulture),
        _ => "Not exposed"
    };
}

public static class MmsUtcTimeDecoder
{
    public static bool TryDecode(ReadOnlySpan<byte> payload, out Iec61850UtcTimeEvidence evidence)
    {
        evidence = null!;
        if (payload.Length != 8) return false;
        var flags = payload[7];
        evidence = new Iec61850UtcTimeEvidence(
            (flags & 0x80) != 0, (flags & 0x40) != 0,
            (flags & 0x20) != 0, (byte)(flags & 0x1F));
        return true;
    }

    /// <summary>
    /// Parse only output of Decode(), never ISO strings, alarm timestamps,
    /// process-source labels or numeric guesses. Unknown field flags remain
    /// "Not exposed" in the Browser rather than falsely claiming healthy time.
    /// </summary>
    public static bool TryDecodeRendered(string? value, out Iec61850UtcTimeEvidence evidence)
    {
        evidence = null!;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var span = value.AsSpan();
        const string divider = " UTC (accuracy=";
        var at = span.IndexOf(divider.AsSpan(), StringComparison.Ordinal);
        if (at != 23 || !span.EndsWith(")".AsSpan(), StringComparison.Ordinal))
            return false;
        if (!DateTime.TryParseExact(span[..23],
                "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                DateTimeStyles.None, out _))
            return false;
        var tail = span[(at + divider.Length)..^1];
        var comma = tail.IndexOf(',');
        var accuracyText = (comma < 0 ? tail : tail[..comma]);
        if (!byte.TryParse(accuracyText, NumberStyles.None,
                CultureInfo.InvariantCulture, out var accuracy) || accuracy > 31 ||
            accuracy.ToString(CultureInfo.InvariantCulture).Length != accuracyText.Length)
            return false;
        var rest = comma < 0 ? ReadOnlySpan<char>.Empty : tail[comma..];
        var leap = false;
        var failure = false;
        var unsynchronized = false;
        if (rest.StartsWith(", leapSecondKnown".AsSpan(), StringComparison.Ordinal))
        {
            leap = true;
            rest = rest[", leapSecondKnown".Length..];
        }
        if (rest.StartsWith(", clockFailure".AsSpan(), StringComparison.Ordinal))
        {
            failure = true;
            rest = rest[", clockFailure".Length..];
        }
        if (rest.StartsWith(", notSynchronized".AsSpan(), StringComparison.Ordinal))
        {
            unsynchronized = true;
            rest = rest[", notSynchronized".Length..];
        }
        if (rest.Length != 0) return false;
        evidence = new Iec61850UtcTimeEvidence(leap, failure, unsynchronized, accuracy);
        return true;
    }
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
