using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ArIED61850Tester;

/// <summary>
/// Model-first, read-only IEC 61850 browser. Refreshing the browser is purely local.
/// An explicit button is the only way to request a separate read-only MMS RCB check.
/// No discovery, report activation, write, reservation, or reconnect on navigation.
/// </summary>
public partial class MainWindow
{
    private const int IedBrowserWorkspaceIndex = 7;
    private readonly IedBrowserTreeProjection _iedBrowserTree = new();
    private readonly IedBrowserDetailProjection _iedBrowserDetails = new();
    private readonly IedBrowserNavigationTrail _iedBrowserNavigation = new();
    private bool _iedBrowserSyncingSelection;
    private bool _iedBrowserForceRefresh;
    private Iec61850MonitorDevice? _iedBrowserCachedDevice;
    private LiveIedModelDiscoveryDocument? _iedBrowserCachedModel;
    private MmsRcbAvailabilityResult? _iedBrowserCachedSnapshot;
    private IReadOnlyList<RcbExportRow> _iedBrowserCachedRcbRows = Array.Empty<RcbExportRow>();
    private string _iedBrowserCachedSourcePath = string.Empty;
    private DateTime _iedBrowserCachedSourceStamp;
    private Iec61850MonitorDevice? _iedBrowserObservedDevice;
    private MmsRcbAvailabilityResult? _iedBrowserSnapshot;
    private CancellationTokenSource? _iedBrowserReadOperation;

    internal void OpenIedBrowserRcb(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;
        MainTabs.SelectedIndex = IedBrowserWorkspaceIndex;
        RefreshIedBrowser();
    }

    private void IedBrowserRefresh_Click(object sender, RoutedEventArgs e)
    {
        // The operator explicitly requested a new local projection, not MMS discovery.
        _iedBrowserForceRefresh = true;
        RefreshIedBrowser();
    }

