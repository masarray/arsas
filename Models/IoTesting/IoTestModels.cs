using System.ComponentModel;
using System.Text.Json.Serialization;

namespace ArIED61850Tester.Models.IoTesting;

public enum IoTestPointState
{
    Pending,
    WaitingForOn,
    WaitingForOff,
    Passed,
    Review,
    Failed,
    Disabled
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

public enum IoEvidenceTransition
{
    Baseline,
    On,
    Off
}

public enum IoEvidenceVerdict
{
    Accepted,
    Rejected,
    Review
}

public sealed class IoTestEvidence
{
    public IoEvidenceTransition Transition { get; init; }
    public IoEvidenceVerdict Verdict { get; init; }
    public string Value { get; init; } = string.Empty;
    public DateTimeOffset PcTimestamp { get; init; }
    public DateTimeOffset? IedTimestamp { get; init; }
    public string Quality { get; init; } = string.Empty;
    public string Source { get; init; } = string.Empty;
    public long SourceSequence { get; init; }
    public long ConnectionGeneration { get; init; }
    public string Reason { get; init; } = string.Empty;
}

public sealed class IoTestPointRuntime : ObservableObject
{
    private IoTestPointState _state = IoTestPointState.Pending;
    private string _currentValue = "—";
    private string _currentQuality = "—";
    private string _currentSource = "—";
    private string _currentIedTimestamp = "—";
    private string _statusReason = "Waiting to start";
    private IoTestEvidence? _onEvidence;
    private IoTestEvidence? _offEvidence;

    public IoTestPointState State
    {
        get => _state;
        set
        {
            if (Set(ref _state, value))
            {
                Raise(nameof(StateText));
                Raise(nameof(IsComplete));
            }
        }
    }

    public string CurrentValue { get => _currentValue; set => Set(ref _currentValue, value ?? "—"); }
    public string CurrentQuality { get => _currentQuality; set => Set(ref _currentQuality, value ?? "—"); }
    public string CurrentSource { get => _currentSource; set => Set(ref _currentSource, value ?? "—"); }
    public string CurrentIedTimestamp { get => _currentIedTimestamp; set => Set(ref _currentIedTimestamp, value ?? "—"); }
    public string StatusReason { get => _statusReason; set => Set(ref _statusReason, value ?? string.Empty); }
    public IoTestEvidence? OnEvidence { get => _onEvidence; set { if (Set(ref _onEvidence, value)) RaiseEvidenceProperties(); } }
    public IoTestEvidence? OffEvidence { get => _offEvidence; set { if (Set(ref _offEvidence, value)) RaiseEvidenceProperties(); } }

    public string StateText => State switch
    {
        IoTestPointState.Pending => "PENDING",
        IoTestPointState.WaitingForOn => "WAIT ON",
        IoTestPointState.WaitingForOff => "WAIT OFF",
        IoTestPointState.Passed => "PASS",
        IoTestPointState.Review => "REVIEW",
        IoTestPointState.Failed => "FAILED",
        IoTestPointState.Disabled => "DISABLED",
        _ => State.ToString().ToUpperInvariant()
    };

    public bool IsComplete => State is IoTestPointState.Passed or IoTestPointState.Review or IoTestPointState.Failed;
    public string OnRelayTimestampText => EvidenceTimestampText(OnEvidence);
    public string OffRelayTimestampText => EvidenceTimestampText(OffEvidence);
    public string OnEvidenceToolTip => EvidenceToolTip(OnEvidence);
    public string OffEvidenceToolTip => EvidenceToolTip(OffEvidence);

    private static string EvidenceTimestampText(IoTestEvidence? evidence)
        => evidence?.IedTimestamp?.ToString("yyyy-MM-dd HH:mm:ss.fff zzz") ?? "—";

    private static string EvidenceToolTip(IoTestEvidence? evidence)
    {
        if (evidence == null)
            return "No evidence captured";
        var relay = evidence.IedTimestamp?.ToString("O") ?? "unavailable";
        return $"Relay: {relay}\nPC: {evidence.PcTimestamp:O}\nQuality: {evidence.Quality}\nSource: {evidence.Source}\nVerdict: {evidence.Verdict}\n{evidence.Reason}";
    }

    private void RaiseEvidenceProperties()
    {
        Raise(nameof(OnRelayTimestampText));
        Raise(nameof(OffRelayTimestampText));
        Raise(nameof(OnEvidenceToolTip));
        Raise(nameof(OffEvidenceToolTip));
    }
}

public sealed class IoTestPointPlan : ObservableObject
{
    private bool _testEnabled;
    private IoTestLiveBindingState _liveBindingState;
    private string _liveBindingReason = string.Empty;
    private string _liveDeviceId = string.Empty;
    private string _liveSignalReference = string.Empty;

    public required string TestPointId { get; init; }
    public required string IedName { get; init; }
    public required string IpAddress { get; init; }
    public required string SignalName { get; init; }
    public string Description { get; init; } = string.Empty;
    public string ObjectReference { get; init; } = string.Empty;
    public string FunctionalConstraint { get; init; } = string.Empty;
    public string ExpectedOnText { get; init; } = string.Empty;
    public string ExpectedOffText { get; init; } = string.Empty;
    public string SignalType { get; init; } = string.Empty;
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
