using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// An LN's Read DO selector can target ONLY a declared direct DO owner.
/// Nested CF/SG structures may expand in the detail grid, but their path
/// is never a separately declared LN DataObject and cannot hijack Read DO.
/// </summary>
public static class IedBrowserExactDoFocus
{
    public static bool IsDeclaredOwner(LiveIedLogicalNodeModel ln, string? reference)
        => ln is not null && !string.IsNullOrWhiteSpace(reference) &&
           ln.DataObjects.Count(obj => string.Equals(obj.Reference,
               reference, StringComparison.Ordinal)) == 1;
}
