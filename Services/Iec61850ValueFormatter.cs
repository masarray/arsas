using System.Globalization;
using System.Text.RegularExpressions;

namespace ArIED61850Tester.Services;

public static class Iec61850ValueFormatter
{
    public static string Format(object? value, string dataType, string unit)
    {
        // Compatibility guard for report engines/IEDs that expose a scalar leaf as
        // its enclosing CDC structure. Named stVal is authoritative regardless of
        // scalar type. An indexed first child is collapsed only when signal metadata
        // already says the selected leaf is numeric (e.g. BCR actVal).
        if (TryExtractStructuredScalar(value, dataType, out var structuredScalar))
            value = structuredScalar;

        return FormatNormalizedValue(value, dataType, unit);
    }

    // Both entry points normalize any legacy structure exactly once before formatting.
    // Keep the established generic and report-specific state vocabularies separate.
    private static string FormatNormalizedValue(object? value, string dataType, string unit)
    {
        if (IsDbposDataType(dataType) && TryNormalizeDbpos(value, out var dbpos))
            return FormatOperatorDbpos(dbpos);

        return value switch
        {
            null => "-",
            bool b => b ? "True" : "False",
            byte b => AppendUnit(b.ToString(CultureInfo.InvariantCulture), unit),
            sbyte b => AppendUnit(b.ToString(CultureInfo.InvariantCulture), unit),
            short s => AppendUnit(s.ToString(CultureInfo.InvariantCulture), unit),
            ushort s => AppendUnit(s.ToString(CultureInfo.InvariantCulture), unit),
            // Generic IEC enums (Mod, Beh, Health, etc.) have their own
            // mappings. 1/2 are NOT universally Open/Close.
            int i => AppendUnit(i.ToString(CultureInfo.InvariantCulture), unit),
            uint i => AppendUnit(i.ToString(CultureInfo.InvariantCulture), unit),
            long l => AppendUnit(l.ToString(CultureInfo.InvariantCulture), unit),
            ulong l => AppendUnit(l.ToString(CultureInfo.InvariantCulture), unit),
            double d => AppendUnit(d.ToString("0.######", CultureInfo.InvariantCulture), unit),
            float f => AppendUnit(f.ToString("0.######", CultureInfo.InvariantCulture), unit),
            decimal d => AppendUnit(d.ToString("0.######", CultureInfo.InvariantCulture), unit),
            DateTime dt => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
                dt, "yyyy-MM-dd HH:mm:ss.fff"),
            DateTimeOffset dto => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
                dto, "yyyy-MM-dd HH:mm:ss.fff zzz"),
            string text when TryParseInvariantNumber(text, out var numericText) => AppendUnit(numericText, unit),
            _ => value.ToString() ?? "-"
        };
    }

    /// <summary>
    /// Normalizes report-delivered process-state text for the operator-facing live/event
    /// surfaces without mutating the raw MMS/report evidence. Semantic report projection can
    /// legitimately deliver lower-case strings such as "true", "off" and
    /// "intermediate-state"; the live UI must preserve the established IEC 61850 state
    /// vocabulary and bit-code context.
    /// </summary>
    public static string FormatReportProcessValue(
        object? value,
        string dataType,
        string unit,
        string category,
        string reference)
    {
        var normalizedValue = value;
        if (TryExtractStructuredScalar(normalizedValue, dataType, out var structuredScalar))
            normalizedValue = structuredScalar;

        // IEC 61850 BSC ValWithTrans.posVal is a SIGNED step-position
        // integer (-64..63), not BOOLEAN. A generic discovery hint such as
        // Boolean/SPC must never coerce integer 0/1 into false/true.
        // Preserve raw/invalid evidence rather than guessing a tap step.
        if (IsBscStepPosition(reference, dataType))
            return FormatBscStepPosition(normalizedValue, unit);

        if (IsPositionSemantic(dataType, category, reference) &&
            TryNormalizeDbpos(normalizedValue, out var dbpos))
        {
            return FormatOperatorDbpos(dbpos);
        }

        if (TryNormalizeBoolean(normalizedValue, out var boolean) ||
            (IsBooleanDataType(dataType) &&
             TryNormalizeBooleanCode(normalizedValue, out boolean)))
        {
            return boolean ? "True [1]" : "False [0]";
        }

        return FormatNormalizedValue(normalizedValue, dataType, unit);
    }

    public static bool IsBscStepPosition(string? reference, string? dataType)
    {
        var normalized = (reference ?? string.Empty).Trim()
            .Replace('
    {
        if (IsDbposDataType(dataType))
            return true;

        if ((category ?? string.Empty).Trim().Equals("Position", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedReference = (reference ?? string.Empty)
            .Trim()
            .Replace((char)36, '.')
            .ToLowerInvariant();

        return normalizedReference.Contains(".pos.stval", StringComparison.Ordinal) ||
               normalizedReference.EndsWith(".pos", StringComparison.Ordinal);
    }

    private static bool TryNormalizeBoolean(object? value, out bool boolean)
    {
        switch (value)
        {
            case bool typed:
                boolean = typed;
                return true;
            case string text when bool.TryParse(text.Trim(), out var parsed):
                boolean = parsed;
                return true;
            default:
                boolean = false;
                return false;
        }
    }

    private static bool IsBooleanDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized is "BOOL" or "BOOLEAN" or "SPS" or "SPC" or "SINGLEPOINTSTATUS";
    }

    private static bool TryNormalizeBooleanCode(object? value, out bool boolean)
    {
        switch (value)
        {
            case byte b when b <= 1:
                boolean = b == 1;
                return true;
            case sbyte b when b is 0 or 1:
                boolean = b == 1;
                return true;
            case short s when s is 0 or 1:
                boolean = s == 1;
                return true;
            case ushort s when s <= 1:
                boolean = s == 1;
                return true;
            case int i when i is 0 or 1:
                boolean = i == 1;
                return true;
            case uint i when i <= 1:
                boolean = i == 1;
                return true;
            case long l when l is 0 or 1:
                boolean = l == 1;
                return true;
            case ulong l when l <= 1:
                boolean = l == 1;
                return true;
            case string text when text.Trim() is "0" or "1":
                boolean = text.Trim() == "1";
                return true;
            default:
                boolean = false;
                return false;
        }
    }

    public static string FormatOperatorDbpos(int code) => code switch
    {
        0 => "Intermediate [00]",
        1 => "Open [01]",
        2 => "Close [10]",
        3 => "Bad state [11]",
        _ => code.ToString(CultureInfo.InvariantCulture)
    };

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
           dataType.Equals("DPS", StringComparison.OrdinalIgnoreCase) ||
           dataType.Equals("DoublePointStatus", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumericDataType(string dataType)
    {
        var normalized = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Contains("int", StringComparison.Ordinal) ||
               normalized.Contains("uint", StringComparison.Ordinal) ||
               normalized.Contains("float", StringComparison.Ordinal) ||
               normalized.Contains("double", StringComparison.Ordinal) ||
               normalized.Contains("decimal", StringComparison.Ordinal) ||
               normalized.Contains("counter", StringComparison.Ordinal) ||
               normalized.Contains("bcr", StringComparison.Ordinal);
    }

    /// <summary>
    /// An actual two-bit MMS BIT STRING renders as one octet with exactly
    /// six unused low bits. Only this wire-proven representation can rescue a
    /// missing CDC type, and ONLY with an exact model ST.stVal/q declaration.
    /// Reject trailing text, wrong bit length and nonzero padding. Raw evidence
    /// is never changed; this function is presentation-only.
    /// </summary>
    public static bool TryNormalizeTwoBitWireStatus(string? text, out int code)
    {
        code = 0;
        // Raw IED presentation can be malformed or adversarial. The canonical
        // two-bit renderer is a few dozen characters, so bound regex work.
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var rendered = Regex.Match(
            text,
            @"^\s*bits\(\s*(?:0x)?([0-9a-f]{2})\s*,\s*unused\s*=\s*6\s*\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!rendered.Success ||
            !byte.TryParse(rendered.Groups[1].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var octet) ||
            (octet & 0x3f) != 0)
            return false;
        code = octet >> 6;
        return true;
    }

    private static bool TryParseDbposText(string text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var bracketCode = Regex.Match(text, @"\[(00|01|10|11)\]", RegexOptions.CultureInvariant);
        if (bracketCode.Success)
            return TryParseBits(bracketCode.Groups[1].Value, out code);

        if (TryNormalizeTwoBitWireStatus(text, out code))
            return true;

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

    private static bool TryExtractStructuredScalar(object? value, string dataType, out object? scalar)
    {
        scalar = null;
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        var stVal = Regex.Match(
            text,
            @"^\s*Structure\(\s*\d+\s*\)\s*\{\s*stVal\s*=\s*([^,}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (stVal.Success)
            return TryParseRenderedScalar(stVal.Groups[1].Value, out scalar);

        if (!IsNumericDataType(dataType))
            return false;

        var indexed = Regex.Match(
            text,
            @"^\s*Structure\(\s*\d+\s*\)\s*\{\s*\[0\]\s*=\s*([^,}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return indexed.Success && TryParseRenderedScalar(indexed.Groups[1].Value, out scalar);
    }

    private static bool TryParseRenderedScalar(string raw, out object? scalar)
    {
        scalar = null;
        var text = raw.Trim();
        if (bool.TryParse(text, out var boolean))
        {
            scalar = boolean;
            return true;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            scalar = integer;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            scalar = floating;
            return true;
        }

        // Keep bit-string text intact so Dbpos decoding can interpret it safely.
        if (text.StartsWith("bits(", StringComparison.OrdinalIgnoreCase))
        {
            scalar = text;
            return true;
        }

        return false;
    }

    private static bool TryParseInvariantNumber(string text, out string formatted)
    {
        formatted = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
        formatted = number.ToString("0.######", CultureInfo.InvariantCulture);
        return true;
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
, '.');
        // BSC is a CDC; its actual readback is ValWithTrans.posVal (INT8).
        // Do not infer integer semantics from the word TapChg alone.
        return normalized.EndsWith(".valWTr.posVal",
                   StringComparison.OrdinalIgnoreCase) ||
               ((dataType ?? string.Empty).Trim().Equals("BSC",
                   StringComparison.OrdinalIgnoreCase) &&
                normalized.EndsWith(".posVal", StringComparison.OrdinalIgnoreCase));
    }

    public static string FormatBscStepPosition(object? value, string? unit = null)
    {
        string? number = value switch
        {
            sbyte x => x.ToString(CultureInfo.InvariantCulture),
            byte x when x <= 63 => x.ToString(CultureInfo.InvariantCulture),
            short x when x is >= -64 and <= 63 => x.ToString(CultureInfo.InvariantCulture),
            ushort x when x <= 63 => x.ToString(CultureInfo.InvariantCulture),
            int x when x is >= -64 and <= 63 => x.ToString(CultureInfo.InvariantCulture),
            uint x when x <= 63 => x.ToString(CultureInfo.InvariantCulture),
            long x when x is >= -64 and <= 63 => x.ToString(CultureInfo.InvariantCulture),
            ulong x when x <= 63 => x.ToString(CultureInfo.InvariantCulture),
            string text when sbyte.TryParse(text.Trim(), NumberStyles.Integer,
                CultureInfo.InvariantCulture, out var step) &&
                step is >= -64 and <= 63
                => step.ToString(CultureInfo.InvariantCulture),
            _ => null
        };
        if (number != null)
            return AppendUnit(number, unit ?? string.Empty);
        // A typed Boolean on an INT8 status is not evidence that position
        // is 0/1. Do not rewrite or silently corrupt that wire observation.
        return value switch
        {
            null => "-",
            bool boolean => boolean
                ? "true (BSC INT8 type mismatch)"
                : "false (BSC INT8 type mismatch)",
            _ => value.ToString() ?? "-"
        };
    }

    private static bool IsPositionSemantic(string dataType, string category, string reference)
    {
        if (IsDbposDataType(dataType))
            return true;

        if ((category ?? string.Empty).Trim().Equals("Position", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedReference = (reference ?? string.Empty)
            .Trim()
            .Replace((char)36, '.')
            .ToLowerInvariant();

        return normalizedReference.Contains(".pos.stval", StringComparison.Ordinal) ||
               normalizedReference.EndsWith(".pos", StringComparison.Ordinal);
    }

    private static bool TryNormalizeBoolean(object? value, out bool boolean)
    {
        switch (value)
        {
            case bool typed:
                boolean = typed;
                return true;
            case string text when bool.TryParse(text.Trim(), out var parsed):
                boolean = parsed;
                return true;
            default:
                boolean = false;
                return false;
        }
    }

    private static bool IsBooleanDataType(string? dataType)
    {
        var normalized = (dataType ?? string.Empty)
            .Trim()
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        return normalized is "BOOL" or "BOOLEAN" or "SPS" or "SPC" or "SINGLEPOINTSTATUS";
    }

    private static bool TryNormalizeBooleanCode(object? value, out bool boolean)
    {
        switch (value)
        {
            case byte b when b <= 1:
                boolean = b == 1;
                return true;
            case sbyte b when b is 0 or 1:
                boolean = b == 1;
                return true;
            case short s when s is 0 or 1:
                boolean = s == 1;
                return true;
            case ushort s when s <= 1:
                boolean = s == 1;
                return true;
            case int i when i is 0 or 1:
                boolean = i == 1;
                return true;
            case uint i when i <= 1:
                boolean = i == 1;
                return true;
            case long l when l is 0 or 1:
                boolean = l == 1;
                return true;
            case ulong l when l <= 1:
                boolean = l == 1;
                return true;
            case string text when text.Trim() is "0" or "1":
                boolean = text.Trim() == "1";
                return true;
            default:
                boolean = false;
                return false;
        }
    }

    public static string FormatOperatorDbpos(int code) => code switch
    {
        0 => "Intermediate [00]",
        1 => "Open [01]",
        2 => "Close [10]",
        3 => "Bad state [11]",
        _ => code.ToString(CultureInfo.InvariantCulture)
    };

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
           dataType.Equals("DPS", StringComparison.OrdinalIgnoreCase) ||
           dataType.Equals("DoublePointStatus", StringComparison.OrdinalIgnoreCase);

    private static bool IsNumericDataType(string dataType)
    {
        var normalized = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        return normalized.Contains("int", StringComparison.Ordinal) ||
               normalized.Contains("uint", StringComparison.Ordinal) ||
               normalized.Contains("float", StringComparison.Ordinal) ||
               normalized.Contains("double", StringComparison.Ordinal) ||
               normalized.Contains("decimal", StringComparison.Ordinal) ||
               normalized.Contains("counter", StringComparison.Ordinal) ||
               normalized.Contains("bcr", StringComparison.Ordinal);
    }

    /// <summary>
    /// An actual two-bit MMS BIT STRING renders as one octet with exactly
    /// six unused low bits. Only this wire-proven representation can rescue a
    /// missing CDC type, and ONLY with an exact model ST.stVal/q declaration.
    /// Reject trailing text, wrong bit length and nonzero padding. Raw evidence
    /// is never changed; this function is presentation-only.
    /// </summary>
    public static bool TryNormalizeTwoBitWireStatus(string? text, out int code)
    {
        code = 0;
        // Raw IED presentation can be malformed or adversarial. The canonical
        // two-bit renderer is a few dozen characters, so bound regex work.
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var rendered = Regex.Match(
            text,
            @"^\s*bits\(\s*(?:0x)?([0-9a-f]{2})\s*,\s*unused\s*=\s*6\s*\)\s*$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!rendered.Success ||
            !byte.TryParse(rendered.Groups[1].Value, NumberStyles.HexNumber,
                CultureInfo.InvariantCulture, out var octet) ||
            (octet & 0x3f) != 0)
            return false;
        code = octet >> 6;
        return true;
    }

    private static bool TryParseDbposText(string text, out int code)
    {
        code = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var bracketCode = Regex.Match(text, @"\[(00|01|10|11)\]", RegexOptions.CultureInvariant);
        if (bracketCode.Success)
            return TryParseBits(bracketCode.Groups[1].Value, out code);

        if (TryNormalizeTwoBitWireStatus(text, out code))
            return true;

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

    private static bool TryExtractStructuredScalar(object? value, string dataType, out object? scalar)
    {
        scalar = null;
        if (value is not string text || string.IsNullOrWhiteSpace(text))
            return false;

        var stVal = Regex.Match(
            text,
            @"^\s*Structure\(\s*\d+\s*\)\s*\{\s*stVal\s*=\s*([^,}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (stVal.Success)
            return TryParseRenderedScalar(stVal.Groups[1].Value, out scalar);

        if (!IsNumericDataType(dataType))
            return false;

        var indexed = Regex.Match(
            text,
            @"^\s*Structure\(\s*\d+\s*\)\s*\{\s*\[0\]\s*=\s*([^,}]+)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return indexed.Success && TryParseRenderedScalar(indexed.Groups[1].Value, out scalar);
    }

    private static bool TryParseRenderedScalar(string raw, out object? scalar)
    {
        scalar = null;
        var text = raw.Trim();
        if (bool.TryParse(text, out var boolean))
        {
            scalar = boolean;
            return true;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            scalar = integer;
            return true;
        }

        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var floating))
        {
            scalar = floating;
            return true;
        }

        // Keep bit-string text intact so Dbpos decoding can interpret it safely.
        if (text.StartsWith("bits(", StringComparison.OrdinalIgnoreCase))
        {
            scalar = text;
            return true;
        }

        return false;
    }

    private static bool TryParseInvariantNumber(string text, out string formatted)
    {
        formatted = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
            return false;
        if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return false;
        formatted = number.ToString("0.######", CultureInfo.InvariantCulture);
        return true;
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
