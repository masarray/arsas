using System.Globalization;
using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models;

public static class GooseEngineeringValueFormatter
{
    private static readonly Regex CompactBitString = new(
        @"^bits\((?<hex>[0-9A-Fa-f]{2}),\s*unused=(?<unused>[67])\)$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static string Format(string? rawValue)
    {
        var text = rawValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(text) || text == "<missing in frame>")
            return text;

        var match = CompactBitString.Match(text);
        if (!match.Success ||
            !byte.TryParse(match.Groups["hex"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
        {
            return text;
        }

        if (match.Groups["unused"].Value == "7")
            return (value & 0x80) == 0 ? "false" : "true";

        return ((value >> 6) & 0x03) switch
        {
            0 => "Intermediate [00]",
            1 => "Open [01]",
            2 => "Closed [10]",
            _ => "Invalid [11]"
        };
    }
}
