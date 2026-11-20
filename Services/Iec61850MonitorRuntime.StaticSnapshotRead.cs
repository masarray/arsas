using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// One explicitly requested initial-image snapshot, never a monitoring policy.
/// Published counts mean real accepted exact MMS Read observations, not
/// verified Report ingress or good process quality.
/// </summary>
public sealed record StaticPendingSnapshotReadResult(
    int Eligible, int Attempted, int Published, int Failed,
    int Unverified, bool BudgetExpired);

public sealed partial class Iec61850MonitorRuntime
{
    internal const int StaticPendingSnapshotMaxTargets = 77;
    private static readonly TimeSpan StaticPendingSnapshotBudget = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan StaticPendingSnapshotPerReadBudget = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Bounded one-shot commissioning read while reporting remains active.
    /// The per-IED GUI facade serializes this with commands/lifecycle and
    /// lets Stop preempt. The native client owns MMS single-flight I/O.
    /// </summary>
    public async Task<StaticPendingSnapshotReadResult> ReadPendingStaticInitialImageOnceAsync(
        Iec61850MonitorDevice device, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_sessions.TryGetValue(device.DeviceId, out var session) ||
            !ReferenceEquals(session.Device, device) ||
            !session.StaticDataSetReportOnly ||
            !device.IsMonitoring || !device.IsConnected || !session.Client.IsConnected ||
            Iec61850ControlFlightAdmission.IsUnavailable(
                ref session.ControlCommandActive, ref session.ControlFlightQuarantined))
            throw new InvalidOperationException(
                "Pending Snapshot Read requires an active, healthy Static DataSet report-only session and no control flight.");

        var association = session.BrowserObservationAssociation;
        if (association <= 0 || device.LiveValues.IsOffline ||
            device.LiveValues.Association != association)
            throw new InvalidOperationException(
                "Pending Snapshot Read has no current MMS association; reconnect before trying a new snapshot.");

        // Capture only the selected monitor points, never the 31,481 FC
        // discovery objects. Report process values and current Read values
        // are excluded. Avoid continuously re-scanning a large signal model.
        var eligible = session.Points.Values
            .Where(point => session.States.TryGetValue(point.PointKey, out var state) &&
                IsExactPendingStaticSnapshotCandidate(
                    point, state.HasValue, state.ReportTrafficSeen))
            .OrderBy(point => point.IecReference, StringComparer.Ordinal)
            .ToArray();
        var targets = eligible.Take(StaticPendingSnapshotMaxTargets).ToArray();
        var attempted = 0;
        var published = 0;
        var unverified = 0;
        var failed = 0;
        var budgetExpired = false;

        using var overall = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        overall.CancelAfter(StaticPendingSnapshotBudget);
        foreach (var point in targets)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (overall.IsCancellationRequested)
            {
                budgetExpired = true;
                break;
            }
            // A replaced association or preemptive Stop can never publish
            // old evidence to a new session, even after a successful wire Read.
            if (!IsSamePendingSnapshotAssociation(session, device, association))
                throw new OperationCanceledException(
                    "IED monitoring association changed during Snapshot Read.", cancellationToken);
            if (!session.States.TryGetValue(point.PointKey, out var state) ||
                !IsExactPendingStaticSnapshotCandidate(
                    point, state.HasValue, state.ReportTrafficSeen))
                continue;

            attempted++;
            using var leaf = CancellationTokenSource.CreateLinkedTokenSource(overall.Token);
            leaf.CancelAfter(StaticPendingSnapshotPerReadBudget);
            try
            {
                var raw = await session.Client.ReadValueAsync(
                    point.IecReference, point.FunctionalConstraint, point.IecDataType,
                    leaf.Token).ConfigureAwait(false);
                if (!IsSamePendingSnapshotAssociation(session, device, association))
                    throw new OperationCanceledException(
                        "IED association changed during Snapshot Read.", cancellationToken);

                // The native Read adapter can consult alternate measurement
                // leaves. Never silently treat an alternate as the selected
                // static DataSet member; exact wire-reference evidence required.
                if (raw is not Iec61850ReadValue observed ||
                    !IsExactStaticSnapshotReadReference(
                        point.IecReference, observed.ReadReference))
                {
                    unverified++;
                    continue;
                }
                var resolved = new ResolvedIecSignalRead(observed, point.IecReference);
                if (PublishVerifiedMmsReadToCanonical(
                    device.LiveValues, association, resolved, point.FunctionalConstraint,
                    point.IecDataType,
                    observed.HasQuality ? observed.Quality : "Unknown",
                    observed.HasDeviceTimestamp ? observed.DeviceTimestamp : "-"))
                    published++;
                else
                    unverified++;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                failed++;
                if (overall.IsCancellationRequested)
                {
                    budgetExpired = true;
                    break;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                failed++;
                if (failed <= 3)
                    Log("WARN", device.Name,
                        $"Explicit Snapshot Read failed for {point.IecReference} [{point.FunctionalConstraint}]: " +
                        $"{ex.GetType().Name}: {ex.Message}");
            }
        }

        // The existing bounded canonical Read journal projects these values
        // in the monitor loop, with status "Snapshot / awaiting InformationReport".
        // Do not update WPF from this worker or emit synthetic SOE.
        Log(unverified > 0 || failed > 0 || budgetExpired ? "WARN" : "INFO",
            device.Name,
            $"Explicit pending Snapshot Read: eligible={eligible.Length}, attempted={attempted}, " +
            $"canonicalAccepted={published}, failed={failed}, unverified={unverified}, " +
            $"budgetExpired={budgetExpired}; source=MMS Read, not RCB ingress. " +
            "No GI, cyclic polling, RCB/control writes or SOE emitted by this operation.");
        return new StaticPendingSnapshotReadResult(
            eligible.Length, attempted, published, failed, unverified, budgetExpired);
    }

    private bool IsSamePendingSnapshotAssociation(
        DeviceSession session, Iec61850MonitorDevice device, long association)
        => _sessions.TryGetValue(device.DeviceId, out var current) &&
           ReferenceEquals(current, session) &&
           device.IsMonitoring && device.IsConnected && session.Client.IsConnected &&
           device.LiveValues.Association == association && !device.LiveValues.IsOffline &&
           session.BrowserObservationAssociation == association &&
           Volatile.Read(ref session.ControlFlightQuarantined) == 0;

    internal static bool IsExactPendingStaticSnapshotCandidate(
        Iec61850MonitorPoint point, bool hasValue, bool reportTrafficSeen)
        => !hasValue && !reportTrafficSeen && !point.IsOffline &&
           !string.IsNullOrWhiteSpace(point.IecReference) &&
           !string.IsNullOrWhiteSpace(point.DataSetReference) &&
           point.FunctionalConstraint is "ST" or "MX";

    internal static bool IsExactStaticSnapshotReadReference(
        string selectedReference, string? actualReadReference)
        => !string.IsNullOrWhiteSpace(selectedReference) &&
           !string.IsNullOrWhiteSpace(actualReadReference) &&
           string.Equals(
               IedBrowserLiveValues.CanonicalReference(selectedReference),
               IedBrowserLiveValues.CanonicalReference(actualReadReference),
               StringComparison.Ordinal);
}
