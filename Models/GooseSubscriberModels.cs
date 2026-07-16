using System.Collections.ObjectModel;

namespace ArIED61850Tester.Models;

public sealed class GooseAdapterOption
{
    public int Index { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string MacAddress { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public string Selector => Index.ToString(System.Globalization.CultureInfo.InvariantCulture);
    public string DisplayText => $"[{Index}] {FirstReadable(FriendlyName, Description, Name, "Network adapter")}";
    public string DetailText => string.Join(" • ", new[] { FriendlyName, Description, MacAddress, Name }
        .Where(value => !string.IsNullOrWhiteSpace(value))
        .Distinct(StringComparer.OrdinalIgnoreCase));
    public override string ToString() => DisplayText;

    private static string FirstReadable(params string?[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "Network adapter";
}

public sealed class GooseLeafValueRow : ObservableObject
{
    private string _signalName = string.Empty;
    private string _signalReference = string.Empty;
    private string _functionalConstraint = string.Empty;
    private string _cdc = string.Empty;
    private string _bType = string.Empty;
    private string _value = "-";
    private string _previousValue = string.Empty;
    private string _bindingSource = "Unbound";
    private bool _isChanged;
    private bool _isHighlighted;
    private DateTimeOffset _highlightUntilUtc;

    public int Order { get; init; }
    public int DataSetIndex { get; init; }
    public string SignalName { get => _signalName; set => Set(ref _signalName, value ?? string.Empty); }
    public string SignalReference { get => _signalReference; set => Set(ref _signalReference, value ?? string.Empty); }
    public string FunctionalConstraint { get => _functionalConstraint; set => Set(ref _functionalConstraint, value ?? string.Empty); }
    public string Cdc { get => _cdc; set => Set(ref _cdc, value ?? string.Empty); }
    public string BType { get => _bType; set => Set(ref _bType, value ?? string.Empty); }
    public string Value { get => _value; set => Set(ref _value, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string PreviousValue { get => _previousValue; set => Set(ref _previousValue, value ?? string.Empty); }
    public string BindingSource { get => _bindingSource; set => Set(ref _bindingSource, string.IsNullOrWhiteSpace(value) ? "Unbound" : value); }
    public bool IsChanged { get => _isChanged; set => Set(ref _isChanged, value); }
    public bool IsHighlighted { get => _isHighlighted; private set => Set(ref _isHighlighted, value); }
    public string TypeText => string.Join(" / ", new[] { Cdc, BType }.Where(item => !string.IsNullOrWhiteSpace(item)));

    public void Apply(GooseLeafValueSnapshot snapshot)
    {
        SignalName = snapshot.SignalName;
        SignalReference = snapshot.SignalReference;
        FunctionalConstraint = snapshot.FunctionalConstraint;
        Cdc = snapshot.Cdc;
        BType = snapshot.BType;
        PreviousValue = snapshot.PreviousValue;
        Value = snapshot.Value;
        BindingSource = snapshot.BindingSource;
        IsChanged = snapshot.IsChanged;
        if (snapshot.IsChanged)
        {
            _highlightUntilUtc = DateTimeOffset.UtcNow.AddSeconds(5);
            IsHighlighted = true;
        }
        Raise(nameof(TypeText));
    }

    public bool ExpireHighlight(DateTimeOffset nowUtc)
    {
        if (!IsHighlighted || nowUtc < _highlightUntilUtc)
            return false;
        IsHighlighted = false;
        return true;
    }
}

public sealed partial class GooseStreamRow : ObservableObject
{
    private string _appIdText = "-";
    private string _goCbRef = string.Empty;
    private string _goId = string.Empty;
    private string _dataSetReference = string.Empty;
    private string _sourceMac = string.Empty;
    private string _destinationMac = string.Empty;
    private string _vlanText = "-";
    private string _stateNumberText = "-";
    private string _sequenceNumberText = "-";
    private string _sequenceStatus = "Unknown";
    private string _timeAllowedToLiveText = "-";
    private string _configurationRevisionText = "-";
    private string _modelIedName = string.Empty;
    private string _bindingSource = "Unbound";
    private string _diagnosticsSummary = string.Empty;
    private string _lastSeenText = "-";
    private long _packetCount;
    private int _changedValueCount;
    private bool _test;
    private bool _needsCommissioning;

    public string StreamKey { get; init; } = string.Empty;
    public ObservableCollection<GooseLeafValueRow> Leaves { get; } = new();

    public string AppIdText { get => _appIdText; set => Set(ref _appIdText, value ?? "-"); }
    public string GoCbRef { get => _goCbRef; set => Set(ref _goCbRef, value ?? string.Empty); }
    public string GoId { get => _goId; set => Set(ref _goId, value ?? string.Empty); }
    public string DataSetReference { get => _dataSetReference; set => Set(ref _dataSetReference, value ?? string.Empty); }
    public string SourceMac { get => _sourceMac; set => Set(ref _sourceMac, value ?? string.Empty); }
    public string DestinationMac { get => _destinationMac; set => Set(ref _destinationMac, value ?? string.Empty); }
    public string VlanText { get => _vlanText; set => Set(ref _vlanText, value ?? "-"); }
    public string StateNumberText { get => _stateNumberText; set => Set(ref _stateNumberText, value ?? "-"); }
    public string SequenceNumberText { get => _sequenceNumberText; set => Set(ref _sequenceNumberText, value ?? "-"); }
    public string SequenceStatus { get => _sequenceStatus; set => Set(ref _sequenceStatus, value ?? "Unknown"); }
    public string TimeAllowedToLiveText { get => _timeAllowedToLiveText; set => Set(ref _timeAllowedToLiveText, value ?? "-"); }
    public string ConfigurationRevisionText { get => _configurationRevisionText; set => Set(ref _configurationRevisionText, value ?? "-"); }
    public string ModelIedName { get => _modelIedName; set => Set(ref _modelIedName, value ?? string.Empty); }
    public string BindingSource { get => _bindingSource; set => Set(ref _bindingSource, string.IsNullOrWhiteSpace(value) ? "Unbound" : value); }
    public string DiagnosticsSummary { get => _diagnosticsSummary; set { if (Set(ref _diagnosticsSummary, value ?? string.Empty)) Raise(nameof(HasDiagnostics)); } }
    public bool HasDiagnostics => !string.IsNullOrWhiteSpace(DiagnosticsSummary);
    public string LastSeenText { get => _lastSeenText; set => Set(ref _lastSeenText, value ?? "-"); }
    public long PacketCount { get => _packetCount; set => Set(ref _packetCount, Math.Max(0, value)); }
    public int ChangedValueCount { get => _changedValueCount; set => Set(ref _changedValueCount, Math.Max(0, value)); }
    public bool Test { get => _test; set => Set(ref _test, value); }
    public bool NeedsCommissioning { get => _needsCommissioning; set => Set(ref _needsCommissioning, value); }

    public string IdentityText => !string.IsNullOrWhiteSpace(GoCbRef) ? GoCbRef : (!string.IsNullOrWhiteSpace(GoId) ? GoId : AppIdText);
    public string FlagsText => Test && NeedsCommissioning ? "TEST • ndsCom" : Test ? "TEST" : NeedsCommissioning ? "ndsCom" : "Normal";
    public string HealthText => HasDiagnostics ? "Attention" : "Healthy";

    public void Apply(GooseStreamSnapshot snapshot)
    {
        AppIdText = snapshot.AppIdText;
        GoCbRef = snapshot.GoCbRef;
        GoId = snapshot.GoId;
        DataSetReference = snapshot.DataSetReference;
        SourceMac = snapshot.SourceMac;
        DestinationMac = snapshot.DestinationMac;
        VlanText = snapshot.VlanText;
        StateNumberText = snapshot.StateNumberText;
        SequenceNumberText = snapshot.SequenceNumberText;
        SequenceStatus = snapshot.SequenceStatus;
        TimeAllowedToLiveText = snapshot.TimeAllowedToLiveText;
        ConfigurationRevisionText = snapshot.ConfigurationRevisionText;
        ModelIedName = snapshot.ModelIedName;
        BindingSource = snapshot.BindingSource;
        DiagnosticsSummary = snapshot.DiagnosticsSummary;
        LastSeenText = snapshot.LastSeenText;
        PacketCount = snapshot.PacketCount;
        ChangedValueCount = snapshot.ChangedValueCount;
        Test = snapshot.Test;
        NeedsCommissioning = snapshot.NeedsCommissioning;
        ApplyLeaves(snapshot.Leaves);
        Raise(nameof(IdentityText));
        Raise(nameof(FlagsText));
        Raise(nameof(HealthText));
        RaisePresentationProperties();
    }

    private void ApplyLeaves(IReadOnlyList<GooseLeafValueSnapshot> snapshots)
    {
        while (Leaves.Count > snapshots.Count)
            Leaves.RemoveAt(Leaves.Count - 1);

        for (var index = 0; index < snapshots.Count; index++)
        {
            if (index >= Leaves.Count)
            {
                Leaves.Add(new GooseLeafValueRow
                {
                    Order = snapshots[index].Order,
                    DataSetIndex = snapshots[index].DataSetIndex
                });
            }

            Leaves[index].Apply(snapshots[index]);
        }
    }
}

public sealed record GooseLeafBindingDefinition(
    int Index,
    string SignalName,
    string SignalReference,
    string FunctionalConstraint,
    string Cdc,
    string BType);

public sealed record GooseStreamBindingDefinition(
    string ModelIedName,
    string Source,
    ushort? AppId,
    string GoCbRef,
    string GoId,
    string DataSetReference,
    uint? ConfigurationRevision,
    IReadOnlyList<GooseLeafBindingDefinition> Leaves);

public sealed record GooseLeafValueSnapshot(
    int Order,
    int DataSetIndex,
    string SignalName,
    string SignalReference,
    string FunctionalConstraint,
    string Cdc,
    string BType,
    string Value,
    string PreviousValue,
    bool IsChanged,
    string BindingSource);

public sealed record GooseStreamSnapshot(
    string StreamKey,
    string AppIdText,
    string GoCbRef,
    string GoId,
    string DataSetReference,
    string SourceMac,
    string DestinationMac,
    string VlanText,
    string StateNumberText,
    string SequenceNumberText,
    string SequenceStatus,
    string TimeAllowedToLiveText,
    string ConfigurationRevisionText,
    string ModelIedName,
    string BindingSource,
    string DiagnosticsSummary,
    string LastSeenText,
    long PacketCount,
    int ChangedValueCount,
    bool Test,
    bool NeedsCommissioning,
    IReadOnlyList<GooseLeafValueSnapshot> Leaves);