    private void IedBrowserExportRcb_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { } device || device.IsBusy || device.IsDemo)
            return;
        IedEditRcbMulti_Click(new Button { Tag = device }, new RoutedEventArgs());
    }

    private async void IedBrowserReadRcb_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device == null || !device.IsConnected || device.IsBusy || _iedBrowserReadOperation != null)
            return;

        var sourceDevice = device;
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        _iedBrowserReadOperation = cts;
        IedBrowserReadRcbButton.IsEnabled = false;
        IedBrowserSubtitle.Text = "Reading RCB availability through an explicit read-only MMS association…";

        try
        {
            var result = await _rcbAvailabilityProbe.CheckAsync(device, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(SelectedDevice, sourceDevice) ||
                !ReferenceEquals(_iedBrowserObservedDevice, sourceDevice) || !sourceDevice.IsConnected ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex)
                return;

            _iedBrowserSnapshot = result;
            RefreshIedBrowser();
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(SelectedDevice, sourceDevice))
                IedBrowserSubtitle.Text = "RCB check cancelled or timed out; no RCB modified.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(SelectedDevice, sourceDevice))
                IedBrowserSubtitle.Text = $"Read-only RCB check failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_iedBrowserReadOperation, cts))
                _iedBrowserReadOperation = null;
            cts.Dispose();
            if (ReferenceEquals(SelectedDevice, sourceDevice))
                IedBrowserReadRcbButton.IsEnabled = sourceDevice.IsConnected && !sourceDevice.IsBusy;
        }
    }

    private void CancelIedBrowserRead()
    {
        _iedBrowserReadOperation?.Cancel();
    }

    private void ObserveIedBrowserDevice(Iec61850MonitorDevice? device)
    {
        if (ReferenceEquals(_iedBrowserObservedDevice, device)) return;
        CancelIedBrowserRead();
        if (_iedBrowserObservedDevice != null)
            _iedBrowserObservedDevice.PropertyChanged -= IedBrowserDevice_PropertyChanged;
        _iedBrowserObservedDevice = device;
        _iedBrowserSnapshot = null;
        if (device != null)
            device.PropertyChanged += IedBrowserDevice_PropertyChanged;
    }

    private void IedBrowserDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Iec61850MonitorDevice.IsConnected)
                                    or nameof(Iec61850MonitorDevice.IsBusy)))
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind,
                new Action(() => IedBrowserDevice_PropertyChanged(sender, e)));
            return;
        }

        if (!ReferenceEquals(sender, _iedBrowserObservedDevice))
            return;

        // A disconnected/replaced association can never supply "live" inspector values.
        if (_iedBrowserObservedDevice?.IsConnected != true)
        {
            CancelIedBrowserRead();
            _iedBrowserSnapshot = null;
        }
        if (MainTabs.SelectedIndex == IedBrowserWorkspaceIndex)
            RefreshIedBrowser();
    }

    private void RefreshIedBrowser()
    {
        if (IedBrowserTab == null || MainTabs.SelectedIndex != IedBrowserWorkspaceIndex)
            return;

        var device = SelectedDevice;
        ObserveIedBrowserDevice(device);
        IedBrowserReadRcbButton.IsEnabled = device?.IsConnected == true &&
                                            device.IsBusy == false && _iedBrowserReadOperation == null;
        IedBrowserExportRcbButton.IsEnabled = device is { IsBusy: false, IsDemo: false };
        if (device == null)
        {
            _iedBrowserCachedDevice = null;
            _iedBrowserNavigation.SelectFromTree(null);
            IedBrowserBackButton.IsEnabled = false;
            _iedBrowserTree.Reset([]);
            IedBrowserTreeGrid.ItemsSource = _iedBrowserTree.Rows;
            _iedBrowserDetails.Reset([]);
            IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;
            IedBrowserSubtitle.Text = "Select an IED in Explorer. Browsing does not open MMS sessions.";
            IedBrowserObjectPath.Text = "No IED selected";
            IedBrowserDetailStatus.Text = "Select an existing engineering model";
            return;
        }

        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        IedBrowserSubtitle.Text = $"{device.Name} · {device.EndpointText} · " +
            (device.IsConnected ? "Connected" : "Offline model") +
            $" · {model?.LogicalDevices.Count ?? 0} LD / {model?.DataSets.Count ?? 0} DataSets / " +
            $"{model?.ReportControls.Count ?? 0} RCB";

        // Source SCL parsing and hierarchical projection are performed only when
        // device/model/check/source really changes, not on every IsBusy/connection notification.
        var sourcePath = device.SclSourcePath ?? string.Empty;
        var stamp = DateTime.MinValue;
        try
        {
            if (sourcePath.Length > 0 && File.Exists(sourcePath))
                stamp = File.GetLastWriteTimeUtc(sourcePath);
        }
        catch (IOException) { /* An unavailable source file cannot poison in-memory browsing. */ }
        catch (UnauthorizedAccessException) { /* Still show the cached/discovered model. */ }

        var changed = _iedBrowserForceRefresh ||
                      !ReferenceEquals(device, _iedBrowserCachedDevice) ||
                      !ReferenceEquals(model, _iedBrowserCachedModel) ||
                      !ReferenceEquals(_iedBrowserSnapshot, _iedBrowserCachedSnapshot) ||
                      !string.Equals(sourcePath, _iedBrowserCachedSourcePath, StringComparison.Ordinal) ||
                      stamp != _iedBrowserCachedSourceStamp;
        if (changed)
        {
            _iedBrowserForceRefresh = false;
            var sameDevice = ReferenceEquals(device, _iedBrowserCachedDevice);
            var priorKey = sameDevice
                ? (IedBrowserTreeGrid.SelectedItem as IedBrowserTreeRow)?.Key
                : null;
            var expandedKeys = sameDevice
                ? _iedBrowserTree.ExpandedKeys()
                : new HashSet<string>(StringComparer.Ordinal);

            SclReportControlInventoryResult? inventory = null;
            if (stamp != DateTime.MinValue)
            {
                try
                {
                    inventory = SclReportControlFilter.InspectFile(
                        sourcePath, EffectiveSclIedName(device), device.SclAccessPointName);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
                                             or InvalidDataException or ArgumentException)
                {
                    IedBrowserDetailStatus.Text = "Source inventory unavailable: " + ex.Message;
                }
            }

            var rcbs = BuildRcbExportRows(device, inventory, _iedBrowserSnapshot);
            if (rcbs.Count == 0 && model != null)
                rcbs = BuildLiveModelRcbRows(model, _iedBrowserSnapshot);
            _iedBrowserCachedRcbRows = rcbs;
            _iedBrowserCachedDevice = device;
            _iedBrowserCachedModel = model;
            _iedBrowserCachedSnapshot = _iedBrowserSnapshot;
            _iedBrowserCachedSourcePath = sourcePath;
            _iedBrowserCachedSourceStamp = stamp;

            var roots = IedBrowserPresentation.Roots(model, _iedBrowserCachedRcbRows);
            _iedBrowserTree.Reset(roots, expandedKeys);
            // Reports is the familiar entry point; selecting the category shows its
            // full RCB list on the right. Preserve explicit expansion on same-IED refresh.
            if (!sameDevice)
            {
                var reports = roots.FirstOrDefault(r => r.Key == "category/reports");
                if (reports is not null)
                {
                    _iedBrowserTree.Expand(reports);
                    var ld = reports.GetChildren().FirstOrDefault();
                    if (ld is not null)
                    {
                        _iedBrowserTree.Expand(ld);
                        var ln = ld.GetChildren().FirstOrDefault();
                        if (ln is not null) _iedBrowserTree.Expand(ln);
                    }
                }
            }

            IedBrowserTreeGrid.ItemsSource = _iedBrowserTree.Rows;
            var selected = priorKey == null ? null : _iedBrowserTree.FindVisible(priorKey);
            selected ??= roots.FirstOrDefault(r => r.Key == "category/reports");
            selected ??= roots.FirstOrDefault();
            // Source identity changed: previous folder objects are no longer authoritative.
            _iedBrowserNavigation.SelectFromTree(selected);
            _iedBrowserSyncingSelection = true;
            try { IedBrowserTreeGrid.SelectedItem = selected; }
            finally { _iedBrowserSyncingSelection = false; }
        }
        RenderIedBrowserSelection(_iedBrowserNavigation.Current ?? IedBrowserTreeGrid.SelectedItem as IedBrowserTreeRow,
            model, device.IsConnected);
    }

    private void IedBrowserTreeToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IedBrowserTreeRow row })
            return;
        _iedBrowserTree.Toggle(row);
        IedBrowserTreeGrid.SelectedItem = row;
        e.Handled = true;
    }

    private void IedBrowserTree_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(sender, IedBrowserTreeGrid) || _iedBrowserSyncingSelection)
            return;
        _iedBrowserNavigation.SelectFromTree(IedBrowserTreeGrid.SelectedItem as IedBrowserTreeRow);
        var model = SelectedDevice?.LiveDiscoveryModel ?? SelectedDevice?.SclWorkspace?.DesignModel;
        RenderIedBrowserSelection(_iedBrowserNavigation.Current,
            model, SelectedDevice?.IsConnected == true);
    }
    private void IedBrowserDetailToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IedBrowserDetailRow detail } || !detail.HasChildren)
            return;
        _iedBrowserDetails.Toggle(detail);
        e.Handled = true;
    }

    private void IedBrowserDetailItem_DoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // The ListViewItem is the click source: never open a previously selected row
        // when an operator double-clicks whitespace or another row.
        if (sender is not System.Windows.Controls.ListViewItem { DataContext: IedBrowserDetailRow detail })
            return;

        if (detail.NavigateTo is { } target)
        {
            if (!_iedBrowserNavigation.Enter(target))
                return;
            SynchronizeIedBrowserTree(target);
            var model = SelectedDevice?.LiveDiscoveryModel ?? SelectedDevice?.SclWorkspace?.DesignModel;
            RenderIedBrowserSelection(target, model, SelectedDevice?.IsConnected == true);
            e.Handled = true;
            return;
        }

        if (!detail.HasChildren)
            return;
        // Quality/time and ordered dataset member rows drill down lazily in place,
        // preserving the selected IEC object and the surrounding report context.
        _iedBrowserDetails.Toggle(detail);
        e.Handled = true;
    }

    private void IedBrowserBack_Click(object sender, RoutedEventArgs e)
    {
        var prior = _iedBrowserNavigation.Back();
        if (prior == null) return;
        SynchronizeIedBrowserTree(prior);
        var model = SelectedDevice?.LiveDiscoveryModel ?? SelectedDevice?.SclWorkspace?.DesignModel;
        RenderIedBrowserSelection(prior, model, SelectedDevice?.IsConnected == true);
    }

    private void SynchronizeIedBrowserTree(IedBrowserTreeRow page)
    {
        // DO/DA pages live exclusively in the right pane; their owning LN stays
        // selected in the left tree, avoiding deep or very long Explorer branches.
        var anchor = page;
        if (page.Key.StartsWith("model/do/", StringComparison.Ordinal) ||
            page.Key.StartsWith("model/da/", StringComparison.Ordinal))
        {
            while (anchor.Kind != "LN" && anchor.Parent is not null)
                anchor = anchor.Parent;
        }

        // Expanding an ancestor batch-resets the virtualized tree and can emit
        // a transient SelectionChanged(null). Suppress those events for the
        // *entire* reveal-and-select operation, or the folder history is lost.
        _iedBrowserSyncingSelection = true;
        try
        {
            _iedBrowserTree.Reveal(anchor);
            var visible = _iedBrowserTree.FindVisible(anchor.Key);
            if (visible is null) return;
            IedBrowserTreeGrid.SelectedItem = visible;
            IedBrowserTreeGrid.ScrollIntoView(visible);
        }
        finally { _iedBrowserSyncingSelection = false; }
    }

    private void RenderIedBrowserSelection(
        IedBrowserTreeRow? row, LiveIedModelDiscoveryDocument? model, bool connected)
    {
        IedBrowserBackButton.IsEnabled = _iedBrowserNavigation.CanGoBack;
        if (row == null)
        {
            IedBrowserObjectPath.Text = "Select an object in the left tree";
            IedBrowserDetailStatus.Text = "Cached engineering model · read-only navigation";
            _iedBrowserDetails.Reset([]);
            IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;
            return;
        }

        var reference = row.Payload switch
        {
            RcbExportRow rcb => rcb.Reference,
            LiveIedDataSetModel ds => ds.Reference,
            LiveIedControlBlockModel block => block.Reference,
            LiveIedDataObjectModel obj => obj.Reference,
            LiveIedDataAttributeModel attr => attr.ObjectReference,
            LiveIedFileModel file => file.Path,
            _ => row.Title
        };
        var segments = new Stack<string>();
        for (var current = row; current != null; current = current.Parent)
            segments.Push(current.Title);
        IedBrowserObjectPath.Text = (SelectedDevice?.Name ?? "IED") +
            " • " + string.Join(" • ", segments);
        IedBrowserObjectPath.ToolTip = reference;
        IedBrowserDetailStatus.Text = row.Payload is RcbExportRow r
            ? r.Inspection.Summary(connected)
            : connected ? "Connected · cached model metadata (not a live read)"
                        : "Offline · cached SCL/discovery model";
        _iedBrowserDetails.Reset(IedBrowserPresentation.Details(row, model, connected));
        IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;
        // Folder entry opens at its first child, regardless of the scroll
        // position in the previously inspected RCB, LN or DataSet.
        if (_iedBrowserDetails.Rows.Count > 0)
            IedBrowserDetailList.ScrollIntoView(_iedBrowserDetails.Rows[0]);
    }

}
