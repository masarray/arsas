using ArIED61850Tester.Models;

using System.ComponentModel;
using System.Globalization;
using System.Text.Json.Serialization;

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

public enum IoTestLiveBindingState
{
    NotEvaluated,
    DeviceNotLoaded,
    SignalNotFound,
    BoundExact,
    BoundNormalized,
    LivePointReady
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

public sealed class IoTestPointRuntime : ObservableObject
{
    private IoTestPointState _state = IoTestPointState.NotStarted;
    private bool? _lastObservedState;
    private long _lastSequence = -1;
    private long _connectionGeneration = -1;
    private IoTestTransitionEvidence? _onEvidence;
    private IoTestTransitionEvidence? _offEvidence;
    private FatValueEvidence? _value1Evidence;
    private FatValueEvidence? _value2Evidence;
    private string _statusReason = "Not started";
    private int _attempt;
    private string _currentValue = "-";
    private string _currentQuality = "Unknown";
    private string _currentSource = "Not connected";
    private string _currentIedTimestamp = "—";

    public IoTestPointState State
    {
        get => _state;
        internal set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(IsComplete));
                Raise(nameof(StateText));
            }
        }
    }

    public bool? LastObservedState { get => _lastObservedState; internal set => Set(ref _lastObservedState, value); }
    public long LastSequence { get => _lastSequence; internal set => Set(ref _lastSequence, value); }
    public long ConnectionGeneration { get => _connectionGeneration; internal set => Set(ref _connectionGeneration, value); }
    public IoTestTransitionEvidence? OnEvidence
    {
        get => _onEvidence;
        internal set
        {
            if (!Set(ref _onEvidence, value))
                return;
            Raise(nameof(OnRelayTimestampText));
            Raise(nameof(OnEvidenceToolTip));
        }
    }

    public IoTestTransitionEvidence? OffEvidence
    {
        get => _offEvidence;
        internal set
        {
            if (!Set(ref _offEvidence, value))
                return;
            Raise(nameof(OffRelayTimestampText));
            Raise(nameof(OffEvidenceToolTip));
        }
    }

    public FatValueEvidence? Value1Evidence
    {
        get => _value1Evidence;
        internal set
        {
            if (!Set(ref _value1Evidence, value))
                return;
            Raise(nameof(Value1RelayTimestampText));
            Raise(nameof(Value1EvidenceToolTip));
        }
    }

    public FatValueEvidence? Value2Evidence
    {
        get => _value2Evidence;
        internal set
        {
            if (!Set(ref _value2Evidence, value))
                return;
            Raise(nameof(Value2RelayTimestampText));
            Raise(nameof(Value2EvidenceToolTip));
        }
    }

    public string StatusReason { get => _statusReason; internal set => Set(ref _statusReason, value ?? string.Empty); }
    public int Attempt { get => _attempt; internal set => Set(ref _attempt, value); }
    public string CurrentValue { get => _currentValue; internal set => Set(ref _currentValue, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string CurrentQuality { get => _currentQuality; internal set => Set(ref _currentQuality, string.IsNullOrWhiteSpace(value) ? "Unknown" : value); }
    public string CurrentSource { get => _currentSource; internal set => Set(ref _currentSource, string.IsNullOrWhiteSpace(value) ? "Unknown" : value); }

    [JsonIgnore]
    public string CurrentIedTimestamp
    {
        get => _currentIedTimestamp;
        internal set => Set(ref _currentIedTimestamp, string.IsNullOrWhiteSpace(value) ? "—" : value);
    }

    public bool IsComplete => State is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed;

    [JsonIgnore]
    public string StateText => State switch
    {
        IoTestPointState.NotStarted => "Not started",
        IoTestPointState.WaitingForBaseline => "Waiting baseline",
        IoTestPointState.WaitingForOffBaseline => "Waiting OFF",
        IoTestPointState.ArmedForOn => "Ready for ON",
        IoTestPointState.OnCaptured => "ON captured",
        IoTestPointState.Passed => "PASS",
        IoTestPointState.Review => "REVIEW",
        IoTestPointState.Failed => "FAIL",
        _ => State.ToString()
    };

    [JsonIgnore]
    public string OnRelayTimestampText => FormatRelayTimestamp(OnEvidence?.IedTimestamp);

    [JsonIgnore]
    public string OffRelayTimestampText => FormatRelayTimestamp(OffEvidence?.IedTimestamp);

    [JsonIgnore]
    public string OnEvidenceToolTip => BuildEvidenceToolTip(OnEvidence, "ON");

    [JsonIgnore]
    public string OffEvidenceToolTip => BuildEvidenceToolTip(OffEvidence, "OFF");

    [JsonIgnore]
    public string Value1RelayTimestampText => FormatRelayTimestamp(Value1Evidence?.IedTimestamp);

    [JsonIgnore]
    public string Value2RelayTimestampText => FormatRelayTimestamp(Value2Evidence?.IedTimestamp);

    [JsonIgnore]
    public string Value1EvidenceToolTip => BuildFatEvidenceToolTip(Value1Evidence, "Value 1");

    [JsonIgnore]
    public string Value2EvidenceToolTip => BuildFatEvidenceToolTip(Value2Evidence, "Value 2");

    internal void ApplyObservation(IoTestObservation observation)
    {
        CurrentValue = observation.RawValue;
        CurrentQuality = observation.Quality;
        CurrentSource = observation.AcquisitionSource;
        CurrentIedTimestamp = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            observation.IedTimestamp,
            "yyyy-MM-dd HH:mm:ss.fff zzz");
    }

    internal void SetFatValueEvidence(FatValueEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(evidence);
        if (evidence.Slot == FatValueSlot.Value1)
            Value1Evidence = evidence;
        else
            Value2Evidence = evidence;
    }

    private static string FormatRelayTimestamp(DateTimeOffset? value)
        => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            value,
            "yyyy-MM-dd HH:mm:ss.fff");

    private static string BuildEvidenceToolTip(IoTestTransitionEvidence? evidence, string label)
    {
        if (evidence == null)
            return $"{label} transition has not been captured.";

        var displayed = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            evidence.IedTimestamp,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            "not supplied");
        var rawRelay = evidence.IedTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "not supplied";
        var captured = evidence.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
        return $"Displayed (rounded to nearest ms): {displayed}\n" +
               $"Decoded IED timestamp (full precision): {rawRelay}\n" +
               $"ARSAS capture (full precision): {captured}\n" +
               $"Quality: {evidence.Quality}\nSource: {evidence.AcquisitionSource}\n" +
               $"{evidence.Verdict}: {evidence.VerdictReason}";
    }

    private static string BuildFatEvidenceToolTip(FatValueEvidence? evidence, string label)
    {
        if (evidence == null)
            return $"{label} has not been captured.";

        var displayed = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            evidence.IedTimestamp,
            "yyyy-MM-dd HH:mm:ss.fff zzz",
            "not supplied");
        return $"{label}: {evidence.RawValue}\n" +
               $"IED timestamp: {displayed}\n" +
               $"ARSAS capture: {evidence.CapturedAt:O}\n" +
               $"Quality: {evidence.Quality}\nSource: {evidence.AcquisitionSource}\n" +
               $"Capture: {evidence.CaptureKind}";
    }

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

