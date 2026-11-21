using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>RCB metadata is historical evidence, not permission to write or arm an RCB.</summary>
public sealed record RcbMetadataValues
{
    public string ReportId { get; init; } = string.Empty;
    public string Enabled { get; init; } = string.Empty;
    public string Reservation { get; init; } = string.Empty;
    public string ReserveTime { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;
    public string TriggerOptions { get; init; } = string.Empty;
    public string OptionalFields { get; init; } = string.Empty;
    public string BufferTime { get; init; } = string.Empty;
    public string ConfigurationRevision { get; init; } = string.Empty;
    public string IntegrityPeriod { get; init; } = string.Empty;

    public static RcbMetadataValues FromDiscovery(LiveIedReportControlModel model) => new()
    {
        ReportId = model.ReportId, Enabled = model.EnabledState,
        Reservation = model.ReservationState, ReserveTime = model.ReservationTimeSeconds,
        TriggerOptions = model.TriggerOptions, OptionalFields = model.OptionalFields,
        BufferTime = model.BufferTimeMs, ConfigurationRevision = model.ConfRev,
        IntegrityPeriod = model.IntegrityPeriodMs
    };

    public static RcbMetadataValues FromReadOnlyCheck(MmsRcbAvailabilitySnapshot snapshot) => new()
    {
        ReportId = snapshot.ReportId, Enabled = snapshot.EnabledState,
        Reservation = snapshot.ReservationState, ReserveTime = snapshot.ReservationTimeSeconds,
        Owner = snapshot.Owner, TriggerOptions = snapshot.TriggerOptions,
        OptionalFields = snapshot.OptionalFields, BufferTime = snapshot.BufferTimeMs,
        ConfigurationRevision = snapshot.ConfRev, IntegrityPeriod = snapshot.IntegrityPeriodMs
    };
}

public sealed record RcbInspectionProperty(string Property, string Value, string Source);

/// <summary>
/// An offline model is explicitly distinguished from a read-only MMS observation.
/// Disconnect invalidates live-state presentation, but never discards model identity.
/// No report-history details can be inferred from an RCB availability probe.
/// </summary>
public sealed class RcbInspectionEvidence
{
    public RcbMetadataValues Model { get; init; } = new();
    public string ModelSource { get; init; } = "Unavailable";
    public RcbMetadataValues? ReadOnlyCheck { get; init; }
    public DateTimeOffset? CheckedAtUtc { get; init; }

    public string ReportId => !string.IsNullOrWhiteSpace(ReadOnlyCheck?.ReportId)
        ? ReadOnlyCheck.ReportId : Model.ReportId;
    public string ReportIdDisplay => Show(ReportId);
    public string ReportIdSource => !string.IsNullOrWhiteSpace(ReadOnlyCheck?.ReportId)
        ? "Read-only MMS RptID snapshot" : !string.IsNullOrWhiteSpace(Model.ReportId)
            ? ModelSource : "Unavailable";

    public static RcbInspectionEvidence FromSource(string reportId, MmsRcbAvailabilitySnapshot? snapshot)
        => new()
        {
            Model = new RcbMetadataValues { ReportId = reportId },
            ModelSource = "Source SCL ReportControl@rptID",
            ReadOnlyCheck = snapshot is null ? null : RcbMetadataValues.FromReadOnlyCheck(snapshot),
            CheckedAtUtc = snapshot?.CheckedAtUtc
        };

    public static RcbInspectionEvidence FromDiscovery(LiveIedReportControlModel model, MmsRcbAvailabilitySnapshot? snapshot)
        => new()
        {
            Model = RcbMetadataValues.FromDiscovery(model),
            ModelSource = "Saved discovery model (not live state)",
            ReadOnlyCheck = snapshot is null ? null : RcbMetadataValues.FromReadOnlyCheck(snapshot),
            CheckedAtUtc = snapshot?.CheckedAtUtc
        };

    public string Summary(bool connected)
        => !connected
            ? "Offline • model metadata only; live RCB state unavailable"
            : CheckedAtUtc.HasValue
                ? $"Read-only MMS check • {CheckedAtUtc.Value.ToLocalTime():HH:mm:ss} (snapshot, not a subscription)"
                : $"{ModelSource} • no current MMS state check";

    public IReadOnlyList<RcbInspectionProperty> Properties(RcbExportRow row, bool connected)
    {
        // No previous live state is promoted to current state after Stop/Disconnect.
        var live = connected ? ReadOnlyCheck : null;
        var source = live == null ? ModelSource : "Read-only MMS availability snapshot";
        var list = new List<RcbInspectionProperty>
        {
            new("RCB Name", Show(row.Name), "RCB model identity"),
            new("RCB Type", row.Buffered ? "BRCB" : "URCB", "RCB model identity"),
            new("Control Block reference", Show(row.Reference), "RCB model identity"),
            new("Report ID", Show(!string.IsNullOrWhiteSpace(live?.ReportId) ? live.ReportId : Model.ReportId),
                !string.IsNullOrWhiteSpace(live?.ReportId) ? source : ModelSource),
            new("DataSet reference", Show(row.DataSetReference),
                row.IsSourceBacked ? "SCL / effective read-only DataSet binding" : "Discovery / effective read-only DataSet binding"),
            StateField("Enabled / RptEna", live?.Enabled, Model.Enabled, connected, source),
            StateField("Reservation", live?.Reservation, Model.Reservation, connected, source),
            StateField("Reserve time (s)", live?.ReserveTime, Model.ReserveTime, connected, source),
            OwnerField(live?.Owner, connected, source),
            Field("Trigger options", live?.TriggerOptions, Model.TriggerOptions, source),
            Field("Optional fields", live?.OptionalFields, Model.OptionalFields, source),
            Field("Buffer time (ms)", live?.BufferTime, Model.BufferTime, source),
            Field("Configuration revision", live?.ConfigurationRevision, Model.ConfigurationRevision, source),
            Field("Integrity period (ms)", live?.IntegrityPeriod, Model.IntegrityPeriod, source),
            new("DataSet members", row.MemberCount > 0 ? row.MemberCount.ToString() : "—", "Directory / model members")
        };
        return list;
    }

