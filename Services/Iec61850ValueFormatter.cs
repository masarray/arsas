using System.Globalization;
using System.Text.RegularExpressions;

namespace ArIED61850Tester.Services;

public static class Iec61850ValueFormatter
{
    public static string Format(object? value, string dataType, string unit)
    {
        if (IsDbposDataType(dataType) && TryNormalizeDbpos(value, out var dbpos))
            return FormatDbpos(dbpos);

        return value switch
        {
            null => "-",
            bool b => b ? "True" : "False",
            byte b => AppendUnit(b.ToString(CultureInfo.InvariantCulture), unit),
            sbyte b => AppendUnit(b.ToString(CultureInfo.InvariantCulture), unit),
            short s => AppendUnit(s.ToString(CultureInfo.InvariantCulture), unit),
            ushort s => AppendUnit(s.ToString(CultureInfo.InvariantCulture), unit),
            int i when dataType.Equals("Enum", StringComparison.OrdinalIgnoreCase) && i == 1 => "Open",
            int i when dataType.Equals("Enum", StringComparison.OrdinalIgnoreCase) && i == 2 => "Closed",
            int i => AppendUnit(i.ToString(CultureInfo.InvariantCulture), unit),
            uint i => AppendUnit(i.ToString(CultureInfo.InvariantCulture), unit),
            long l => AppendUnit(l.ToString(CultureInfo.InvariantCulture), unit),
            ulong l => AppendUnit(l.ToString(CultureInfo.InvariantCulture), unit),
            double d => AppendUnit(d.ToString("0.######", CultureInfo.InvariantCulture), unit),
            float f => AppendUnit(f.ToString("0.######", CultureInfo.InvariantCulture), unit),
            decimal d => AppendUnit(d.ToString("0.######", CultureInfo.InvariantCulture), unit),
            DateTime dt => dt.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture),
            DateTimeOffset dto => dto.ToString("yyyy-MM-dd HH:mm:ss.fff zzz", CultureInfo.InvariantCulture),
            _ => value.ToString() ?? "-"
        };
    }

    public static bool TryNormalizeDbpos(object? value, out int code)
    {
        code = 0;
        switch (value)
        {
            case byte b when b <= 3: code = b; return true;
            case sbyte b when b is >= 0 and <= 3: code = b; return true;
            case short s when s is >= 0 and <= 3: code = s; return true;
            case ushort s when s <= 3: code = s; return true;
            case int i when i is >= 0 and <= 3: code = i; return true;
            case uint i when i <= 3: code = (int)i; return true;
            case long l when l is >= 0 and <= 3: code = (int)l; return true;
            case ulong l when l <= 3: code = (int)l; return true;
            case bool b: code = b ? 2 : 1; return true;
            case string text: return TryParseDbposText(text, out code);
            default: return false;
        }
    }

    private static bool IsDbposDataType(string dataType)
        => dataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
           dataType.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
           dataType.Equals("DoublePointStatus", StringComparison.OrdinalIgnoreCase);

    private static string FormatDbpos(int code) => code switch
    {
        0 => "Intermediate [00]",
        1 => "Open [01]",
        2 => "Closed [10]",
        3 => "Bad state [11]",
        _ => code.ToString(CultureInfo.InvariantCulture)
    };

    private static bool TryParseDbposText(string text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var bracketCode = Regex.Match(text, @"\[(00|01|10|11)\]", RegexOptions.CultureInvariant);
        if (bracketCode.Success)
            return TryParseBits(bracketCode.Groups[1].Value, out code);

        var renderedBits = Regex.Match(
            text,
            @"bits\(\s*(?:0x)?([0-9a-f]{2})\s*,\s*unused\s*=\s*(\d+)\s*\)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (renderedBits.Success &&
            byte.TryParse(renderedBits.Groups[1].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var raw) &&
            int.TryParse(renderedBits.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var unused) &&
            unused == 6)
        {
            code = (raw >> unused) & 0x03;
            return true;
        }

        var compact = text.Trim().Replace(" ", "").Replace("_", "").Replace("-", "").ToLowerInvariant();
        switch (compact)
        {
            case "0":
            case "00":
            case "intermediate":
            case "intermediatestate": code = 0; return true;
            case "1":
            case "01":
            case "open":
            case "off": code = 1; return true;
            case "2":
            case "10":
            case "closed":
            case "close":
            case "on": code = 2; return true;
            case "3":
            case "11":
            case "bad":
            case "badstate":
            case "invalid": code = 3; return true;
            default: return false;
        }
    }

    private static bool TryParseBits(string bits, out int code)
    {
        code = bits switch
        {
            "00" => 0,
            "01" => 1,
            "10" => 2,
            "11" => 3,
            _ => -1
        };
        return code >= 0;
    }

    private static string AppendUnit(string value, string unit)
        => string.IsNullOrWhiteSpace(unit) ? value : $"{value} {unit}";
}
