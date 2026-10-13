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
            Field("Enabled / RptEna", live?.Enabled, Model.Enabled, source),
            Field("Reservation", live?.Reservation, Model.Reservation, source),
            Field("Reserve time (s)", live?.ReserveTime, Model.ReserveTime, source),
            Field("Owner", live?.Owner, Model.Owner, source),
            Field("Trigger options", live?.TriggerOptions, Model.TriggerOptions, source),
            Field("Optional fields", live?.OptionalFields, Model.OptionalFields, source),
            Field("Buffer time (ms)", live?.BufferTime, Model.BufferTime, source),
            Field("Configuration revision", live?.ConfigurationRevision, Model.ConfigurationRevision, source),
            Field("Integrity period (ms)", live?.IntegrityPeriod, Model.IntegrityPeriod, source),
            new("DataSet members", row.MemberCount > 0 ? row.MemberCount.ToString() : "—", "Directory / model members")
        };
        return list;
    }

    private RcbInspectionProperty Field(string name, string? live, string? model, string liveSource)
        => !string.IsNullOrWhiteSpace(live)
            ? new(name, live.Trim(), liveSource)
            : new(name, Show(model), string.IsNullOrWhiteSpace(model) ? "Unavailable" : ModelSource);

    private static string Show(string? text) => string.IsNullOrWhiteSpace(text) ? "—" : text.Trim();
}
