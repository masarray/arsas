using System;
using System.Linq;

namespace ArIED61850Tester.Protocol.Diagnostics;

public static class HexDump
{
    public static byte[] Parse(string hex)
    {
        if (string.IsNullOrWhiteSpace(hex)) return Array.Empty<byte>();
        var tokens = hex
            .Replace("\r", " ")
            .Replace("\n", " ")
            .Replace("\t", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return tokens.Select(t => Convert.ToByte(t, 16)).ToArray();
    }

    public static string ToCompactString(ReadOnlySpan<byte> data, int maxBytes = 96)
    {
        if (data.Length == 0) return string.Empty;
        var length = Math.Min(data.Length, Math.Max(0, maxBytes));
        var text = string.Join(" ", data[..length].ToArray().Select(b => b.ToString("X2")));
        return data.Length > length ? $"{text} ... (+{data.Length - length} byte)" : text;
    }

    public static bool Contains(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        if (pattern.Length == 0) return true;
        if (pattern.Length > data.Length) return false;
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.Slice(i, pattern.Length).SequenceEqual(pattern))
                return true;
        }
        return false;
    }
}
