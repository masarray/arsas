namespace ArIED61850Tester.Models;

/// <summary>
/// One fail-closed verdict for both operator-initiated MMS Read paths and
/// the canonical Read publication fence. A successful BER response alone
/// does not prove that a scalar was decoded. In particular, an unknown MMS
/// tag or a rendered parent structure is not a usable IEC attribute value.
/// Report/GOOSE/vector composition retain their independent typed authority.
/// </summary>
public static class IedBrowserReadValuePolicy
{
    public static bool TryAcceptDisplay(
        string? candidate, out string display, out string rejection)
    {
        display = candidate?.Trim() ?? "";
        rejection = "";
        if (display.Length == 0)
            rejection = "IED returned no scalar value.";
        else if (display.Length > 512)
            rejection = "Rendered result exceeds the scalar display boundary.";
        else if (display.Equals("-", StringComparison.Ordinal) ||
                 display.Equals("Unknown", StringComparison.OrdinalIgnoreCase) ||
                 display.Equals("Not read", StringComparison.OrdinalIgnoreCase) ||
                 display.Equals("Read failed", StringComparison.OrdinalIgnoreCase) ||
                 display.Equals("Pending read", StringComparison.OrdinalIgnoreCase) ||
                 display.Equals("Unavailable", StringComparison.OrdinalIgnoreCase))
            rejection = "IED returned an unresolved value; raw MMS type/evidence needs inspection.";
        else if (display.StartsWith("unknown(tag=", StringComparison.OrdinalIgnoreCase))
            rejection = "Unsupported MMS BER tag; raw bytes must be decoded by ARIEC61850 before publication.";
        else if (display.StartsWith("Structure(", StringComparison.OrdinalIgnoreCase) ||
                 display.StartsWith("Array(", StringComparison.OrdinalIgnoreCase) ||
                 display.StartsWith("{", StringComparison.Ordinal) ||
                 display.StartsWith("[", StringComparison.Ordinal) ||
                 display.Contains("System.Collections", StringComparison.OrdinalIgnoreCase))
            rejection = "MMS structure/array has no verified scalar leaf; use schema-bound DO/DA projection.";
        return rejection.Length == 0;
    }
}
