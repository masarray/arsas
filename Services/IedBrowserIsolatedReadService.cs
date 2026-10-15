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
        CancellationToken cancellationToken)
        => Task.Run(async () =>
        {
            // Capture before crossing the UI/thread boundary. The current
            // monitoring association owns this exact live discovery model;
            // SCL-only metadata is never promoted to verified MMS schema.
            var lease = IedBrowserIsolatedReadSchemaLease.Capture(device);
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
                    leafToken.CancelAfter(TimeSpan.FromSeconds(5));
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
                            read?.UtcTimeEvidence, read?.QualityEvidence));
                        successful++;
                    }
                    else
                        values.Report(new IedBrowserReadResult(target, "", "", "",
                            raw is null && client.LastErrorMessage.Length > 0
                                ? client.LastErrorMessage : reason));
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
