using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Adds a tri-state select-all checkbox to the fault-record Get column. Selecting all
/// affects only rows that are currently eligible for download; clearing always clears
/// every row so stale disabled selections cannot remain hidden.
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
            ToolTip = "Check / uncheck all downloadable fault records"
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
                if (target)
                    row.IsSelected = row.CanSelectForDownload;
                else
                    row.IsSelected = false;
            }
        }
        finally
        {
            _faultRecordHeaderBulkUpdate = false;
        }

        RaiseSelectionState();
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

        var eligibleCount = Records.Count(row => row.CanSelectForDownload);
        header.IsEnabled = !IsBusy && eligibleCount > 0;
        header.IsChecked = GetFaultRecordHeaderSelectionState();
    }

    private bool? GetFaultRecordHeaderSelectionState()
    {
        var eligible = Records.Where(row => row.CanSelectForDownload).ToArray();
        if (eligible.Length == 0)
            return false;

        var selected = eligible.Count(row => row.IsSelected);
        if (selected == 0)
            return false;
        if (selected == eligible.Length)
            return true;
        return null;
    }

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
