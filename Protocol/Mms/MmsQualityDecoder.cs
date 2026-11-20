using System;
using System.Collections.Generic;
using System.Globalization;

namespace ArIED61850Tester.Protocol.Mms;

/// <summary>Thirteen verified IEC 61850 Quality bits, not inferred defaults.</summary>
public sealed record MmsQualityEvidence(
    string Validity, bool Overflow, bool OutOfRange, bool BadReference,
    bool Oscillatory, bool Failure, bool OldData, bool Inconsistent,
    bool Inaccurate, bool Substituted, bool Test, bool OperatorBlocked)
{
    public string Source => Substituted ? "substituted" : "process";

    public string FormatField(string field) => field switch
    {
        "" => Validity,
        "Validity" => Validity.ToLowerInvariant(),
        "Source" => Source,
        "Overflow" => Flag(Overflow),
        "OutOfRange" => Flag(OutOfRange),
        "BadReference" => Flag(BadReference),
        "Oscillatory" => Flag(Oscillatory),
        "Failure" => Flag(Failure),
        "OldData" => Flag(OldData),
        "Inconsistent" => Flag(Inconsistent),
        "Inaccurate" => Flag(Inaccurate),
        "Test" => Flag(Test),
        "OperatorBlocked" => Flag(OperatorBlocked),
        _ => "Not exposed"
    };

    public string Summary
    {
        get
        {
            var parts = new List<string> { Validity };
            Add(parts, Overflow, "Overflow");
            Add(parts, OutOfRange, "OutOfRange");
            Add(parts, BadReference, "BadReference");
            Add(parts, Oscillatory, "Oscillatory");
            Add(parts, Failure, "Failure");
            Add(parts, OldData, "OldData");
            Add(parts, Inconsistent, "Inconsistent");
            Add(parts, Inaccurate, "Inaccurate");
            Add(parts, Substituted, "Substituted");
            Add(parts, Test, "Test");
            Add(parts, OperatorBlocked, "OperatorBlocked");
            return string.Join(" / ", parts);
        }
    }

    private static string Flag(bool value) => value ? "true" : "false";
    private static void Add(List<string> parts, bool enabled, string name)
    {
        if (enabled) parts.Add(name);
    }
}

/// <summary>
/// Existing MMS Quality decoder, now fail-closed on width and unused-bit
/// metadata. The rendered-value adapter is used only for schema-verified q.
/// </summary>
public static class MmsQualityDecoder
{
    public static string Decode(ReadOnlySpan<byte> bitStringPayload)
        => TryDecode(bitStringPayload, out var quality)
            ? quality.Summary : "Quality: unavailable";

    public static bool TryDecode(ReadOnlySpan<byte> payload, out MmsQualityEvidence quality)
    {
        quality = null!;
        // Quality has exactly 13 significant bits. Reject nonzero padding.
        if (payload.Length != 3 || payload[0] != 3 || (payload[2] & 0x07) != 0)
            return false;

        var validity = (Bit(payload, 0) ? 2 : 0) | (Bit(payload, 1) ? 1 : 0);
        quality = new MmsQualityEvidence(
            validity switch
            {
                0 => "Good",
                1 => "Invalid",
                2 => "Reserved",
                _ => "Questionable"
            },
            Bit(payload, 2), Bit(payload, 3), Bit(payload, 4),
            Bit(payload, 5), Bit(payload, 6), Bit(payload, 7),
            Bit(payload, 8), Bit(payload, 9), Bit(payload, 10),
            Bit(payload, 11), Bit(payload, 12));
        return true;
    }

    private static bool Bit(ReadOnlySpan<byte> payload, int index)
        => (payload[1 + index / 8] & (0x80 >> (index % 8))) != 0;

    /// <summary>
    /// Adapter for the already existing MMS renderer's bits(0000,unused=3)
    /// representation. It does not decide which references have Quality type.
    /// </summary>
    public static bool TryDecodeRendered(string? rendered, out MmsQualityEvidence quality)
    {
        quality = null!;
        if (string.IsNullOrWhiteSpace(rendered))
            return false;

        var text = rendered.AsSpan().Trim();
        if (!text.StartsWith("bits(".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            text[^1] != ')')
            return false;
        var body = text[5..^1];
        var comma = body.IndexOf(',');
        if (comma < 0) return false;
        var hex = body[..comma].Trim();
        if (hex.StartsWith("0x".AsSpan(), StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        var suffix = body[(comma + 1)..].Trim();
        if (!suffix.StartsWith("unused=".AsSpan(), StringComparison.OrdinalIgnoreCase) ||
            !byte.TryParse(suffix[7..].Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var padding) ||
            hex.Length != 4 ||
            !byte.TryParse(hex[..2], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var first) ||
            !byte.TryParse(hex[2..], NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var second))
            return false;

        Span<byte> payload = stackalloc byte[] { padding, first, second };
        return TryDecode(payload, out quality);
    }
}
