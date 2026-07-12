using System;
using System.Collections.Generic;

namespace ArIED61850Tester.Protocol.Mms;

public static class MmsQualityDecoder
{
    public static string Decode(ReadOnlySpan<byte> bitStringPayload)
    {
        if (bitStringPayload.Length < 2)
            return "Quality: empty";

        var bits = ExpandBits(bitStringPayload[1..]);
        var validity = bits.Count >= 2
            ? ((bits[0] ? 2 : 0) | (bits[1] ? 1 : 0))
            : 0;

        var validityText = validity switch
        {
            0 => "Good",
            1 => "Invalid",
            2 => "Reserved",
            3 => "Questionable",
            _ => "Unknown"
        };

        var details = new List<string> { validityText };
        Add(details, bits, 2, "Overflow");
        Add(details, bits, 3, "OutOfRange");
        Add(details, bits, 4, "BadReference");
        Add(details, bits, 5, "Oscillatory");
        Add(details, bits, 6, "Failure");
        Add(details, bits, 7, "OldData");
        Add(details, bits, 8, "Inconsistent");
        Add(details, bits, 9, "Inaccurate");
        Add(details, bits, 10, "Substituted");
        Add(details, bits, 11, "Test");
        Add(details, bits, 12, "OperatorBlocked");
        return string.Join(" / ", details);
    }

    private static List<bool> ExpandBits(ReadOnlySpan<byte> bytes)
    {
        var result = new List<bool>(bytes.Length * 8);
        foreach (var b in bytes)
        {
            for (var bit = 7; bit >= 0; bit--)
                result.Add(((b >> bit) & 0x01) != 0);
        }
        return result;
    }

    private static void Add(List<string> details, List<bool> bits, int index, string label)
    {
        if (bits.Count > index && bits[index]) details.Add(label);
    }
}
