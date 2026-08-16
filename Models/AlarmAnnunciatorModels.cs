namespace ArIED61850Tester.Models;

/// <summary>
/// Operator-facing annunciator window fed only by authoritative SOE/Event Log edges.
/// A momentary active edge is retained until acknowledgement, while acknowledgement
/// never masks a condition that is still physically active.
/// </summary>
public sealed class AlarmAnnunciatorItem : ObservableObject
{
    public const string NormalState = "Normal";
    public const string ActiveUnacknowledgedState = "ActiveUnacknowledged";
    public const string ActiveAcknowledgedState = "ActiveAcknowledged";
    public const string ReturnedUnacknowledgedState = "ReturnedUnacknowledged";

    private string _deviceName = "IED";
    private string _signalName = "Signal";
    private string _iecReference = string.Empty;
    private string _iecTelegram = string.Empty;
    private string _iecDataType = string.Empty;
    private string _currentValue = "-";
    private string _quality = "Unknown";
    private string _lastEventTimestamp = "-";
    private string _sourceMode = "SOE";
    private bool _currentProcessActive;
    private bool _hasLatchedOccurrence;
    private bool _isAcknowledged;
    private bool _flashPhase = true;
    private int _activationCount;
    private DateTimeOffset? _acknowledgedAt;

    public string DeviceId { get; init; } = string.Empty;
    public string PointKey { get; init; } = string.Empty;
    public string ConfiguredReference { get; init; } = string.Empty;

    public string DeviceName { get => _deviceName; set => Set(ref _deviceName, string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim()); }
    public string SignalName { get => _signalName; set => Set(ref _signalName, string.IsNullOrWhiteSpace(value) ? "Signal" : value.Trim()); }
    public string IecReference { get => _iecReference; set => Set(ref _iecReference, value?.Trim() ?? string.Empty); }
    public string IecTelegram { get => _iecTelegram; set => Set(ref _iecTelegram, value?.Trim() ?? string.Empty); }
    public string IecDataType { get => _iecDataType; set => Set(ref _iecDataType, value?.Trim() ?? string.Empty); }
    public string CurrentValue { get => _currentValue; private set => Set(ref _currentValue, string.IsNullOrWhiteSpace(value) ? "-" : value.Trim()); }
    public string Quality { get => _quality; private set => Set(ref _quality, string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim()); }
    public string LastEventTimestamp { get => _lastEventTimestamp; private set => Set(ref _lastEventTimestamp, string.IsNullOrWhiteSpace(value) ? "-" : value.Trim()); }
    public string SourceMode { get => _sourceMode; private set => Set(ref _sourceMode, string.IsNullOrWhiteSpace(value) ? "SOE" : value.Trim()); }
    public bool CurrentProcessActive { get => _currentProcessActive; private set { if (Set(ref _currentProcessActive, value)) RaiseStateProperties(); } }
    public bool HasLatchedOccurrence { get => _hasLatchedOccurrence; private set { if (Set(ref _hasLatchedOccurrence, value)) RaiseStateProperties(); } }
    public bool IsAcknowledged { get => _isAcknowledged; private set { if (Set(ref _isAcknowledged, value)) RaiseStateProperties(); } }
    public int ActivationCount { get => _activationCount; private set => Set(ref _activationCount, Math.Max(0, value)); }
    public DateTimeOffset? AcknowledgedAt { get => _acknowledgedAt; private set { if (Set(ref _acknowledgedAt, value)) Raise(nameof(AcknowledgedText)); } }

    public string VisualState => !HasLatchedOccurrence
        ? NormalState
        : !IsAcknowledged
            ? CurrentProcessActive ? ActiveUnacknowledgedState : ReturnedUnacknowledgedState
            : ActiveAcknowledgedState;

    public string StateText => VisualState switch
    {
        ActiveUnacknowledgedState => "ALARM • UNACK",
        ActiveAcknowledgedState => "ALARM • ACK",
        ReturnedUnacknowledgedState => "RTN • UNACK",
        _ => "NORMAL"
    };

