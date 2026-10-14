using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only, association-fenced adapter from canonical Report observation to
/// existing Explorer/Live Monitor point presentation. RuntimePointState still
/// owns event edges, SOE and RCB mechanics. It cannot mutate the evidence store.
/// </summary>
public sealed record CanonicalPointPresentation(
    string Value, string Quality, string DeviceTimestamp,
    string SourceMode, bool FromCanonicalReport);

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

    /// <summary>
    /// The process value and its quality/time/source must be taken from ONE
    /// accepted observation. Never mix a newer canonical value with quality
    /// or device time from a different coalesced runtime frame.
    /// </summary>
    public static CanonicalPointPresentation Project(
        Iec61850MonitorDevice device, Iec61850MonitorPoint point,
        Iec61850PointSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(point);
        ArgumentNullException.ThrowIfNull(snapshot);

        var fallback = new CanonicalPointPresentation(
            snapshot.Value, snapshot.Quality, snapshot.DeviceTimestamp,
            snapshot.SourceMode, false);
        if (!device.IsMonitoring || !device.IsConnected ||
            snapshot.ObservationAssociation == 0 ||
            snapshot.ObservationAssociation != device.LiveValues.Association ||
            string.IsNullOrWhiteSpace(point.IecReference) ||
            string.IsNullOrWhiteSpace(point.FunctionalConstraint))
            return fallback;
        if (!device.LiveValues.TryGet(point.IecReference, point.FunctionalConstraint,
                out var observed, preferReport: true) ||
            observed.IsStale ||
            observed.Association != snapshot.ObservationAssociation ||
            !observed.Source.StartsWith("Report", StringComparison.Ordinal))
            return fallback;

        // IEC engineering formatting is presentation only. The raw Report
        // evidence and typed q/t remain unchanged in the canonical ledger.
        return new CanonicalPointPresentation(
            Iec61850ValueFormatter.FormatReportProcessValue(
                observed.Value, point.IecDataType, point.Unit,
                point.Category, point.IecReference),
            QualityOrUnknown(observed.Quality),
            TimestampOrUnavailable(observed.DeviceTimestamp),
            snapshot.IsReportTraffic ? snapshot.SourceMode : "Report", true);
    }

    public static string ProcessDisplay(
        Iec61850MonitorDevice device, Iec61850MonitorPoint point,
        Iec61850PointSnapshot snapshot)
        => Project(device, point, snapshot).Value;

    public static string QualityOrUnknown(string? quality)
        => string.IsNullOrWhiteSpace(quality) ? "Unknown" : quality;

    public static string TimestampOrUnavailable(string? timestamp)
        => string.IsNullOrWhiteSpace(timestamp) ? "-" : timestamp;
}
