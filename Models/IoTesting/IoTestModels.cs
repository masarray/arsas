namespace ArIED61850Tester.Models.IoTesting;

public enum IoTestPointState
{
    NotStarted,
    WaitingForBaseline,
    WaitingForOffBaseline,
    ArmedForOn,
    OnCaptured,
    Passed,
    Review,
    Failed
}

public enum IoEvidenceTransition
{
    Baseline,
    On,
    Off
}

public enum IoEvidenceVerdict
{
    Accepted,
    Review,
    Rejected
}

public sealed record IoTestObservation(
    bool? NormalizedState,
    string RawValue,
    DateTimeOffset CapturedAt,
    DateTimeOffset? IedTimestamp,
    string Quality,
    string AcquisitionSource,
    long Sequence,
    long ConnectionGeneration);

public sealed record IoTestTransitionEvidence(
    Guid EvidenceId,
    IoEvidenceTransition Transition,
    bool? PreviousState,
    bool ObservedState,
    string RawValue,
    DateTimeOffset CapturedAt,
    DateTimeOffset? IedTimestamp,
    string Quality,
    string AcquisitionSource,
    long Sequence,
    long ConnectionGeneration,
    IoEvidenceVerdict Verdict,
    string VerdictReason);

public sealed class IoTestPointRuntime
{
    public IoTestPointState State { get; internal set; } = IoTestPointState.NotStarted;
    public bool? LastObservedState { get; internal set; }
    public long LastSequence { get; internal set; } = -1;
    public long ConnectionGeneration { get; internal set; } = -1;
    public IoTestTransitionEvidence? OnEvidence { get; internal set; }
    public IoTestTransitionEvidence? OffEvidence { get; internal set; }
    public string StatusReason { get; internal set; } = "Not started";
    public int Attempt { get; internal set; }

    public bool IsComplete => State is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed;

    internal void ResetAttempt()
    {
        State = IoTestPointState.WaitingForBaseline;
        LastObservedState = null;
        LastSequence = -1;
        ConnectionGeneration = -1;
        OnEvidence = null;
        OffEvidence = null;
        StatusReason = "Waiting for a trustworthy baseline";
        Attempt++;
    }
}

public sealed class IoTestPointPlan
{
    public required string TestPointId { get; init; }
    public required string IedName { get; init; }
    public required string IpAddress { get; init; }
    public required string SignalName { get; init; }
    public required string ObjectReference { get; init; }
    public required string FunctionalConstraint { get; init; }
    public required string ExpectedOnText { get; init; }
    public required string ExpectedOffText { get; init; }
    public string SourceSheet { get; init; } = string.Empty;
    public int SourceRow { get; init; }
    public bool TestEnabled { get; set; } = true;
    public bool ImportReady { get; init; } = true;
    public string BindingStatus { get; init; } = string.Empty;
    public IoTestPointRuntime Runtime { get; } = new();
}

public sealed class IoTestIedPlan
{
    public required string IedName { get; init; }
    public required string IpAddress { get; init; }
    public string IedRole { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string VoltageLevel { get; init; } = string.Empty;
    public List<IoTestPointPlan> TestPoints { get; init; } = new();

    public int EnabledCount => TestPoints.Count(point => point.TestEnabled);
    public int PassedCount => TestPoints.Count(point => point.Runtime.State == IoTestPointState.Passed);
    public int ReviewCount => TestPoints.Count(point => point.Runtime.State == IoTestPointState.Review);
    public int PendingCount => EnabledCount - PassedCount - ReviewCount;
}

public sealed class IoTestProject
{
    public required string ProjectId { get; init; }
    public required string SchemaVersion { get; init; }
    public required string ProjectName { get; init; }
    public string SourceWorkbookName { get; init; } = string.Empty;
    public string SourceWorkbookSha256 { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<IoTestIedPlan> Ieds { get; init; } = new();

    public int SignalCount => Ieds.Sum(ied => ied.TestPoints.Count);
    public int ReadySignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.ImportReady));
}
