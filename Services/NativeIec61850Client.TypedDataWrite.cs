using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public enum IedBrowserNativeWriteState
{
    NotSent,
    Rejected,
    Unknown,
    AcceptedVerified,
    AcceptedReadbackMismatch,
    AcceptedReadbackUnavailable
}

public sealed record IedBrowserNativeWriteResult(
    IedBrowserNativeWriteState State,
    string Message,
    MmsWriteResult? WriteResponse = null,
    MmsReadResult? Readback = null);

/// <summary>
/// The ONLY native generic Browser data-write entrypoint. Uses the existing
/// ARIEC MMS association and its single shared I/O gate. Caller must furnish
/// an epoch-aware lease, exact GVA signature, and exact prior typed read.
/// No Write is issued on inspection, preview, stale lease or type failure.
/// No silent retry, rollback or implicit control operation.
/// </summary>
public sealed partial class NativeIec61850Client
{
    public Task<IedBrowserNativeWriteResult> WriteVerifiedDataAttributeAsync(
        MmsObjectReference reference,
        string expectedGvaSignature,
        MmsDataValue expectedOldValue,
        MmsDataValue proposedValue,
        Func<bool> leaseIsCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(expectedOldValue);
        ArgumentNullException.ThrowIfNull(proposedValue);
        ArgumentNullException.ThrowIfNull(leaseIsCurrent);

        // Keep GVA, compare-before-write, Confirmed-Write and exact readback
        // within the SAME client's MMS arbitration gate.
        return RunMmsOperationAsync(async () =>
        {
            if (!IsConnected || !leaseIsCurrent() || cancellationToken.IsCancellationRequested)
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "Association lease is no longer current.");

            MmsVariableAccessAttributesResult gva;
            try
            {
                gva = await _session.GetVariableAccessAttributesAsync(
                    reference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "Schema inspection cancelled; no Write sent.");
            }

            var rejection = IedBrowserTypedWriteAdmission.RejectReason(
                reference, gva, expectedGvaSignature, proposedValue);
            rejection ??= IedBrowserTypedWriteAdmission.RejectReason(
                reference, gva, expectedGvaSignature, expectedOldValue);
            if (rejection is not null)
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, rejection);

            if (!IsConnected || !leaseIsCurrent() || cancellationToken.IsCancellationRequested)
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "Association changed before read.");

            MmsReadResult before;
            try
            {
                before = await _session.ReadSingleVariableAsync(
                    reference, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "Pre-write read cancelled; no Write sent.");
            }

            if (!before.IsSuccess || before.Value is null ||
                !SameValue(before.Value, expectedOldValue))
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent,
                    "Exact pre-write read differs or is unavailable. Refresh and review.");

            if (SameValue(expectedOldValue, proposedValue))
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "No changed value; no Write sent.");

            if (!IsConnected || !leaseIsCurrent() || cancellationToken.IsCancellationRequested)
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.NotSent, "Association changed before dispatch.");

            MmsWriteResult write;
            try
            {
                // After entering this call, cancellation/transport failure may
                // mean the IED processed the request. NEVER retry automatically.
                write = await _session.WriteSingleVariableAsync(
                    reference, proposedValue, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or
                                       IOException or InvalidOperationException or
                                       ObjectDisposedException)
            {
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.Unknown,
                    $"Write outcome unknown after dispatch ({ex.GetType().Name}); read manually before retry.");
            }

            if (!write.IsSuccess || write.AccessResults.Count != 1 ||
                !write.AccessResults[0].IsSuccess)
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.Rejected,
                    $"IED did not confirm one successful Write access: {write.Message}", write);

            if (!IsConnected || !leaseIsCurrent())
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.AcceptedReadbackUnavailable,
                    "Write accepted; association changed before readback.", write);

            try
            {
                // A readback failure does NOT erase a confirmed Write.
                var after = await _session.ReadSingleVariableAsync(
                    reference, cancellationToken).ConfigureAwait(false);
                if (!after.IsSuccess || after.Value is null)
                    return new IedBrowserNativeWriteResult(
                        IedBrowserNativeWriteState.AcceptedReadbackUnavailable,
                        "Write accepted; exact readback unavailable.", write, after);
                return SameValue(after.Value, proposedValue)
                    ? new IedBrowserNativeWriteResult(
                        IedBrowserNativeWriteState.AcceptedVerified,
                        "Write accepted and exact same-FC readback matches.", write, after)
                    : new IedBrowserNativeWriteResult(
                        IedBrowserNativeWriteState.AcceptedReadbackMismatch,
                        "Write accepted; exact same-FC readback differs.", write, after);
            }
            catch (Exception ex) when (ex is OperationCanceledException or
                                       IOException or InvalidOperationException or
                                       ObjectDisposedException)
            {
                return new IedBrowserNativeWriteResult(
                    IedBrowserNativeWriteState.AcceptedReadbackUnavailable,
                    $"Write accepted; readback unavailable ({ex.GetType().Name}).", write);
            }
        }, cancellationToken);
    }

    private static bool SameValue(MmsDataValue left, MmsDataValue right)
        => left.Kind == right.Kind &&
           MmsDataCodec.Encode(left).AsSpan().SequenceEqual(MmsDataCodec.Encode(right));
}
