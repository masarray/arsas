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

        if (normalized is "true" or "on" or "active" or "asserted" or "energized")
            return Active;
        if (normalized is "false" or "off" or "inactive" or "deasserted" or "deenergized")
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
}