public sealed class IoTestPointPlan : ObservableObject
{
    private bool _testEnabled = true;
    private FatSignalDisposition _fatDisposition = FatSignalDisposition.Included;
    private IoTestLiveBindingState _liveBindingState = IoTestLiveBindingState.NotEvaluated;
    private string _liveBindingReason = "Live binding has not been evaluated";
    private string _liveDeviceId = string.Empty;
    private string _liveSignalReference = string.Empty;

    public IoTestPointPlan()
    {
        Runtime.PropertyChanged += Runtime_PropertyChanged;
    }

    public required string TestPointId { get; init; }
    public required string IedName { get; init; }
    public required string IpAddress { get; init; }
    public required string SignalName { get; init; }
    public required string ObjectReference { get; init; }
    public required string FunctionalConstraint { get; init; }
    public required string ExpectedOnText { get; init; }
    public required string ExpectedOffText { get; init; }
    public int ExpectedOnRaw { get; init; } = 1;
    public int ExpectedOffRaw { get; init; }
    public string DataType { get; init; } = "SDI";
    public string SignalAddress { get; init; } = string.Empty;
    public string DataSetName { get; init; } = string.Empty;
    public string LogicalDevice { get; init; } = string.Empty;
    public string LogicalNode { get; init; } = string.Empty;
    public string DataObject { get; init; } = string.Empty;
    public string DataAttribute { get; init; } = string.Empty;
    public string Cdc { get; init; } = string.Empty;
    public string SourceIecReference { get; init; } = string.Empty;
    public string ReportDisplayReference { get; init; } = string.Empty;
    public string EventLogSearchReference { get; init; } = string.Empty;
    public string EvidenceExpected { get; init; } = string.Empty;
    public string MappingQuality { get; init; } = string.Empty;
    public string ReviewStatus { get; init; } = string.Empty;
    public string ReviewReason { get; init; } = string.Empty;
    public string EventLogMatch { get; init; } = string.Empty;
    public string EvidenceReference { get; init; } = string.Empty;
    public string ReviewerComment { get; init; } = string.Empty;
    public string SourceSheet { get; init; } = string.Empty;
    public int SourceRow { get; init; }
    public FatSignalKind SignalKind { get; init; } = FatSignalKind.Discrete;
    public FatCaptureMode CaptureMode { get; init; } = FatCaptureMode.AutomaticTransition;
    public bool TestEnabled
    {
        get => _testEnabled;
        set
        {
            if (!Set(ref _testEnabled, value))
                return;
            RaiseFatComputedProperties();
        }
    }
    public bool ImportReady { get; init; } = true;
    public string BindingStatus { get; init; } = string.Empty;
    public string BindingEvidence { get; init; } = string.Empty;
    public IoTestPointRuntime Runtime { get; } = new();

