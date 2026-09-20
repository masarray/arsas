namespace ArIED61850Tester.Models;

/// <summary>
/// Presentation-only state classification for IEC 61850 process values.
/// The tone describes process state, never alarm severity or quality.
/// </summary>
public static class Iec61850ValueStatePresentation
{
    public const string Active = "Active";
    public const string Inactive = "Inactive";
    public const string Abnormal = "Abnormal";
    public const string Neutral = "Neutral";

    public static string Classify(string? value, string? dataType = null)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" ||
            text.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return Neutral;
        }

        var normalized = text.ToLowerInvariant();

        // DPC state codes are process states, not health/severity. Intermediate and
        // bad are the only forms that deserve the amber attention tone.
        if (normalized.Contains("intermediate", StringComparison.Ordinal) ||
            normalized.Contains("bad", StringComparison.Ordinal) ||
            HasStateCode(normalized, "00") || HasStateCode(normalized, "11"))
        {
            return Abnormal;
        }

        if (normalized.Contains("closed", StringComparison.Ordinal) || HasStateCode(normalized, "10"))
            return Active;
        if (normalized.Contains("open", StringComparison.Ordinal) || HasStateCode(normalized, "01"))
            return Inactive;

        if (normalized.StartsWith("true", StringComparison.Ordinal) ||
            normalized is "on" or "active" or "asserted" or "energized")
            return Active;
        if (normalized.StartsWith("false", StringComparison.Ordinal) ||
            normalized is "off" or "inactive" or "deasserted" or "deenergized")
            return Inactive;

        // Bare 0/1 can be an analog value, counter, set point, or enum. Only treat it
        // as a binary state when discovery metadata proves the point is Boolean/SPS.
        if (IsBooleanType(dataType))
        {
            if (normalized is "1" or "1.0") return Active;
            if (normalized is "0" or "0.0") return Inactive;
        }

        return Neutral;
    }

    private static bool HasStateCode(string value, string code)
        => value.Contains($"[{code}]", StringComparison.Ordinal);

    private static bool IsBooleanType(string? dataType)
    {
        var type = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        return type is "bool" or "boolean" or "sps" or "singlepointstatus" ||
               type.Contains("boolean", StringComparison.Ordinal);
    }

    /// <summary>
    /// Compact operator badge derived only from the declared IEC/MMS data type.
    /// Unlike ValueVisualKind this never infers a type from the rendered value, signal
    /// name, category, or numeric shape. Unknown metadata intentionally yields no badge.
    /// </summary>
    public static string TypeToken(string? dataType)
    {
        var type = (dataType ?? string.Empty).Trim();
        if (type.Length == 0)
            return string.Empty;

        var normalized = type
            .Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();

        if (normalized is "DBPOS" or "DPC" or "DPS" or "DOUBLEPOINTSTATUS")
            return "DP";

        if (normalized is "BOOL" or "BOOLEAN" or "SPS" or "SPC" or "SINGLEPOINTSTATUS")
            return "B";

        if (normalized.StartsWith("FLOAT", StringComparison.Ordinal) ||
            normalized is "REAL" or "REAL32" or "REAL64" or "DOUBLE")
        {
            return "F";
        }

        if (normalized.StartsWith("UINT", StringComparison.Ordinal) ||
            normalized is "INT8U" or "INT16U" or "INT24U" or "INT32U" or "INT64U")
        {
            return "U";
        }

        if (normalized.StartsWith("INT", StringComparison.Ordinal) ||
            normalized is "INTEGER" or "SIGNED")
        {
            return "I";
        }

        if (normalized.StartsWith("ENUM", StringComparison.Ordinal) ||
            normalized is "ENUMERATED")
        {
            return "E";
        }

        return string.Empty;
    }

    public const string Analog = "Analog";
    public const string BooleanTrue = "BooleanTrue";
    public const string BooleanFalse = "BooleanFalse";
    public const string PositionOpen = "PositionOpen";
    public const string PositionClose = "PositionClose";
    public const string PositionIntermediate = "PositionIntermediate";
    public const string PositionBad = "PositionBad";

    /// <summary>
    /// Operator-facing visual family for process values. This is intentionally more
    /// specific than ValueTone: it differentiates analog, Boolean and DPC/position
    /// semantics without changing process truth, alarm severity or quality.
    /// </summary>
    public static string ClassifyVisualKind(
        string? value,
        string? dataType = null,
        string? category = null,
        string? reference = null)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" ||
            text.Equals("Pending", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
        {
            return Neutral;
        }

        var normalized = text.ToLowerInvariant();

        if (IsPositionSemantic(dataType, category, reference))
        {
            if (normalized.Contains("intermediate", StringComparison.Ordinal) || HasStateCode(normalized, "00"))
                return PositionIntermediate;
            if (normalized.Contains("bad", StringComparison.Ordinal) || HasStateCode(normalized, "11"))
                return PositionBad;
            if (normalized.Contains("open", StringComparison.Ordinal) || HasStateCode(normalized, "01"))
                return PositionOpen;
            if (normalized.Contains("close", StringComparison.Ordinal) || HasStateCode(normalized, "10"))
                return PositionClose;
        }

        // Explicit True/False presentation is already semantic evidence from the report
        // formatter and remains safe even when discovery metadata is less specific.
        if (normalized.StartsWith("true", StringComparison.Ordinal))
            return BooleanTrue;
        if (normalized.StartsWith("false", StringComparison.Ordinal))
            return BooleanFalse;

        // ON/OFF and bare 0/1 are ambiguous outside a proven Boolean/SPS type. Do not
        // reclassify a generic Enum merely because its text happens to use those words.
        if (IsBooleanType(dataType))
        {
            if (normalized is "on" or "active" or "asserted" or "energized" or "1" or "1.0")
                return BooleanTrue;

            if (normalized is "off" or "inactive" or "deasserted" or "deenergized" or "0" or "0.0")
                return BooleanFalse;
        }

        if (IsAnalogSemantic(text, dataType, category))
            return Analog;

        return Neutral;
    }

    private static bool IsPositionSemantic(string? dataType, string? category, string? reference)
    {
        var type = (dataType ?? string.Empty).Trim();
        if (type.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("DoublePointStatus", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if ((category ?? string.Empty).Trim().Equals("Position", StringComparison.OrdinalIgnoreCase))
            return true;

        var normalizedReference = (reference ?? string.Empty)
            .Trim()
            .Replace((char)36, '.')
            .ToLowerInvariant();

        return normalizedReference.Contains(".pos.stval", StringComparison.Ordinal) ||
               normalizedReference.EndsWith(".pos", StringComparison.Ordinal);
    }

    private static bool IsAnalogSemantic(string text, string? dataType, string? category)
    {
        if ((category ?? string.Empty).Trim().Equals("Measurement", StringComparison.OrdinalIgnoreCase))
            return true;

        var type = (dataType ?? string.Empty).Trim().ToLowerInvariant();
        if (type.Contains("float", StringComparison.Ordinal) ||
            type.Contains("double", StringComparison.Ordinal) ||
            type.Contains("decimal", StringComparison.Ordinal) ||
            type.Contains("real", StringComparison.Ordinal) ||
            type.Contains("analog", StringComparison.Ordinal))
        {
            return true;
        }

        return LooksLikeAnalogAggregate(text);
    }

    private static bool LooksLikeAnalogAggregate(string text)
    {
        var parts = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length < 2)
            return false;

        var numericMembers = 0;
        foreach (var part in parts)
        {
            var separator = part.IndexOf('=');
            if (separator <= 0 || separator + 1 >= part.Length)
                return false;

            var number = part[(separator + 1)..].Trim();
            if (double.TryParse(
                    number,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out _))
            {
                numericMembers++;
            }
        }

        return numericMembers == parts.Length;
    }
}

