namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable report-time projection of one native FAT device. The snapshot deliberately
/// copies persisted evidence instead of binding a report directly to live Explorer rows,
/// so preview/export content cannot drift while acquisition continues in the background.
/// </summary>
public sealed record NativeFatReportSnapshot(
    string DeviceId,
    string IedName,
    string IpAddress,
    int Port,
    DateTimeOffset GeneratedUtc,
    IReadOnlyList<NativeFatReportRow> Rows)
{
    public int CurrentCount => Rows.Count(row => !row.IsHistorical);
    public int HistoricalCount => Rows.Count(row => row.IsHistorical);
    public int PassCount => Rows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Pass);
    public int ReviewCount => Rows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Review);
    public int FailCount => Rows.Count(row => !row.IsHistorical && row.Result == NativeFatResult.Fail);
    public int UntestedCount => Math.Max(0, CurrentCount - PassCount - ReviewCount - FailCount);

    public string SummaryText =>
        $"Current {CurrentCount} · PASS {PassCount} · REVIEW {ReviewCount} · FAIL {FailCount} · UNTESTED {UntestedCount} · historical {HistoricalCount}";
}

public sealed record NativeFatReportRow(
    string SignalName,
    string IecReference,
    string FunctionalConstraint,
    string DataType,
    bool IsHistorical,
    string Result,
    int HistoryCount,
    string Value1Text,
    string Value1Quality,
    string Value1DeviceTimestamp,
    string Value1SourceMode,
    DateTimeOffset? Value1CapturedUtc,
    string Value2Text,
    string Value2Quality,
    string Value2DeviceTimestamp,
    string Value2SourceMode,
    DateTimeOffset? Value2CapturedUtc)
{
    public string ScopeText => IsHistorical ? "HISTORICAL" : "CURRENT";
    public string HistoryText => HistoryCount == 0 ? "—" : $"{HistoryCount} record{(HistoryCount == 1 ? string.Empty : "s")}";

    public string Value1CapturedText => Value1CapturedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";
    public string Value2CapturedText => Value2CapturedUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss.fff") ?? "—";

    public string EvidenceSummaryText =>
        $"{IecReference} [{FunctionalConstraint}] · {DataType}\n" +
        $"V1: {Value1Text} · q={Value1Quality} · IED={NormalizeTimestamp(Value1DeviceTimestamp)} · capture={Value1CapturedText} · {NormalizeSource(Value1SourceMode)}\n" +
        $"V2: {Value2Text} · q={Value2Quality} · IED={NormalizeTimestamp(Value2DeviceTimestamp)} · capture={Value2CapturedText} · {NormalizeSource(Value2SourceMode)}\n" +
        $"{ScopeText} · {Result} · history {HistoryText}";

    private static string NormalizeTimestamp(string value)
        => string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

    private static string NormalizeSource(string value)
        => string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
}
