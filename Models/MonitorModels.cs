using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Workspace;

namespace ArIED61850Tester.Models;

public sealed class Iec61850MonitorDevice : ObservableObject
{
    private string _name = "IED";
    private string _identitySource = string.Empty;
    private string _logicalDeviceSummary = string.Empty;
    private string _ipAddress = "192.168.1.10";
    private int _port = 102;
    private string _status = "Disconnected";
    private string _detail = "Connect the endpoint to discover its real IEC 61850 IEDName.";
    private string _acquisitionMode = "Not started";
    private bool _isConnected;
    private bool _isBusy;
    private bool _isMonitoring;
    private bool _isActive;
    private bool _hasReportStream;
    private bool _isDemo;
    private bool _reportPulseActive;
    private string _busyStage = "Preparing IEC 61850 session…";
    private double _discoveryProgressPercent;
    private double _discoveryProgressTargetPercent;
    private string _discoveryProgressStepText = "Step 0 of 15";
    private string _discoveryProgressPercentText = "0%";
    private int _selectedSignalCount;
    private int _selectedLiveSignalCount;
    private int _selectedControlSignalCount;
    private bool _isBulkSignalSelectionUpdate;
    private bool _hasDiscoveryCache;
    private string _busyTitle = "Discovering IEC 61850 IED";
    private int _unreadEventCount;
    private SclIedWorkspace? _sclWorkspace;
    private LiveIedModelDiscoveryDocument? _liveDiscoveryModel;
    private SclLiveModelComparisonResult? _sclComparison;
    private string _sclSourcePath = string.Empty;
    private string _sclSourceSha256 = string.Empty;
    private string _sclIedName = string.Empty;
    private string _sclAccessPointName = string.Empty;

    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public BulkObservableCollection<SignalDefinition> Signals { get; } = new();
    public BulkObservableCollection<Iec61850MonitorPoint> Points { get; } = new();
    public BulkObservableCollection<SignalDefinition> CommandSignals { get; } = new();
    public Iec61850DeviceDiagnosticSnapshot LastDiagnosticSnapshot { get; set; } = new();

    public SclIedWorkspace? SclWorkspace
    {
        get => _sclWorkspace;
        set
        {
            if (ReferenceEquals(_sclWorkspace, value)) return;
            _sclWorkspace = value;
            RefreshComputed();
        }
    }

    public LiveIedModelDiscoveryDocument? LiveDiscoveryModel
    {
        get => _liveDiscoveryModel;
        set
        {
            if (ReferenceEquals(_liveDiscoveryModel, value)) return;
            _liveDiscoveryModel = value;
            RefreshComputed();
        }
    }

    public SclLiveModelComparisonResult? SclComparison
    {
        get => _sclComparison;
        set
        {
            if (ReferenceEquals(_sclComparison, value)) return;
            _sclComparison = value;
            RefreshComputed();
        }
    }

    public string SclSourcePath
    {
        get => _sclSourcePath;
        set => Set(ref _sclSourcePath, value?.Trim() ?? string.Empty);
    }

    public string SclSourceSha256
    {
        get => _sclSourceSha256;
        set => Set(ref _sclSourceSha256, value?.Trim() ?? string.Empty);
    }

    public string SclIedName
    {
        get => _sclIedName;
        set => Set(ref _sclIedName, value?.Trim() ?? string.Empty);
    }

    public string SclAccessPointName
    {
        get => _sclAccessPointName;
        set => Set(ref _sclAccessPointName, value?.Trim() ?? string.Empty);
    }

    public bool HasSclDesignModel => SclWorkspace != null || !string.IsNullOrWhiteSpace(SclSourceSha256);
    public bool RequiresEndpointBinding => HasSclDesignModel && string.IsNullOrWhiteSpace(IpAddress);
    public bool HasSclConfigurationDrift => SclComparison?.RequiresFullDiscovery == true;
    public string SclVerificationText => !HasSclDesignModel
        ? string.Empty
        : SclComparison == null
            ? IsConnected ? "SCL associated • full model unverified" : "SCL offline model"
            : SclComparison.IsCompatible
                ? "SCL verified against live model"
                : $"SCL drift • {SclComparison.BlockingFindingCount} blocking finding(s)";

