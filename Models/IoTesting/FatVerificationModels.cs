using System.Text.Json.Serialization;

namespace ArIED61850Tester.Models.IoTesting;

/// <summary>
/// FAT v2 treats every static DataSet member as verification scope by default.
/// Inclusion is an operator decision and must never be changed by acquisition state.
/// </summary>
public enum FatSignalDisposition
{
    Included,
    ExcludedByOperator
}

public enum FatSignalKind
{
    Discrete,
    Analog,
    Other
}

public enum FatCaptureMode
{
    AutomaticTransition,
    OperatorSnapshot
}

public enum FatValueSlot
{
    Value1,
    Value2
}

public enum FatEvidenceCaptureKind
{
    AutomaticTransition,
    OperatorSnapshot,
    AutomaticValue,
    OperatorRecapture
}

public enum FatAutoCaptureStage
{
    WaitingValue1,
    WaitingChange,
    StabilizingValue2,
    Complete
}

public sealed record FatValueEvidence(
    Guid EvidenceId,
    FatValueSlot Slot,
    FatEvidenceCaptureKind CaptureKind,
    string RawValue,
    DateTimeOffset CapturedAt,
    DateTimeOffset? IedTimestamp,
    string Quality,
    string AcquisitionSource,
    long Sequence,
    long ConnectionGeneration);

/// <summary>
/// One FAT row. Identity follows static DataSet membership, not only the resolved runtime
/// reference, so the same IEC object may legitimately appear in more than one DataSet.
/// </summary>
public sealed class FatVerificationSignal
{
    [JsonInclude]
    public FatSignalDisposition Disposition { get; private set; } = FatSignalDisposition.Included;

    public required string SignalId { get; init; }
    public required string IedName { get; init; }
    public string AccessPointName { get; init; } = string.Empty;
    public required string DataSetReference { get; init; }
    public int DataSetMemberIndex { get; init; }
    public required string StaticMemberReference { get; init; }
    public required string RuntimeReference { get; init; }
    public string SignalName { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string DataType { get; init; } = string.Empty;
    public FatSignalKind SignalKind { get; init; } = FatSignalKind.Other;
    public FatCaptureMode CaptureMode { get; init; } = FatCaptureMode.OperatorSnapshot;

    [JsonInclude]
    public FatValueEvidence? Value1Evidence { get; private set; }

    [JsonInclude]
    public FatValueEvidence? Value2Evidence { get; private set; }

    [JsonIgnore]
    public bool IsIncludedInFat => Disposition == FatSignalDisposition.Included;

    [JsonIgnore]
    public bool HasCompleteEvidence => Value1Evidence is not null && Value2Evidence is not null;

    /// <summary>
    /// Explicit operator action. Removing a row never destroys evidence or source identity.
    /// </summary>
    public void RemoveFromFat() => Disposition = FatSignalDisposition.ExcludedByOperator;

    /// <summary>
    /// Explicit operator action. Restoring a row returns the same row and evidence to scope.
    /// </summary>
    public void RestoreToFat() => Disposition = FatSignalDisposition.Included;

    public void SetCurrentEvidence(FatValueEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Slot == FatValueSlot.Value1)
            Value1Evidence = evidence;
        else
            Value2Evidence = evidence;
    }
}

public sealed class FatVerificationProject
{
    public string ProjectId { get; init; } = Guid.NewGuid().ToString("N");
    public List<FatVerificationSignal> Signals { get; init; } = new();

    [JsonIgnore]
    public IReadOnlyList<FatVerificationSignal> IncludedSignals =>
        Signals.Where(signal => signal.IsIncludedInFat).ToArray();

    [JsonIgnore]
    public IReadOnlyList<FatVerificationSignal> RemovedSignals =>
        Signals.Where(signal => !signal.IsIncludedInFat).ToArray();

    public bool RemoveSignal(string signalId)
    {
        var signal = FindSignal(signalId);
        if (signal is null)
            return false;
        signal.RemoveFromFat();
        return true;
    }

    public bool RestoreSignal(string signalId)
    {
        var signal = FindSignal(signalId);
        if (signal is null)
            return false;
        signal.RestoreToFat();
        return true;
    }

    public int RestoreSignals(IEnumerable<string> signalIds)
    {
        ArgumentNullException.ThrowIfNull(signalIds);
        var requested = signalIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var restored = 0;
        foreach (var signal in Signals)
        {
            if (!requested.Contains(signal.SignalId) || signal.IsIncludedInFat)
                continue;
            signal.RestoreToFat();
            restored++;
        }
        return restored;
    }

    public int RestoreAllSignals()
    {
        var restored = 0;
        foreach (var signal in Signals.Where(signal => !signal.IsIncludedInFat))
        {
            signal.RestoreToFat();
            restored++;
        }
        return restored;
    }

    private FatVerificationSignal? FindSignal(string signalId)
    {
        if (string.IsNullOrWhiteSpace(signalId))
            return null;
        return Signals.FirstOrDefault(signal =>
            signal.SignalId.Equals(signalId, StringComparison.OrdinalIgnoreCase));
    }
}
