using ArIED61850Tester.Models;

namespace ArIED61850Tester.Models.IoTesting;

public enum IoTestSessionState
{
    Idle,
    Running,
    Paused,
    Interrupted,
    Completed,
    Stopped,
    Faulted
}

public sealed record IoTestSessionActionResult(bool Succeeded, string Message)
{
    public static IoTestSessionActionResult Success(string message) => new(true, message);
    public static IoTestSessionActionResult Failure(string message) => new(false, message);
}

public sealed record IoTestJournalEntry
{
    public required string EventType { get; init; }
    public required DateTimeOffset RecordedAtUtc { get; init; }
    public required string ProjectId { get; init; }
    public required Guid SessionId { get; init; }
    public required string IedName { get; init; }
    public string IpAddress { get; init; } = string.Empty;
    public string SourceWorkbookName { get; init; } = string.Empty;
    public string SourceWorkbookSha256 { get; init; } = string.Empty;
    public string ApplicationVersion { get; init; } = string.Empty;
    public string Operator { get; init; } = string.Empty;
    public string Workstation { get; init; } = string.Empty;
    public string TestPointId { get; init; } = string.Empty;
    public string SignalName { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
    public int Attempt { get; init; }
    public string Transition { get; init; } = string.Empty;
    public string PreviousValue { get; init; } = string.Empty;
    public string ObservedValue { get; init; } = string.Empty;
    public bool? NormalizedState { get; init; }
    public DateTimeOffset? IedTimestamp { get; init; }
    public string Quality { get; init; } = string.Empty;
    public string AcquisitionSource { get; init; } = string.Empty;
    public string ReportReason { get; init; } = string.Empty;
    public long PointSequence { get; init; }
    public long ConnectionGeneration { get; init; }
    public string Verdict { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
}

public sealed record IoTestJournalEnvelope(
    long JournalSequence,
    string PreviousHash,
    string Hash,
    IoTestJournalEntry Entry);

public sealed record IoTestJournalVerificationResult(
    bool IsValid,
    int RecordCount,
    string LastHash,
    string Error);