    [JsonInclude]
    public FatSignalDisposition FatDisposition
    {
        get => _fatDisposition;
        private set
        {
            if (!Set(ref _fatDisposition, value))
                return;
            RaiseFatComputedProperties();
        }
    }

    [JsonIgnore]
    public bool IsIncludedInFat => FatDisposition == FatSignalDisposition.Included;

    [JsonIgnore]
    public bool IsOperatorSnapshot => CaptureMode == FatCaptureMode.OperatorSnapshot;

    [JsonIgnore]
    public bool CanCaptureOperatorSnapshot =>
        IsOperatorSnapshot && IsIncludedInFat && TestEnabled && ImportReady && IsLiveBound;

    [JsonIgnore]
    public bool IsFatEvidenceComplete => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OnEvidence is not null && Runtime.OffEvidence is not null
        : Runtime.Value1Evidence is not null && Runtime.Value2Evidence is not null;

    [JsonIgnore]
    public string Value1Text => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OnEvidence?.RawValue ?? "—"
        : Runtime.Value1Evidence?.RawValue ?? "—";

    [JsonIgnore]
    public string Value2Text => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OffEvidence?.RawValue ?? "—"
        : Runtime.Value2Evidence?.RawValue ?? "—";

    [JsonIgnore]
    public string Value1RelayTimestampText => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OnRelayTimestampText
        : Runtime.Value1RelayTimestampText;

    [JsonIgnore]
    public string Value2RelayTimestampText => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OffRelayTimestampText
        : Runtime.Value2RelayTimestampText;

    [JsonIgnore]
    public string Value1EvidenceToolTip => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OnEvidenceToolTip
        : Runtime.Value1EvidenceToolTip;

    [JsonIgnore]
    public string Value2EvidenceToolTip => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.OffEvidenceToolTip
        : Runtime.Value2EvidenceToolTip;

    [JsonIgnore]
    public string FatStatusText
    {
        get
        {
            if (!IsIncludedInFat)
                return "REMOVED";
            if (CaptureMode == FatCaptureMode.AutomaticTransition)
                return Runtime.StateText;
            if (IsFatEvidenceComplete)
                return "COMPLETE";
            if (Runtime.Value1Evidence is not null || Runtime.Value2Evidence is not null)
                return "1 / 2 captured";
            return "Ready to capture";
        }
    }