    private RcbInspectionProperty StateField(
        string name, string? checkedValue, string? discoveryValue,
        bool connected, string liveSource)
    {
        if (UsableObservation(checkedValue))
            return new(name, checkedValue!.Trim(), liveSource);

        // Discovery happened before report activation and may explicitly say
        // RptEna=false. That snapshot is NOT the current RCB state. Presenting
        // it as plain false after the monitor wrote RptEna=true is misleading.
        // Only an exact, read-only MMS check may populate operational fields.
        return connected
            ? new(name, "Not verified", "Current MMS state requires an exact read-only RCB check")
            : new(name, "—", "Offline; no live MMS state");
    }

    private static bool UsableObservation(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        return text is not ("—" or "-" or "?") &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Not read", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Unavailable", StringComparison.OrdinalIgnoreCase);
    }

    private static RcbInspectionProperty OwnerField(
        string? rawOwner, bool connected, string liveSource)
    {
        if (!connected)
            return new("Owner", "—", "Offline; no live MMS state");
        var verified = IedBrowserRcbOwnerDisplay.FormatCheckedSnapshot(rawOwner);
        return verified.Length > 0
            ? new("Owner", verified, liveSource)
            : new("Owner", "Not verified",
                "No current read-only MMS Owner observation; unsupported or unreadable Owner is not inferred");
    }

    private RcbInspectionProperty Field(string name, string? live, string? model, string liveSource)
        => !string.IsNullOrWhiteSpace(live)
            ? new(name, live.Trim(), liveSource)
            : new(name, Show(model), string.IsNullOrWhiteSpace(model) ? "Unavailable" : ModelSource);

    private static string Show(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
}


/// <summary>
/// Read-only Owner display for the selected, exact RCB. The live Browser path
/// accepts only verified MMS OCTET STRING bytes. A separate explicit availability
/// check already supplies raw engine-derived Owner metadata. Neither source
/// identifies the client process or authorizes an RCB reservation/write.
/// </summary>
public static class IedBrowserRcbOwnerDisplay
{
    public static bool TryFormatVerifiedOctet(
        string? rawHex, MmsDataKind? verifiedKind, out string display)
    {
        display = string.Empty;
        if (verifiedKind != MmsDataKind.OctetString ||
            !TryCanonicalHex(rawHex, out var hex))
            return false;
        display = DisplayHex(hex);
        return true;
    }

    public static string FormatCheckedSnapshot(string? rawOwner)
        => TryCanonicalHex(rawOwner, out var hex)
            ? DisplayHex(hex)
            : string.Empty;

    /// <summary>
    /// Describe the decoded address without inferring the claiming program or
    /// user. A device that reports its own endpoint as Owner is ambiguous:
    /// association ownership requires a separate, session-bound verification.
    /// </summary>
    public static string ExplainIdentity(string? rawOwner, string? iedEndpoint)
    {
        if (!TryCanonicalHex(rawOwner, out var hex))
            return "Owner was not returned as a verified OCTET STRING.";
        if (!MmsRcbOwnerIdentity.TryDecodeIpAddress(hex, out var ownerIp) ||
            ownerIp is null)
            return "IED-reported Owner bytes are not a standard IP address; client application and process are unknown.";

        var reported = ownerIp.ToString();
        if (System.Net.IPAddress.TryParse(iedEndpoint, out var deviceIp) &&
            ownerIp.Equals(deviceIp))
            return $"IED-reported Owner address {reported} matches this IED endpoint. This does not identify the client application or prove who reserved the RCB.";
        return $"IED-reported Owner address: {reported}. The remote application or user cannot be identified from Owner octets alone; verify the active MMS association.";
    }

    private static string DisplayHex(string hex)
    {
        // IEC Owner may contain a 4-byte IPv4 or 16-byte IPv6 address.
        // Unknown sizes remain original exact wire bytes, never an inferred IP.
        return MmsRcbOwnerIdentity.TryDecodeIpAddress(hex, out var ip) &&
               ip is not null
            ? $"{ip} (x'{hex}')"
            : $"x'{hex}'";
    }

    private static bool TryCanonicalHex(string? value, out string hex)
    {
        hex = (value ?? string.Empty).Trim();
        // Normalize IEC diagnostic x'...' octet notation to the engine's raw hex.
        if (hex.StartsWith("x'", StringComparison.OrdinalIgnoreCase) &&
            hex.EndsWith("'", StringComparison.Ordinal))
            hex = hex[2..^1];
        if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            hex = hex[2..];
        if (hex.Length is < 2 or > 128 || (hex.Length & 1) != 0 ||
            !hex.All(Uri.IsHexDigit))
        {
            hex = string.Empty;
            return false;
        }
        hex = hex.ToUpperInvariant();
        return true;
    }
}
