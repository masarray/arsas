namespace ArIED61850Tester.Models;

public enum Iec61850ControlModelKind
{
    Unknown = -1,
    StatusOnly = 0,
    DirectNormal = 1,
    SboNormal = 2,
    DirectEnhanced = 3,
    SboEnhanced = 4
}

public sealed class Iec61850ControlCapabilities
{
    public string ObjectReference { get; init; } = string.Empty;
    public string StatusReference { get; init; } = string.Empty;
    public string ControlModelReference { get; init; } = string.Empty;
    public Iec61850ControlModelKind ControlModel { get; init; } = Iec61850ControlModelKind.Unknown;
    public string ControlModelText { get; init; } = "Unknown";
    public string ControlCdc { get; init; } = string.Empty;
    public string ControlValueType { get; init; } = string.Empty;
    public string CtlValSignature { get; init; } = string.Empty;
    public string CurrentValue { get; init; } = "-";
    public string CurrentState { get; init; } = "Unknown";
    public bool EngineControlServiceAvailable { get; init; }
    public bool IsOperationallyReady { get; init; }
    public bool SupportsCommandTermination { get; init; }
    public bool SupportsTimeActivatedOperate { get; init; }
    public string SboTimeoutText { get; init; } = "-";
    public string OperTimeoutText { get; init; } = "-";
    public string SequenceText { get; init; } = "No safe command sequence available";
    public string DiscoveryEvidence { get; init; } = string.Empty;
    public string EngineControlServiceStatus { get; init; } = "ARIEC61850 native control service is unavailable.";

    public bool SupportsOperate => EngineControlServiceAvailable && IsOperationallyReady &&
                                   ControlModel is not Iec61850ControlModelKind.StatusOnly and not Iec61850ControlModelKind.Unknown;
    public bool UsesSelectBeforeOperate => ControlModel is Iec61850ControlModelKind.SboNormal or Iec61850ControlModelKind.SboEnhanced;
    public bool UsesSelectWithValue => ControlModel == Iec61850ControlModelKind.SboEnhanced;
}

public sealed class Iec61850ControlCommandRequest
{
    public required SignalDefinition Signal { get; init; }
    public string ValueText { get; init; } = string.Empty;
    public bool InterlockCheck { get; init; } = true;
    public bool SynchroCheck { get; init; }
    public bool TestMode { get; init; }
    public string Originator { get; init; } = "ARSAS";
    public string OriginCategory { get; init; } = "Maintenance";
    public int FeedbackTimeoutMs { get; init; } = 12000;
    public int CommandTerminationTimeoutMs { get; init; } = 10000;
}

public sealed class Iec61850ControlCommandResult
{
    public bool IsSuccess { get; init; }
    public bool ServiceAccepted { get; init; }
    public bool FeedbackConfirmed { get; init; }
    public bool CommandTerminationReceived { get; init; }
    public bool PositiveTermination { get; init; }
    public string CompletionState { get; init; } = string.Empty;
    public string Stage { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string ControlModelText { get; init; } = string.Empty;
    public string SequenceText { get; init; } = string.Empty;
    public string RequestedValue { get; init; } = string.Empty;
    public string FeedbackValue { get; init; } = string.Empty;
    public string ControlError { get; init; } = string.Empty;
    public string AddCause { get; init; } = string.Empty;
    public string LastApplErrorText { get; init; } = string.Empty;
    public string ClientError { get; init; } = string.Empty;
    public string ControlNumber { get; init; } = "-";
    public string SequenceTimestamp { get; init; } = "-";
    public string ElapsedText { get; init; } = "-";
    public string FeedbackElapsedText { get; init; } = "-";
    public string TotalElapsedText { get; init; } = "-";
    public string RequestHex { get; init; } = string.Empty;
    public string ResponseHex { get; init; } = string.Empty;
}
