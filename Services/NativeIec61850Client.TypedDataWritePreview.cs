using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

/// <summary>
/// Read-only, per-association inspection for a single exact MMS DA. This
/// object contains staged evidence, never approval or write authority.
/// </summary>
public sealed record IedBrowserNativeWritePreview(
    bool IsAvailable,
    string Message,
    MmsObjectReference Reference,
    string GvaSignature = "",
    MmsTypeSpecificationNode? Type = null,
    MmsDataValue? OldValue = null)
{
    public string CurrentText => OldValue is null ? "-" : MmsDataCodec.ToDisplayString(OldValue);
}

public sealed partial class NativeIec61850Client
{
    public Task<IedBrowserNativeWritePreview> PreviewDataAttributeWriteAsync(
        MmsObjectReference reference,
        Func<bool> leaseIsCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leaseIsCurrent);
        return RunMmsOperationAsync(async () =>
        {
            IedBrowserNativeWritePreview Deny(string reason) =>
                new(false, reason, reference);
            if (!IsConnected || !leaseIsCurrent())
                return Deny("MMS association changed; preview unavailable.");

            MmsVariableAccessAttributesResult gva;
            MmsReadResult read;
            try
            {
                gva = await _session.GetVariableAccessAttributesAsync(
                    reference, cancellationToken).ConfigureAwait(false);
                if (!gva.IsSuccess || gva.TypeSpecification is null)
                    return Deny("GVA unavailable: " + gva.Message);
                if (!IsConnected || !leaseIsCurrent())
                    return Deny("Association changed during GVA.");
                read = await _session.ReadSingleVariableAsync(
                    reference, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or IOException or
                                       InvalidOperationException or ObjectDisposedException)
            {
                return Deny($"MMS preview failed ({ex.GetType().Name}).");
            }
            if (!read.IsSuccess || read.Value is null || !IsConnected || !leaseIsCurrent())
                return Deny("Current typed value could not be read on this association.");

            var signature = IedBrowserTypedWriteAdmission.Fingerprint(gva.TypeSpecification);
            var rejection = IedBrowserTypedWriteAdmission.RejectReason(
                reference, gva, signature, read.Value);
            return rejection is null
                ? new IedBrowserNativeWritePreview(
                    true, "Read-only current value and exact live wire type verified.",
                    reference, signature, gva.TypeSpecification, read.Value)
                : Deny(rejection);
        }, cancellationToken);
    }
}
