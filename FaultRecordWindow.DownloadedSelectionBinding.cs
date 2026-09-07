using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Single visual/state authority for downloaded COMTRADE re-download selection.
/// The legacy row model intentionally rejects IsSelected for already-downloaded records,
/// so a notifying proxy owns the native WPF CheckBox while the existing staged-overwrite
/// HashSet remains the transfer authority. No synthetic glyph or manual IsChecked paint is used.
/// </summary>
public partial class FaultRecordWindow
{
    private readonly Dictionary<string, DownloadedSelectionProxy> _downloadedSelectionProxies =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _downloadedSelectionBindingInstalled;

    [ModuleInitializer]
    internal static void RegisterDownloadedSelectionBindingAuthority()
    {
        EventManager.RegisterClassHandler(
            typeof(FaultRecordWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(DownloadedSelectionBinding_WindowLoaded),
            handledEventsToo: true);
    }

    private static void DownloadedSelectionBinding_WindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FaultRecordWindow window || window._downloadedSelectionBindingInstalled)
            return;

        // Install after the normal window Loaded path has completed. This intentionally
        // removes the old manual PreviewMouseDown checkbox toggle and lets WPF TwoWay binding
        // own the native check mark from this point forward.
        window.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(window.InstallDownloadedSelectionBindingAuthority));
    }

    private void InstallDownloadedSelectionBindingAuthority()
    {
        if (_downloadedSelectionBindingInstalled)
            return;

        _downloadedSelectionBindingInstalled = true;
        FaultRecordsGrid.PreviewMouseLeftButtonDown -= RedownloadGrid_PreviewMouseLeftButtonDown;
        FaultRecordsGrid.LoadingRow += DownloadedSelectionBinding_LoadingRow;
        Closed += DownloadedSelectionBinding_Closed;

        RebindVisibleDownloadedSelectionRows();
    }

    private void DownloadedSelectionBinding_Closed(object? sender, EventArgs e)
    {
        FaultRecordsGrid.LoadingRow -= DownloadedSelectionBinding_LoadingRow;
        Closed -= DownloadedSelectionBinding_Closed;
        _downloadedSelectionProxies.Clear();
    }

    private void DownloadedSelectionBinding_LoadingRow(object? sender, DataGridRowEventArgs e)
    {
        // RedownloadUx.LoadingRow was registered first and performs the legacy setup at
        // DispatcherPriority.Loaded. Re-apply the authoritative binding one turn later.
        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => BindDownloadedSelectionRow(e.Row)));
    }

    private void RebindVisibleDownloadedSelectionRows()
    {
        FaultRecordsGrid.UpdateLayout();
        foreach (var row in Records)
        {
            if (FaultRecordsGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
                BindDownloadedSelectionRow(gridRow);
        }
    }

    private void BindDownloadedSelectionRow(DataGridRow gridRow)
    {
        if (gridRow.DataContext is not FaultRecordRow row ||
            row.LocalState != FaultRecordLocalState.Downloaded)
        {
            return;
        }

        var checkBox = FindVisualDescendants<CheckBox>(gridRow).FirstOrDefault();
        if (checkBox == null)
            return;

        var proxy = GetDownloadedSelectionProxy(row);

        // Remove every legacy manual checkbox writer. The native CheckBox gets one TwoWay
        // binding and therefore its visual tick can no longer diverge from transfer state.
        checkBox.Click -= DownloadedRecordCheckBox_Click;
        BindingOperations.ClearBinding(checkBox, ToggleButton.IsCheckedProperty);
        BindingOperations.ClearBinding(checkBox, UIElement.IsEnabledProperty);
        BindingOperations.SetBinding(
            checkBox,
            ToggleButton.IsCheckedProperty,
            new Binding(nameof(DownloadedSelectionProxy.IsSelected))
            {
                Source = proxy,
                Mode = BindingMode.TwoWay,
                UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged
            });
        checkBox.IsEnabled = row.Record.Files.Count > 0 && !IsBusy;
        checkBox.ToolTip = "Already downloaded. Check to download again and safely overwrite the existing local copy.";
    }

    private DownloadedSelectionProxy GetDownloadedSelectionProxy(FaultRecordRow row)
    {
        var recordId = row.Record.RecordId;
        if (_downloadedSelectionProxies.TryGetValue(recordId, out var existing))
            return existing;

        var proxy = new DownloadedSelectionProxy(
            _redownloadSelections.Contains(recordId),
            selected => DownloadedSelectionProxy_Changed(row, selected));
        _downloadedSelectionProxies[recordId] = proxy;
        return proxy;
    }

    private void DownloadedSelectionProxy_Changed(FaultRecordRow row, bool selected)
    {
        if (selected)
            _redownloadSelections.Add(row.Record.RecordId);
        else
            _redownloadSelections.Remove(row.Record.RecordId);

        UpdateSmartSelectionUi();
        RefreshFaultRecordHeaderSelection();
    }

    private bool IsDownloadedTransferSelected(FaultRecordRow row)
        => _downloadedSelectionProxies.TryGetValue(row.Record.RecordId, out var proxy)
            ? proxy.IsSelected
            : _redownloadSelections.Contains(row.Record.RecordId);

    private void SetDownloadedTransferSelection(FaultRecordRow row, bool selected)
    {
        if (row.LocalState != FaultRecordLocalState.Downloaded)
            return;

        var proxy = GetDownloadedSelectionProxy(row);
        proxy.IsSelected = selected;

        // If the state was already equal, the proxy intentionally emits no notification;
        // still synchronize the backing set because scan/local-state transitions can rebuild it.
        if (selected)
            _redownloadSelections.Add(row.Record.RecordId);
        else
            _redownloadSelections.Remove(row.Record.RecordId);

        Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() =>
            {
                ConfigureRecordRow(row); // preserve legacy enabled/tooltip semantics
                if (FaultRecordsGrid.ItemContainerGenerator.ContainerFromItem(row) is DataGridRow gridRow)
                    BindDownloadedSelectionRow(gridRow); // then restore the one authoritative binding
                UpdateSmartSelectionUi();
                RefreshFaultRecordHeaderSelection();
            }));
    }

    private sealed class DownloadedSelectionProxy : INotifyPropertyChanged
    {
        private readonly Action<bool> _changed;
        private bool _isSelected;

        public DownloadedSelectionProxy(bool isSelected, Action<bool> changed)
        {
            _isSelected = isSelected;
            _changed = changed;
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                    return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
                _changed(value);
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }
}
