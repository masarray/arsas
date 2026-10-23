using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Compatibility presentation adapter. Never infer process state from a raw
/// value without the corresponding declared IEC 61850 type; the canonical
/// MMS/report formatter is the only Dbpos and Boolean vocabulary authority.
/// </summary>
public static class GooseEngineeringValueFormatter
{
    public static string Format(string? rawValue, string? dataType = null)
    {
        var text = rawValue?.Trim() ?? string.Empty;
        if (text.Length == 0 || text == "<missing in frame>")
            return text;

        var type = (dataType ?? string.Empty).Trim();
        if (type.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("DoublePointStatus", StringComparison.OrdinalIgnoreCase))
            return Iec61850ValueFormatter.FormatReportProcessValue(
                text, "Dbpos", "", "Position", "");

        if (type.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("BOOL", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("SPS", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("SPC", StringComparison.OrdinalIgnoreCase))
            return Iec61850ValueFormatter.FormatReportProcessValue(
                text, "Boolean", "", "Status", "");

        return text;
    }
}
