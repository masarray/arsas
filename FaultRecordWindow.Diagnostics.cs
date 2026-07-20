using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Threading;

namespace ArIED61850Tester;

public partial class FaultRecordWindow
{
    private bool _diagnosticHooksInstalled;
    private string _selectedTransferDiagnostic =
        "No transfer failure has been recorded. Run a download, then select a failed row.";

    static FaultRecordWindow()
    {
        EventManager.RegisterClassHandler(
            typeof(DataGridRow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnFaultRecordRowLoaded),
            handledEventsToo: true);
    }

    public string SelectedTransferDiagnostic
    {
        get => _selectedTransferDiagnostic;
        private set => Set(ref _selectedTransferDiagnostic, value ?? string.Empty);
    }

    private static void OnFaultRecordRowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not DataGridRow row ||
            Window.GetWindow(row) is not FaultRecordWindow)
        {
            return;
        }

        BindingOperations.SetBinding(
            row,
            FrameworkElement.ToolTipProperty,
            new Binding(nameof(FaultRecordRow.Detail))
            {
                Mode = BindingMode.OneWay,
                FallbackValue = string.Empty,
                TargetNullValue = string.Empty
            });
        ToolTipService.SetShowDuration(row, 30_000);
    }

    private void Diagnostics_ContentRendered(object? sender, EventArgs e)
    {
        if (_diagnosticHooksInstalled)
            return;

        _diagnosticHooksInstalled = true;
        Records.CollectionChanged += DiagnosticRecords_CollectionChanged;
        foreach (var row in Records)
            AttachDiagnosticRow(row);
    }

    private void DiagnosticRecords_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (var item in e.NewItems.OfType<FaultRecordRow>())
                AttachDiagnosticRow(item);
        }

        if (e.OldItems != null)
        {
            foreach (var item in e.OldItems.OfType<FaultRecordRow>())
                item.PropertyChanged -= DiagnosticRow_PropertyChanged;
        }
    }

    private void AttachDiagnosticRow(FaultRecordRow row)
    {
        row.PropertyChanged -= DiagnosticRow_PropertyChanged;
        row.PropertyChanged += DiagnosticRow_PropertyChanged;
    }

    private void DiagnosticRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is not FaultRecordRow row ||
            e.PropertyName != nameof(FaultRecordRow.Detail) ||
            string.IsNullOrWhiteSpace(row.Detail) ||
            !row.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        ShowTransferDiagnostic(row, expand: true);
    }

    private void FaultRecordsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FaultRecordsGrid.SelectedItem is FaultRecordRow row &&
            !string.IsNullOrWhiteSpace(row.Detail))
        {
            ShowTransferDiagnostic(
                row,
                expand: row.Status.Equals("Failed", StringComparison.OrdinalIgnoreCase));
        }
    }

    private void ShowTransferDiagnostic(FaultRecordRow row, bool expand)
    {
        void Apply()
        {
            SelectedTransferDiagnostic = BuildVisibleDiagnostic(row);
            if (!ReferenceEquals(FaultRecordsGrid.SelectedItem, row))
                FaultRecordsGrid.SelectedItem = row;
            FaultRecordsGrid.ScrollIntoView(row);
            if (expand)
                TransferDiagnosticsExpander.IsExpanded = true;
        }

        if (Dispatcher.CheckAccess())
            Apply();
        else
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(Apply));
    }

    private string BuildVisibleDiagnostic(FaultRecordRow row)
        => $"Device            : {DeviceName}\r\n" +
           $"Endpoint          : {EndpointText}\r\n" +
           $"Record            : {row.RecordName}\r\n" +
           $"Remote directory  : {row.RemoteDirectory}\r\n" +
           $"Files             : {row.FilesText}\r\n" +
           $"Status            : {row.Status}\r\n" +
           $"Captured local    : {DateTimeOffset.Now:O}\r\n" +
           new string('=', 76) + "\r\n" +
           row.Detail.Trim();

    private void CopyDiagnostic_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(SelectedTransferDiagnostic))
            return;

        try
        {
            Clipboard.SetText(SelectedTransferDiagnostic);
            StatusText = "Transfer diagnostic copied to the clipboard.";
        }
        catch (Exception ex)
        {
            StatusText = $"Could not copy the transfer diagnostic: {ex.Message}";
        }
    }
}