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
    private int _iedBrowserNavigationRenderTicket;
    private long _iedBrowserLastLiveVersion = -1;
    private IedBrowserTreeRow? _iedBrowserRenderedPage;
    private bool? _iedBrowserRenderedConnected;
    private bool _iedBrowserForceRefresh;
    private Iec61850MonitorDevice? _iedBrowserCachedDevice;
    private LiveIedModelDiscoveryDocument? _iedBrowserCachedModel;
    private IedBrowserSchemaIndex? _iedBrowserValueSchemaIndex;
    private MmsRcbAvailabilityResult? _iedBrowserCachedSnapshot;
    private IReadOnlyList<RcbExportRow> _iedBrowserCachedRcbRows = Array.Empty<RcbExportRow>();
    private string _iedBrowserCachedSourcePath = string.Empty;
    private DateTime _iedBrowserCachedSourceStamp;
    private Iec61850MonitorDevice? _iedBrowserObservedDevice;
    private MmsRcbAvailabilityResult? _iedBrowserSnapshot;
    private IReadOnlyList<IedBrowserRemoteFile> _iedBrowserRemoteFiles = Array.Empty<IedBrowserRemoteFile>();
    private Iec61850MonitorDevice? _iedBrowserFileOwner;
    private bool _iedBrowserFilesLoaded;
    private bool _iedBrowserFilesLoading;
    private CancellationTokenSource? _iedBrowserFileReadOperation;
    private CancellationTokenSource? _iedBrowserReadOperation;

    /// <summary>
    /// Task-first navigation for both Open SCL and IED Discovery. This touches
    /// only UI selection and the already-built read-only engineering projection;
    /// connecting/reading still requires the explicit, cancellable Read command.
    /// </summary>
    internal void OpenIedBrowserModel(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;
        MainTabs.SelectedIndex = IedBrowserWorkspaceIndex;
        UpdateNavigationVisuals(IedBrowserWorkspaceIndex, animate: true);
        RefreshIedBrowser();

        var category = _iedBrowserTree.FindVisible("category/model");
        if (category is null) return; // Model unavailable, Browser explains why.

        // Materialize only the first LD and LN. Select its LN to show DO/DA
        // details immediately, without walking every sibling IED object.
        _iedBrowserTree.Expand(category);
        var ld = category.GetChildren().FirstOrDefault();
        if (ld is not null) _iedBrowserTree.Expand(ld);
        var firstNode = ld?.GetChildren().FirstOrDefault();
        var target = firstNode ?? ld ?? category;
        _iedBrowserNavigation.SelectFromTree(target);
        SynchronizeIedBrowserTree(target);
        RenderIedBrowserSelection(target,
            device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel,
            device.IsConnected);
        _iedBrowserLastLiveVersion = -1;
        RefreshIedBrowserVisibleValues();
    }

    internal void OpenIedBrowserRcb(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        SelectedDevice = device;
        MainTabs.SelectedIndex = IedBrowserWorkspaceIndex;
        RefreshIedBrowser();
    }

    private async Task BrowseIedBrowserFilesAsync(Iec61850MonitorDevice device)
    {
        if (_iedBrowserFilesLoading || _iedBrowserReadOperation != null ||
            _iedBrowserFileTransferOperation != null)
            return;
        _iedBrowserFilesLoading = true;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        _iedBrowserFileReadOperation = cts;
        var source = device;
        IedBrowserDetailStatus.Text = "Reading IEC 61850 FileDirectory…";
        try
        {
            // Reuse the exact service in Fault Records / COMTRADE.
            // The service owns its own association and bounded traversal.
            var files = await Task.Run(async () =>
            {
                await using var transfer = new ArIED61850Tester.Services.FaultRecordTransferClient();
                await transfer.ConnectAsync(source.IpAddress, source.Port, cts.Token);
                var catalog = await transfer.DiscoverAsync(remoteDirectory: null, cts.Token);
                return new IedBrowserFileCatalog(catalog);
            }, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested ||
                !ReferenceEquals(source, SelectedDevice) ||
                !ReferenceEquals(source, _iedBrowserFileOwner) || !source.IsConnected)
                return;
            _iedBrowserFileCatalog = files;
            _iedBrowserRemoteFiles = files.Files;
            _iedBrowserFilesLoaded = true;
            _iedBrowserForceRefresh = true;
            RefreshIedBrowser();
            IedBrowserDetailStatus.Text = $"FileDirectory · {files.Files.Count:N0} remote record file(s)";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(source, SelectedDevice))
                IedBrowserDetailStatus.Text = "FileDirectory read cancelled or timed out.";
        }
        catch (Exception ex)
        {
            if (ReferenceEquals(source, SelectedDevice))
                IedBrowserDetailStatus.Text = "FileDirectory read failed: " + ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_iedBrowserFileReadOperation, cts))
            {
                _iedBrowserFileReadOperation = null;
                _iedBrowserFilesLoading = false;
            }
        }
    }

    private void IedBrowserBrowseFiles_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { IsConnected: true } device ||
            _iedBrowserFilesLoading || _iedBrowserReadOperation != null ||
            _iedBrowserFileTransferOperation != null)
        {
            IedBrowserDetailStatus.Text = "Connect this IED to browse remote files.";
            return;
        }
        _iedBrowserFilesLoaded = false;
        _ = BrowseIedBrowserFilesAsync(device);
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

    private void IedBrowserCancelRead_Click(object sender, RoutedEventArgs e)
        => CancelIedBrowserRead();

    private async void IedBrowserReadValues_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        if (device is null || device.IsDemo || device.IsBusy ||
            _iedBrowserReadOperation is not null)
            return;
        // A dedicated read-only MMS association is used if monitoring is active.
        // Its bounded reads never interfere with the reporting session.

        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        var readAll = sender is Button { Tag: "All" };
        var targets = IedBrowserReadPlan.Build(model, _iedBrowserNavigation.Current, readAll);
        if (targets.Count == 0)
        {
            IedBrowserSubtitle.Text = "No model-verified readable FC attributes in this selection.";
            return;
        }
        if (!device.IsConnected && !device.HasSclDesignModel && !device.HasDiscoveryCache)
        {
            IedBrowserSubtitle.Text = "Read requires an MMS connection. Connect/discover this IED first.";
            return;
        }

        var bindingIndex = new IedBrowserReadBindingIndex(device);
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        // Read All may include thousands of attributes. It is cancellable by the
        // operator, never a new continuous acquisition/polling process.
        _iedBrowserReadOperation = cts;
        var sourceDevice = device;
        var successes = 0;
        var failures = 0;
        long readAssociation = -1; // bound after any explicit connect
        // Progress<T> posts asynchronously to WPF: an awaited read sweep
        // can finish before the final posted callbacks execute. Ingestion
        // must be synchronous on the producer side, with coalesced UI drain
        // and a final drain BEFORE disposing the operation token.
        // Declare before binding the callback: a local drain function closes
        // over this inbox and C# definite-assignment rules require the slot.
        IedBrowserReadResultInbox readValues = null!;
        readValues = new IedBrowserReadResultInbox(() =>
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Background,
                new Action(DrainReadResults));
        });
        void DrainReadResults()
        {
            var changed = false;
            readValues.Drain(result =>
            {
                if (!ReferenceEquals(_iedBrowserReadOperation, cts) ||
                    sourceDevice.LiveValues.Association != readAssociation)
                    return; // new association or replaced read flight
                if (result.Error.Length != 0 || string.IsNullOrWhiteSpace(result.Value))
                {
                    failures++;
                    return;
                }
                var accepted = sourceDevice.LiveValues.PublishForAssociation(
                    readAssociation, result.Target.Reference,
                    result.Target.FunctionalConstraint, result.Value,
                    result.Quality, result.DeviceTimestamp, "MMS Read",
                    result.UtcTimeEvidence, result.QualityEvidence);
                if (!accepted) return;
                successes++;
                // Never overwrite a report-owned point during live acquisition.
                if (!sourceDevice.IsMonitoring)
                    bindingIndex.Apply(result);
                changed = true;
            });
            if (changed && ReferenceEquals(SelectedDevice, sourceDevice))
                RefreshIedBrowserVisibleValues();
        }
        device.ResetDiscoveryProgress("Preparing read-only MMS values…",
            readAll ? "Reading all IED values" : "Reading Browser values");
        device.IsBrowserReading = true; // Preserve the ongoing report monitor lifecycle.
        IedBrowserReadPageButton.IsEnabled = false;
        IedBrowserReadAllButton.IsEnabled = false;
        IedBrowserReadRcbButton.IsEnabled = false;
        IedBrowserCancelReadButton.IsEnabled = true;
        try
        {
            if (!device.IsConnected)
            {
                device.BusyStage = "Connecting with verified cached SCL/discovery model…";
                await _runtime.ConnectUsingCachedModelAsync(device, cts.Token);
                if (!device.IsConnected)
                    throw new InvalidOperationException("MMS connection was not established.");
            }

            readAssociation = sourceDevice.LiveValues.Association;
            var readProgress = new Progress<IedBrowserReadProgress>(p =>
            {
                if (ReferenceEquals(_iedBrowserReadOperation, cts) &&
                    ReferenceEquals(SelectedDevice, sourceDevice) &&
                    !cts.IsCancellationRequested &&
                    sourceDevice.LiveValues.Association == readAssociation)
                    sourceDevice.ApplyBrowserReadProgress(p.Completed, p.Total);
            });
            if (device.IsMonitoring)
                await ArIED61850Tester.Services.IedBrowserIsolatedReadService.ReadAsync(
                    device, targets, readValues, readProgress, cts.Token);
            else
                await _runtime.ReadBrowserValuesAsync(device, targets,
                    readValues, readProgress, cts.Token);
            // All producers have returned: consume the final queued results
            // synchronously before calculating counts and closing this flight.
            DrainReadResults();
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedDevice, sourceDevice))
            {
                device.ApplyBrowserReadProgress(targets.Count, targets.Count);
                device.CompleteDiscoveryProgressAnimation();
                IedBrowserSubtitle.Text = $"Read complete · {successes:N0} values / {targets.Count:N0} attributes · {failures:N0} unavailable";
                RefreshIedBrowserVisibleValues();
            }
        }
        catch (OperationCanceledException)
        {
            DrainReadResults(); // retain observations completed before cancellation
            if (ReferenceEquals(SelectedDevice, sourceDevice))
                IedBrowserSubtitle.Text = $"Read cancelled · {successes:N0} value(s) received; no settings changed.";
        }
        catch (Exception ex)
        {
            DrainReadResults(); // retain successful prefix before a transport fault
            if (ReferenceEquals(SelectedDevice, sourceDevice))
            {
                device.MarkDiscoveryFailed(ex.Message);
                IedBrowserSubtitle.Text = "Read failed: " + ex.Message;
            }
        }
        finally
        {
            DrainReadResults();
            if (ReferenceEquals(_iedBrowserReadOperation, cts))
                _iedBrowserReadOperation = null;
            cts.Dispose();
            sourceDevice.IsBrowserReading = false;
            if (ReferenceEquals(SelectedDevice, sourceDevice))
                RefreshIedBrowserReadButtons();
        }
    }


    private void RefreshIedBrowserReadButtons()
    {
        if (IedBrowserReadPageButton == null) return;
        var device = SelectedDevice;
        var connected = device?.IsConnected == true;
        var hasModel = device?.LiveDiscoveryModel != null ||
                       device?.SclWorkspace?.DesignModel != null;
        var canReachIED = connected || !string.IsNullOrWhiteSpace(device?.IpAddress);
        var ready = device is { IsBusy: false, IsDemo: false, IsBrowserReading: false } &&
            hasModel && canReachIED && _iedBrowserReadOperation == null;
        // Opening a browsed SCL is always offline-safe. Explicit Read can then
        // open a cached-model MMS association; no background connect or polling.
        IedBrowserReadPageButton.Content = connected ? "Read" : "Connect & Read";
        IedBrowserReadPageButton.ToolTip = !hasModel
            ? "Open SCL or discover an IED model before reading values."
            : !canReachIED
                ? "Bind an MMS IP address to this IED before reading live values."
                : connected
                    ? "Read this model-verified object through bounded MMS reads."
                    : "Connect using this IED model and read selected values on demand; no RCB or cyclic polling.";
        IedBrowserReadPageButton.IsEnabled = ready;
        IedBrowserReadAllButton.IsEnabled = ready;
        IedBrowserCancelReadButton.IsEnabled = _iedBrowserReadOperation != null;
    }

    private void RefreshIedBrowserVisibleValues()
    {
        if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            SelectedDevice is not { } device)
            return;
        var version = device.LiveValues.Version;
        if (_iedBrowserLastLiveVersion == version) return;
        _iedBrowserLastLiveVersion = version;
        // Unit index is built once per actual received-value revision, not
        // per row or on every render. Unit is authoritative signal metadata.
        var engineeringUnits = IedBrowserEngineeringValuePresentation.BuildUnitIndex(
            device.Signals);
        foreach (var row in _iedBrowserDetails.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.ValueReference)) continue;
            var found = IedBrowserDetailEvidenceResolver.TryResolve(
                device.LiveValues, row, _iedBrowserValueSchemaIndex,
                device.IsMonitoring, out var live, out var observedReference);
            if (!found)
            {
                row.Value = "Not read";
                row.ObservationSource = "";
                row.ObservationTooltip = "";
                continue;
            }
            row.ObservationSource = live.IsStale
                ? "Stale " + live.Source
                : live.Source;
            row.ObservationTooltip =
                $"{observedReference} [{row.FunctionalConstraint}] · {row.ObservationSource} " +
                $"· observed {live.ObservedUtc:yyyy-MM-dd HH:mm:ss} UTC · association {live.Association}";
            // Typed Quality is decoded once at ingress, never guessed from
            // valid-looking text. Offline snapshots remain clearly stale.
            string display;
            if (row.EvidenceKind == "Quality")
                display = live.QualityBits?.FormatField(row.EvidenceField) ??
                    Iec61850QualityPresentation.FormatObserved(live.Value, row.EvidenceField);
            else if (row.EvidenceKind == "Timestamp" && row.EvidenceField.Length > 0)
                display = IedBrowserUtcTimeQualityPresentation.FormatField(
                    live.TimeBits, row.EvidenceField);
            else if (row.EvidenceField.Length == 0)
                display = live.Value;
            else
                display = "Not exposed";
            if (row.IsEngineeringValue && row.EvidenceField.Length == 0)
                display = IedBrowserEngineeringValuePresentation.Format(
                    display, IedBrowserEngineeringValuePresentation.ResolveUnit(
                        engineeringUnits,
                        row.EngineeringUnitReference.Length == 0
                            ? row.ValueReference : row.EngineeringUnitReference,
                        row.FunctionalConstraint));
            row.Value = live.IsStale ? "Offline · " + display : display;
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
        _iedBrowserFileReadOperation?.Cancel();
        _iedBrowserFileTransferOperation?.Cancel();
        _iedBrowserFileReadOperation = null;
        _iedBrowserFilesLoading = false;
        _iedBrowserFileCatalog = null;
        _iedBrowserObservedDevice = device;
        _iedBrowserSnapshot = null;
        _iedBrowserFileOwner = device;
        _iedBrowserRemoteFiles = Array.Empty<IedBrowserRemoteFile>();
        _iedBrowserFilesLoaded = false;
        if (device != null)
            device.PropertyChanged += IedBrowserDevice_PropertyChanged;
    }

    private void IedBrowserDevice_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(Iec61850MonitorDevice.IsConnected)
                                    or nameof(Iec61850MonitorDevice.IsBusy)
                                    or nameof(Iec61850MonitorDevice.IsMonitoring)))
            return;
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(DispatcherPriority.DataBind,
                new Action(() => IedBrowserDevice_PropertyChanged(sender, e)));
            return;
        }

        if (!ReferenceEquals(sender, _iedBrowserObservedDevice))
            return;
        if (e.PropertyName is nameof(Iec61850MonitorDevice.IsMonitoring))
            _iedBrowserForceRefresh = true;

        // A disconnected/replaced association can never supply "live" inspector values.
        if (_iedBrowserObservedDevice?.IsConnected != true)
        {
            _iedBrowserFileReadOperation?.Cancel();
            _iedBrowserFileTransferOperation?.Cancel();
            _iedBrowserFileCatalog = null;
            if (_iedBrowserFilesLoaded)
            {
                _iedBrowserFilesLoaded = false;
                _iedBrowserRemoteFiles = Array.Empty<IedBrowserRemoteFile>();
                _iedBrowserForceRefresh = true;
            }
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
        RefreshIedBrowserReadButtons();
        IedBrowserExportRcbButton.IsEnabled = device is { IsBusy: false, IsDemo: false };
        if (device == null)
        {
            _iedBrowserCachedDevice = null;
            _iedBrowserRenderedPage = null;
            _iedBrowserRenderedConnected = null;
            _iedBrowserNavigation.SelectFromTree(null);
            IedBrowserBackButton.IsEnabled = false;
            _iedBrowserTree.Reset([]);
            IedBrowserTreeGrid.ItemsSource = _iedBrowserTree.Rows;
            ReplaceIedBrowserDetails([]);
            UpdateIedBrowserFilesView(null, null);
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

        // Model index is immutable and independent from transient report/RCB
        // refreshes. Build once per actual engineering schema object.
        if (!ReferenceEquals(_iedBrowserValueSchemaIndex?.SourceModel, model))
            _iedBrowserValueSchemaIndex = model is null
                ? null : new IedBrowserSchemaIndex(model);

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
            // Only *successfully activated* runtime report plans prove caller
            // ownership; a configured RCB name alone never does.
            var callerOwned = _runtime.GetActiveBrowserRcbReferences(device.DeviceId)
                .Select(NormalizeRcbReference)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var report in rcbs)
            {
                if (!callerOwned.Contains(NormalizeRcbReference(report.Reference)))
                    continue;
                report.Availability = AR.Iec61850.Mms.MmsRcbOperationalAvailability.UsedByCaller;
                report.StatusText = "ARSAS active";
                report.Reason = "Successfully activated by the current ARSAS monitor association.";
            }
            _iedBrowserCachedRcbRows = rcbs;
            _iedBrowserCachedDevice = device;
            _iedBrowserCachedModel = model;
            _iedBrowserCachedSnapshot = _iedBrowserSnapshot;
            _iedBrowserCachedSourcePath = sourcePath;
            _iedBrowserCachedSourceStamp = stamp;

            var roots = IedBrowserPresentation.Roots(model, _iedBrowserCachedRcbRows,
                _iedBrowserRemoteFiles, EffectiveSclIedName(device));
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
        var currentPage = _iedBrowserNavigation.Current ?? IedBrowserTreeGrid.SelectedItem as IedBrowserTreeRow;
        // Fast path for routine device notifications: preserve the selected row,
        // focused cell, expanded q/t descendants and right-pane scroll offset.
        if (changed || !ReferenceEquals(currentPage, _iedBrowserRenderedPage) ||
            _iedBrowserRenderedConnected != device.IsConnected)
            RenderIedBrowserSelection(currentPage, model, device.IsConnected);
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
        var chosen = IedBrowserTreeGrid.SelectedItem as IedBrowserTreeRow;
        _iedBrowserNavigation.SelectFromTree(chosen);
        var owner = SelectedDevice;
        var ticket = ++_iedBrowserNavigationRenderTicket;
        // The DataGrid mouse selection and WPF Selector storage are still in
        // transition: never Reset the bound detail ListView in this event.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ticket != _iedBrowserNavigationRenderTicket ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                !ReferenceEquals(SelectedDevice, owner) ||
                !ReferenceEquals(_iedBrowserNavigation.Current, chosen) ||
                !ReferenceEquals(IedBrowserTreeGrid.SelectedItem, chosen))
                return;
            var model = owner?.LiveDiscoveryModel ?? owner?.SclWorkspace?.DesignModel;
            RenderIedBrowserSelection(chosen, model, owner?.IsConnected == true);
            if (chosen?.Key == "category/files" &&
                owner is { IsConnected: true } fileDevice &&
                !_iedBrowserFilesLoaded && !_iedBrowserFilesLoading)
                _ = BrowseIedBrowserFilesAsync(fileDevice);
        }));
    }
    private void IedBrowserDetailToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: IedBrowserDetailRow detail } || !detail.HasChildren)
            return;
        ToggleIedBrowserDetails(detail);
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
        ToggleIedBrowserDetails(detail);
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

    private void ReplaceIedBrowserDetails(IReadOnlyList<IedBrowserDetailRow> rows)
    {
        // Disconnect ListView selection before raising NotifyCollectionChanged
        // Reset. A bound reset during routed selection can corrupt ItemInfo.
        IedBrowserDetailList.SelectedItem = null;
        IedBrowserDetailList.ItemsSource = null;
        _iedBrowserDetails.Reset(rows);
        IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;
    }

    private void ToggleIedBrowserDetails(IedBrowserDetailRow row)
    {
        var owner = SelectedDevice;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                !ReferenceEquals(owner, SelectedDevice) ||
                !_iedBrowserDetails.Rows.Contains(row)) return;
            IedBrowserDetailList.SelectedItem = null;
            IedBrowserDetailList.ItemsSource = null;
            _iedBrowserDetails.Toggle(row);
            IedBrowserDetailList.ItemsSource = _iedBrowserDetails.Rows;
            _iedBrowserLastLiveVersion = -1;
            RefreshIedBrowserVisibleValues();
        }));
    }

    private void RenderIedBrowserSelection(
        IedBrowserTreeRow? row, LiveIedModelDiscoveryDocument? model, bool connected)
    {
        _iedBrowserRenderedPage = row;
        _iedBrowserRenderedConnected = connected;
        IedBrowserBackButton.IsEnabled = _iedBrowserNavigation.CanGoBack;
        UpdateIedBrowserFilesView(row, model);
        if (row == null)
        {
            IedBrowserObjectPath.Text = "Select an object in the left tree";
            IedBrowserDetailStatus.Text = "Cached engineering model · read-only navigation";
            ReplaceIedBrowserDetails([]);
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
        ReplaceIedBrowserDetails(IedBrowserPresentation.Details(row, model, connected));
        _iedBrowserLastLiveVersion = -1;
        RefreshIedBrowserVisibleValues();
        // Folder entry opens at its first child, regardless of the scroll
        // position in the previously inspected RCB, LN or DataSet.
        if (_iedBrowserDetails.Rows.Count > 0)
            IedBrowserDetailList.ScrollIntoView(_iedBrowserDetails.Rows[0]);
    }

}
