using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Adds a tri-state select-all checkbox to the fault-record Get column. A record with relay
/// files is selectable whether it is a first download or an intentional re-download. Native
/// first-download rows use FaultRecordRow.IsSelected; downloaded rows use the notifying
/// re-download selection authority so the native WPF checkbox and transfer state cannot diverge.
/// </summary>
public partial class FaultRecordWindow
{
    private static readonly bool FaultRecordHeaderSelectionClassHandlerRegistered =
        RegisterFaultRecordHeaderSelectionClassHandler();

    private readonly HashSet<FaultRecordRow> _faultRecordHeaderObservedRows = new();
    private bool _faultRecordHeaderSelectionInstallScheduled;
    private bool _faultRecordHeaderSelectionInstalled;
    private bool _faultRecordHeaderBulkUpdate;
    private CheckBox? _faultRecordHeaderSelectionCheckBox;

    private static bool RegisterFaultRecordHeaderSelectionClassHandler()
    {
        EventManager.RegisterClassHandler(
            typeof(FaultRecordWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(OnFaultRecordHeaderSelectionWindowLoaded),
            handledEventsToo: true);
        return true;
    }

    private static void OnFaultRecordHeaderSelectionWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FaultRecordWindow window ||
            window._faultRecordHeaderSelectionInstalled ||
            window._faultRecordHeaderSelectionInstallScheduled)
        {
            return;
        }

        window._faultRecordHeaderSelectionInstallScheduled = true;
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                window._faultRecordHeaderSelectionInstallScheduled = false;
                window.EnsureFaultRecordHeaderSelection();
            }));
    }

    private void EnsureFaultRecordHeaderSelection()
    {
        if (_faultRecordHeaderSelectionInstalled)
        {
            RefreshFaultRecordHeaderSelection();
            return;
        }

        var column = FaultRecordsGrid.Columns.FirstOrDefault(candidate =>
            string.Equals(candidate.Header?.ToString(), "Get", StringComparison.OrdinalIgnoreCase));
        if (column == null)
            return;

        var headerCheckBox = new CheckBox
        {
            Width = 16,
            Height = 16,
            IsThreeState = true,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Cursor = Cursors.Hand,
            Focusable = true,
            ToolTip = "Check / uncheck all downloadable or re-downloadable fault records"
        };
        AutomationProperties.SetName(headerCheckBox, "Toggle all downloadable fault records");
        headerCheckBox.Click += FaultRecordHeaderSelectionCheckBox_Click;

        column.Header = headerCheckBox;
        _faultRecordHeaderSelectionCheckBox = headerCheckBox;
        _faultRecordHeaderSelectionInstalled = true;

        PropertyChanged += FaultRecordHeaderSelectionWindow_PropertyChanged;
        Records.CollectionChanged += FaultRecordHeaderSelectionRecords_CollectionChanged;
        Closed += FaultRecordHeaderSelectionWindow_Closed;
        RewireFaultRecordHeaderRows();
        RefreshFaultRecordHeaderSelection();
    }

    private void FaultRecordHeaderSelectionCheckBox_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
        {
            RefreshFaultRecordHeaderSelection();
            return;
        }

        var target = GetFaultRecordHeaderSelectionState() != true;
        _faultRecordHeaderBulkUpdate = true;
        try
        {
            foreach (var row in Records)
            {
                if (!HasTransferableFiles(row))
                {
                    row.IsSelected = false;
                    if (row.LocalState == FaultRecordLocalState.Downloaded)
                        SetDownloadedTransferSelection(row, false);
                    else
                        _redownloadSelections.Remove(row.Record.RecordId);
                    continue;
                }

                if (row.LocalState == FaultRecordLocalState.Downloaded)
                {
                    row.IsSelected = false;
                    SetDownloadedTransferSelection(row, target);
                }
                else
                {
                    _redownloadSelections.Remove(row.Record.RecordId);
                    row.IsSelected = target && row.CanSelectForDownload;
                }
            }
        }
        finally
        {
            _faultRecordHeaderBulkUpdate = false;
        }

        RaiseSelectionState();
        UpdateSmartSelectionUi();
        RefreshFaultRecordHeaderSelection();
    }

    private void FaultRecordHeaderSelectionWindow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        => RefreshFaultRecordHeaderSelection();

    private void FaultRecordHeaderSelectionRecords_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RewireFaultRecordHeaderRows();
        RefreshFaultRecordHeaderSelection();
    }

    private void RewireFaultRecordHeaderRows()
    {
        var current = Records.ToHashSet();
        foreach (var stale in _faultRecordHeaderObservedRows.Where(row => !current.Contains(row)).ToArray())
        {
            stale.PropertyChanged -= FaultRecordHeaderSelectionRow_PropertyChanged;
            _faultRecordHeaderObservedRows.Remove(stale);
        }

        foreach (var row in Records)
        {
            if (!_faultRecordHeaderObservedRows.Add(row))
                continue;

            row.PropertyChanged += FaultRecordHeaderSelectionRow_PropertyChanged;
        }
    }

    private void FaultRecordHeaderSelectionRow_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_faultRecordHeaderBulkUpdate)
            return;

        RefreshFaultRecordHeaderSelection();
    }

    private void RefreshFaultRecordHeaderSelection()
    {
        var header = _faultRecordHeaderSelectionCheckBox;
        if (header == null)
            return;

        var eligibleCount = Records.Count(HasTransferableFiles);
        header.IsEnabled = !IsBusy && eligibleCount > 0;
        header.IsChecked = GetFaultRecordHeaderSelectionState();
    }

    private bool? GetFaultRecordHeaderSelectionState()
    {
        var eligible = Records.Where(HasTransferableFiles).ToArray();
        if (eligible.Length == 0)
            return false;

        var selected = eligible.Count(IsSelectedForTransfer);
        if (selected == 0)
            return false;
        if (selected == eligible.Length)
            return true;
        return null;
    }

    private static bool HasTransferableFiles(FaultRecordRow row)
        => row.Record.Files.Count > 0;

    private bool IsSelectedForTransfer(FaultRecordRow row)
        => row.LocalState == FaultRecordLocalState.Downloaded
            ? IsDownloadedTransferSelected(row)
            : row.IsSelected;

    private void FaultRecordHeaderSelectionWindow_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= FaultRecordHeaderSelectionWindow_PropertyChanged;
        Records.CollectionChanged -= FaultRecordHeaderSelectionRecords_CollectionChanged;
        Closed -= FaultRecordHeaderSelectionWindow_Closed;

        foreach (var row in _faultRecordHeaderObservedRows)
            row.PropertyChanged -= FaultRecordHeaderSelectionRow_PropertyChanged;
        _faultRecordHeaderObservedRows.Clear();

        if (_faultRecordHeaderSelectionCheckBox != null)
            _faultRecordHeaderSelectionCheckBox.Click -= FaultRecordHeaderSelectionCheckBox_Click;
    }
}