    [JsonIgnore]
    public string FatResultText => CaptureMode == FatCaptureMode.AutomaticTransition
        ? Runtime.State switch
        {
            IoTestPointState.Passed => "✔ PASS",
            IoTestPointState.Review => "⚠ REVIEW",
            IoTestPointState.Failed => "✖ FAILED",
            _ => "—"
        }
        : IsFatEvidenceComplete ? "✔ COMPLETE" : "—";

    [JsonIgnore]
    public string ReportIecReference
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(EventLogSearchReference))
                return EventLogSearchReference.Trim();
            if (!string.IsNullOrWhiteSpace(SourceIecReference))
                return SourceIecReference.Trim();
            if (!string.IsNullOrWhiteSpace(ReportDisplayReference))
                return ReportDisplayReference.Trim();
            return ObjectReference;
        }
    }

    public IoTestLiveBindingState LiveBindingState
    {
        get => _liveBindingState;
        private set
        {
            if (Set(ref _liveBindingState, value))
            {
                Raise(nameof(IsLiveBound));
                Raise(nameof(LiveBindingText));
                Raise(nameof(CanCaptureOperatorSnapshot));
            }
        }
    }

    public string LiveBindingReason { get => _liveBindingReason; private set => Set(ref _liveBindingReason, value ?? string.Empty); }
    public string LiveDeviceId { get => _liveDeviceId; private set => Set(ref _liveDeviceId, value ?? string.Empty); }
    public string LiveSignalReference { get => _liveSignalReference; private set => Set(ref _liveSignalReference, value ?? string.Empty); }
    public bool IsLiveBound => LiveBindingState is IoTestLiveBindingState.BoundExact or IoTestLiveBindingState.BoundNormalized or IoTestLiveBindingState.LivePointReady;
    public string LiveBindingText => LiveBindingState switch
    {
        IoTestLiveBindingState.LivePointReady => "Live",
        IoTestLiveBindingState.BoundExact => "Model bound",
        IoTestLiveBindingState.BoundNormalized => "Model bound*",
        IoTestLiveBindingState.DeviceNotLoaded => "IED not loaded",
        IoTestLiveBindingState.SignalNotFound => "Signal missing",
        _ => "Not checked"
    };

    public void ApplyLiveBinding(
        IoTestLiveBindingState state,
        string reason,
        string? deviceId = null,
        string? signalReference = null)
    {
        LiveDeviceId = deviceId ?? string.Empty;
        LiveSignalReference = signalReference ?? string.Empty;
        LiveBindingReason = reason;
        LiveBindingState = state;
    }

    public void RemoveFromFat() => FatDisposition = FatSignalDisposition.ExcludedByOperator;

    public void RestoreToFat() => FatDisposition = FatSignalDisposition.Included;

    internal void RestoreFatDisposition(FatSignalDisposition disposition)
        => FatDisposition = disposition;

    private void Runtime_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestPointRuntime.OnEvidence) or
            nameof(IoTestPointRuntime.OffEvidence) or
            nameof(IoTestPointRuntime.Value1Evidence) or
            nameof(IoTestPointRuntime.Value2Evidence) or
            nameof(IoTestPointRuntime.State) or
            nameof(IoTestPointRuntime.StatusReason))
        {
            RaiseFatComputedProperties();
        }
    }

    private void RaiseFatComputedProperties()
    {
        Raise(nameof(IsIncludedInFat));
        Raise(nameof(CanCaptureOperatorSnapshot));
        Raise(nameof(IsFatEvidenceComplete));
        Raise(nameof(Value1Text));
        Raise(nameof(Value2Text));
        Raise(nameof(Value1RelayTimestampText));
        Raise(nameof(Value2RelayTimestampText));
        Raise(nameof(Value1EvidenceToolTip));
        Raise(nameof(Value2EvidenceToolTip));
        Raise(nameof(FatStatusText));
        Raise(nameof(FatResultText));
    }
}

public sealed class IoTestIedPlan : ObservableObject
{
    private string _liveDeviceId = string.Empty;
    private string _liveStatusText = "Not evaluated";
    private bool _isPreparing;
    private string _preparationStatusText = string.Empty;
    private bool _isLiveConnected;
    private bool _isLiveMonitoring;
    private bool _notificationsInitialized;

