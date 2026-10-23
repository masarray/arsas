using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Opens a short-lived, read-only MMS association for an explicit RCB availability audit.
/// No report attribute is written and no RCB is reserved or enabled.
/// </summary>
public sealed class RcbAvailabilityProbeService
{
    public async Task<MmsRcbAvailabilityResult> CheckAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        if (string.IsNullOrWhiteSpace(device.IpAddress))
            throw new InvalidOperationException("Bind an MMS endpoint before checking RCB availability.");

        await using var session = new MmsClientSession();
        await session.ConnectAsync(
            device.IpAddress,
            device.Port <= 0 ? 102 : device.Port,
            TimeSpan.FromSeconds(8),
            cancellationToken).ConfigureAwait(false);
        if (!session.IsMmsInitiated)
            throw new InvalidOperationException("The read-only RCB audit association did not reach MMS Initiated state.");

        var discovery = await session.DiscoverAsync(
            probeReportAttributes: true,
            maxReportAttributeProbes: 512,
            cancellationToken).ConfigureAwait(false);

        // Do not infer ownership from discovered signal metadata. A point can retain an
        // RCB reference after discovery even when that RCB was never armed by the current
        // runtime session. Until the monitor runtime exposes its exact active plan set,
        // RptEna / Resv / ResvTms / Owner remain the source of truth. This deliberately
        // favors an honest InUse/Unknown result over a false "ARSAS active" indication.
        return await session.CheckReportControlAvailabilityAsync(
            discovery.ReportInventory,
            discovery.IedDirectory,
            new MmsRcbAvailabilityOptions
            {
                MaxReportControls = 512,
                ReadDataSetDirectories = true,
                CallerOwnedRcbReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            },
            cancellationToken).ConfigureAwait(false);
    }
}