    public string CompactStateText => VisualState switch
    {
        ActiveUnacknowledgedState => "UNACK",
        ActiveAcknowledgedState => "ACK",
        ReturnedUnacknowledgedState => "RTN",
        _ => string.Empty
    };

    public string StateDetail => VisualState switch
    {
        ActiveUnacknowledgedState => "Active condition waiting for acknowledgement",
        ActiveAcknowledgedState => "Acknowledged; condition is still active",
        ReturnedUnacknowledgedState => "Pulse/condition returned, occurrence remains latched until ACK",
        _ => "No pending alarm occurrence"
    };

    public bool IsFlashing => HasLatchedOccurrence && !IsAcknowledged;
    public bool CanAcknowledge => HasLatchedOccurrence && !IsAcknowledged;
    public bool IsNormal => !HasLatchedOccurrence;
    public double LampOpacity => IsFlashing ? (_flashPhase ? 1d : 0.18d) : HasLatchedOccurrence ? 1d : 0.34d;
    public string AcknowledgedText => AcknowledgedAt.HasValue
        ? $"ACK {AcknowledgedAt.Value.LocalDateTime:HH:mm:ss}"
        : "Not acknowledged";
    public string ActivationCountText => ActivationCount == 1 ? "1 occurrence" : $"{ActivationCount} occurrences";

    public void InitializeFromPoint(Iec61850MonitorPoint point)
    {
        ArgumentNullException.ThrowIfNull(point);
        DeviceName = point.DeviceName;
        SignalName = point.SignalName;
        IecReference = point.IecReference;
        IecTelegram = point.IecTelegram;
        IecDataType = point.IecDataType;
        CurrentValue = point.Value;
        Quality = point.Quality;
        LastEventTimestamp = point.DeviceTimestamp;
        SourceMode = point.SourceMode;

        var state = ResolveAlarmActive(point.Value, point.IecDataType);
        if (state == true && !HasLatchedOccurrence)
        {
            CurrentProcessActive = true;
            HasLatchedOccurrence = true;
            IsAcknowledged = false;
            ActivationCount++;
            AcknowledgedAt = null;
        }
        else if (state == false)
        {
            CurrentProcessActive = false;
        }
    }

    public bool ApplyEvent(Iec61850EventEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        DeviceName = entry.DeviceName;
        SignalName = entry.SignalName;
        IecReference = entry.IecReference;
        IecTelegram = entry.IecTelegram;
        IecDataType = entry.IecDataType;
        CurrentValue = entry.EventValue;
        Quality = entry.Quality;
        LastEventTimestamp = entry.DeviceTimestamp;
        SourceMode = entry.SourceMode;

        var nextActive = ResolveAlarmActive(entry.EventValue, entry.IecDataType);
        if (!nextActive.HasValue)
            return false;

        if (nextActive.Value)
        {
            var newOccurrence = !CurrentProcessActive || !HasLatchedOccurrence;
            CurrentProcessActive = true;
            HasLatchedOccurrence = true;
            IsAcknowledged = false;
            AcknowledgedAt = null;
            if (newOccurrence)
                ActivationCount++;
            RaiseStateProperties();
            return true;
        }

        CurrentProcessActive = false;
        if (HasLatchedOccurrence && IsAcknowledged)
            ClearLatchedOccurrence();
        else
            RaiseStateProperties();
        return true;
    }

    public bool Acknowledge(DateTimeOffset acknowledgedAt)
    {
        if (!CanAcknowledge)
            return false;

        IsAcknowledged = true;
        AcknowledgedAt = acknowledgedAt;
        if (!CurrentProcessActive)
            ClearLatchedOccurrence(preserveAcknowledgementTime: true);
        else
            RaiseStateProperties();
        return true;
    }

    public void SetFlashPhase(bool phase)
    {
        if (_flashPhase == phase)
            return;
        _flashPhase = phase;
        Raise(nameof(LampOpacity));
    }

    public void MarkUnavailable(string sourceMode = "Offline")
    {
        SourceMode = sourceMode;
        RaiseStateProperties();
    }

