using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using AR.Iec61850.Scl.Export;

namespace ArIED61850Tester;

public sealed class RcbExportMockRow : INotifyPropertyChanged
{
    private bool _isSelected;

    public required string Name { get; init; }
    public required string Reference { get; init; }
    public required string Type { get; init; }
    public required string DataSetName { get; init; }
    public required string DataSetDetail { get; init; }
    public required int MemberCount { get; init; }
    public required bool IsAvailable { get; init; }
    public required string StatusText { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value)
                return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }

    public bool IsSelectable => IsAvailable && MemberCount > 0;
    public string MemberCountText => MemberCount > 0 ? $"{MemberCount} FCDA" : "0 FCDA";
    public string StatusGlyph => IsAvailable && MemberCount > 0 ? "✅" : "❌";
    public Brush StatusBrush => IsAvailable && MemberCount > 0
        ? new SolidColorBrush(Color.FromRgb(22, 163, 74))
        : new SolidColorBrush(Color.FromRgb(201, 42, 50));
}

public sealed class RcbExportFilterMockViewModel : INotifyPropertyChanged
{
    private RcbExportMockRow? _selectedRow;
    private string _availabilityCheckedText = "Mock result loaded • read-only";

    public RcbExportFilterMockViewModel(string iedName, string endpoint)
    {
        IedName = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        Endpoint = string.IsNullOrWhiteSpace(endpoint) ? "MMS endpoint" : endpoint.Trim();

        Rows = new ObservableCollection<RcbExportMockRow>
        {
            new()
            {
                Name = "A_BRCB01",
                Reference = $"{IedName}LD0/LLN0.BR.A_BRCB01",
                Type = "Buffered",
                DataSetName = "dsTripEvents",
                DataSetDetail = "Static DataSet • protection events",
                MemberCount = 128,
                IsAvailable = true,
                StatusText = "Available"
            },
            new()
            {
                Name = "A_URCB01",
                Reference = $"{IedName}LD0/LLN0.RP.A_URCB01",
                Type = "Unbuffered",
                DataSetName = "dsBayStatus",
                DataSetDetail = "Static DataSet • status indications",
                MemberCount = 84,
                IsAvailable = true,
                StatusText = "Available"
            },
            new()
            {
                Name = "A_BRCB02",
                Reference = $"{IedName}LD0/LLN0.BR.A_BRCB02",
                Type = "Buffered",
                DataSetName = "dsProtection",
                DataSetDetail = "Static DataSet • protection start/trip",
                MemberCount = 96,
                IsAvailable = false,
                StatusText = "In use"
            },
            new()
            {
                Name = "A_BRCB03",
                Reference = $"{IedName}LD0/LLN0.BR.A_BRCB03",
                Type = "Buffered",
                DataSetName = "dsMeasurements",
                DataSetDetail = "Static DataSet • analog measurements",
                MemberCount = 64,
                IsAvailable = false,
                StatusText = "In use"
            },
            new()
            {
                Name = "A_URCB02",
                Reference = $"{IedName}LD0/LLN0.RP.A_URCB02",
                Type = "Unbuffered",
                DataSetName = "—",
                DataSetDetail = "Empty DataSet",
                MemberCount = 0,
                IsAvailable = false,
                StatusText = "No DataSet"
            }
        };
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string IedName { get; }
    public string Endpoint { get; }
    public ObservableCollection<RcbExportMockRow> Rows { get; }

    public RcbExportMockRow? SelectedRow
    {
        get => _selectedRow;
        set
        {
            if (ReferenceEquals(_selectedRow, value))
                return;
            _selectedRow = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(SelectionSummary));
            OnPropertyChanged(nameof(RemovalSummary));
            OnPropertyChanged(nameof(CanExport));
        }
    }

    public string AvailabilityCheckedText
    {
        get => _availabilityCheckedText;
        set
        {
            if (string.Equals(_availabilityCheckedText, value, StringComparison.Ordinal))
                return;
            _availabilityCheckedText = value;
            OnPropertyChanged();
        }
    }

    public bool CanExport => SelectedRow?.IsSelectable == true;

