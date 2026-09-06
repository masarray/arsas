using System.Windows;
using System.Windows.Controls.Primitives;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using Microsoft.Win32;

namespace ArIED61850Tester;

public partial class RcbExportFilterWindow
{
    private static readonly bool P0MultiRcbRegistered = RegisterP0MultiRcb();
    private bool _p0MultiRcbInstalled;

    private static bool RegisterP0MultiRcb()
    {
        EventManager.RegisterClassHandler(
            typeof(RcbExportFilterWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0MultiRcb_Loaded));
        return true;
    }

    private static void P0MultiRcb_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is RcbExportFilterWindow window)
            window.InstallP0MultiRcb();
    }

    private void InstallP0MultiRcb()
    {
        if (_p0MultiRcbInstalled)
            return;
        _p0MultiRcbInstalled = true;

        // Replace only the export edge. Existing availability probing remains read-only.
        ExportButton.Click -= Export_Click;
        ExportButton.Click += P0ExportSelectedRcbs_Click;
        if (ExportButton.Content is FrameworkElement content)
            content.ToolTip = "Export every checked RCB and its configured DataSet into one CID";

        // Checkbox bindings update RcbExportRow.IsSelected before these routed handlers.
        // Refresh the aggregate footer/button state after both check and uncheck without
        // forcing DataGrid row selection to mirror checkbox selection.
        AddHandler(ToggleButton.CheckedEvent, new RoutedEventHandler(P0RcbSelectionChanged), true);
        AddHandler(ToggleButton.UncheckedEvent, new RoutedEventHandler(P0RcbSelectionChanged), true);
    }

    private void P0RcbSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not System.Windows.Controls.CheckBox checkBox ||
            checkBox.DataContext is not Models.RcbExportRow)
            return;

        _viewModel.NotifySelectionChanged();
        RefreshSelectionUi();
    }

    private async void P0ExportSelectedRcbs_Click(object sender, RoutedEventArgs e)
    {
        if (_activeOperation != null)
            return;

        var selected = _viewModel.SelectedRows;
        if (selected.Count == 0)
        {
            MockStatusText.Text = "Select one or more RCBs before export.";
            RefreshSelectionUi();
            return;
        }

        var confirmationRows = selected.Where(row => row.RequiresConfirmation).ToArray();
        if (confirmationRows.Length > 0)
        {
            var activeCount = confirmationRows.Count(row =>
                row.Availability is MmsRcbOperationalAvailability.InUse or MmsRcbOperationalAvailability.UsedByCaller);
            var noDataSetCount = confirmationRows.Count(row =>
                row.Availability is MmsRcbOperationalAvailability.NoDataSet ||
                row.MemberCount == 0 || row.DataSetName == "—");
            var unknownCount = confirmationRows.Length - activeCount - noDataSetCount;
            var details = string.Join(
                "\n",
                new[]
                {
                    activeCount > 0 ? $"• {activeCount} selected RCB(s) are currently active/in use. This is export evidence, not an export lock." : string.Empty,
                    noDataSetCount > 0 ? $"• {noDataSetCount} selected RCB(s) have no populated static DataSet. ARSAS will not invent a DataSet binding." : string.Empty,
                    unknownCount > 0 ? $"• {unknownCount} selected RCB(s) have unknown or conflicting live availability evidence." : string.Empty
                }.Where(text => text.Length > 0));
            if (MessageBox.Show(
                    this,
                    details + "\n\nContinue with the selected export set?",
                    "Confirm RCB Export Set",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
        }

        var setLabel = selected.Count == 1
            ? $"{selected[0].Name} • {selected[0].DataSetName}"
            : $"{selected.Count} selected RCBs • {selected.Sum(row => row.MemberCount):N0} FCDA";
        var editionDialog = new SaveSclWindow(
            _viewModel.IedName,
            $"Legacy SAS filter • {setLabel}",
            SclSchemaProfile.Edition1V16)
        {
            Owner = this
        };
        if (editionDialog.ShowDialog() != true)
            return;

        var schema = editionDialog.ViewModel.SelectedSchemaProfile;
        if (_viewModel.Options.IsMock)
        {
            MockStatusText.Text = $"UX mock: {selected.Count} RCB(s) prepared for {schema.DisplayName}.";
            return;
        }

        var editionSuffix = schema.IsEdition2 ? "ed2" : "ed1";
        var setSuffix = selected.Count == 1 ? SafeFileStem(selected[0].Name) : $"{selected.Count}-rcbs";
        var fileDialog = new SaveFileDialog
        {
            Title = $"Export legacy SAS CID — {selected.Count} selected RCB(s) — {schema.DisplayName}",
            Filter = "Configured IED Description (*.cid)|*.cid|All files (*.*)|*.*",
            DefaultExt = ".cid",
            AddExtension = true,
            FileName = $"{SafeFileStem(_viewModel.IedName)}-legacy-sas-{setSuffix}-{editionSuffix}.cid"
        };
        if (fileDialog.ShowDialog(this) != true)
            return;

        _activeOperation = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        SetBusyState(true, $"Filtering SCL and validating {selected.Count} retained RCB(s)…");
        try
        {
            RcbExportCompletion completion;
            if (selected.Count == 1 && _viewModel.Options.ExportAsync != null)
            {
                completion = await _viewModel.Options.ExportAsync(
                    selected[0], schema.Profile, fileDialog.FileName, _activeOperation.Token);
            }
            else if (Owner is MainWindow engineeringWindow)
            {
                completion = await engineeringWindow.ExportLegacySasRcbsP0Async(
                    _viewModel.IedName,
                    selected,
                    schema.Profile,
                    fileDialog.FileName,
                    _activeOperation.Token);
            }
            else
            {
                throw new InvalidOperationException("Engineering owner is required for multi-RCB production export.");
            }

            MockStatusText.Text = completion.Message;
            ShowSuccessOverlay(completion);
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
            _viewModel.NotifySelectionChanged();
            RefreshSelectionUi();
        }
    }
}
