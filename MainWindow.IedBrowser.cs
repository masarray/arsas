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
    private Iec61850MonitorDevice? _iedBrowserObservedDevice;
    private MmsRcbAvailabilityResult? _iedBrowserSnapshot;
    private CancellationTokenSource? _iedBrowserReadOperation;

    internal void OpenIedBrowserRcb(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;
        MainTabs.SelectedIndex = IedBrowserWorkspaceIndex;
        IedBrowserSections.SelectedIndex = 0;
        RefreshIedBrowser();
    }

    private void IedBrowserRefresh_Click(object sender, RoutedEventArgs e)
    {
        // Read from existing memory/source SCL only, never an MMS request.
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
        IedBrowserRcbGrid.ItemsSource = null;
        IedBrowserRcbProperties.ItemsSource = null;
        IedBrowserRcbEvidence.Text = "Select a Report Control Block to inspect properties.";
        IedBrowserDataSetGrid.ItemsSource = null;
        IedBrowserDataSetMembers.ItemsSource = null;
        IedBrowserDataSetEvidence.Text = "Select a DataSet to inspect the exact FCDA members.";
        IedBrowserModelTree.Items.Clear();
        IedBrowserModelProperties.ItemsSource = null;
        IedBrowserSignalsGrid.ItemsSource = null;

        if (device == null)
        {
            IedBrowserSubtitle.Text = "Select an IED in Explorer. Inspection does not initiate a connection.";
            IedBrowserReadRcbButton.IsEnabled = false;
            IedBrowserExportRcbButton.IsEnabled = false;
            return;
        }

        IedBrowserReadRcbButton.IsEnabled = device.IsConnected && !device.IsBusy &&
                                            _iedBrowserReadOperation == null;
        IedBrowserExportRcbButton.IsEnabled = !device.IsBusy && !device.IsDemo;

        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        IedBrowserSubtitle.Text = $"{device.Name} · {device.EndpointText} · " +
            (device.IsConnected ? "Connected" : "Offline model") +
            $" · {model?.LogicalDevices.Count ?? 0} LD / {model?.DataSets.Count ?? 0} DataSets / " +
            $"{model?.ReportControls.Count ?? 0} RCB";

        SclReportControlInventoryResult? sourceInventory = null;
        if (!string.IsNullOrWhiteSpace(device.SclSourcePath) && File.Exists(device.SclSourcePath))
        {
            try
            {
                sourceInventory = SclReportControlFilter.InspectFile(
                    device.SclSourcePath, EffectiveSclIedName(device), device.SclAccessPointName);
            }
            catch (Exception ex)
            {
                IedBrowserRcbEvidence.Text = $"Source SCL RCB inventory: {ex.Message}";
            }
        }

        var rows = BuildRcbExportRows(device, sourceInventory, _iedBrowserSnapshot);
        if (rows.Count == 0 && model != null)
            rows = BuildLiveModelRcbRows(model, _iedBrowserSnapshot);
        IedBrowserRcbGrid.ItemsSource = rows;
        IedBrowserDataSetGrid.ItemsSource = model?.DataSets;
        IedBrowserSignalsGrid.ItemsSource = device.Signals;

        if (model == null)
            return;

        // Create only LD/LN nodes now. DO and DA children are materialized upon expansion
        // so large 1000+ DO IEDs do not freeze the WPF dispatcher.
        foreach (var ld in model.LogicalDevices)
        {
            var deviceNode = new TreeViewItem
            {
                Header = $"{ld.MmsDomain} · {ld.LogicalNodes.Count} LN",
                Tag = ld
            };
            foreach (var ln in ld.LogicalNodes)
            {
                var node = new TreeViewItem
                {
                    Header = $"{ln.Name} · {ln.DataObjects.Count} DO",
                    Tag = ln
                };
                if (ln.DataObjects.Count > 0)
                    node.Items.Add(new TreeViewItem { Header = "Loading…" });
                deviceNode.Items.Add(node);
            }
            IedBrowserModelTree.Items.Add(deviceNode);
        }
    }

    private void IedBrowserRcb_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IedBrowserRcbGrid.SelectedItem is not RcbExportRow row)
            return;
        var connected = SelectedDevice?.IsConnected == true;
        IedBrowserRcbEvidence.Text = row.Inspection.Summary(connected) +
            " · Unavailable means not measured (not zero). No RCB write on inspect.";
        IedBrowserRcbProperties.ItemsSource = row.Inspection.Properties(row, connected);
    }

    private void IedBrowserDataSet_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (IedBrowserDataSetGrid.SelectedItem is not LiveIedDataSetModel set)
            return;
        IedBrowserDataSetEvidence.Text = $"{set.Reference} · {set.MemberCount} FCDA" +
            (set.IsDeletable.HasValue ? $" · deletable={set.IsDeletable.Value}" : "") +
            " · saved SCL/discovery membership";
        IedBrowserDataSetMembers.ItemsSource = set.Members;
    }

    private void IedBrowserModelNode_Expanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is not TreeViewItem item ||
            item.Items.Count != 1 || item.Items[0] is not TreeViewItem { Tag: null })
            return;

        if (item.Tag is LiveIedLogicalNodeModel ln)
        {
            item.Items.Clear();
            foreach (var data in ln.DataObjects)
            {
                var node = new TreeViewItem
                {
                    Header = $"{data.Name} · {data.InferredCdc}",
                    Tag = data
                };
                if (data.Attributes.Count > 0)
                    node.Items.Add(new TreeViewItem { Header = "Loading…" });
                item.Items.Add(node);
            }
        }
        else if (item.Tag is LiveIedDataObjectModel obj)
        {
            item.Items.Clear();
            foreach (var attribute in obj.Attributes)
                item.Items.Add(new TreeViewItem
                {
                    Header = $"[{attribute.FunctionalConstraint}] {attribute.AttributePath}",
                    Tag = attribute
                });
        }
    }

    private void IedBrowserModelTree_SelectedItemChanged(object sender,
        RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is not TreeViewItem item)
            return;
        static RcbInspectionProperty P(string name, string value)
            => new(name, string.IsNullOrWhiteSpace(value) ? "—" : value, "Saved model");
        IedBrowserModelProperties.ItemsSource = item.Tag switch
        {
            LiveIedLogicalDeviceModel ld => new[]
            {
                P("MMS Domain", ld.MmsDomain), P("LDevice instance", ld.Inst),
                P("Logical Nodes", ld.LogicalNodes.Count.ToString())
            },
            LiveIedLogicalNodeModel ln => new[]
            {
                P("Logical Node", ln.Name), P("Class", ln.LnClass),
                P("Type", ln.ProposedLnTypeId), P("Data Objects", ln.DataObjects.Count.ToString())
            },
            LiveIedDataObjectModel obj => new[]
            {
                P("Reference", obj.Reference), P("Name", obj.Name),
                P("CDC (inferred)", obj.InferredCdc),
                P("Confidence", obj.ConfidenceLevel.ToString()),
                P("Data Attributes", obj.Attributes.Count.ToString())
            },
            LiveIedDataAttributeModel attr => new[]
            {
                P("Reference", attr.ObjectReference), P("DA path", attr.AttributePath),
                P("FC", attr.FunctionalConstraint), P("MMS Reference", attr.MmsReference),
                P("MMS type", attr.MmsType), P("SCL bType", attr.SclBType),
                P("Type source", attr.TypeSource), P("Confidence", attr.TypeConfidence.ToString())
            },
            _ => Array.Empty<RcbInspectionProperty>()
        };
    }
}
