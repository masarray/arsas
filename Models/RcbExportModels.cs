using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester.Models;

public sealed class RcbExportRow : ObservableObject
{
    private bool _isSelected;
    private int _memberCount;
    private MmsRcbOperationalAvailability _availability = MmsRcbOperationalAvailability.Unknown;
    private MmsRcbAvailabilityConfidence _confidence = MmsRcbAvailabilityConfidence.Unknown;
    private string _statusText = "Not checked";
    private string _reason = "Availability has not been checked against the live IED.";
    private string _owner = string.Empty;

    public string SourceSelectionKey { get; init; } = string.Empty;
    public string ExportName { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public string ScopeText { get; init; } = string.Empty;
    public string Type { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetName { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string DataSetDetail { get; init; } = string.Empty;
    public bool HasEvidenceConflict { get; init; }
    public bool IsSourceBacked { get; init; }
    public bool IsIndexedSource { get; init; }

    public bool IsSelected { get => _isSelected; set => Set(ref _isSelected, value); }

    public int MemberCount
    {
        get => _memberCount;
        set
        {
            if (!Set(ref _memberCount, Math.Max(0, value))) return;
            Raise(nameof(MemberCountText));
            Raise(nameof(IsSelectable));
        }
    }

    public MmsRcbOperationalAvailability Availability
    {
        get => _availability;
        set
        {
            if (!Set(ref _availability, value)) return;
            Raise(nameof(IsSelectable));
            Raise(nameof(RequiresConfirmation));
            Raise(nameof(StatusGlyph));
            Raise(nameof(StatusBrush));
        }
    }

    public MmsRcbAvailabilityConfidence Confidence { get => _confidence; set => Set(ref _confidence, value); }
    public string StatusText
    {
        get => _statusText;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim();
            if (normalized.Equals("Not checked", StringComparison.OrdinalIgnoreCase) &&
                MemberCount == 0 &&
                (string.IsNullOrWhiteSpace(DataSetName) || DataSetName == "—"))
            {
                normalized = "No DataSet";
            }
            Set(ref _statusText, normalized);
        }
    }
    public string Reason { get => _reason; set => Set(ref _reason, value?.Trim() ?? string.Empty); }
    public string Owner { get => _owner; set => Set(ref _owner, value?.Trim() ?? string.Empty); }

    // Availability/ownership is evidence for the operator, not an export lock.
    // Every discovered RCB remains selectable so the exported engineering model
    // can truthfully represent what the IED exposes, including InUse/NoDataSet.
    public bool IsSelectable => true;

    public bool RequiresConfirmation => HasEvidenceConflict || Availability is not MmsRcbOperationalAvailability.Available;

    public string MemberCountText => MemberCount > 0 ? $"{MemberCount:N0} FCDA" : "0 FCDA";
    public string StatusGlyph => Availability switch
    {
        MmsRcbOperationalAvailability.Available => "✅",
        MmsRcbOperationalAvailability.UsedByCaller => "●",
        MmsRcbOperationalAvailability.Unknown => "⚠",
        _ => "❌"
    };
    public Brush StatusBrush => HasEvidenceConflict
        ? BrushFrom(201, 42, 50)
        : Availability switch
        {
            MmsRcbOperationalAvailability.Available => BrushFrom(22, 163, 74),
            MmsRcbOperationalAvailability.UsedByCaller => BrushFrom(37, 99, 235),
            MmsRcbOperationalAvailability.Unknown => BrushFrom(202, 138, 4),
            _ => BrushFrom(201, 42, 50)
        };
    public string SelectionIdentity => string.IsNullOrWhiteSpace(Reference) ? Name : Reference;

    public static string ToStatusText(MmsRcbOperationalAvailability availability)
        => availability switch
        {
            MmsRcbOperationalAvailability.Available => "Available",
            MmsRcbOperationalAvailability.InUse => "In use",
            MmsRcbOperationalAvailability.UsedByCaller => "ARSAS active",
            MmsRcbOperationalAvailability.NoDataSet => "No DataSet",
            MmsRcbOperationalAvailability.DataSetEmpty => "Empty DataSet",
            MmsRcbOperationalAvailability.DataSetUnreadable => "DataSet unreadable",
            _ => "Unknown"
        };

    private static SolidColorBrush BrushFrom(byte red, byte green, byte blue)
    {
        var brush = new SolidColorBrush(Color.FromRgb(red, green, blue));
        brush.Freeze();
        return brush;
    }
}

public sealed class RcbExportWindowOptions
{
    public string IedName { get; init; } = "IED";
    public string Endpoint { get; init; } = string.Empty;
    public bool IsMock { get; init; }
    public bool CanCheckAvailability { get; init; }
    public IReadOnlyList<RcbExportRow> Rows { get; init; } = Array.Empty<RcbExportRow>();
    public Func<CancellationToken, Task<IReadOnlyList<RcbExportRow>>>? RefreshAvailabilityAsync { get; init; }
    public Func<RcbExportRow, SclSchemaProfile, string, CancellationToken, Task<RcbExportCompletion>>? ExportAsync { get; init; }
}

public sealed class RcbExportCompletion
{
    public string OutputPath { get; init; } = string.Empty;
    public string ReportPath { get; init; } = string.Empty;
    public string SummaryPath { get; init; } = string.Empty;
    public string SchemaDisplayName { get; init; } = string.Empty;
    public string RetainedReportControl { get; init; } = string.Empty;
    public string DataSetName { get; init; } = string.Empty;
    public int DataSetMemberCount { get; init; }
    public int RemovedReportControlCount { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class RcbExportFilterViewModel : ObservableObject
{
    private RcbExportRow? _selectedRow;
    private string _availabilityCheckedText;

    public RcbExportFilterViewModel(RcbExportWindowOptions options)
    {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        IedName = string.IsNullOrWhiteSpace(options.IedName) ? "IED" : options.IedName.Trim();
        Endpoint = string.IsNullOrWhiteSpace(options.Endpoint) ? "Offline SCL model" : options.Endpoint.Trim();
        _availabilityCheckedText = options.IsMock
            ? "Mock result loaded • read-only"
            : options.CanCheckAvailability ? "Not checked • press Check Availability" : "Offline SCL • live ownership unknown";
        Rows = new ObservableCollection<RcbExportRow>(SortRows(options.Rows));
    }

    public RcbExportWindowOptions Options { get; }
    public string IedName { get; }
    public string Endpoint { get; }
    public ObservableCollection<RcbExportRow> Rows { get; }
    public Visibility MockBadgeVisibility => Options.IsMock ? Visibility.Visible : Visibility.Collapsed;
    public string SafetyText => Options.IsMock
        ? "Read-only availability mock — no RCB is reserved or modified"
        : "Read-only availability check — status is informational and never hides or locks an RCB from export";

    public RcbExportRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            Raise();
            RaiseSelectionProperties();
        }
    }

    public IReadOnlyList<RcbExportRow> SelectedRows => Rows.Where(row => row.IsSelected).ToArray();
    public string AvailabilityCheckedText { get => _availabilityCheckedText; set => Set(ref _availabilityCheckedText, value ?? string.Empty); }
    public bool CanExport => Rows.Any(row => row.IsSelected);
    public string SelectionSummary
    {
        get
        {
            var selected = SelectedRows;
            if (selected.Count == 0) return "No RCB selected";
            var totalMembers = selected.Sum(row => row.MemberCount);
            return selected.Count == 1
                ? $"{selected[0].Name} • {selected[0].ScopeText} • {selected[0].Type} • {selected[0].DataSetName} • {selected[0].MemberCount:N0} members"
                : $"{selected.Count} RCB selected • {selected.Select(row => row.DataSetName).Where(name => name != "—").Distinct(StringComparer.OrdinalIgnoreCase).Count()} DataSet • {totalMembers:N0} FCDA";
        }
    }
    public string RemovalSummary
    {
        get
        {
            var selectedCount = SelectedRows.Count;
            return $"{selectedCount} retained • {Math.Max(0, Rows.Count - selectedCount)} removed";
        }
    }

    // Historical name retained for XAML/code-behind compatibility. P0 semantics are now
    // additive: a checkbox check never clears another checked RCB. Automatic preferred-row
    // selection only runs when the operator has no selection yet.
    public void SelectOnly(RcbExportRow? row)
    {
        if (row == null)
        {
            SelectedRow = null;
            RaiseSelectionProperties();
            return;
        }

        var anySelected = Rows.Any(candidate => candidate.IsSelected);
        if (row.IsSelected || !anySelected)
        {
            row.IsSelected = true;
            SelectedRow = row;
        }
        RaiseSelectionProperties();
    }

    public void ReplaceRows(IReadOnlyList<RcbExportRow> rows)
    {
        var previousSelections = SelectedRows
            .Select(row => row.SelectionIdentity)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var previousFocus = SelectedRow?.SelectionIdentity;
        Rows.Clear();
        foreach (var row in SortRows(rows))
        {
            row.IsSelected = previousSelections.Contains(row.SelectionIdentity);
            Rows.Add(row);
        }
        _selectedRow = Rows.FirstOrDefault(row =>
            !string.IsNullOrWhiteSpace(previousFocus) &&
            row.SelectionIdentity.Equals(previousFocus, StringComparison.OrdinalIgnoreCase))
            ?? Rows.LastOrDefault(row => row.IsSelected);
        Raise(nameof(SelectedRow));
        RaiseSelectionProperties();
    }

    public void ClearSelection()
    {
        foreach (var row in Rows) row.IsSelected = false;
        _selectedRow = null;
        Raise(nameof(SelectedRow));
        RaiseSelectionProperties();
    }

    public void NotifySelectionChanged()
        => RaiseSelectionProperties();

    private void RaiseSelectionProperties()
    {
        Raise(nameof(SelectedRows));
        Raise(nameof(SelectionSummary));
        Raise(nameof(RemovalSummary));
        Raise(nameof(CanExport));
    }

    private static IEnumerable<RcbExportRow> SortRows(IEnumerable<RcbExportRow> rows)
        => rows.OrderByDescending(row => row.MemberCount > 0)
            .ThenBy(row => AvailabilityRank(row.Availability))
            .ThenByDescending(row => row.Buffered)
            .ThenBy(row => row.Reference, StringComparer.OrdinalIgnoreCase);

    private static int AvailabilityRank(MmsRcbOperationalAvailability availability)
        => availability switch
        {
            MmsRcbOperationalAvailability.Available => 0,
            MmsRcbOperationalAvailability.UsedByCaller => 1,
            MmsRcbOperationalAvailability.InUse => 2,
            MmsRcbOperationalAvailability.Unknown => 3,
            MmsRcbOperationalAvailability.DataSetUnreadable => 4,
            MmsRcbOperationalAvailability.DataSetEmpty => 5,
            MmsRcbOperationalAvailability.NoDataSet => 6,
            _ => 9
        };
}
