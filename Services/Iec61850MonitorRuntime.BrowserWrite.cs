using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Browser configuration Writes share control-flight admission, native MMS
/// association and cancellation quarantine. No temporary write connection,
/// hidden retry, process-value polling or bypass around the Command Dock.
/// </summary>
public sealed partial class Iec61850MonitorRuntime
{
    public async Task<MmsReadResult> ReadBrowserConfigurationAsync(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        IedBrowserWriteRow target, CancellationToken cancellationToken)
    {
        var session = CurrentBrowserWriteSession(deviceId, model, association, target);
        return await session.Client.ReadBrowserConfigurationAsync(
            target.Reference, target.FunctionalConstraint, cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<BrowserMmsWriteOutcome> WriteBrowserConfigurationAsync(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        IedBrowserWriteRow target, MmsDataValue desired, CancellationToken cancellationToken)
    {
        var session = CurrentBrowserWriteSession(deviceId, model, association, target);
        return RunAdmittedBrowserWriteAsync(
            deviceId, model, association, session,
            target.Reference, target.FunctionalConstraint,
            "OPERATOR_BROWSER_MMS_WRITE",
            token => session.Client.WriteBrowserConfigurationAsync(
                target.Reference, target.FunctionalConstraint, desired, token),
            cancellationToken);
    }

    /// <summary>
    /// Only this explicit IED Browser action may request PurgeBuf.
    /// Recovery, RCB monitoring, GI, and generic DA Write never call it.
    /// </summary>
    public Task<BrowserMmsWriteOutcome> WriteBrowserPurgeBufAsync(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        string rcbReference, bool explicitOperatorConfirmation,
        CancellationToken cancellationToken)
    {
        if (!explicitOperatorConfirmation)
            throw new InvalidOperationException(
                "PurgeBuf requires an explicit operator-confirmed IED Browser action.");
        var session = CurrentBrowserPurgeSession(
            deviceId, model, association, rcbReference, out var exactReference);
        return RunAdmittedBrowserWriteAsync(
            deviceId, model, association, session, exactReference, "BR",
            "OPERATOR_BROWSER_PURGEBUF_WRITE",
            token => session.Client.WriteBrowserPurgeBufAsync(exactReference, token),
            cancellationToken);
    }

    private async Task<BrowserMmsWriteOutcome> RunAdmittedBrowserWriteAsync(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        DeviceSession session, string reference, string fc, string operation,
        Func<CancellationToken, Task<BrowserMmsWriteOutcome>> action,
        CancellationToken cancellationToken)
    {
        if (!Iec61850ControlFlightAdmission.TryEnter(
                ref session.ControlCommandActive, ref session.ControlFlightQuarantined))
            throw new InvalidOperationException(
                "An IEC control/configuration flight is active or quarantined on this MMS association.");
        var lifetime = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, session.ControlLifetimeCancellation.Token);
        Task<BrowserMmsWriteOutcome>? native = null;
        try
        {
            Log("WARN", session.Device.Name,
                operation + ": " + reference + " [" + fc +
                "]; explicit operator confirmation; at most one Write.");
            native = action(lifetime.Token);
            var result = await Iec61850ControlFlightAdmission.WaitForNativeAsync(
                native, ControlFlightDeadline, cancellationToken,
                () => Iec61850ControlFlightAdmission.Quarantine(ref session.ControlFlightQuarantined),
                lifetime.Cancel).ConfigureAwait(false);
            if (!_sessions.TryGetValue(deviceId, out var current) ||
                !ReferenceEquals(current, session) ||
                session.ControlLifetimeCancellation.IsCancellationRequested ||
                session.Device.LiveValues.Association != association ||
                !ReferenceEquals(session.Device.LiveDiscoveryModel, model))
                throw new InvalidOperationException(
                    "MMS association/model changed; this Write outcome cannot be attributed to the new session.");
            Log(result.Succeeded ? "INFO" : "WARN", session.Device.Name,
                operation + "_RESULT: " + reference +
                "; accepted=" + result.WriteAccepted +
                "; noChange=" + result.NoChange +
                "; readbackVerified=" + result.ReadbackVerified +
                "; " + result.Detail);
            return result;
        }
        finally
        {
            if (native is { IsCompleted: false })
            {
                Iec61850ControlFlightAdmission.Quarantine(ref session.ControlFlightQuarantined);
                // Do not free admission while a native MMS Write could be in flight.
                _ = ObserveBrowserWriteCompletionAsync(native, lifetime, session);
            }
            else
            {
                Iec61850ControlFlightAdmission.Release(ref session.ControlCommandActive);
                lifetime.Dispose();
            }
        }
    }

    private DeviceSession CurrentBrowserPurgeSession(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        string rcbReference, out string exactRcbReference)
    {
        if (!_sessions.TryGetValue(deviceId, out var session) ||
            !session.Client.IsConnected || !session.Device.IsConnected ||
            session.Device.IsDemo || session.Device.IsBusy ||
            session.Device.IsMonitoring || session.Device.LiveValues.IsOffline ||
            session.ControlLifetimeCancellation.IsCancellationRequested ||
            !ReferenceEquals(session.Device.LiveDiscoveryModel, model) ||
            session.Device.LiveValues.Association != association ||
            !BrowserPurgeBufPolicy.TryResolve(model, rcbReference,
                out exactRcbReference))
            throw new InvalidOperationException(
                "Manual PurgeBuf requires one exact current live BRCB, an idle association and stopped ARSAS monitoring.");

        if (GetActiveBrowserRcbReferences(deviceId).Any(active =>
                BrowserPurgeBufPolicy.SameExactBrcbReference(active, exactRcbReference)))
            throw new InvalidOperationException(
                "PurgeBuf is blocked while this RCB has an ARSAS reporting subscription.");

        return session;
    }

    private static async Task ObserveBrowserWriteCompletionAsync(
        Task<BrowserMmsWriteOutcome> native, CancellationTokenSource lifetime,
        DeviceSession session)
    {
        try { await native.ConfigureAwait(false); }
        catch (Exception) { /* outcome remains UNKNOWN, quarantine survives */ }
        finally
        {
            Iec61850ControlFlightAdmission.Release(ref session.ControlCommandActive);
            lifetime.Dispose();
        }
    }

    private DeviceSession CurrentBrowserWriteSession(
        string deviceId, LiveIedModelDiscoveryDocument model, long association,
        IedBrowserWriteRow target)
    {
        if (!_sessions.TryGetValue(deviceId, out var session) ||
            !session.Client.IsConnected || !session.Device.IsConnected ||
            session.Device.IsDemo || session.ControlLifetimeCancellation.IsCancellationRequested ||
            !ReferenceEquals(session.Device.LiveDiscoveryModel, model) ||
            session.Device.LiveValues.Association != association)
            throw new InvalidOperationException(
                "Browser Write requires the exact current live MMS model and association.");
        var matching = model.LogicalDevices
            .SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .SelectMany(obj => obj.Attributes)
            .Where(attr => string.Equals(attr.MmsReference, target.Reference,
                                StringComparison.Ordinal) &&
                           string.Equals(attr.FunctionalConstraint, target.FunctionalConstraint,
                                StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (matching.Length != 1 ||
            !IedBrowserWritePlan.IsSupported(target.DataType) ||
            !string.Equals(
                string.IsNullOrWhiteSpace(matching[0].SclBType)
                    ? matching[0].MmsType : matching[0].SclBType,
                target.DataType, StringComparison.OrdinalIgnoreCase) ||
            target.FunctionalConstraint is not ("CF" or "DC" or "SP" or "SG" or "SE"))
            throw new InvalidOperationException(
                "DA Write target/type was not uniquely verified in the current LIVE MMS directory.");
        return session;
    }
}