    public required string IedName { get; init; }
    public required string IpAddress { get; init; }
    public string IedRole { get; init; } = string.Empty;
    public string Location { get; init; } = string.Empty;
    public string VoltageLevel { get; init; } = string.Empty;
    public string Switchgear { get; init; } = string.Empty;
    public List<IoTestPointPlan> TestPoints { get; init; } = new();

    public string LatestComtradeFiles { get; set; } = string.Empty;
    public string LatestComtradeRemotePath { get; set; } = string.Empty;
    public string LatestComtradeCompleteness { get; set; } = string.Empty;
    public string LatestComtradeAcquisitionSource { get; set; } = string.Empty;
    public DateTimeOffset? LatestComtradeModifiedAtUtc { get; set; }
    public DateTimeOffset? LatestComtradeCapturedAtUtc { get; set; }
    public int LatestComtradeFileCount { get; set; }
    public long LatestComtradeKnownSizeBytes { get; set; }

    [JsonIgnore]
    public bool HasRemoteComtradeEvidence => !string.IsNullOrWhiteSpace(LatestComtradeFiles);

    public string LiveDeviceId { get => _liveDeviceId; private set => Set(ref _liveDeviceId, value ?? string.Empty); }
    public string LiveStatusText { get => _liveStatusText; private set => Set(ref _liveStatusText, value ?? string.Empty); }

    [JsonIgnore]
    public bool IsPreparing
    {
        get => _isPreparing;
        private set
        {
            if (!Set(ref _isPreparing, value))
                return;
            Raise(nameof(CardStateText));
            Raise(nameof(ConnectionActionText));
            Raise(nameof(CanPrepareConnection));
        }
    }

    [JsonIgnore]
    public string PreparationStatusText
    {
        get => _preparationStatusText;
        private set => Set(ref _preparationStatusText, value?.Trim() ?? string.Empty);
    }

    [JsonIgnore]
    public bool IsLiveConnected
    {
        get => _isLiveConnected;
        private set
        {
            if (!Set(ref _isLiveConnected, value))
                return;
            Raise(nameof(CardStateText));
            Raise(nameof(ConnectionActionText));
        }
    }

    [JsonIgnore]
    public bool IsLiveMonitoring
    {
        get => _isLiveMonitoring;
        private set
        {
            if (!Set(ref _isLiveMonitoring, value))
                return;
            Raise(nameof(CardStateText));
            Raise(nameof(ConnectionActionText));
        }
    }

    [JsonIgnore]
    public string CardStateText => IsPreparing ? "CONNECTING" : IsLiveMonitoring ? "LIVE" : IsLiveConnected ? "READY" : "OFFLINE";

    [JsonIgnore]
    public string ConnectionActionText => IsPreparing
        ? "Connecting…"
        : IsLiveMonitoring
            ? "Refresh"
            : IsLiveConnected
                ? "Prepare"
                : "Connect";

    [JsonIgnore]
    public bool CanPrepareConnection => !IsPreparing;

    public int EnabledCount => TestPoints.Count(point => point.IsIncludedInFat && point.TestEnabled);
    public int CompleteCount => TestPoints.Count(point => point.IsIncludedInFat && point.TestEnabled && point.IsFatEvidenceComplete);
    public int PassedCount => TestPoints.Count(point => point.IsIncludedInFat && point.TestEnabled && point.Runtime.State == IoTestPointState.Passed);
    public int ReviewCount => TestPoints.Count(point => point.IsIncludedInFat && point.TestEnabled && point.Runtime.State == IoTestPointState.Review);
    public int BoundCount => TestPoints.Count(point => point.IsIncludedInFat && point.IsLiveBound);
    public int RemovedCount => TestPoints.Count(point => !point.IsIncludedInFat);
    public int PendingCount => Math.Max(0, EnabledCount - CompleteCount);

