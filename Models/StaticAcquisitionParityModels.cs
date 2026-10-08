namespace ArIED61850Tester.Models;

public enum StaticAcquisitionIngressKind
{
    Unknown,
    LiveDiscovery,
    OpenScl
}

public enum StaticAcquisitionParityStatus
{
    NotAvailable,
    AwaitingOtherIngress,
    Equivalent,
    Mismatch,
    InsufficientEvidence
}

/// <summary>
/// Source-neutral semantic evidence for one Static DataSet planning pass.
///
/// Concrete runtime RCB instance names are retained for diagnostics but are deliberately
/// excluded from SemanticFingerprint. An indexed RCB slot may legitimately change from
/// Rpt01 to Rpt02 across associations while the configured DataSet/report semantics remain
/// equivalent.
/// </summary>
public sealed class StaticAcquisitionIngressEvidence
{
    public DateTimeOffset CapturedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public StaticAcquisitionIngressKind Ingress { get; init; }
    public string IedName { get; init; } = string.Empty;
    public string SemanticFingerprint { get; init; } = string.Empty;
    public int RequestedPointCount { get; init; }
    public int PlannedReportCount { get; init; }
    public int StaticBrcbSignalCount { get; init; }
    public int StaticUrcbSignalCount { get; init; }
    public int UncoveredSignalCount { get; init; }
    public IReadOnlyList<string> SemanticLines { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> RuntimeTargets { get; init; } = Array.Empty<string>();
    public bool IsComparable { get; init; }
    public string IncomparableReason { get; init; } = string.Empty;

    public string Summary =>
        $"{Ingress}: fingerprint={SemanticFingerprint}, requested={RequestedPointCount}, " +
        $"plans={PlannedReportCount}, staticBRCB={StaticBrcbSignalCount}, " +
        $"staticURCB={StaticUrcbSignalCount}, uncovered={UncoveredSignalCount}";
}

public sealed class StaticAcquisitionParitySnapshot
{
    public StaticAcquisitionIngressEvidence? Discovery { get; init; }
    public StaticAcquisitionIngressEvidence? OpenScl { get; init; }
    public StaticAcquisitionParityStatus Status { get; init; } = StaticAcquisitionParityStatus.NotAvailable;
    public IReadOnlyList<string> Differences { get; init; } = Array.Empty<string>();

    public string Summary => Status switch
    {
        StaticAcquisitionParityStatus.Equivalent =>
            $"MATCH • {Discovery?.SemanticFingerprint ?? OpenScl?.SemanticFingerprint ?? "unavailable"}",
        StaticAcquisitionParityStatus.Mismatch =>
            $"MISMATCH • discovery={Discovery?.SemanticFingerprint ?? "missing"} • scl={OpenScl?.SemanticFingerprint ?? "missing"}",
        StaticAcquisitionParityStatus.InsufficientEvidence =>
            $"INCOMPLETE EVIDENCE • {string.Join("; ", Differences.Take(2))}",
        StaticAcquisitionParityStatus.AwaitingOtherIngress =>
            $"AWAITING PEER INGRESS • {(Discovery is not null ? "Discovery captured" : "Open SCL captured")}",
        _ => "not captured"
    };
}