    public string SelectionSummary => SelectedRow == null
        ? "No RCB selected"
        : $"{SelectedRow.Name} • {SelectedRow.Type} • {SelectedRow.DataSetName} • {SelectedRow.MemberCount} members";

    public string RemovalSummary => SelectedRow == null
        ? $"0 retained • {Rows.Count} unchanged"
        : $"1 retained • {Rows.Count - 1} removed";

    public void SelectOnly(RcbExportMockRow? row)
    {
        foreach (var candidate in Rows)
            candidate.IsSelected = ReferenceEquals(candidate, row);
        SelectedRow = row;
    }

    public void ClearSelection() => SelectOnly(null);

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public partial class RcbExportFilterWindow : Window
{
    private readonly RcbExportFilterMockViewModel _viewModel;
    private bool _selectionUpdateInProgress;

    public RcbExportFilterWindow(string iedName, string endpoint)
    {
        InitializeComponent();
        _viewModel = new RcbExportFilterMockViewModel(iedName, endpoint);
        DataContext = _viewModel;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _viewModel.SelectOnly(_viewModel.Rows.FirstOrDefault(row => row.IsSelectable));
        RefreshSelectionUi();
    }

    private void RcbCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportMockRow row })
            return;

        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.SelectOnly(row);
            RcbGrid.SelectedItem = row;
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }
        RefreshSelectionUi();
    }

    private void RcbCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportMockRow row })
            return;

        if (ReferenceEquals(_viewModel.SelectedRow, row))
            _viewModel.SelectedRow = null;
        RefreshSelectionUi();
    }

    private async void CheckAvailability_Click(object sender, RoutedEventArgs e)
    {
        CheckAvailabilityButton.IsEnabled = false;
        CheckAvailabilityText.Text = "Checking…";
        _viewModel.AvailabilityCheckedText = "Reading RptEna / reservation state…";
        MockStatusText.Text = "Read-only availability check in progress — no RCB will be reserved.";

        await Task.Delay(650);

        _viewModel.AvailabilityCheckedText = $"Checked {DateTime.Now:HH:mm:ss} • read-only";
        CheckAvailabilityText.Text = "Check Availability";
        CheckAvailabilityButton.IsEnabled = true;
        MockStatusText.Text = "Availability refreshed. Select one green RCB for the legacy SAS CID.";
    }

    private void ClearAll_Click(object sender, RoutedEventArgs e)
    {
        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.ClearSelection();
            RcbGrid.SelectedItem = null;
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }
        MockStatusText.Text = "All RCBs cleared. Select exactly one available RCB.";
        RefreshSelectionUi();
    }

    private void SelectAvailable_Click(object sender, RoutedEventArgs e)
    {
        var row = _viewModel.Rows.FirstOrDefault(candidate => candidate.IsSelectable);
        if (row == null)
            return;

        _selectionUpdateInProgress = true;
        try
        {
            _viewModel.SelectOnly(row);
            RcbGrid.SelectedItem = row;
            RcbGrid.ScrollIntoView(row);
        }
        finally
        {
            _selectionUpdateInProgress = false;
        }
        MockStatusText.Text = $"{row.Name} selected. Export will retain this RCB only.";
        RefreshSelectionUi();
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        var selected = _viewModel.SelectedRow;
        if (selected?.IsSelectable != true)
        {
            MockStatusText.Text = "Select exactly one available RCB with a populated DataSet before export.";
            RefreshSelectionUi();
            return;
        }

        var editionDialog = new SaveSclWindow(
            _viewModel.IedName,
            $"Legacy SAS filter • {selected.Name} • {selected.DataSetName}",
            SclSchemaProfile.Edition1V16)
        {
            Owner = this
        };

        if (editionDialog.ShowDialog() == true)
        {
            var edition = editionDialog.ViewModel.SelectedSchemaProfile;
            MockStatusText.Text = $"UX mock: {selected.Name} prepared for {edition.DisplayName}. Engine export will be connected later.";
        }
    }

    private void RefreshSelectionUi()
    {
        ExportButton.IsEnabled = _viewModel.CanExport;
        if (_viewModel.SelectedRow is { } selected)
            MockStatusText.Text = $"{selected.Name} selected • {selected.DataSetName} • {selected.MemberCount} FCDA.";
    }
}