    // Smart Auto reporting: existing static RCB/DataSet first, temporary association-
    // scoped dynamic DataSet/URCB second, MMS polling only when reporting cannot be armed.
    public bool AllowDynamicDataSetWrites { get; set; } = true;

    /// <summary>
    /// True when the complete signal workspace came from a successful discovery and
    /// can be persisted in an ArIED project for fast reconnect without a full scan.
    /// </summary>
    public bool HasDiscoveryCache
    {
        get => _hasDiscoveryCache;
        set
        {
            if (Set(ref _hasDiscoveryCache, value))
                RefreshComputed();
        }
    }


    public int UnreadEventCount
    {
        get => _unreadEventCount;
        private set
        {
            if (Set(ref _unreadEventCount, Math.Max(0, value)))
            {
                Raise(nameof(HasUnreadEvents));
                Raise(nameof(UnreadEventText));
            }
        }
    }

    public bool HasUnreadEvents => UnreadEventCount > 0;
    public string UnreadEventText => UnreadEventCount > 99 ? "99+" : UnreadEventCount.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void AddUnreadEvents(int count)
    {
        if (count > 0)
            UnreadEventCount += count;
    }

    public void ClearUnreadEvents()
        => UnreadEventCount = 0;

    public string BusyTitle
    {
        get => _busyTitle;
        set => Set(ref _busyTitle, string.IsNullOrWhiteSpace(value) ? "Working with IEC 61850 IED" : value.Trim());
    }

    public string Name
    {
        get => _name;
        set
        {
            if (Set(ref _name, string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim()))
                RefreshComputed();
        }
    }

    public string IdentitySource
    {
        get => _identitySource;
        set
        {
            if (Set(ref _identitySource, value?.Trim() ?? string.Empty))
                RefreshComputed();
        }
    }

    public string LogicalDeviceSummary
    {
        get => _logicalDeviceSummary;
        set
        {
            if (Set(ref _logicalDeviceSummary, value?.Trim() ?? string.Empty))
                RefreshComputed();
        }
    }

    public string IpAddress
    {
        get => _ipAddress;
        set
        {
            if (Set(ref _ipAddress, value?.Trim() ?? string.Empty))
                RefreshComputed();
        }
    }

    public int Port
    {
        get => _port;
        set
        {
            if (Set(ref _port, value <= 0 ? 102 : value))
                RefreshComputed();
        }
    }

    public string Status
    {
        get => _status;
        set
        {
            if (Set(ref _status, string.IsNullOrWhiteSpace(value) ? "Disconnected" : value))
                RefreshComputed();
        }
    }

    public string Detail
    {
        get => _detail;
        set
        {
            if (Set(ref _detail, value ?? string.Empty))
                RefreshComputed();
        }
    }

    public string AcquisitionMode
    {
        get => _acquisitionMode;
        set
        {
            if (Set(ref _acquisitionMode, string.IsNullOrWhiteSpace(value) ? "Not started" : value))
                RefreshComputed();
        }
    }

