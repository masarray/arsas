namespace ArIED61850Tester.Models;

/// <summary>
/// Captures the last native IEC 61850 connection/discovery state before a session
/// is disposed. It is intentionally retained on the IED workspace so a useful
/// support report can still be copied after a failed connection attempt.
/// </summary>
public sealed class Iec61850DeviceDiagnosticSnapshot
{
    public DateTimeOffset CapturedAt { get; init; } = DateTimeOffset.Now;
    public string Phase { get; init; } = string.Empty;
    public string FailureKind { get; init; } = string.Empty;
    public string FriendlyMessage { get; init; } = string.Empty;
    public string ExceptionType { get; init; } = string.Empty;
    public string ExceptionMessage { get; init; } = string.Empty;
    public string ConnectionMode { get; init; } = string.Empty;
    public string NativeState { get; init; } = string.Empty;
    public bool TransportReady { get; init; }
    public bool MmsReady { get; init; }
    public string AssociationAttemptSummary { get; init; } = string.Empty;
    public string AssociationResponseHex { get; init; } = string.Empty;
    public string DiscoverySummary { get; init; } = string.Empty;
    public string DiscoveryRequestHex { get; init; } = string.Empty;
    public string DiscoveryResponseHex { get; init; } = string.Empty;
}
