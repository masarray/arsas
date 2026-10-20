using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Display-only DPC semantics shared by Browser Data Model, DataSets and
/// Report detail rows. Model typing and exact ST references gate conversion;
/// Report and MMS Read evidence remain untouched in the per-IED ledger.
/// </summary>
public static class IedBrowserDoublePointPresentation
{
    public static string Format(string rawDisplay,
        string selectedReference, string declaredReference,
        string fc, IedBrowserSchemaIndex? schema)
    {
        if (schema is null ||
            ( !string.Equals(fc, "ST", StringComparison.OrdinalIgnoreCase) &&
              !string.Equals(fc, "SV", StringComparison.OrdinalIgnoreCase)) ||
            !(schema.IsDeclaredDoublePointValue(selectedReference, fc) ||
              schema.IsDeclaredDoublePointValue(declaredReference, fc)))
            return rawDisplay;

        return Iec61850ValueFormatter.FormatReportProcessValue(
            rawDisplay, "Dbpos", "", "Position", selectedReference);
    }

    /// <summary>
    /// A substituted DPC's subQ is IEC Quality (13 bits), not the 2-bit
    /// double-point status. Only an exact model-declared SV.subVal owner
    /// authorizes this visual projection. Unverified/non-13-bit input stays
    /// raw; the MMS observation remains unchanged.
    /// </summary>
    public static string FormatSubstitutionQuality(
        string rawDisplay, string reference, string fc, IedBrowserSchemaIndex? schema)
    {
        if (schema is null ||
            !string.Equals(fc, "SV", StringComparison.OrdinalIgnoreCase))
            return rawDisplay;
        var canonical = IedBrowserLiveValues.CanonicalReference(reference);
        if (!canonical.EndsWith(".subQ", StringComparison.Ordinal))
            return rawDisplay;
        var matchingValue = canonical[..^"subQ".Length] + "subVal";
        if (!schema.IsDeclaredDoublePointValue(matchingValue, "SV") ||
            !ArIED61850Tester.Protocol.Mms.MmsQualityDecoder.TryDecodeRendered(
                rawDisplay, out var quality))
            return rawDisplay;
        return quality.Summary;
    }
}
