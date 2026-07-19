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
    public string Type { get; init; } = string.Empty;
    public bool Buffered { get; init; }
    public string DataSetName { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string DataSetDetail { get; init; } = string.Empty;
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
    public string StatusText { get => _statusText; set => Set(ref _statusText, string.IsNullOrWhiteSpace(value) ? "Unknown" : value.Trim()); }
    public string Reason { get => _reason; set => Set(ref _reason, value?.Trim() ?? string.Empty); }
    public string Owner { get => _owner; set => Set(ref _owner, value?.Trim() ?? string.Empty); }

    public bool IsSelectable => MemberCount > 0 && Availability is
        MmsRcbOperationalAvailability.Available or
        MmsRcbOperationalAvailability.UsedByCaller or
        MmsRcbOperationalAvailability.Unknown;

    public bool RequiresConfirmation => Availability is
        MmsRcbOperationalAvailability.Unknown or
        MmsRcbOperationalAvailability.UsedByCaller;

    public string MemberCountText => MemberCount > 0 ? $"{MemberCount:N0} FCDA" : "0 FCDA";
    public string StatusGlyph => Availability switch
    {
        MmsRcbOperationalAvailability.Available => "✅",
        MmsRcbOperationalAvailability.UsedByCaller => "●",
        MmsRcbOperationalAvailability.Unknown => "⚠",
        _ => "❌"
    };
    public Brush StatusBrush => Availability switch
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
        : "Read-only availability check — ARSAS never reserves or modifies an RCB in this window";

    public RcbExportRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value)) return;
            _selectedRow = value;
            Raise(); Raise(nameof(SelectionSummary)); Raise(nameof(RemovalSummary)); Raise(nameof(CanExport));
        }
    }

    public string AvailabilityCheckedText { get => _availabilityCheckedText; set => Set(ref _availabilityCheckedText, value ?? string.Empty); }
    public bool CanExport => SelectedRow?.IsSelectable == true;
    public string SelectionSummary => SelectedRow == null
        ? "No RCB selected"
        : $"{SelectedRow.Name} • {SelectedRow.Type} • {SelectedRow.DataSetName} • {SelectedRow.MemberCount:N0} members";
    public string RemovalSummary => SelectedRow == null
        ? $"0 retained • {Rows.Count} unchanged"
        : $"1 retained • {Math.Max(0, Rows.Count - 1)} removed";

    public void SelectOnly(RcbExportRow? row)
    {
        foreach (var candidate in Rows) candidate.IsSelected = ReferenceEquals(candidate, row);
        SelectedRow = row;
    }

    public void ReplaceRows(IReadOnlyList<RcbExportRow> rows)
    {
        var previous = SelectedRow?.SelectionIdentity;
        Rows.Clear();
        foreach (var row in SortRows(rows)) Rows.Add(row);
        var restored = Rows.FirstOrDefault(row => row.IsSelectable && row.SelectionIdentity.Equals(previous, StringComparison.OrdinalIgnoreCase));
        SelectOnly(restored);
        Raise(nameof(SelectionSummary)); Raise(nameof(RemovalSummary)); Raise(nameof(CanExport));
    }

    public void ClearSelection() => SelectOnly(null);

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
