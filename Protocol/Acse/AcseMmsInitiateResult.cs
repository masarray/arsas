using System;
using System.IO;
using ArIED61850Tester.Protocol.Diagnostics;

namespace ArIED61850Tester.Protocol.Acse;

public sealed class AcseMmsInitiateResult
{
    public bool IsAccepted { get; init; }
    public string ProfileName { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;
    public int ResponseLength { get; init; }

    public static AcseMmsInitiateResult Parse(ReadOnlyMemory<byte> response, string profileName = "")
    {
        var span = response.Span;
        var preview = HexDump.ToCompactString(span);
        var prefix = string.IsNullOrWhiteSpace(profileName) ? string.Empty : $"[{profileName}] ";

        if (span.Length == 0)
            return Reject(profileName, $"{prefix}IED returned an empty ACSE/MMS initiate response.", span, preview);

        // ISO session layer matters here. 0x0E is Accept SPDU in the IEC61850 MMS profile.
        // 0x19 is Abort SPDU in the field traces that previously fooled the loose parser because
        // the payload can still contain ACSE-looking bytes. Treat it as hard association failure.
        if (span[0] == 0x19)
            return Reject(profileName, $"{prefix}IED returned ISO Session Abort SPDU (0x19) during ACSE/MMS initiate. Response: {preview}", span, preview);

        if (span[0] == 0x0A || span[0] == 0x0C)
            return Reject(profileName, $"{prefix}IED rejected/refused the ISO session during ACSE/MMS initiate. Session SPDU=0x{span[0]:X2}. Response: {preview}", span, preview);

        if (span[0] != 0x0E)
            return Reject(profileName, $"{prefix}Unexpected ISO session response to ACSE/MMS initiate. First byte=0x{span[0]:X2}. Response: {preview}", span, preview);

        // Practical acceptance marker: ISO Session Accept plus Presentation CPA and ACSE AARE.
        // Keep this parser conservative. Do not mark MMS-ready if the session accepted but no AARE
        // or MMS initiate response marker exists.
        var hasPresentationAccept = HexDump.Contains(span, new byte[] { 0x31 }) || HexDump.Contains(span, new byte[] { 0xA0 });
        var hasAcseAare = HexDump.Contains(span, new byte[] { 0x61 });
        var hasUserInformation = HexDump.Contains(span, new byte[] { 0xBE });
        if (!hasPresentationAccept || !hasAcseAare)
            return Reject(profileName, $"{prefix}ISO session accepted but Presentation/ACSE AARE markers were incomplete. Response: {preview}", span, preview);

        return new AcseMmsInitiateResult
        {
            IsAccepted = true,
            ProfileName = profileName,
            ResponseLength = span.Length,
            ResponseHexPreview = preview,
            Message = $"{prefix}ACSE/MMS association accepted ({span.Length} byte). {(hasUserInformation ? "MMS Initiate response detected." : "AARE detected; MMS Initiate marker not explicit.")} RX: {preview}"
        };
    }

    private static AcseMmsInitiateResult Reject(string profileName, string message, ReadOnlySpan<byte> response, string preview)
    {
        return new AcseMmsInitiateResult
        {
            IsAccepted = false,
            ProfileName = profileName,
            Message = message,
            ResponseLength = response.Length,
            ResponseHexPreview = preview
        };
    }

    public void ThrowIfRejected()
    {
        if (!IsAccepted)
            throw new InvalidDataException(Message);
    }
}

public sealed class AcseAssociationAttempt
{
    public string ProfileName { get; init; } = string.Empty;
    public bool IsAccepted { get; init; }
    public string Message { get; init; } = string.Empty;
    public string ResponseHexPreview { get; init; } = string.Empty;

    public string Summary => $"{ProfileName}: {(IsAccepted ? "ACCEPTED" : "FAILED")} - {Message}";
}
