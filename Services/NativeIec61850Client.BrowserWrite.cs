using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

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

    /// <summary>
    /// Explicit operator-only BRCB PurgeBuf. Does not reserve, reconfigure,
    /// enable, disable, poll process values, or retry after an uncertain ACK.
    /// A readback of FALSE verifies the command's self-reset, NOT the amount
    /// of history that was discarded.
    /// </summary>
    public async Task<BrowserMmsWriteOutcome> WriteBrowserPurgeBufAsync(
        string rcbReference, CancellationToken cancellationToken)
    {
        if (!IsConnected || _liveModel is null ||
            !BrowserPurgeBufPolicy.TryResolve(_liveModel, rcbReference, out var exact))
            throw new InvalidOperationException(
                "Current native MMS association has no exact verified buffered RCB identity.");

        var enabledTarget = MmsObjectReference.FromIec61850Reference(
            exact + ".RptEna", "BR");
        var purgeTarget = MmsObjectReference.FromIec61850Reference(
            exact + ".PurgeBuf", "BR");

        return await RunMmsOperationAsync(async () =>
        {
            // Never try to purge a reporting-enabled BRCB. A foreign SAS
            // reservation may also refuse Write; that refusal is final.
            var enabled = await _session.ReadSingleVariableAsync(
                enabledTarget, cancellationToken).ConfigureAwait(false);
            if (!enabled.IsSuccess || enabled.Value?.Kind != MmsDataKind.Boolean ||
                enabled.Value.Value is not bool isEnabled)
                return new BrowserMmsWriteOutcome(false, false, false, "", "",
                    "Live BR.RptEna Boolean pre-read failed; no PurgeBuf Write sent.");
            if (isEnabled)
                return new BrowserMmsWriteOutcome(false, false, false, "RptEna=true", "",
                    "BRCB reporting is enabled. PurgeBuf denied without changing RptEna.");

            var before = await _session.ReadSingleVariableAsync(
                purgeTarget, cancellationToken).ConfigureAwait(false);
            if (!before.IsSuccess || before.Value?.Kind != MmsDataKind.Boolean ||
                before.Value.Value is not bool wasSet || wasSet)
                return new BrowserMmsWriteOutcome(false, false, false, "", "",
                    "Live BR.PurgeBuf Boolean pre-read failed or already TRUE; no Write sent.");

            cancellationToken.ThrowIfCancellationRequested();
            var sent = await _session.WriteSingleVariableAsync(
                purgeTarget, MmsDataValue.Boolean(true), cancellationToken)
                .ConfigureAwait(false);
            if (!sent.IsSuccess || sent.AccessResults.Count != 1 ||
                !sent.AccessResults[0].IsSuccess)
                return new BrowserMmsWriteOutcome(false, false, false, "false", "",
                    "PurgeBuf MMS Write not acknowledged: " + sent.Message +
                    ". No automatic retry; verify manually before another action.");

            var after = await _session.ReadSingleVariableAsync(
                purgeTarget, cancellationToken).ConfigureAwait(false);
            if (!after.IsSuccess || after.Value?.Kind != MmsDataKind.Boolean ||
                after.Value.Value is not bool reset)
                return new BrowserMmsWriteOutcome(true, false, false, "false", "",
                    "PurgeBuf Write ACK received; self-reset readback unavailable. Never retry automatically.");

            return new BrowserMmsWriteOutcome(true, !reset, false, "false",
                reset ? "true" : "false",
                reset
                    ? "PurgeBuf Write ACK received but command still TRUE; outcome unresolved. No automatic retry."
                    : "PurgeBuf Write ACK received; BR.PurgeBuf returned FALSE (self-reset verified). Historical buffer contents cannot be reconstructed.");
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
