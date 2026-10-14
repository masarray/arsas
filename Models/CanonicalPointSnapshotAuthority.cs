using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only, association-fenced adapter from canonical Report observation to
/// existing Explorer/Live Monitor point presentation. RuntimePointState still
/// owns event edges, SOE and RCB mechanics. It cannot mutate the evidence store.
/// </summary>
public static class CanonicalPointSnapshotAuthority
{
    public static Iec61850PointSnapshot NewerSnapshot(
        Iec61850PointSnapshot previous, Iec61850PointSnapshot incoming)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(incoming);
        if (previous.ObservationAssociation != incoming.ObservationAssociation &&
            previous.ObservationAssociation > 0 && incoming.ObservationAssociation > 0)
            return incoming.ObservationAssociation > previous.ObservationAssociation
                ? incoming : previous;
        return incoming.Sequence >= previous.Sequence ? incoming : previous;
    }

    public static string ProcessDisplay(
        Iec61850MonitorDevice device, Iec61850MonitorPoint point,
        Iec61850PointSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(snapshot);

        // Non-monitoring reads, disconnected/reconnecting sessions and
        // unresolved MMS identities must retain the original runtime snapshot.
        if (!device.IsMonitoring || !device.IsConnected ||
            snapshot.ObservationAssociation == 0 ||
            snapshot.ObservationAssociation != device.LiveValues.Association ||
            string.IsNullOrWhiteSpace(point.IecReference) ||
            string.IsNullOrWhiteSpace(point.FunctionalConstraint))
            return snapshot.Value;
        if (!device.LiveValues.TryGet(point.IecReference, point.FunctionalConstraint,
                out var canonical, preferReport: true) ||
            canonical.IsStale ||
            canonical.Association != snapshot.ObservationAssociation ||
            !canonical.Source.StartsWith("Report", StringComparison.Ordinal))
            return snapshot.Value;

        // Unit/CDC normalization is a *view*, not a second stored observation.
        // The exact raw Report evidence remains available to Browser q/t views.
        return Iec61850ValueFormatter.FormatReportProcessValue(
            canonical.Value, point.IecDataType, point.Unit,
            point.Category, point.IecReference);
    }
}
