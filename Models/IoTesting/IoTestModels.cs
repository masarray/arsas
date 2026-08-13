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

    internal void ApplyObservation(IoTestObservation observation)
    {
        CurrentValue = observation.RawValue;
        CurrentQuality = observation.Quality;
        CurrentSource = observation.AcquisitionSource;
        CurrentIedTimestamp = global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            observation.IedTimestamp,
            "yyyy-MM-dd HH:mm:ss.fff zzz");
    }

    private static string FormatRelayTimestamp(DateTimeOffset? value)
        => global::ArIED61850Tester.Iec61850TimestampPresentation.FormatMilliseconds(
            value,
            "yyyy-MM-dd HH:mm:ss.fff");

    private static string BuildEvidenceToolTip(IoTestTransitionEvidence? evidence, string label)
    {
        if (evidence == null)
            return $"{label} transition has not been captured.";

        var relay = evidence.IedTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "not supplied";
        var captured = evidence.CapturedAt.ToString("O", CultureInfo.InvariantCulture);
        return $"Relay timestamp: {relay}\nARSAS capture: {captured}\nQuality: {evidence.Quality}\nSource: {evidence.AcquisitionSource}\n{evidence.Verdict}: {evidence.VerdictReason}";
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
    private IoTestLiveBindingState _liveBindingState = IoTestLiveBindingState.NotEvaluated;
    private string _liveBindingReason = "Live binding has not been evaluated";
    private string _liveDeviceId = string.Empty;
    private string _liveSignalReference = string.Empty;

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
    public bool TestEnabled { get => _testEnabled; set => Set(ref _testEnabled, value); }
    public bool ImportReady { get; init; } = true;
    public string BindingStatus { get; init; } = string.Empty;
    public string BindingEvidence { get; init; } = string.Empty;
    public IoTestPointRuntime Runtime { get; } = new();

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

    // Persisted IED-level file-service evidence. A successful IEC 61850 FileDirectory
    // listing is sufficient FAT evidence that the remote file service is accessible;
    // FileOpen/FileRead download remains optional deeper verification.
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

    public int EnabledCount => TestPoints.Count(point => point.TestEnabled);
    public int PassedCount => TestPoints.Count(point => point.Runtime.State == IoTestPointState.Passed);
    public int ReviewCount => TestPoints.Count(point => point.Runtime.State == IoTestPointState.Review);
    public int BoundCount => TestPoints.Count(point => point.IsLiveBound);
    public int PendingCount => Math.Max(0, EnabledCount - PassedCount - ReviewCount);

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
        if (e.PropertyName is nameof(IoTestPointPlan.TestEnabled) or nameof(IoTestPointPlan.LiveBindingState))
            RaiseProgressProperties();
    }

    private void PointRuntime_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(IoTestPointRuntime.State) or nameof(IoTestPointRuntime.OnEvidence) or nameof(IoTestPointRuntime.OffEvidence))
            RaiseProgressProperties();
    }

    private void RaiseProgressProperties()
    {
        Raise(nameof(EnabledCount));
        Raise(nameof(PassedCount));
        Raise(nameof(ReviewCount));
        Raise(nameof(BoundCount));
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

    public int SignalCount => Ieds.Sum(ied => ied.TestPoints.Count);
    public int ReadySignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.ImportReady));
    public int LiveBoundSignalCount => Ieds.Sum(ied => ied.TestPoints.Count(point => point.IsLiveBound));

    public void InitializeRuntimeNotifications()
    {
        foreach (var ied in Ieds)
            ied.InitializeRuntimeNotifications();
    }
}
