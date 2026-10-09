using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// IEC 61850 command identity is the full DataObject reference within one
/// IED, not a DataSet membership, RCB or WPF SignalDefinition instance.
/// </summary>
public static class Iec61850ControlIdentity
{
    public static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('\\', '/').Replace('$', '.');

    public static int CountSelected(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .Select(s => Normalize(s.ObjectReference))
            .Where(s => s.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count();

    public static SignalDefinition[] DistinctOperable(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject &&
                              s.ControlModelResolved && s.ControlSupportsOperate && !s.IsGenericControl)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            // Prefer an in-flight owner in legacy cached duplicate models.
            .Select(group => group.OrderByDescending(s => s.ControlCommandBusy)
                .ThenByDescending(s => s.ControlConfirmationPending).First()).ToArray();

    public static SignalDefinition[] InspectionRepresentatives(IEnumerable<SignalDefinition> signals)
        => signals.Where(s => s.IsSelected && s.IsValidControlObject)
            .GroupBy(s => Normalize(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(s => s.ControlModelResolved).First())
            .Where(s => !s.ControlModelResolved).ToArray();
}