    public bool IsConnected
    {
        get => _isConnected;
        set
        {
            if (Set(ref _isConnected, value))
                RefreshComputed();
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set
        {
            if (Set(ref _isBusy, value))
                RefreshComputed();
        }
    }

    public bool IsMonitoring
    {
        get => _isMonitoring;
        set
        {
            if (Set(ref _isMonitoring, value))
                RefreshComputed();
        }
    }

    public bool IsActive
    {
        get => _isActive;
        set => Set(ref _isActive, value);
    }

    public bool HasReportStream
    {
        get => _hasReportStream;
        set
        {
            if (Set(ref _hasReportStream, value))
                RefreshComputed();
        }
    }

    public bool IsDemo
    {
        get => _isDemo;
        set
        {
            if (Set(ref _isDemo, value))
                RefreshComputed();
        }
    }

    public bool ReportPulseActive
    {
        get => _reportPulseActive;
        set => Set(ref _reportPulseActive, value);
    }

    public string BusyStage
    {
        get => _busyStage;
        set => Set(ref _busyStage, string.IsNullOrWhiteSpace(value) ? "Working…" : value.Trim());
    }

    public double DiscoveryProgressPercent
    {
        get => _discoveryProgressPercent;
        private set => Set(ref _discoveryProgressPercent, Math.Clamp(value, 0d, 100d));
    }

    public string DiscoveryProgressStepText
    {
        get => _discoveryProgressStepText;
        private set => Set(ref _discoveryProgressStepText, value ?? string.Empty);
    }

    public string DiscoveryProgressPercentText
    {
        get => _discoveryProgressPercentText;
        private set => Set(ref _discoveryProgressPercentText, value ?? string.Empty);
    }

    public void ResetDiscoveryProgress(
        string message = "Preparing IEC 61850 session…",
        string title = "Discovering IED")
    {
        BusyTitle = title;
        BusyStage = message;
        _discoveryProgressTargetPercent = 0d;
        DiscoveryProgressPercent = 0d;
        DiscoveryProgressStepText = "Step 0 of 15";
        DiscoveryProgressPercentText = "0%";
    }

    public void ApplyDiscoveryProgress(IedDiscoveryProgress progress)
    {
        ArgumentNullException.ThrowIfNull(progress);
        BusyStage = progress.Message;

        // Discovery milestones can arrive in large jumps. Keep the real target separate
        // from the displayed value so every IED card glides independently.
        _discoveryProgressTargetPercent = Math.Max(
            _discoveryProgressTargetPercent,
            progress.NormalizedPercent);
        DiscoveryProgressStepText = progress.StepText;
    }

    public bool AdvanceDiscoveryProgressAnimation()
    {
        var remaining = _discoveryProgressTargetPercent - DiscoveryProgressPercent;
        if (remaining <= 0.04d)
        {
            if (remaining > 0d)
                DiscoveryProgressPercent = _discoveryProgressTargetPercent;
            DiscoveryProgressPercentText = $"{DiscoveryProgressPercent:0}%";
            return false;
        }

        // 20 FPS UI timer: eased movement with a bounded speed. Normal milestones
        // move slowly; the final completion phase can glide slightly faster so the
        // overlay reaches 100% without an abrupt last-frame jump.
        var isCompleting = _discoveryProgressTargetPercent >= 99.9d;
        var movement = Math.Clamp(
            remaining * (isCompleting ? 0.18d : 0.14d),
            0.12d,
            isCompleting ? 3.0d : 1.5d);
        DiscoveryProgressPercent = Math.Min(
            _discoveryProgressTargetPercent,
            DiscoveryProgressPercent + movement);
        DiscoveryProgressPercentText = $"{DiscoveryProgressPercent:0}%";
        return true;
    }

    public void CompleteDiscoveryProgressAnimation()
    {
        _discoveryProgressTargetPercent = 100d;
        DiscoveryProgressPercent = 100d;
        DiscoveryProgressPercentText = "100%";
    }

    public void MarkDiscoveryFailed(string message)
    {
        BusyStage = string.IsNullOrWhiteSpace(message) ? "Connection failed" : message.Trim();
        _discoveryProgressTargetPercent = DiscoveryProgressPercent;
        DiscoveryProgressPercentText = $"{DiscoveryProgressPercent:0}%";
    }

    public string EndpointText => string.IsNullOrWhiteSpace(IpAddress) ? "No endpoint" : $"{IpAddress}:{Port}";
    public int SignalCount => Signals.Count;
    public int SelectedSignalCount => _selectedSignalCount;
    public int SelectedLiveSignalCount => _selectedLiveSignalCount;
    public int SelectedControlSignalCount => _selectedControlSignalCount;
    public bool IsBulkSignalSelectionUpdate => _isBulkSignalSelectionUpdate;
    public int PointCount => Points.Count;
    public bool IsActionEnabled => !IsBusy && !IsDemo;
    public bool CanEditSignals => SignalCount > 0 && !IsBusy && !IsMonitoring && !IsDemo;
    public bool CanStartMonitor => IsConnected && SelectedLiveSignalCount > 0 && !IsBusy && !IsDemo;
    public bool CanStartOrStopMonitor => !IsBusy && !IsDemo && (IsMonitoring || SelectedLiveSignalCount > 0);
    public bool CanPlayAction => !IsBusy && !IsDemo && (!IsConnected || (!IsMonitoring && SelectedLiveSignalCount > 0));
    public bool CanStopAction => !IsBusy && !IsDemo && (IsConnected || IsMonitoring);
    public bool CanRescan => !IsBusy && !IsMonitoring && !IsDemo;
    public bool CanSaveScl => !IsBusy && !IsDemo && (SclWorkspace != null || LiveDiscoveryModel != null);
    public string SaveSclToolTip => SclWorkspace != null
        ? $"Save {Name} from the opened SCL design model"
        : LiveDiscoveryModel != null
            ? $"Save {Name} from the last successful live MMS discovery"
            : HasDiscoveryCache
                ? $"Run Re-scan for {Name} to capture a complete live model before saving SCL"
                : $"Open SCL or complete live discovery for {Name} before saving";
    public string CacheStateText => HasSclDesignModel
        ? $"SCL design model • {SignalCount:N0} signals"
        : HasDiscoveryCache
            ? $"Saved live model • {SignalCount:N0} signals"
            : "No cached model • discovery required";
    public string ReadyWorkspaceTitle => HasSclDesignModel
        ? RequiresEndpointBinding ? "SCL model ready — endpoint required" : "SCL design model is ready"
        : HasDiscoveryCache ? "Saved IED model is ready" : "IED is ready";
    public string ReadyWorkspaceMessage => HasSclDesignModel
        ? RequiresEndpointBinding
            ? "The LD/LN/DO/DA model is available offline. Press Play to bind an MMS endpoint before connecting."
            : SelectedLiveSignalCount > 0
                ? $"{SelectedLiveSignalCount:N0} SCL signal(s) are selected. Play performs a fast MMS association; Re-scan compares the complete live model."
                : "Browse and choose signals offline. Play associates without repeating full discovery; Re-scan performs design-versus-live verification."
        : HasDiscoveryCache && SelectedLiveSignalCount > 0
            ? $"{SelectedLiveSignalCount:N0} saved live signal(s) are selected. Use Play or Connect All for immediate live values without a full discovery scan."
            : HasDiscoveryCache
                ? "The complete signal model is restored. Choose signals offline; Apply & Start Live connects and starts monitoring automatically."
                : "Choose signals in the wizard; Apply & Start Live begins monitoring automatically.";
    public string ConnectionActionLabel => IsBusy ? "Working…" : IsConnected ? "Disconnect" : "Connect";
    public string MonitorActionLabel => IsBusy ? "Working…" : IsMonitoring ? "Stop Monitor" : "Start Monitor";
    public string ActivityText => IsBusy ? "Working…" : IsMonitoring ? "Monitoring" : Status;
    public string SummaryText => $"{SignalCount} scanned • {SelectedSignalCount} selected • {PointCount} live";
    public string IdentityText => string.IsNullOrWhiteSpace(LogicalDeviceSummary)
        ? EndpointText
        : HasSclDesignModel
            ? $"{EndpointText} • {LogicalDeviceSummary}"
            : $"{EndpointText} • LD {LogicalDeviceSummary}";
    public string ConnectionGlyph => IsBusy ? "…" : IsConnected ? "⏻" : "↗";
    public string ConnectionToolTip => IsBusy
        ? "IED connection operation is running"
        : IsConnected
            ? $"Disconnect {Name}"
            : HasSclDesignModel
                ? $"Connect {Name} using the SCL design model"
                : $"Connect and discover {EndpointText}";
    public string MonitorGlyph => IsBusy ? "…" : IsMonitoring ? "■" : "▶";
    public string MonitorToolTip => IsBusy
        ? "IED session operation is running"
        : IsMonitoring
            ? $"Stop monitoring {Name}"
            : SelectedLiveSignalCount == 0
                ? $"Choose signals for {Name}"
                : $"Start monitoring {Name}";
    public string ConfigureToolTip => IsMonitoring
        ? $"Stop monitoring {Name} before changing its signal selection"
        : $"Open signal selection wizard for {Name}";
    public string PlayToolTip => !IsConnected
        ? RequiresEndpointBinding
            ? $"Bind an MMS endpoint for {Name}, then fast-connect from the SCL design model"
            : HasSclDesignModel
                ? $"Fast-connect {Name} from the SCL design model; use Re-scan for full live comparison"
                : HasDiscoveryCache
                    ? $"Fast-connect {Name} from the saved model and start its selected live values"
                    : $"Connect and discover {EndpointText}"
        : IsMonitoring
            ? $"{Name} is already monitoring"
            : SelectedLiveSignalCount == 0
                ? $"Choose signals for {Name} first"
                : $"Start monitoring {Name}";
    public string StopToolTip => IsMonitoring
        ? $"Stop monitoring {Name}"
        : IsConnected
            ? $"Disconnect {Name}"
            : $"{Name} is already disconnected";
    public string RemoveToolTip => $"Remove {Name} from the workspace";

    public void BeginBulkSignalSelection()
        => _isBulkSignalSelectionUpdate = true;

    public void EndBulkSignalSelection()
    {
        _isBulkSignalSelectionUpdate = false;
        RecountSelectedSignals();
    }

    public void ApplySignalSelectionChange(SignalDefinition signal, bool isSelected)
    {
        ArgumentNullException.ThrowIfNull(signal);
        var delta = isSelected ? 1 : -1;
        _selectedSignalCount = Math.Clamp(_selectedSignalCount + delta, 0, Signals.Count);
        if (signal.IsValidControlObject)
            _selectedControlSignalCount = Math.Clamp(_selectedControlSignalCount + delta, 0, Signals.Count);
        else if (signal.CanPublishAsSignal)
            _selectedLiveSignalCount = Math.Clamp(_selectedLiveSignalCount + delta, 0, Signals.Count);
        RefreshCommandSignals();
        RefreshComputed();
    }

    public void RecountSelectedSignals()
    {
        _selectedSignalCount = Signals.Count(signal =>
            signal.IsSelected && (!signal.IsControlSignal || signal.IsValidControlObject));
        _selectedLiveSignalCount = Signals.Count(signal => signal.IsSelected && signal.CanPublishAsSignal);
        _selectedControlSignalCount = Signals.Count(signal => signal.IsSelected && signal.IsValidControlObject);
        RefreshCommandSignals();
        RefreshComputed();
    }

    public void RefreshCommandSignalProjection()
        => RefreshCommandSignals();

    private void RefreshCommandSignals()
    {
        // The Command Panel is an operating surface, not a second signal browser.
        // Keep unresolved, StatusOnly, unsupported, and feedback-only objects out so
        // the panel contains only actions the connected IED has proven executable.
        var selected = Signals
            .Where(signal => signal.IsSelected && signal.IsValidControlObject)
            .Where(signal => signal.ControlModelResolved && signal.ControlSupportsOperate)
            .Where(signal => !signal.IsGenericControl)
            .OrderBy(signal => signal.SortPriority)
            .ThenBy(signal => signal.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        CommandSignals.ReplaceAll(selected);
    }

    public void RefreshComputed()
    {
        Raise(nameof(EndpointText));
        Raise(nameof(SignalCount));
        Raise(nameof(SelectedSignalCount));
        Raise(nameof(SelectedLiveSignalCount));
        Raise(nameof(SelectedControlSignalCount));
        Raise(nameof(PointCount));
        Raise(nameof(IsActionEnabled));
        Raise(nameof(CanEditSignals));
        Raise(nameof(CanStartMonitor));
        Raise(nameof(CanStartOrStopMonitor));
        Raise(nameof(CanPlayAction));
        Raise(nameof(CanStopAction));
        Raise(nameof(CanRescan));
        Raise(nameof(CanSaveScl));
        Raise(nameof(SaveSclToolTip));
        Raise(nameof(CacheStateText));
        Raise(nameof(HasSclDesignModel));
        Raise(nameof(RequiresEndpointBinding));
        Raise(nameof(HasSclConfigurationDrift));
        Raise(nameof(SclVerificationText));
        Raise(nameof(ReadyWorkspaceTitle));
        Raise(nameof(ReadyWorkspaceMessage));
        Raise(nameof(ConnectionActionLabel));
        Raise(nameof(MonitorActionLabel));
        Raise(nameof(ActivityText));
        Raise(nameof(SummaryText));
        Raise(nameof(IdentityText));
        Raise(nameof(ConnectionGlyph));
        Raise(nameof(ConnectionToolTip));
        Raise(nameof(MonitorGlyph));
        Raise(nameof(MonitorToolTip));
        Raise(nameof(ConfigureToolTip));
        Raise(nameof(PlayToolTip));
        Raise(nameof(StopToolTip));
        Raise(nameof(RemoveToolTip));
    }
}

public sealed class Iec61850MonitorPoint : ObservableObject
{
    private string _value = "-";
    private string _quality = "Unknown";
    private string _deviceTimestamp = "-";
    private string _sourceMode = "Waiting";
    private string _reason = "-";
    private string _status = "Queued";
    private long _sequence;
    private bool _isRecentlyChanged;

    public string DeviceId { get; set; } = string.Empty;
    public string DeviceName { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string SignalName { get; set; } = string.Empty;
    public string IecReference { get; set; } = string.Empty;
    public string QualityReference { get; set; } = string.Empty;
    public string TimestampReference { get; set; } = string.Empty;
    public string FunctionalConstraint { get; set; } = string.Empty;
    public string IecDataType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string DataSetReference { get; set; } = string.Empty;
    public string ReportControlReference { get; set; } = string.Empty;
    public int PollingIntervalMs { get; set; } = 1000;

    public string PointKey => $"{DeviceId}|{NormalizeReference(IecReference)}";
    public string IecTelegram => StripIedNamePrefix(IecReference, DeviceName);
    public string Value { get => _value; set => Set(ref _value, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string Quality { get => _quality; set => Set(ref _quality, string.IsNullOrWhiteSpace(value) ? "Unknown" : value); }
    public string DeviceTimestamp { get => _deviceTimestamp; set => Set(ref _deviceTimestamp, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string SourceMode { get => _sourceMode; set => Set(ref _sourceMode, string.IsNullOrWhiteSpace(value) ? "Unknown" : value); }
    public string Reason { get => _reason; set => Set(ref _reason, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string Status { get => _status; set => Set(ref _status, string.IsNullOrWhiteSpace(value) ? "Unknown" : value); }
    public long Sequence { get => _sequence; set => Set(ref _sequence, value); }
    public bool IsRecentlyChanged { get => _isRecentlyChanged; set => Set(ref _isRecentlyChanged, value); }

    /// <summary>
    /// Applies a process value and returns true only for a real semantic transition.
    /// Initial population and formatting-only differences are deliberately ignored.
    /// </summary>
    public bool ApplyProcessValue(string? value)
    {
        var next = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        var previous = Value;
        Value = next;
        return IsInitializedProcessValue(previous) &&
               IsInitializedProcessValue(next) &&
               !AreSemanticallyEquivalent(previous, next);
    }

    private static bool IsInitializedProcessValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        return text.Length > 0 && text != "-" &&
               !text.Equals("Pending", StringComparison.OrdinalIgnoreCase) &&
               !text.Equals("Unknown", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool AreSemanticallyEquivalent(string? left, string? right)
    {
        var a = NormalizeSemanticValue(left);
        var b = NormalizeSemanticValue(right);
        if (a == b) return true;

        if (TryParseSemanticNumber(a, out var an) && TryParseSemanticNumber(b, out var bn))
            return an == bn;

        return false;
    }

    private static bool TryParseSemanticNumber(string value, out decimal number)
    {
        if (value == "binary:1")
        {
            number = 1m;
            return true;
        }
        if (value == "binary:0")
        {
            number = 0m;
            return true;
        }

        return decimal.TryParse(
            value,
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out number);
    }

    private static string NormalizeSemanticValue(string? value)
    {
        var text = (value ?? string.Empty).Trim().ToLowerInvariant();
        if (text is "true" or "on" or "active" or "asserted") return "binary:1";
        if (text is "false" or "off" or "inactive" or "deasserted") return "binary:0";
        if (text.Contains("open") && text.Contains("01")) return "dbpos:01";
        if (text.Contains("closed") && text.Contains("10")) return "dbpos:10";
        if (text.Contains("intermediate") && text.Contains("00")) return "dbpos:00";
        if (text.Contains("bad") && text.Contains("11")) return "dbpos:11";
        return string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
    }

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    internal static string StripIedNamePrefix(string? reference, string? iedName)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var name = (iedName ?? string.Empty).Trim();
        var slash = text.IndexOf('/');
        if (slash <= 0 || string.IsNullOrWhiteSpace(name))
            return text;

        var domain = text[..slash];
        if (!domain.StartsWith(name, StringComparison.OrdinalIgnoreCase) || domain.Length <= name.Length)
            return text;

        return domain[name.Length..] + text[slash..];
    }
}

public sealed class Iec61850PointSnapshot
{
    public required Iec61850MonitorPoint Point { get; init; }
    public string PreviousValue { get; init; } = "-";
    public string Value { get; init; } = "-";
    public string Quality { get; init; } = "Unknown";
    public string DeviceTimestamp { get; init; } = "-";
    public string SourceMode { get; init; } = "MMS Polling";
    public string Reason { get; init; } = "cyclic";
    public string Status { get; init; } = "Live";
    /// <summary>
    /// True only when the runtime observed a real semantic process-value transition.
    /// This flag survives UI batching so a fast report/poll sequence cannot erase the
    /// three-second commissioning highlight before WPF renders it.
    /// </summary>
    public bool IsValueEdge { get; init; }
    public bool IsReportTraffic { get; init; }
    public long Sequence { get; init; }
}

public sealed class Iec61850EventEntry
{
    public long Sequence { get; init; }
    public string DeviceId { get; init; } = string.Empty;
    public string PointKey { get; init; } = string.Empty;
    public string DeviceTimestamp { get; init; } = "-";
    public string DeviceName { get; init; } = string.Empty;
    public string IpAddress { get; init; } = string.Empty;
    public string SignalName { get; init; } = string.Empty;
    public string IecReference { get; init; } = string.Empty;
    public string OldValue { get; init; } = "-";
    public string NewValue { get; init; } = "-";
    public string Quality { get; init; } = "Unknown";
    public string SourceMode { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string EdgeType
    {
        get
        {
            if (TryParseBinaryState(OldValue, out var oldState) &&
                TryParseBinaryState(NewValue, out var newState) &&
                oldState != newState)
            {
                return newState ? "Rising" : "Falling";
            }

            return "State change";
        }
    }

    public string ChangeText => $"{EdgeType} · {OldValue} → {NewValue}";
    public string EventValue => string.IsNullOrWhiteSpace(NewValue) ? "-" : NewValue;
    public string ValueTone
    {
        get
        {
            var text = EventValue.Trim().ToLowerInvariant();
            if (text.Contains("closed") ||
                text is "true" or "on" or "active" or "asserted" or "1" or "1.0")
                return "Energized";
            if (text.Contains("open") ||
                text is "false" or "off" or "inactive" or "deasserted" or "0" or "0.0")
                return "Deenergized";
            if (text.Contains("intermediate") || text.Contains("bad") ||
                text.Contains("00") || text.Contains("11"))
                return "Abnormal";
            return "Neutral";
        }
    }

    public string IecTelegram => Iec61850MonitorPoint.StripIedNamePrefix(IecReference, DeviceName);

    private static bool TryParseBinaryState(string? value, out bool state)
    {
        state = false;
        var text = (value ?? string.Empty).Trim();
        if (bool.TryParse(text, out state))
            return true;

        if (text.Equals("1", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("1.0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("on", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("active", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("asserted", StringComparison.OrdinalIgnoreCase))
        {
            state = true;
            return true;
        }

        if (text.Equals("0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("0.0", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("off", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("inactive", StringComparison.OrdinalIgnoreCase) ||
            text.Equals("deasserted", StringComparison.OrdinalIgnoreCase))
        {
            state = false;
            return true;
        }

        return false;
    }
}

public sealed class Iec61850TesterProject
{
    public int SchemaVersion { get; set; } = 3;
    public string ProjectName { get; set; } = "ArIED 61850 Session";
    public int DefaultPollingIntervalMs { get; set; } = 1000;
    public List<Iec61850TesterDeviceProfile> Devices { get; set; } = new();
}

public sealed class Iec61850TesterDeviceProfile
{
    public string DeviceId { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "IED";
    public string IdentitySource { get; set; } = string.Empty;
    public string LogicalDeviceSummary { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public int Port { get; set; } = 102;
    public bool AllowDynamicDataSetWrites { get; set; } = true;
    public bool DiscoverySucceeded { get; set; }
    public string SclSourcePath { get; set; } = string.Empty;
    public string SclSourceSha256 { get; set; } = string.Empty;
    public string SclIedName { get; set; } = string.Empty;
    public string SclAccessPointName { get; set; } = string.Empty;
    public List<string> SelectedReferences { get; set; } = new();
    public List<Iec61850CachedSignalProfile> CachedSignals { get; set; } = new();
}

/// <summary>
/// Stable, JSON-friendly discovery snapshot. It deliberately stores model metadata,
/// not live values, so an opened project can reconnect immediately without presenting
/// stale process data as current.
/// </summary>
public sealed class Iec61850CachedSignalProfile
{
    public string Name { get; set; } = string.Empty;
    public string ObjectReference { get; set; } = string.Empty;
    public string DisplayReference { get; set; } = string.Empty;
    public string FunctionalConstraint { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Unit { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Medium";
    public string DataSetReference { get; set; } = string.Empty;
    public string ReportControlReference { get; set; } = string.Empty;
    public string ReportCoverageReason { get; set; } = string.Empty;
    public string QualityReference { get; set; } = string.Empty;
    public string TimestampReference { get; set; } = string.Empty;
    public string Source { get; set; } = "Project cache";
    public bool IsReportCapable { get; set; }
    public string ReportCoverage { get; set; } = "Polling fallback";
    public bool IsControlSignal { get; set; }
    public string ControlCdc { get; set; } = string.Empty;
    public string ControlModelReference { get; set; } = string.Empty;
    public string ControlStatusReference { get; set; } = string.Empty;
    public string ControlModelText { get; set; } = "Auto-detect";
    public string ControlValueType { get; set; } = string.Empty;
    public bool IsSelected { get; set; }

    public static Iec61850CachedSignalProfile FromSignal(SignalDefinition signal)
    {
        ArgumentNullException.ThrowIfNull(signal);
        return new Iec61850CachedSignalProfile
        {
            Name = signal.Name,
            ObjectReference = signal.ObjectReference,
            DisplayReference = signal.DisplayReference,
            FunctionalConstraint = signal.FunctionalConstraint,
            DataType = signal.DataType,
            Category = signal.Category,
            Unit = signal.Unit,
            Confidence = signal.Confidence,
            DataSetReference = signal.DataSetReference,
            ReportControlReference = signal.ReportControlReference,
            ReportCoverageReason = signal.ReportCoverageReason,
            QualityReference = signal.QualityReference,
            TimestampReference = signal.TimestampReference,
            Source = signal.Source,
            IsReportCapable = signal.IsReportCapable,
            ReportCoverage = signal.ReportCoverage,
            IsControlSignal = signal.IsControlSignal,
            ControlCdc = signal.ControlCdc,
            ControlModelReference = signal.ControlModelReference,
            ControlStatusReference = signal.ControlStatusReference,
            ControlModelText = signal.ControlModelText,
            ControlValueType = signal.ControlValueType,
            IsSelected = signal.IsSelected
        };
    }

    public SignalDefinition ToSignal()
        => new()
        {
            Name = Name,
            ObjectReference = ObjectReference,
            DisplayReference = DisplayReference,
            FunctionalConstraint = FunctionalConstraint,
            DataType = DataType,
            Category = Category,
            Unit = Unit,
            Confidence = Confidence,
            DataSetReference = DataSetReference,
            ReportControlReference = ReportControlReference,
            ReportCoverageReason = ReportCoverageReason,
            QualityReference = QualityReference,
            TimestampReference = TimestampReference,
            Source = string.IsNullOrWhiteSpace(Source) ? "Project cache" : Source,
            IsReportCapable = IsReportCapable,
            ReportCoverage = ReportCoverage,
            IsControlSignal = IsControlSignal,
            ControlCdc = ControlCdc,
            ControlModelReference = ControlModelReference,
            ControlStatusReference = ControlStatusReference,
            ControlModelText = ControlModelText,
            ControlValueType = ControlValueType,
            IsSelected = IsSelected,
            Value = "-",
            Quality = "Unknown",
            DeviceTimestamp = "-",
            ProbeStatus = "Restored from saved discovery model"
        };
}