    public void ApplyLiveDeviceBinding(
        string? deviceId,
        string status,
        bool isConnected = false,
        bool isMonitoring = false)
    {
        LiveDeviceId = deviceId ?? string.Empty;
        LiveStatusText = status;
        IsLiveConnected = isConnected;
        IsLiveMonitoring = isMonitoring;
        RaiseProgressProperties();
    }

    public void SetPreparationState(bool isPreparing, string? status = null)
    {
        IsPreparing = isPreparing;
        if (status != null)
            PreparationStatusText = status;
        if (!isPreparing && string.IsNullOrWhiteSpace(status))
            PreparationStatusText = string.Empty;
        Raise(nameof(CardStateText));
        Raise(nameof(ConnectionActionText));
        Raise(nameof(CanPrepareConnection));
    }

    public void InitializeRuntimeNotifications()
    {
        if (_notificationsInitialized)
            return;
        _notificationsInitialized = true;

        foreach (var point in TestPoints)
        {
            point.PropertyChanged += Point_PropertyChanged;
            point.Runtime.PropertyChanged += PointRuntime_PropertyChanged;
        }
        RaiseProgressProperties();
    }

    private void Point_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestPointPlan.TestEnabled) or nameof(IoTestPointPlan.LiveBindingState) or nameof(IoTestPointPlan.FatDisposition))
            RaiseProgressProperties();
    }

    private void PointRuntime_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestPointRuntime.State) or
            nameof(IoTestPointRuntime.OnEvidence) or
            nameof(IoTestPointRuntime.OffEvidence) or
            nameof(IoTestPointRuntime.Value1Evidence) or
            nameof(IoTestPointRuntime.Value2Evidence))
        {
            RaiseProgressProperties();
        }
    }

    private void RaiseProgressProperties()
    {
        Raise(nameof(EnabledCount));
        Raise(nameof(CompleteCount));
        Raise(nameof(PassedCount));
        Raise(nameof(ReviewCount));
        Raise(nameof(BoundCount));
        Raise(nameof(RemovedCount));
        Raise(nameof(PendingCount));
    }
}

public sealed class IoFatDocumentControl
{
    public string ClientProject { get; init; } = string.Empty;
    public string SupplierName { get; init; } = string.Empty;
    public string PurchaseOrderTitle { get; init; } = string.Empty;
    public string PurchaserDocumentNumber { get; init; } = string.Empty;
    public string CompanyProjectDocumentNumber { get; init; } = string.Empty;
    public string DocumentTitle { get; init; } = string.Empty;
    public string Revision { get; init; } = string.Empty;
    public string IssueStatus { get; init; } = string.Empty;
    public string SourceDocumentName { get; init; } = string.Empty;
}

public sealed class IoTestProject
{
    public required string ProjectId { get; init; }
    public required string SchemaVersion { get; init; }
    public required string ProjectName { get; init; }
    public string SourceWorkbookName { get; init; } = string.Empty;
    public string SourceWorkbookSha256 { get; init; } = string.Empty;
    public DateTimeOffset ImportedAt { get; init; } = DateTimeOffset.UtcNow;
    public IoFatDocumentControl DocumentControl { get; init; } = new();
    public List<IoTestIedPlan> Ieds { get; init; } = new();

    [JsonInclude]
    public List<IoFatSourceDescriptor> Sources { get; private set; } = new();

    [JsonInclude]
    public string SourceSetSha256 { get; private set; } = string.Empty;

    public int SignalCount => Ieds.Sum(ied => ied.TestPoints.Count);
    public int IncludedSignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.IsIncludedInFat));
    public int RemovedSignalCount => SignalCount - IncludedSignalCount;
    public int ReadySignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.IsIncludedInFat && point.ImportReady));
    public int LiveBoundSignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.IsIncludedInFat && point.IsLiveBound));

    public void SetSources(IEnumerable<IoFatSourceDescriptor> sources, string sourceSetSha256)
    {
        ArgumentNullException.ThrowIfNull(sources);
        Sources = sources.ToList();
        SourceSetSha256 = sourceSetSha256?.Trim().ToLowerInvariant() ?? string.Empty;
    }

    public void InitializeRuntimeNotifications()
    {
        foreach (var ied in Ieds)
            ied.InitializeRuntimeNotifications();
    }
}