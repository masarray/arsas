using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Explicit read-only snapshot over an independent MMS association. It does not
/// join, reserve, enable, write or change any reporting session. IEC devices that
/// refuse a second association return a visible connection error to the operator.
/// </summary>
public static class IedBrowserIsolatedReadService
{
    public static Task<int> ReadAsync(
        Iec61850MonitorDevice device, IReadOnlyList<IedBrowserReadTarget> targets,
        IProgress<IedBrowserReadResult> values, IProgress<IedBrowserReadProgress> progress,
        CancellationToken cancellationToken, bool automatic = false,
        IReadOnlyList<IedBrowserReadTarget>? verifiedRootScope = null)
    {
        // Capture before crossing the UI/thread boundary. The current
        // monitoring association owns this exact live discovery model;
        // SCL-only metadata is never promoted to verified MMS schema.
        var lease = IedBrowserIsolatedReadSchemaLease.Capture(device);
        return Task.Run(async () =>
        {
            await using var client = new NativeIec61850Client();
            await client.ConnectAsync(device.IpAddress, device.Port, cancellationToken)
                .ConfigureAwait(false);
            if (!client.IsConnected)
                throw new InvalidOperationException("IED rejected independent read-only MMS association. " +
                    "Stop monitoring if the IED allows only one client.");

            if (lease is not null)
            {
                if (!lease.IsCurrent(device))
                    throw new InvalidOperationException(
                        "Live IED model or monitoring association changed before isolated MMS Read.");
                // The secondary client has no discovery model unless we provide
                // this exact association-verified reference/FC type inventory.
                // Without it ARIEC cannot bind structured parent MMS values to
                // the requested SG/DA leaf, even when the wire read succeeds.
                client.BindIsolatedBrowserReadSchema(lease.Model);
            }

            var successful = 0;
            var sgRootFlight = new IedBrowserSgRootReadFlight(
                client, targets, verifiedRootScope);
            // One association-scoped flight, only used AFTER a scalar Read
            // is genuinely unresolved; positive/negative parent probes are
            // memoized once per exact DO+FC, never per every child.
            var doReadFlight = new IedBrowserTypedDoReadFlight(
                client, verifiedRootScope ?? targets);
            for (var i = 0; i < targets.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (lease is not null && !lease.IsCurrent(device))
                    throw new InvalidOperationException(
                        "Live IED discovery/model or monitoring association changed during isolated MMS Read.");
                var target = targets[i];
                try
                {
                    using var leafToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                    // The root attempt has its OWN bounded per-root timeout
                    // and negative-result cache. Do not pass a short leaf
                    // timeout to it: otherwise each sibling retries the same
                    // timed-out GVA root and starves all other settings.
                    leafToken.CancelAfter(TimeSpan.FromSeconds(automatic ? 2 : 5));
                    var verifiedSg = await sgRootFlight.TryReadAsync(
                        target, cancellationToken).ConfigureAwait(false);
                    // A typed root may prove OTHER exact children even when
                    // the queued target itself was absent or refused.
                    foreach (var extra in sgRootFlight.DrainUnscheduledSiblings(target))
                    {
                        values.Report(extra);
                        successful++;
                    }
                    if (verifiedSg is not null)
                    {
                        values.Report(verifiedSg);
                        successful++;
                        progress.Report(new IedBrowserReadProgress(i + 1, targets.Count));
                        continue;
                    }
                    var raw = await client.ReadValueAsync(target.Reference,
                        target.FunctionalConstraint, target.DataType, leafToken.Token)
                        .ConfigureAwait(false);
                    cancellationToken.ThrowIfCancellationRequested();
                    var read = raw as Iec61850ReadValue;
                    var display = raw?.ToString() ?? "";
                    if (IedBrowserReadValuePolicy.TryAcceptDisplay(
                            display, out var accepted, out var reason))
                    {
                        values.Report(new IedBrowserReadResult(target, accepted,
                            read?.Quality ?? "", read?.DeviceTimestamp ?? "", "",
                            read?.UtcTimeEvidence, read?.QualityEvidence,
                            read?.Value is bool typed ? typed : null));
                        successful++;
                    }
                    else
                    {
                        // Some IEDs expose ST/SE as an ordered DO structure
                        // but reject or cannot decode its individual MMS leaf.
                        // Never guess a missing value: only publish the exact
                        // child proved by a separate GVA+Read of that DO.
                        var verifiedParent = await doReadFlight.TryReadAsync(
                            target, cancellationToken).ConfigureAwait(false);
                        if (verifiedParent is not null)
                        {
                            values.Report(verifiedParent);
                            successful++;
                        }
                        else
                            values.Report(new IedBrowserReadResult(target, "", "", "",
                                IedBrowserRejectedScalarEvidence.Explain(raw, reason,
                                    target.DataType, target.FunctionalConstraint,
                                    client.LastErrorMessage)));
                    }
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    values.Report(new IedBrowserReadResult(target, "", "", "", "Read timeout"));
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    values.Report(new IedBrowserReadResult(target, "", "", "", ex.Message));
                }
                progress.Report(new IedBrowserReadProgress(i + 1, targets.Count));
            }
            return successful;
        }, cancellationToken);
    }
}
