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
    /// <summary>
    /// An empty OCTET STRING is a legitimate MMS scalar only when the actual
    /// successfully decoded wire kind is OctetString and the selected live
    /// attribute declares an OCTET data type. Empty rendered text alone is
    /// never sufficient to claim a successful read.
    /// </summary>
    public static bool TryFormatVerifiedEmptyOctet(
        AR.Iec61850.Mms.MmsDataValue? wire, string? modelType,
        out string display)
    {
        display = "";
        if (wire is not { Kind: AR.Iec61850.Mms.MmsDataKind.OctetString } ||
            wire.RawValue.Count != 0 ||
            !((modelType ?? "").StartsWith("Octet", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(modelType, "OCTET STRING", StringComparison.OrdinalIgnoreCase)))
            return false;
        display = "Empty OCTET STRING (0 bytes)";
        return true;
    }

    /// <summary>
    /// IEC MMS permits zero-length VisibleString/MMSString data. An empty
    /// rendered text is not proof: require a decoded primitive wire value and
    /// an exact compatible live declared string type.
    /// </summary>
    public static bool TryFormatVerifiedEmptyVisible(
        AR.Iec61850.Mms.MmsDataValue? wire, string? modelType,
        out string display)
    {
        display = "";
        if (wire?.Value is not string { Length: 0 })
            return false;
        var declared = (modelType ?? "").Trim();
        if (wire.Kind == AR.Iec61850.Mms.MmsDataKind.VisibleString &&
            (declared.StartsWith("VisString", StringComparison.OrdinalIgnoreCase) ||
             declared.Equals("visible-string", StringComparison.OrdinalIgnoreCase)))
        {
            display = "Empty VisibleString (0 characters)";
            return true;
        }
        if (wire.Kind == AR.Iec61850.Mms.MmsDataKind.MmsString &&
            (declared.StartsWith("MmsString", StringComparison.OrdinalIgnoreCase) ||
             declared.Equals("mms-string", StringComparison.OrdinalIgnoreCase)))
        {
            display = "Empty MMSString (0 characters)";
            return true;
        }
        return false;
    }

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
