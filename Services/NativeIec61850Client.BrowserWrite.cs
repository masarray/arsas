using AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed record BrowserMmsWriteOutcome(
    bool WriteAccepted, bool ReadbackVerified, bool NoChange,
    string Before, string After, string Detail)
{
    public bool Succeeded => (WriteAccepted || NoChange) && ReadbackVerified;
}

/// <summary>
/// Native association-bound MMS DA editor: typed pre-read, at most one Write,
/// then independently decoded readback. Uses the SAME ARIEC receive pump and
/// per-IED gate as monitoring/commands. No ad-hoc BER or second connection.
/// </summary>
public sealed partial class NativeIec61850Client
{
    public async Task<MmsReadResult> ReadBrowserConfigurationAsync(
        string reference, string fc, CancellationToken cancellationToken)
    {
        ValidateBrowserConfigurationTarget(reference, fc);
        var target = MmsObjectReference.FromIec61850Reference(reference, fc);
        return await RunMmsOperationAsync(
            () => _session.ReadSingleVariableAsync(target, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task<BrowserMmsWriteOutcome> WriteBrowserConfigurationAsync(
        string reference, string fc, MmsDataValue desired, CancellationToken cancellationToken)
    {
        ValidateBrowserConfigurationTarget(reference, fc);
        // Only a WRITE is forbidden here; read-only inspection is unaffected.
        var wireTarget = MmsObjectReference.FromIec61850Reference(reference, fc);
        if (wireTarget.Item.Split((char)36).Skip(2).Any(segment =>
                segment.Equals("ctlModel", StringComparison.OrdinalIgnoreCase) ||
                segment is "Oper" or "SBO" or "SBOw" or "Cancel"))
            throw new InvalidOperationException(
                "Control service and ctlModel configuration require dedicated native APIs; generic MMS Write forbidden.");
        ArgumentNullException.ThrowIfNull(desired);
        if (desired.Kind is MmsDataKind.Array or MmsDataKind.Structure or
            MmsDataKind.Unknown or MmsDataKind.BitString or MmsDataKind.UtcTime)
            throw new InvalidOperationException("Only admitted primitive typed configuration DA writes are supported.");

        var target = MmsObjectReference.FromIec61850Reference(reference, fc);
        return await RunMmsOperationAsync(async () =>
        {
            var before = await _session.ReadSingleVariableAsync(target, cancellationToken)
                .ConfigureAwait(false);
            if (!before.IsSuccess || before.Value is null)
                return new BrowserMmsWriteOutcome(false, false, false, "", "",
                    "Pre-write live MMS Read failed: " + before.Message + ". No Write sent.");
            var beforeText = MmsDataCodec.ToDisplayString(before.Value);
            if (before.Value.Kind != desired.Kind)
                return new BrowserMmsWriteOutcome(false, false, false, beforeText, "",
                    "Typed live MMS data kind differs from requested encoder. No Write sent.");

            if (MmsDataCodec.Encode(before.Value).AsSpan().SequenceEqual(MmsDataCodec.Encode(desired)))
                return new BrowserMmsWriteOutcome(false, true, true, beforeText, beforeText,
                    "Live value already equals requested value. No Write sent.");

            cancellationToken.ThrowIfCancellationRequested();
            var sent = await _session.WriteSingleVariableAsync(target, desired, cancellationToken)
                .ConfigureAwait(false);
            if (!sent.IsSuccess || sent.AccessResults.Count != 1 || !sent.AccessResults[0].IsSuccess)
                return new BrowserMmsWriteOutcome(false, false, false, beforeText, "",
                    "MMS Confirmed-Write not accepted: " + sent.Message + ". No automatic retry.");

            var after = await _session.ReadSingleVariableAsync(target, cancellationToken)
                .ConfigureAwait(false);
            if (!after.IsSuccess || after.Value is null)
                return new BrowserMmsWriteOutcome(true, false, false, beforeText, "",
                    "Write acknowledged, but exact MMS readback failed: " + after.Message);
            var afterText = MmsDataCodec.ToDisplayString(after.Value);
            var matches = after.Value.Kind == desired.Kind &&
                MmsDataCodec.Encode(after.Value).AsSpan().SequenceEqual(MmsDataCodec.Encode(desired));
            return new BrowserMmsWriteOutcome(true, matches, false, beforeText, afterText,
                matches ? "Confirmed-Write accepted and typed MMS readback verified." :
                    "Write accepted but readback mismatch. Do not retry automatically.");
        }, cancellationToken).ConfigureAwait(false);
    }

    private void ValidateBrowserConfigurationTarget(string reference, string fc)
    {
        if (!IsConnected || _liveModel is null)
            throw new InvalidOperationException("The current LIVE MMS association/model is unavailable.");
        if (string.IsNullOrWhiteSpace(reference) || !reference.Contains('/'))
            throw new ArgumentException("Expected an exact live LD/LN.DO.DA reference.");
        if (fc is not ("CF" or "DC" or "SP" or "SG" or "SE"))
            throw new InvalidOperationException("Browser MMS Write refuses control/process/report namespaces.");
        var target = MmsObjectReference.FromIec61850Reference(reference, fc);
        if (string.IsNullOrWhiteSpace(target.Domain) || string.IsNullOrWhiteSpace(target.Item))
            throw new InvalidOperationException("Invalid domain/item identity for MMS Write.");
    }
}
