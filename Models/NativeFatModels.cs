using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace ArIED61850Tester.Models;

/// <summary>
/// Persistent, acquisition-independent FAT state layered on top of the canonical
/// Engineering/IED Explorer signal model. Nothing here owns MMS, report-control,
/// polling, or command runtime state.
/// </summary>
public sealed class NativeFatDeviceState
{
    public int SchemaVersion { get; set; } = 1;
    public string DeviceId { get; set; } = string.Empty;
    public string IedName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<NativeFatSignalState> Signals { get; set; } = new();

    [System.Text.Json.Serialization.JsonIgnore]
    public string StoragePath { get; set; } = string.Empty;
}

public sealed class NativeFatSignalState
{
    /// <summary>
    /// Stable per-IED identity: normalized IEC object reference + FC. Display labels
    /// are deliberately excluded so a signal rename does not erase commissioning work.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public string SignalName { get; set; } = string.Empty;
    public string IecReference { get; set; } = string.Empty;
    public string FunctionalConstraint { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public DateTimeOffset FirstSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool IsHistorical { get; set; }
    public NativeFatCapture? Value1 { get; set; }
    public NativeFatCapture? Value2 { get; set; }
    public string Result { get; set; } = NativeFatResult.Untested;
    public List<NativeFatHistoryEntry> History { get; set; } = new();
}

public sealed class NativeFatCapture
{
    public string Value { get; set; } = "-";
    public string Quality { get; set; } = "Unknown";
    public string DeviceTimestamp { get; set; } = "-";
    public string SourceMode { get; set; } = "Unknown";
    public long Sequence { get; set; }
    public DateTimeOffset CapturedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class NativeFatHistoryEntry
{
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public NativeFatCapture? Value1 { get; set; }
    public NativeFatCapture? Value2 { get; set; }
    public string Note { get; set; } = string.Empty;
}

public static class NativeFatResult
{
    public const string Untested = "UNTESTED";
    public const string Pass = "PASS";
    public const string Review = "REVIEW";
    public const string Fail = "FAIL";
}

public static class NativeFatIdentity
{
    public static string BuildKey(SignalDefinition signal)
        => BuildKey(signal.ObjectReference, signal.FunctionalConstraint);

    public static string BuildKey(Iec61850MonitorPoint point)
        => BuildKey(point.IecReference, point.FunctionalConstraint);

    public static string BuildKey(string? reference, string? functionalConstraint)
    {
        var normalized = NormalizeReference(reference);
        var fc = (functionalConstraint ?? string.Empty).Trim().ToUpperInvariant();
        return string.IsNullOrWhiteSpace(fc) ? normalized : $"{normalized}|{fc}";
    }

    public static string NormalizeReference(string? reference)
    {
        var value = (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".", StringComparison.Ordinal);
        return value.ToUpperInvariant();
    }
}

/// <summary>
/// Lightweight FAT projection. Engineering identity and current value come directly
/// from the Explorer's SignalDefinition, with a monitor point used when available for
/// the richer IEC telegram/acquisition metadata. FAT captures/results remain separate.
/// Historical rows intentionally have neither live source.
/// </summary>
public sealed class NativeFatSignalRow : INotifyPropertyChanged, IDisposable
{
    private SignalDefinition? _sourceSignal;
    private Iec61850MonitorPoint? _sourcePoint;

    public NativeFatSignalRow(
        NativeFatSignalState state,
        SignalDefinition? sourceSignal,
        Iec61850MonitorPoint? sourcePoint)
    {
        State = state ?? throw new ArgumentNullException(nameof(state));
        AttachSources(sourceSignal, sourcePoint);
    }

    public NativeFatSignalState State { get; }
    public SignalDefinition? SourceSignal => _sourceSignal;
    public Iec61850MonitorPoint? SourcePoint => _sourcePoint;
    public string Key => State.Key;
    public string SignalName => _sourceSignal?.Name ?? _sourcePoint?.SignalName ?? State.SignalName;
    public string IecReference => _sourceSignal?.ObjectReference ?? _sourcePoint?.IecReference ?? State.IecReference;
    public string IecTelegram => _sourcePoint?.IecTelegram ?? _sourceSignal?.DisplayReference ?? State.IecReference;
    public string DataType => _sourceSignal?.DataType ?? _sourcePoint?.IecDataType ?? State.DataType;
    public string FunctionalConstraint => _sourceSignal?.FunctionalConstraint ?? _sourcePoint?.FunctionalConstraint ?? State.FunctionalConstraint;
    public string LiveValue => _sourcePoint?.DisplayValue ?? _sourceSignal?.Value ?? "-";
    public string Quality => _sourcePoint?.Quality ?? _sourceSignal?.Quality ?? (IsHistorical ? "Historical" : "Unknown");
    public string DeviceTimestamp => _sourcePoint?.DeviceTimestamp ?? _sourceSignal?.DeviceTimestamp ?? "-";
    public string Value1Text => State.Value1?.Value ?? "-";
    public string Value2Text => State.Value2?.Value ?? "-";
    public string Result => string.IsNullOrWhiteSpace(State.Result) ? NativeFatResult.Untested : State.Result;
    public bool IsHistorical => _sourceSignal == null && _sourcePoint == null || State.IsHistorical;
    public string StatusText => IsHistorical ? "HISTORICAL" : State.Value1 == null && State.Value2 == null ? "READY" : "CAPTURED";
    public int HistoryCount => State.History?.Count ?? 0;
    public string HistoryText => HistoryCount == 0 ? "—" : $"{HistoryCount} record{(HistoryCount == 1 ? string.Empty : "s")}";
    public bool CanCapture => _sourceSignal != null || _sourcePoint != null;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? StateChanged;

