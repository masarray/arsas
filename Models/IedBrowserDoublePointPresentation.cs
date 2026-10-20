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
}
