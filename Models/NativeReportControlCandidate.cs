namespace ArIED61850Tester.Models;

public sealed class NativeReportControlCandidate : ObservableObject
{
    private string _dataSetReference = string.Empty;
    private string _reportId = string.Empty;
    private string _confRev = string.Empty;
    private string _integrityPeriodMs = string.Empty;
    private string _enabledState = string.Empty;
    private string _status = "Discovered";

    public string Domain { get; set; } = string.Empty;
    public string LogicalNode { get; set; } = string.Empty;
    public string FunctionalConstraint { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Reference { get; set; } = string.Empty;
    public bool Buffered { get; set; }
    public string DataSetReference { get => _dataSetReference; set { if (Set(ref _dataSetReference, value)) Raise(nameof(Summary)); } }
    public string ReportId { get => _reportId; set => Set(ref _reportId, value); }
    public string ConfRev { get => _confRev; set => Set(ref _confRev, value); }
    public string IntegrityPeriodMs { get => _integrityPeriodMs; set => Set(ref _integrityPeriodMs, value); }
    public string EnabledState { get => _enabledState; set => Set(ref _enabledState, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public List<string> Attributes { get; set; } = new();

    public string DisplayName => string.IsNullOrWhiteSpace(Name) ? Reference : Name;
    public string Mode => Buffered ? "BRCB" : "URCB";
    public string Summary => $"{Mode} {Reference}" + (string.IsNullOrWhiteSpace(DataSetReference) ? string.Empty : $" -> {DataSetReference}");
}