    public void AttachSources(SignalDefinition? signal, Iec61850MonitorPoint? point)
    {
        if (!ReferenceEquals(_sourceSignal, signal))
        {
            if (_sourceSignal != null)
                _sourceSignal.PropertyChanged -= SourceSignal_PropertyChanged;
            _sourceSignal = signal;
            if (_sourceSignal != null)
                _sourceSignal.PropertyChanged += SourceSignal_PropertyChanged;
        }

        if (!ReferenceEquals(_sourcePoint, point))
        {
            if (_sourcePoint != null)
                _sourcePoint.PropertyChanged -= SourcePoint_PropertyChanged;
            _sourcePoint = point;
            if (_sourcePoint != null)
                _sourcePoint.PropertyChanged += SourcePoint_PropertyChanged;
        }

        RaiseAll();
    }

    public bool CaptureValue(int slot)
    {
        if (!CanCapture || slot is < 1 or > 2)
            return false;

        var capture = CaptureCurrent();
        if (slot == 1)
            State.Value1 = capture;
        else
            State.Value2 = capture;

        State.LastSeenUtc = DateTimeOffset.UtcNow;
        AppendHistory($"Capture Value {slot}");
        Raise(nameof(Value1Text));
        Raise(nameof(Value2Text));
        Raise(nameof(StatusText));
        Raise(nameof(HistoryCount));
        Raise(nameof(HistoryText));
        StateChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void SetResult(string result)
    {
        result = result switch
        {
            NativeFatResult.Pass => NativeFatResult.Pass,
            NativeFatResult.Fail => NativeFatResult.Fail,
            NativeFatResult.Review => NativeFatResult.Review,
            _ => NativeFatResult.Untested
        };
        if (State.Result.Equals(result, StringComparison.OrdinalIgnoreCase))
            return;

        State.Result = result;
        State.LastSeenUtc = DateTimeOffset.UtcNow;
        AppendHistory($"Result {result}");
        Raise(nameof(Result));
        Raise(nameof(HistoryCount));
        Raise(nameof(HistoryText));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    public void ResetCurrentResult()
    {
        if (State.Value1 == null && State.Value2 == null &&
            State.Result.Equals(NativeFatResult.Untested, StringComparison.OrdinalIgnoreCase))
            return;

        AppendHistory("Reset current FAT state");
        State.Value1 = null;
        State.Value2 = null;
        State.Result = NativeFatResult.Untested;
        State.LastSeenUtc = DateTimeOffset.UtcNow;
        Raise(nameof(Value1Text));
        Raise(nameof(Value2Text));
        Raise(nameof(Result));
        Raise(nameof(StatusText));
        Raise(nameof(HistoryCount));
        Raise(nameof(HistoryText));
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private NativeFatCapture CaptureCurrent()
        => new()
        {
            Value = LiveValue,
            Quality = Quality,
            DeviceTimestamp = DeviceTimestamp,
            SourceMode = _sourcePoint?.SourceMode ?? _sourceSignal?.ReportPlan ?? "Explorer",
            Sequence = _sourcePoint?.Sequence ?? 0,
            CapturedUtc = DateTimeOffset.UtcNow
        };

    private void AppendHistory(string action)
    {
        State.History ??= new List<NativeFatHistoryEntry>();
        State.History.Add(new NativeFatHistoryEntry
        {
            TimestampUtc = DateTimeOffset.UtcNow,
            Action = action,
            Result = Result,
            Value1 = Clone(State.Value1),
            Value2 = Clone(State.Value2)
        });
    }

    private static NativeFatCapture? Clone(NativeFatCapture? source)
        => source == null ? null : new NativeFatCapture
        {
            Value = source.Value,
            Quality = source.Quality,
            DeviceTimestamp = source.DeviceTimestamp,
            SourceMode = source.SourceMode,
            Sequence = source.Sequence,
            CapturedUtc = source.CapturedUtc
        };

    private void SourceSignal_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(SignalDefinition.Value):
                if (_sourcePoint == null) Raise(nameof(LiveValue));
                break;
            case nameof(SignalDefinition.Quality):
                if (_sourcePoint == null) Raise(nameof(Quality));
                break;
            case nameof(SignalDefinition.DeviceTimestamp):
                if (_sourcePoint == null) Raise(nameof(DeviceTimestamp));
                break;
            default:
                return;
        }
    }

    private void SourcePoint_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(Iec61850MonitorPoint.Value):
            case nameof(Iec61850MonitorPoint.DisplayValue):
                Raise(nameof(LiveValue));
                break;
            case nameof(Iec61850MonitorPoint.Quality):
                Raise(nameof(Quality));
                break;
            case nameof(Iec61850MonitorPoint.DeviceTimestamp):
                Raise(nameof(DeviceTimestamp));
                break;
            default:
                return;
        }
    }

    private void RaiseAll()
    {
        Raise(nameof(SignalName));
        Raise(nameof(IecReference));
        Raise(nameof(IecTelegram));
        Raise(nameof(DataType));
        Raise(nameof(FunctionalConstraint));
        Raise(nameof(LiveValue));
        Raise(nameof(Quality));
        Raise(nameof(DeviceTimestamp));
        Raise(nameof(IsHistorical));
        Raise(nameof(StatusText));
        Raise(nameof(CanCapture));
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public void Dispose()
    {
        if (_sourceSignal != null)
            _sourceSignal.PropertyChanged -= SourceSignal_PropertyChanged;
        if (_sourcePoint != null)
            _sourcePoint.PropertyChanged -= SourcePoint_PropertyChanged;
        _sourceSignal = null;
        _sourcePoint = null;
    }
}