/// <summary>
/// Presentation-only attention tone for IEC 61850 quality text. Process state remains
/// independent: this classifier only decides how strongly the Quality column should
/// call for operator attention.
/// </summary>
public static class Iec61850QualityPresentation
{
    public const string Good = "Good";
    public const string Attention = "Attention";
    public const string Bad = "Bad";
    public const string Unknown = "Unknown";

    public static string Classify(string? quality)
    {
        var text = (quality ?? string.Empty).Trim();
        if (text.Length == 0 || text == "-" || text.Equals("Unknown", StringComparison.OrdinalIgnoreCase))
            return Unknown;

        var normalized = text.ToLowerInvariant();
        if (ContainsAny(normalized, "invalid", "bad", "failure", "failed"))
            return Bad;

        if (ContainsAny(
                normalized,
                "questionable",
                "olddata",
                "old data",
                "substituted",
                "test",
                "operatorblocked",
                "operator blocked",
                "overflow",
                "outofrange",
                "out of range",
                "inaccurate",
                "oscillatory"))
        {
            return Attention;
        }

        if (normalized.Contains("good", StringComparison.Ordinal))
            return Good;

        // A non-empty quality string that is not explicitly proven Good still deserves
        // a contained amber cue rather than being silently presented as healthy.
        return Attention;
    }

    private static bool ContainsAny(string source, params string[] needles)
        => needles.Any(needle => source.Contains(needle, StringComparison.Ordinal));
}
