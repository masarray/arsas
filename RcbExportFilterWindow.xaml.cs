using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class RcbExportFilterWindow : Window
{
    private readonly RcbExportFilterViewModel _viewModel;
    private bool _selectionUpdateInProgress;
    private CancellationTokenSource? _activeOperation;

    public RcbExportFilterWindow(RcbExportWindowOptions options)
    {
        InitializeComponent();
        _viewModel = new RcbExportFilterViewModel(options);
        DataContext = _viewModel;
    }

    public RcbExportFilterWindow(string iedName, string endpoint)
        : this(BuildMockOptions(iedName, endpoint))
    {
    }

    protected override void OnClosed(EventArgs e)
    {
        _activeOperation?.Cancel();
        _activeOperation?.Dispose();
        base.OnClosed(e);
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var initial = FirstPreferredRow();
        _viewModel.SelectOnly(initial);
        RcbGrid.SelectedItem = initial;
        CheckAvailabilityButton.IsEnabled = _viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null;
        MockStatusText.Text = initial == null
            ? "No selectable populated RCB is currently available."
            : $"{initial.Name} selected • {initial.DataSetName} • {initial.MemberCount:N0} FCDA.";
        RefreshSelectionUi();
    }

    private void RcbCheckBox_Checked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportRow row })
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

        MockStatusText.Text = $"{row.Name} selected • {row.DataSetName} • {row.MemberCount:N0} FCDA.";
        RefreshSelectionUi();
    }

    private void RcbCheckBox_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_selectionUpdateInProgress || sender is not CheckBox { DataContext: RcbExportRow row })
            return;

        if (ReferenceEquals(_viewModel.SelectedRow, row))
            _viewModel.SelectedRow = null;
        RefreshSelectionUi();
    }

    private async void CheckAvailability_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperation != null)
            return;

        CheckAvailabilityButton.IsEnabled = false;
        CheckAvailabilityText.Text = "Checking…";
        _viewModel.AvailabilityCheckedText = "Reading RptEna, reservation, Owner, and DataSet directory…";
        MockStatusText.Text = "Read-only availability check in progress — no RCB will be reserved or modified.";
        _activeOperation = new CancellationTokenSource(TimeSpan.FromSeconds(35));

        try
        {
            if (_viewModel.Options.IsMock)
            {
                await Task.Delay(650, _activeOperation.Token);
            }
            else
            {
                var refresh = _viewModel.Options.RefreshAvailabilityAsync
                    ?? throw new InvalidOperationException("Connect the IED before checking live RCB availability.");
                var rows = await refresh(_activeOperation.Token);
                _viewModel.ReplaceRows(rows);
                RcbGrid.SelectedItem = _viewModel.SelectedRow;
            }

            _viewModel.AvailabilityCheckedText = $"Checked {DateTime.Now:HH:mm:ss} • read-only";
            MockStatusText.Text = "Availability refreshed. Green is proven free; red is occupied/unusable; yellow requires confirmation.";
        }
        catch (OperationCanceledException)
        {
            _viewModel.AvailabilityCheckedText = "Availability check cancelled or timed out";
            MockStatusText.Text = "No RCB was modified. Retry after confirming MMS connectivity.";
        }
        catch (Exception ex)
        {
            _viewModel.AvailabilityCheckedText = "Availability check failed • no RCB modified";
            MockStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "Check RCB Availability", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            CheckAvailabilityText.Text = "Check Availability";
            CheckAvailabilityButton.IsEnabled = _viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null;
            RefreshSelectionUi();
        }
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

        MockStatusText.Text = "All RCBs cleared. Select exactly one populated RCB.";
        RefreshSelectionUi();
    }

    private void SelectAvailable_Click(object sender, RoutedEventArgs e)
    {
        var row = FirstPreferredRow();
        if (row == null)
        {
            MockStatusText.Text = "No selectable populated RCB is available.";
            return;
        }

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

    private async void Export_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperation != null)
            return;

        var selected = _viewModel.SelectedRow;
        if (selected?.IsSelectable != true)
        {
            MockStatusText.Text = "Select exactly one RCB with a populated DataSet before export.";
            RefreshSelectionUi();
            return;
        }

        if (selected.RequiresConfirmation)
        {
            var warning = selected.Availability == MmsRcbOperationalAvailability.UsedByCaller
                ? "This RCB is active in the current ARSAS session. The CID can be generated, but stop ARSAS reporting before the target SAS tries to reserve or enable this RCB."
                : "Live availability could not be proven from the attributes exposed by this IED. The CID can still be generated, but verify the RCB is not used by another client before importing it.";
            if (MessageBox.Show(this, warning + "\n\nContinue with export?", "Confirm RCB Selection",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
        }

        var editionDialog = new SaveSclWindow(
            _viewModel.IedName,
            $"Legacy SAS filter • {selected.Name} • {selected.DataSetName}",
            SclSchemaProfile.Edition1V16)
        {
            Owner = this
        };
        if (editionDialog.ShowDialog() != true)
            return;

        var schema = editionDialog.ViewModel.SelectedSchemaProfile;
        if (_viewModel.Options.IsMock || _viewModel.Options.ExportAsync == null)
        {
            MockStatusText.Text = $"UX mock: {selected.Name} prepared for {schema.DisplayName}. Production engine export is disabled in demo mode.";
            return;
        }

        var editionSuffix = schema.IsEdition2 ? "ed2" : "ed1";
        var fileDialog = new SaveFileDialog
        {
            Title = $"Export legacy SAS CID — {selected.Name} — {schema.DisplayName}",
            Filter = "Configured IED Description (*.cid)|*.cid|All files (*.*)|*.*",
            DefaultExt = ".cid",
            AddExtension = true,
            FileName = $"{SafeFileStem(_viewModel.IedName)}-legacy-sas-{SafeFileStem(selected.Name)}-{editionSuffix}.cid"
        };
        if (fileDialog.ShowDialog(this) != true)
            return;

        _activeOperation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusyState(true, "Filtering SCL and validating one retained RCB…");
        try
        {
            var completion = await _viewModel.Options.ExportAsync(
                selected,
                schema.Profile,
                fileDialog.FileName,
                _activeOperation.Token);

            MockStatusText.Text = completion.Message;
            var resultText =
                $"Legacy SAS CID export completed.\n\n" +
                $"Schema: {completion.SchemaDisplayName}\n" +
                $"Retained RCB: {completion.RetainedReportControl}\n" +
                $"DataSet: {completion.DataSetName} ({completion.DataSetMemberCount:N0} FCDA)\n" +
                $"Removed RCBs: {completion.RemovedReportControlCount:N0}\n\n" +
                $"CID: {completion.OutputPath}\n" +
                $"Evidence: {completion.ReportPath}";
            MessageBox.Show(this, resultText, "RCB Export Complete", MessageBoxButton.OK, MessageBoxImage.Information);

            var directory = Path.GetDirectoryName(completion.OutputPath);
            if (!string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{completion.OutputPath}\"")
                {
                    UseShellExecute = true
                });
            }
        }
        catch (OperationCanceledException)
        {
            MockStatusText.Text = "Export cancelled or timed out. The source SCL was not modified.";
        }
        catch (Exception ex)
        {
            MockStatusText.Text = ex.Message;
            MessageBox.Show(this, ex.Message, "RCB Export Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            _activeOperation.Dispose();
            _activeOperation = null;
            SetBusyState(false, string.Empty);
            RefreshSelectionUi();
        }
    }

    private RcbExportRow? FirstPreferredRow()
        => _viewModel.Rows.FirstOrDefault(row => row.IsSelectable && row.Availability == MmsRcbOperationalAvailability.Available)
           ?? _viewModel.Rows.FirstOrDefault(row => row.IsSelectable);

    private void SetBusyState(bool busy, string status)
    {
        ExportButton.IsEnabled = !busy && _viewModel.CanExport;
        CheckAvailabilityButton.IsEnabled = !busy && (_viewModel.Options.IsMock || _viewModel.Options.RefreshAvailabilityAsync != null);
        RcbGrid.IsEnabled = !busy;
        if (!string.IsNullOrWhiteSpace(status))
            MockStatusText.Text = status;
    }

    private void RefreshSelectionUi()
    {
        ExportButton.IsEnabled = _activeOperation == null && _viewModel.CanExport;
    }

    private static string SafeFileStem(string? value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string((value ?? "IED")
            .Trim()
            .Select(character => invalid.Contains(character) ? '_' : character)
            .ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "IED" : cleaned;
    }

    private static RcbExportWindowOptions BuildMockOptions(string iedName, string endpoint)
    {
        var name = string.IsNullOrWhiteSpace(iedName) ? "IED" : iedName.Trim();
        var rows = new[]
        {
            MockRow(name, "A_BRCB01", true, "dsTripEvents", "Static DataSet • protection events", 128, MmsRcbOperationalAvailability.Available),
            MockRow(name, "A_URCB01", false, "dsBayStatus", "Static DataSet • status indications", 84, MmsRcbOperationalAvailability.Available),
            MockRow(name, "A_BRCB02", true, "dsProtection", "Static DataSet • protection start/trip", 96, MmsRcbOperationalAvailability.InUse),
            MockRow(name, "A_BRCB03", true, "dsMeasurements", "Static DataSet • analog measurements", 64, MmsRcbOperationalAvailability.InUse),
            MockRow(name, "A_URCB02", false, "—", "Empty DataSet", 0, MmsRcbOperationalAvailability.DataSetEmpty)
        };
        return new RcbExportWindowOptions
        {
            IedName = name,
            Endpoint = endpoint,
            IsMock = true,
            CanCheckAvailability = true,
            Rows = rows
        };
    }

    private static RcbExportRow MockRow(
        string iedName,
        string rcbName,
        bool buffered,
        string dataSet,
        string detail,
        int memberCount,
        MmsRcbOperationalAvailability availability)
        => new()
        {
            Name = rcbName,
            ExportName = rcbName,
            Reference = $"{iedName}LD0/LLN0.{(buffered ? "BR" : "RP")}.{rcbName}",
            Type = buffered ? "Buffered" : "Unbuffered",
            Buffered = buffered,
            DataSetName = dataSet,
            DataSetReference = dataSet == "—" ? string.Empty : $"{iedName}LD0/LLN0.{dataSet}",
            DataSetDetail = detail,
            MemberCount = memberCount,
            Availability = availability,
            Confidence = MmsRcbAvailabilityConfidence.Exact,
            StatusText = RcbExportRow.ToStatusText(availability),
            Reason = availability == MmsRcbOperationalAvailability.Available
                ? "Mock RptEna=false and reservation state free."
                : availability == MmsRcbOperationalAvailability.InUse
                    ? "Mock RptEna=true or reservation is held by another client."
                    : "Mock DataSet is empty."
        };
}