    private void ClearLatchedOccurrence(bool preserveAcknowledgementTime = false)
    {
        HasLatchedOccurrence = false;
        IsAcknowledged = false;
        if (!preserveAcknowledgementTime)
            AcknowledgedAt = null;
        RaiseStateProperties();
    }

    private void RaiseStateProperties()
    {
        Raise(nameof(VisualState));
        Raise(nameof(StateText));
        Raise(nameof(CompactStateText));
        Raise(nameof(StateDetail));
        Raise(nameof(IsFlashing));
        Raise(nameof(CanAcknowledge));
        Raise(nameof(IsNormal));
        Raise(nameof(LampOpacity));
        Raise(nameof(AcknowledgedText));
        Raise(nameof(ActivationCountText));
    }

    public static bool? ResolveAlarmActive(string? value, string? dataType)
    {
        var tone = Iec61850ValueStatePresentation.Classify(value, dataType);
        return tone switch
        {
            Iec61850ValueStatePresentation.Active => true,
            Iec61850ValueStatePresentation.Inactive => false,
            // Intermediate/bad DPC states are abnormal operator conditions and should
            // be retained when an engineer explicitly configured the point as an alarm.
            Iec61850ValueStatePresentation.Abnormal => true,
            _ => null
        };
    }
}

/// <summary>
/// Stable per-IED alarm group used by the annunciator rail. Only one group's alarm
/// windows are rendered at a time, which keeps the UI bounded even when a project has
/// hundreds of IEDs and thousands of configured annunciator points.
/// </summary>
public sealed class AlarmAnnunciatorDeviceGroup : ObservableObject
{
    public const string NormalState = "Normal";
    public const string ActiveState = "Active";
    public const string UnacknowledgedState = "Unacknowledged";

    private string _deviceName = "IED";
    private int _activeCount;
    private int _unacknowledgedCount;
    private bool _flashPhase = true;

    public string DeviceId { get; init; } = string.Empty;
    public string DeviceName { get => _deviceName; set => Set(ref _deviceName, string.IsNullOrWhiteSpace(value) ? "IED" : value.Trim()); }
    public BulkObservableCollection<AlarmAnnunciatorItem> Alarms { get; } = new();

    public int ConfiguredCount => Alarms.Count;
    public int ActiveCount => _activeCount;
    public int UnacknowledgedCount => _unacknowledgedCount;
    public bool HasUnacknowledged => UnacknowledgedCount > 0;
    public string VisualState => HasUnacknowledged ? UnacknowledgedState : ActiveCount > 0 ? ActiveState : NormalState;
    public string StatusText => HasUnacknowledged
        ? $"{UnacknowledgedCount} UNACK • {ConfiguredCount} windows"
        : ActiveCount > 0
            ? $"{ActiveCount} ACTIVE • {ConfiguredCount} windows"
            : $"{ConfiguredCount} windows";
    public double LampOpacity => HasUnacknowledged ? (_flashPhase ? 1d : 0.18d) : ActiveCount > 0 ? 1d : 0.30d;

    public void Recalculate(bool flashPhase)
    {
        _flashPhase = flashPhase;
        var active = Alarms.Count(item => item.HasLatchedOccurrence && item.CurrentProcessActive);
        var unacknowledged = Alarms.Count(item => item.CanAcknowledge);
        var changed = false;
        if (_activeCount != active)
        {
            _activeCount = active;
            changed = true;
            Raise(nameof(ActiveCount));
        }
        if (_unacknowledgedCount != unacknowledged)
        {
            _unacknowledgedCount = unacknowledged;
            changed = true;
            Raise(nameof(UnacknowledgedCount));
            Raise(nameof(HasUnacknowledged));
        }

        if (changed)
        {
            Raise(nameof(VisualState));
            Raise(nameof(StatusText));
        }
        Raise(nameof(ConfiguredCount));
        Raise(nameof(LampOpacity));
    }

    public void SetFlashPhase(bool phase)
    {
        if (!HasUnacknowledged || _flashPhase == phase)
            return;
        _flashPhase = phase;
        Raise(nameof(LampOpacity));
    }
}
