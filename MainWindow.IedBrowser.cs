using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

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
    private string? _iedBrowserSelectedDoReference;
    private int _iedBrowserNavigationRenderTicket;
    private long _iedBrowserLastLiveVersion = -1;
    private LiveIedCanonicalModel? _iedBrowserLastCfCanonicalModel;
    private IedBrowserTreeRow? _iedBrowserRenderedPage;
    private bool? _iedBrowserRenderedConnected;
    private bool _iedBrowserForceRefresh;
    private Iec61850MonitorDevice? _iedBrowserCachedDevice;
    private long _iedBrowserCachedAssociation = -1;
    // UI freshness fence only: a monitor/RCB state transition invalidates
    // previous RptEna display proof without modifying the live-value ledger.
    private DateTime _iedBrowserRcbEnabledMinReadUtc = DateTime.MinValue;
    private LiveIedModelDiscoveryDocument? _iedBrowserCachedModel;
    private IedBrowserSchemaIndex? _iedBrowserValueSchemaIndex;
    // Immutable EnumType declarations, never a second live-value store.
    // A SHA-verified SCL source and exact online SG MMS type are both required.
    private IedBrowserSclEnumEvidence? _iedBrowserSclEnumEvidence;
    private Iec61850MonitorDevice? _iedBrowserSclEnumDevice;
    private LiveIedModelDiscoveryDocument? _iedBrowserSclEnumLiveModel;
    private string _iedBrowserSclEnumSourceSha = "";
    private int _iedBrowserSclEnumLoadTicket;
    private string _iedBrowserSclEnumAbsenceReason = "";
    private MmsRcbAvailabilityResult? _iedBrowserCachedSnapshot;
    // Metadata-only finite admission: attempts never cache an observed value.
    // The device.LiveValues ledger remains the sole MMS Report/Read authority.
    private readonly IedBrowserFocusedAdmission _iedBrowserAutoReadAdmission = new();

    private IReadOnlyList<RcbExportRow> _iedBrowserCachedRcbRows = Array.Empty<RcbExportRow>();
    private string _iedBrowserCachedSourcePath = string.Empty;
    private DateTime _iedBrowserCachedSourceStamp;
    private Iec61850MonitorDevice? _iedBrowserObservedDevice;
    private MmsRcbAvailabilityResult? _iedBrowserSnapshot;
    private IedBrowserRcbCheckLease? _iedBrowserSnapshotLease;
    private IReadOnlyList<IedBrowserRemoteFile> _iedBrowserRemoteFiles = Array.Empty<IedBrowserRemoteFile>();
    private Iec61850MonitorDevice? _iedBrowserFileOwner;
    private bool _iedBrowserFilesLoaded;
    private bool _iedBrowserFilesLoading;
    private CancellationTokenSource? _iedBrowserFileReadOperation;
    private CancellationTokenSource? _iedBrowserReadOperation;
    // Last explicit Read's failure reasons only. No secondary scalar store.
    // Model/association/IED identity gates prevent stale errors after reconnect.
    private IedBrowserReadFailureEvidence? _iedBrowserReadFailureEvidence;
    private IedBrowserGooseExpectedIdentity? _iedBrowserGooseExpected;
    private LiveIedDataSetModel? _iedBrowserGooseDirectory;

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
            // Read root FIRST so a slow recursive fault-record scan cannot
            // hide valid MMS files. The existing ARIEC session stays the only
            // wire authority; COMTRADE discovery is an optional enrichment.
            var files = await Task.Run(async () =>
            {
                await using var transfer = new ArIED61850Tester.Services.FaultRecordTransferClient();
                await transfer.ConnectAsync(source.IpAddress, source.Port, cts.Token);
                return await IedBrowserFileDiscoveryPlanner.DiscoverAsync(
                    ct => transfer.ReadRootFileDirectoryAsync(ct),
                    ct => transfer.DiscoverAsync(null, ct),
                    cts.Token,
                    publishRoot: early => Dispatcher.InvokeAsync(() =>
                    {
                        if (cts.IsCancellationRequested ||
                            !ReferenceEquals(_iedBrowserFileReadOperation, cts) ||
                            !ReferenceEquals(source, SelectedDevice) ||
                            !ReferenceEquals(source, _iedBrowserFileOwner) ||
                            !source.IsConnected)
                            return;
                        _iedBrowserFileCatalog = early;
                        _iedBrowserRemoteFiles = early.Files;
                        _iedBrowserFilesLoaded = true;
                        _iedBrowserForceRefresh = true;
                        RefreshIedBrowser();
                        IedBrowserDetailStatus.Text =
                            $"FileDirectory · {early.Files.Count:N0} root entries · checking verified records…";
                    }).Task).ConfigureAwait(false);
            }, cts.Token).ConfigureAwait(true);
            if (cts.IsCancellationRequested ||
                !ReferenceEquals(source, SelectedDevice) ||
                !ReferenceEquals(source, _iedBrowserFileOwner) || !source.IsConnected)
                return;
            _iedBrowserFileCatalog = files.Files;
            _iedBrowserRemoteFiles = files.Files.Files;
            _iedBrowserFilesLoaded = true;
            _iedBrowserForceRefresh = true;
            RefreshIedBrowser();
            var notes = new[] { files.RootWarning, files.CatalogWarning }
                .Where(note => !string.IsNullOrWhiteSpace(note))
                .Concat(files.CatalogDiagnostics).Take(4).ToArray();
            IedBrowserDetailStatus.Text = files.Files.Files.Count > 0
                ? $"FileDirectory · {files.Files.Files.Count:N0} entries" +
                  (notes.Length > 0 ? " · partial inventory (details in tooltip)" : " · verified root/records")
                : "FileDirectory returned no browsable entries. " +
                  (notes.Length == 0 ? "IED directory is empty." : notes[0]);
            IedBrowserDetailStatus.ToolTip = notes.Length == 0
                ? null : string.Join(Environment.NewLine, notes);
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
        // The toolbar action must reveal the Files workspace before
        // starting its read; otherwise a successful directory response is
        // invisible while the operator stays on SG/Data Model.
        var filesPage = _iedBrowserTree.FindVisible("category/files");
        if (filesPage is not null)
        {
            _iedBrowserSyncingSelection = true;
            try { IedBrowserTreeGrid.SelectedItem = filesPage; }
            finally { _iedBrowserSyncingSelection = false; }
            _iedBrowserNavigation.SelectFromTree(filesPage);
            RenderIedBrowserSelection(filesPage,
                device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel,
                device.IsConnected);
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
        // A 35-second read-only check may outlive a fast disconnect/reconnect.
        // Freeze the existing per-IED association and schema identities now.
        var checkLease = new IedBrowserRcbCheckLease(sourceDevice);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(35));
        _iedBrowserReadOperation = cts;
        IedBrowserReadRcbButton.IsEnabled = false;
        IedBrowserSubtitle.Text = "Reading RCB availability through an explicit read-only MMS association…";

        try
        {
            var result = await _rcbAvailabilityProbe.CheckAsync(device, cts.Token);
            if (cts.IsCancellationRequested || !ReferenceEquals(SelectedDevice, sourceDevice) ||
                !ReferenceEquals(_iedBrowserObservedDevice, sourceDevice) ||
                !checkLease.IsCurrent(sourceDevice) ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex)
                return;

            _iedBrowserSnapshotLease = checkLease;
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

    private async void IedBrowserExportReadEvidence_Click(object sender, RoutedEventArgs e)
    {
        var device = SelectedDevice;
        var model = device?.LiveDiscoveryModel;
        var page = _iedBrowserNavigation.Current;
        if (device is null || model is null || page is null)
        {
            IedBrowserSubtitle.Text =
                "Read evidence export requires an online-verified model and selected object.";
            return;
        }
        IedBrowserReadFailureSample? ResolveAbsence(IedBrowserReadTarget target)
        {
            if (_iedBrowserReadFailureEvidence?.TryGet(device,
                    target.Reference, "", target.FunctionalConstraint,
                    out var reason, out var kind) == true)
                return new IedBrowserReadFailureSample(
                    target.Reference, target.FunctionalConstraint, reason, kind);
            if (device.InitialHydrationFailures?.TryGet(device,
                    target.Reference, "", target.FunctionalConstraint,
                    out reason, out kind) == true)
                return new IedBrowserReadFailureSample(
                    target.Reference, target.FunctionalConstraint, reason, kind);
            return null;
        }
        // Validate the EXACT same bounded selection used for exported rows.
        // A category, Report control or stale/no-target page is not a read.
        var exportTargets = IedBrowserReadEvidenceCsv.Plan(model, page);
        if (exportTargets.Count == 0)
        {
            IedBrowserSubtitle.Text =
                $"ZERO_TARGETS · {page.Kind}: {page.Title} · " +
                "No verified FC attributes for this page. Select a DataSet, LD, LN, DO or DA.";
            return;
        }
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "ARSAS-IEC-read-evidence-" +
                       DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".csv",
            DefaultExt = ".csv",
            AddExtension = true,
            Filter = "Read-only IEC CSV evidence (*.csv)|*.csv|" +
                     "Selected-page evidence bundle (*.zip)|*.zip"
        };
        if (dialog.ShowDialog(this) != true) return;
        try
        {
            var csv = IedBrowserReadEvidenceCsv.BuildSelection(
                exportTargets, device.LiveValues, ResolveAbsence);
            if (dialog.FilterIndex == 2)
            {
                var appVersion = typeof(MainWindow).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "NOT_CAPTURED";
                var engineVersion = typeof(AR.Iec61850.Mms.MmsDataValue).Assembly
                    .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
                    .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
                    .FirstOrDefault()?.InformationalVersion ?? "NOT_CAPTURED";
                var association = device.LiveValues.Association;
                var capturedAtUtc = DateTimeOffset.UtcNow;
                var bundle = await Task.Run(() =>
                    IedBrowserReadEvidenceBundle.Build(
                        csv, appVersion, engineVersion, association, capturedAtUtc));
                var exportPath = System.IO.Path.ChangeExtension(dialog.FileName, ".zip");
                await System.IO.File.WriteAllBytesAsync(exportPath, bundle);
            }
            else
            {
                await System.IO.File.WriteAllTextAsync(dialog.FileName, csv,
                    new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            }
            IedBrowserSubtitle.Text =
                $"Read evidence exported · {exportTargets.Count} exact FC targets · " +
                "ledger/failure evidence; GVA/invoke not instrumented.";
        }
        catch (Exception ex)
        {
            IedBrowserSubtitle.Text = "Read evidence export failed: " + ex.Message;
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

        var configRequest = (sender as Button)?.Tag as IedBrowserVisibleConfigurationReadRequest;
        var companionRequest = (sender as Button)?.Tag as IedBrowserExactCompanionReadRequest;
        // A selected Report's CF unit hydration is a READ OF THE REFERENCED
        // DATASET METADATA, not an RCB Inspect action. It must enter the
        // ordinary typed Read → association-fenced LiveValues pipeline.
        var reportUnitRequest = sender is Button { Tag: "ReportUnitAuto" };
        var automatic = sender is Button { Tag: "Auto" } ||
            configRequest is not null || companionRequest is not null || reportUnitRequest;
        var readAll = sender is Button { Tag: "All" };
        if (!readAll && !reportUnitRequest && companionRequest is null)
        {
            switch (IedBrowserReadPlan.SelectionKind(_iedBrowserNavigation.Current))
            {
                case IedBrowserReadSelectionKind.ReadOnlyRcb:
                    if (!device.IsConnected)
                    {
                        IedBrowserSubtitle.Text = "Connect the IED before read-only RCB inspection.";
                        return;
                    }
                    IedBrowserReadRcb_Click(sender, e);
                    return;
                case IedBrowserReadSelectionKind.GooseInspectionRequiresSubscriber:
                    // A verified MMS GoEna/ndsCom read is allowed. GOOSE
                    // frame reception still requires the passive subscriber.
                    break;
            }
        }
        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        var currentPage = _iedBrowserNavigation.Current;
        var selectedDo = !readAll && sender is not Button { Tag: "LN" } &&
            ReferenceEquals(_iedBrowserRenderedPage, currentPage) &&
            currentPage?.Payload is LiveIedLogicalNodeModel
                ? _iedBrowserSelectedDoReference : null;
        var targets = IedBrowserReadPlan.Build(
            model, currentPage, readAll, selectedDo);
        // No cardinality cut-off: Read All retains every exact model/FC
        // target, but expedites verified FC-root coverage before slow leaves.
        if (readAll && !automatic)
            targets = IedBrowserReadPlan.PrioritizeExplicitFullRead(targets);
        if (!automatic && !readAll && selectedDo is null &&
            currentPage?.Kind == "LN")
            targets = IedBrowserReadPlan.PrioritizeExplicitLn(targets);
        if (automatic)
        {
            if (reportUnitRequest)
            {
                if (currentPage is not { Kind: "RCB", Payload: RcbExportRow })
                    return;
                targets = IedBrowserMeasurementMetadataReadPlan.BuildForPage(
                    device.LiveDiscoveryModel, currentPage, device.LiveValues);
            }
            else if (companionRequest is not null)
            {
                if (!ReferenceEquals(_iedBrowserRenderedPage, currentPage) ||
                    !_iedBrowserDetails.Rows.Any(x => x.IsExpanded &&
                        x.EvidenceKind == companionRequest.Kind &&
                        string.Equals(IedBrowserLiveValues.CanonicalReference(x.ValueReference),
                            IedBrowserLiveValues.CanonicalReference(companionRequest.Reference),
                            StringComparison.Ordinal) &&
                        string.Equals(x.FunctionalConstraint, companionRequest.Fc,
                            StringComparison.OrdinalIgnoreCase)))
                    return;
                targets = IedBrowserExactCompanionReadRequest.Build(
                    device.LiveDiscoveryModel, currentPage, device.LiveValues,
                    companionRequest.Reference, companionRequest.Fc, companionRequest.Kind);
            }
            else if (configRequest is not null)
            {
                // Expanded phase/Nameplate metadata: exact missing CF/DC leaves
                // of the current verified LN. A stale/collapsed row may not read.
                if (!ReferenceEquals(_iedBrowserRenderedPage, currentPage) ||
                    !_iedBrowserDetails.Rows.Any(x => x.IsExpanded &&
                        string.Equals(x.ReadObjectReference,
                            configRequest.Scope, StringComparison.Ordinal)))
                    return;
                targets = IedBrowserVisibleConfigurationReadRequest.Build(
                    device.LiveDiscoveryModel, currentPage,
                    configRequest.Scope, device.LiveValues);
            }
            else
            {
                // Existing bounded process-value hydration; no IED-wide scan.
                // During SGCB continuation, failures from the PREVIOUS bounded
                // chunk cannot be mistaken for unattempted leaves. Their exact
                // association-bound absence evidence is reused, but a manual
                // Read always permits an explicit retry.
                Func<IedBrowserReadTarget, bool>? skipFailed = null;
                if (currentPage?.Kind == "SGCB" &&
                    _iedBrowserReadFailureEvidence is { } previousFailures &&
                    previousFailures.IsCurrentFor(device))
                    skipFailed = target => previousFailures.TryGet(
                        device, target.Reference, "", target.FunctionalConstraint,
                        out _, out var kind) && kind != IedBrowserReadAbsenceKind.Deferred;
                targets = IedBrowserReadPlan.BuildFocused(
                    device.LiveDiscoveryModel, currentPage, device.LiveValues,
                    selectedDoReference: selectedDo, skipPreviouslyFailed: skipFailed);
            }
        }
        // A finite focused queue may contain only 32 targets, while ONE
        // verified LN$FC0 wire result contains many more exact children.
        // Admit those siblings only from the CURRENT live MMS model and only
        // beneath FC roots already selected by this same page's queue.
        // Configuration/companion probes cannot widen their explicit scope.
        IReadOnlyList<IedBrowserReadTarget>? verifiedRootScope = null;
        if (sender is Button { Tag: "Auto" } &&
            currentPage?.Kind is "SGCB" or "LN" or "DO" &&
            ReferenceEquals(model, device.LiveDiscoveryModel))
        {
            var fullPage = IedBrowserReadPlan.Build(model, currentPage,
                readAll: false, selectedDoReference: selectedDo);
            verifiedRootScope = IedBrowserTypedRootScope.Build(targets, fullPage);
        }
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

        // Plan transparency: the clicked scope, not whichever DO is visually
        // expanded elsewhere, determines Read targets. Show FC admission so a
        // bounded focused sample cannot be mistaken for a whole LN Read.
        var scheduledFc = IedBrowserReadPlan.DescribeScheduledFunctionalConstraints(targets);
        var readScope = readAll ? "IED" :
            selectedDo is not null ? "DO" :
            currentPage?.Kind ?? "Selection";
        // Admission is claimed only after a concrete, nonempty verified plan
        // reaches the read handler. A queued Dispatcher callback is NOT a read.
        // Rejected/busy/cancelled preflight cannot poison a future visit.
        var automaticKey = automatic && currentPage is not null
            ? IedBrowserFocusedAdmission.PageKey(
                currentPage.Key, reportUnitRequest ? "|unit-CF" :
                    companionRequest is not null
                        ? "|raw-" + companionRequest.Reference + "-" + companionRequest.Fc
                        : configRequest?.Scope ?? selectedDo)
            : null;
        if (automaticKey is not null &&
            !_iedBrowserAutoReadAdmission.TryStart(device.DeviceId,
                device.LiveValues.Association, automaticKey,
                configRequest is not null ? 1 :
                companionRequest is not null ? 2 :
                reportUnitRequest ? 3 :
                currentPage!.Kind == "SGCB" ? 3 :
                currentPage.Kind is "LN" or "DO" ? 2 : 1))
            return;

        // A cached-model connection can replace the catalog. Bind only after
        // connection and reject a previously planned schema.
        IedBrowserReadBindingIndex? bindingIndex = null;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(_applicationCancellation.Token);
        if (automatic) cts.CancelAfter(TimeSpan.FromSeconds(15));
        // Read All may include thousands of attributes. It is cancellable by the
        // operator, never a new continuous acquisition/polling process.
        _iedBrowserReadOperation = cts;
        // Only successive auto SGCB chunks may reuse exact negative evidence.
        // All explicit Reads and other selections start a fresh failure sweep.
        if (!automatic || currentPage?.Kind != "SGCB")
            _iedBrowserReadFailureEvidence = null;
        var sourceDevice = device;
        var successes = 0;
        var failures = 0;
        var unverifiedReadTargets = 0;
        var autoContinue = false;
        // Bounded diagnostic examples; these are never another value cache.
        var failureExamples = new List<string>(3);
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
                    sourceDevice.LiveValues.Association != readAssociation ||
                    bindingIndex is not { IsCurrent: true })
                    return; // superseded association, catalog or read flight
                var scalarValid = IedBrowserReadValuePolicy.TryAcceptVerifiedMmsString(
                    result.Value, result.Target.DataType, result.VerifiedStringWireKind,
                    out var safeValue, out var scalarReason);
                if (result.Error.Length != 0 || !scalarValid)
                {
                    failures++;
                    var message = result.Error.Length != 0
                        ? result.Error : scalarReason;
                    _iedBrowserReadFailureEvidence?.RecordFailure(result.Target, message);
                    changed = true; // failed reads are visible even with zero scalar updates
                    if (failureExamples.Count < 3)
                    {
                        if (message.Length > 180)
                            message = message[..180] + "…";
                        failureExamples.Add(
                            $"{IedBrowserLiveValues.CanonicalReference(result.Target.Reference)} " +
                            $"[{result.Target.FunctionalConstraint}]: {message}");
                    }
                    return;
                }
                var accepted = sourceDevice.LiveValues.PublishForAssociation(
                    readAssociation, result.Target.Reference,
                    result.Target.FunctionalConstraint, safeValue,
                    result.Quality, result.DeviceTimestamp, "MMS Read",
                    result.UtcTimeEvidence, result.QualityEvidence,
                    typedBoolean: result.TypedBoolean,
                    verifiedStringWireKind: result.VerifiedStringWireKind,
                    declaredDataType: result.Target.DataType,
                    readWireShapeEvidence: result.WireShapeEvidence);
                if (!accepted) return;
                _iedBrowserReadFailureEvidence?.ClearFailure(result.Target);
                successes++;
                // All non-control Device Explorer signals can reflect the
                // same ledger even during monitoring if Report has not arrived.
                // Runtime/SOE point rows remain exclusively runtime-owned.
                bindingIndex.Apply(result, sourceDevice.LiveValues, readAssociation);
                changed = true;
            });
            if (changed && ReferenceEquals(SelectedDevice, sourceDevice))
            {
                _iedBrowserLastLiveVersion = -1; // failure-only drain also changes row evidence
                RefreshIedBrowserVisibleValues();
            }
        }
        if (!automatic)
            IedBrowserSubtitle.Text =
                $"Read {readScope} · {targets.Count:N0} verified target(s) · FC {scheduledFc}";
        if (!automatic)
            device.ResetDiscoveryProgress("Preparing read-only MMS values…",
            readAll ? "Reading all IED values" : selectedDo is not null
                ? "Reading selected DO " + selectedDo : "Reading Browser values");
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

            // Pre-connect targets cannot be executed against a replaced model.
            if (!ReferenceEquals(model,
                    sourceDevice.LiveDiscoveryModel ?? sourceDevice.SclWorkspace?.DesignModel))
                throw new InvalidOperationException(
                    "Browser model changed during connection; select the current model and retry Read.");
            readAssociation = sourceDevice.LiveValues.Association;
            bindingIndex = new IedBrowserReadBindingIndex(sourceDevice);
            if (_iedBrowserReadFailureEvidence?.IsCurrentFor(sourceDevice) != true)
                _iedBrowserReadFailureEvidence = new IedBrowserReadFailureEvidence(sourceDevice);
            var readProgress = new Progress<IedBrowserReadProgress>(p =>
            {
                // Progress belongs to the device that owns this read flight,
                // not whichever IED card the operator happens to select now.
                // The flight token and association still fence late callbacks.
                if (ReferenceEquals(_iedBrowserReadOperation, cts) &&
                    !cts.IsCancellationRequested &&
                    sourceDevice.LiveValues.Association == readAssociation)
                    sourceDevice.ApplyBrowserReadProgress(p.Completed, p.Total);
            });
            if (device.IsMonitoring)
                await ArIED61850Tester.Services.IedBrowserIsolatedReadService.ReadAsync(
                    device, targets, readValues, readProgress, cts.Token,
                    automatic: automatic, verifiedRootScope: verifiedRootScope);
            else
                await _runtime.ReadBrowserValuesAsync(device, targets,
                    readValues, readProgress, cts.Token, verifiedRootScope);
            // All producers have returned: consume the final queued results
            // synchronously before calculating counts and closing this flight.
            DrainReadResults();
            if (bindingIndex is not { IsCurrent: true })
                throw new InvalidOperationException(
                    "Browser catalog changed during Read; obsolete result rows were not admitted.");
            if (!cts.IsCancellationRequested && ReferenceEquals(SelectedDevice, sourceDevice))
            {
                if (!automatic)
                {
                    device.ApplyBrowserReadProgress(targets.Count, targets.Count);
                    device.CompleteDiscoveryProgressAnimation();
                }
                // LN/DO/DataSet focused reads can be narrower than the
                // declared page or fail to yield a terminal callback. Preserve
                // both cases as deferred/unknown, never as an IED AccessDenied.
                // Only the current explicit scope is accounted; metadata,
                // companion reads and unrelated model pages are excluded.
                if (!readAll && currentPage?.Kind is "LN" or "DO" or "DA" or "DataSet" &&
                    configRequest is null && companionRequest is null &&
                    !reportUnitRequest)
                {
                    var declaredSelection = IedBrowserReadPlan.Build(
                        model, currentPage, readAll: false, selectedDoReference: selectedDo);
                    bool IsObserved(IedBrowserReadTarget target)
                        => sourceDevice.LiveValues.TryGet(
                            target.Reference, target.FunctionalConstraint, out var value) &&
                           !value.IsStale && value.Association == readAssociation;
                    bool HasTerminalFailure(IedBrowserReadTarget target)
                    {
                        if (_iedBrowserReadFailureEvidence is not { } evidence)
                            return false;
                        return evidence.TryGet(sourceDevice, target.Reference, "",
                                   target.FunctionalConstraint, out _, out var kind) &&
                               kind != IedBrowserReadAbsenceKind.Deferred;
                    }
                    var gaps = IedBrowserReadCompletionAccounting.Classify(
                        declaredSelection, targets, IsObserved, HasTerminalFailure);
                    unverifiedReadTargets = gaps.Count;
                    foreach (var gap in gaps)
                        _iedBrowserReadFailureEvidence?.RecordDeferred(gap.Target,
                            gap.Kind == IedBrowserDeferredReadKind.NotScheduled
                                ? "Not scheduled in this bounded selected-object Read."
                                : "Read queued but no terminal result for this FC; on-wire attempt unverified.");
                }

                if (!readAll && currentPage?.Kind == "SGCB")
                {
                    // The completed 32-target focused chunk is NOT the total
                    // model inventory. Expose the accounted deferred remainder,
                    // including leaves never scheduled for MMS on this selection.
                    var declared = IedBrowserReadPlan.Build(model, currentPage, readAll: false);
                    var observed = 0;
                    var unavailable = 0;
                    foreach (var target in declared)
                    {
                        if (sourceDevice.LiveValues.TryGet(target.Reference,
                                target.FunctionalConstraint, out var present) &&
                            !present.IsStale && present.Association == readAssociation)
                        {
                            observed++;
                            continue;
                        }
                        if (_iedBrowserReadFailureEvidence?.TryGet(
                                sourceDevice, target.Reference, "",
                                target.FunctionalConstraint, out _, out var kind) == true &&
                            kind != IedBrowserReadAbsenceKind.Deferred)
                        {
                            unavailable++;
                            continue;
                        }
                        _iedBrowserReadFailureEvidence?.RecordDeferred(target,
                            "Not yet scheduled within the bounded selected SGCB read; " +
                            "use explicit Read to request all verified SG leaves.");
                    }
                    var deferred = Math.Max(0, declared.Count - observed - unavailable);
                    IedBrowserSubtitle.Text =
                        $"SGCB focused batch · {successes:N0}/{targets.Count:N0} received · " +
                        $"{observed:N0}/{declared.Count:N0} observed · " +
                        $"{unavailable:N0} unavailable · {deferred:N0} deferred";
                }
                else
                    IedBrowserSubtitle.Text =
                        $"Read {readScope} finished · {successes:N0}/{targets.Count:N0} observed · " +
                        $"{failures:N0} failed · {unverifiedReadTargets:N0} deferred/unverified";
                IedBrowserSubtitle.ToolTip =
                    $"Explicit scope: {readScope}; scheduled FC: {scheduledFc}" +
                    (failureExamples.Count == 0
                        ? string.Empty
                        : "\nFirst unavailable attributes (no scalar was published):\n" +
                          string.Join("\n", failureExamples));
                RefreshIedBrowserVisibleValues();
                // A few permanently refused SG leaves must not block the next
                // finite SGCB chunk. Per-page auto admission still caps attempts
                // to three; this never becomes Read All or cyclic polling.
                autoContinue = automatic && successes > 0 &&
                    (failures == 0 || currentPage?.Kind == "SGCB");
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
            if (automaticKey is not null && failures > 0)
                _iedBrowserAutoReadAdmission.Complete(automaticKey);
            // Replan against CURRENT ledger, not an old list of missing values.
            // A fast selection change while a read was busy must get its turn.
            if (ReferenceEquals(SelectedDevice, sourceDevice) &&
                _iedBrowserNavigation.Current is { } currentFocus &&
                (autoContinue ||
                 !ReferenceEquals(currentPage, currentFocus) ||
                 !string.Equals(selectedDo, _iedBrowserSelectedDoReference,
                     StringComparison.Ordinal)))
            {
                QueueIedBrowserFocusedRead(currentFocus,
                    _iedBrowserSelectedDoReference);
                QueueIedBrowserRcbFocusedRead(currentFocus);
            }
            if (configRequest is null && !reportUnitRequest &&
                ReferenceEquals(SelectedDevice, sourceDevice) &&
                _iedBrowserNavigation.Current is { Kind: "LN" } &&
                _iedBrowserReadOperation is null)
                QueueFirstExpandedVisibleConfigurationRead();
        }
    }


    private void IedBrowserGooseSubscribe_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedDevice is not { IsDemo: false } device ||
            _iedBrowserRenderedPage?.Kind != "GoCB" || GooseActionBusy)
            return;
        // The Browser is an independent view of the SAME Npcap subscriber;
        // never navigate to the Sniff tab or instantiate another receiver.
        if (IsGooseCapturing)
        {
            UpdateIedBrowserGooseDetails();
            return;
        }
        if (GooseAdapters.Count == 0)
            RefreshGooseAdapters();
        if (SelectedGooseAdapter is null)
        {
            var proposed = ResolveGooseAdapterForIed(device, out var route);
            if (proposed is not null)
                SelectedGooseAdapter = proposed;
            else
                IedBrowserDetailStatus.Text =
                    "Select the station capture adapter here, then press Subscribe GOOSE. " + route;
        }
        IedBrowserGooseAdapterCombo.Visibility = Visibility.Visible;
        if (SelectedGooseAdapter is null)
        {
            IedBrowserGooseAdapterCombo.Focus();
            RefreshIedBrowserReadButtons();
            return;
        }
        // Existing start path owns decoder, bound model, lifetime and UI queue.
        StartGooseSubscriber_Click(sender, e);
        RefreshIedBrowserReadButtons();
    }

    private async void IedBrowserGooseStop_Click(object sender, RoutedEventArgs e)
    {
        if (!IsGooseCapturing || GooseActionBusy) return;
        await StopGooseSubscriberAsync();
        RefreshIedBrowserReadButtons();
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
        var selectedKind = IedBrowserReadPlan.SelectionKind(_iedBrowserNavigation.Current);
        if (IedBrowserGooseSubscribeButton is not null)
        {
            IedBrowserGooseSubscribeButton.Visibility =
                _iedBrowserRenderedPage?.Kind == "GoCB"
                    ? Visibility.Visible : Visibility.Collapsed;
            IedBrowserGooseSubscribeButton.IsEnabled =
                device is { IsDemo: false } && _iedBrowserReadOperation == null;
            IedBrowserGooseSubscribeButton.Content = IsGooseCapturing
                ? "View live GOOSE" : "Subscribe GOOSE";
            IedBrowserGooseSubscribeButton.IsEnabled =
                device is { IsDemo: false } && !GooseActionBusy;
            IedBrowserGooseStopButton.Visibility =
                _iedBrowserRenderedPage?.Kind == "GoCB" && IsGooseCapturing
                    ? Visibility.Visible : Visibility.Collapsed;
            IedBrowserGooseStopButton.IsEnabled = !GooseActionBusy;
            IedBrowserGooseAdapterCombo.Visibility =
                _iedBrowserRenderedPage?.Kind == "GoCB" &&
                (!IsGooseCapturing || GooseAdapters.Count > 0)
                    ? Visibility.Visible : Visibility.Collapsed;
            IedBrowserGooseAdapterCombo.IsEnabled = !IsGooseCapturing && !GooseActionBusy;
        }
        IedBrowserReadPageButton.Content = selectedKind == IedBrowserReadSelectionKind.ReadOnlyRcb
            ? "Inspect RCB"
            : connected ? "Read" : "Connect & Read";
        IedBrowserReadPageButton.ToolTip = selectedKind == IedBrowserReadSelectionKind.ReadOnlyRcb
            ? "Inspect current RCB attributes through explicit read-only MMS."
            : selectedKind == IedBrowserReadSelectionKind.GooseInspectionRequiresSubscriber
                ? "Read only verified GoCB MMS fields (GoEna/ndsCom); passive frame data still requires explicit capture."
                : !hasModel
            ? "Open SCL or discover an IED model before reading values."
            : !canReachIED
                ? "Bind an MMS IP address to this IED before reading live values."
                : connected
                    ? "Read this model-verified object through bounded MMS reads."
                    : "Connect using this IED model and read selected values on demand; no RCB or cyclic polling.";
        var focusedDo = selectedKind == IedBrowserReadSelectionKind.FcAttributes &&
            _iedBrowserNavigation.Current?.Payload is LiveIedLogicalNodeModel &&
            !string.IsNullOrWhiteSpace(_iedBrowserSelectedDoReference);
        if (IedBrowserReadLnButton is not null)
        {
            IedBrowserReadLnButton.Visibility =
                focusedDo ? Visibility.Visible : Visibility.Collapsed;
            IedBrowserReadLnButton.IsEnabled = ready;
        }
        if (focusedDo)
        {
            IedBrowserReadPageButton.Content = "Read DO";
            IedBrowserReadPageButton.ToolTip = "Read only " +
                _iedBrowserSelectedDoReference +
                "; choose Read LN for the entire Logical Node.";
        }
        else if (selectedKind == IedBrowserReadSelectionKind.FcAttributes &&
                 _iedBrowserNavigation.Current?.Payload is LiveIedLogicalNodeModel)
            IedBrowserReadPageButton.Content = "Read LN";
        IedBrowserReadPageButton.IsEnabled = ready;
        IedBrowserReadAllButton.IsEnabled = ready;
        if (IedBrowserWriteButton is not null)
        {
            var current = _iedBrowserNavigation.Current;
            IedBrowserWriteButton.IsEnabled = device is { IsConnected: true, IsDemo: false } &&
                device.LiveDiscoveryModel is not null && _iedBrowserReadOperation is null &&
                (current?.Kind is "DO" or "DA" ||
                 current?.Kind == "LN" && !string.IsNullOrWhiteSpace(_iedBrowserSelectedDoReference));
        }
        if (IedBrowserExportReadEvidenceButton is not null)
            IedBrowserExportReadEvidenceButton.IsEnabled =
                device?.LiveDiscoveryModel is not null &&
                _iedBrowserNavigation.Current is not null;
        IedBrowserCancelReadButton.IsEnabled = _iedBrowserReadOperation != null;
    }

    private bool TryGetBrowserReadFailure(
        Iec61850MonitorDevice device, IedBrowserDetailRow row, out string reason,
        out IedBrowserReadAbsenceKind kind)
    {
        if (_iedBrowserReadFailureEvidence?.TryGet(
                device, row.ValueReference, row.FallbackValueReference,
                row.FunctionalConstraint, out reason, out kind) == true)
            return true;
        if (device.InitialHydrationFailures?.TryGet(
                device, row.ValueReference, row.FallbackValueReference,
                row.FunctionalConstraint, out reason, out kind) == true)
            return true;
        reason = "";
        kind = IedBrowserReadAbsenceKind.Failed;
        return false;
    }

    private void RefreshIedBrowserRcbEnabledIndicators(Iec61850MonitorDevice device)
    {
        // One model identity index per coalesced flush; no full-IED MMS probe,
        // duplicate live state store or per-RCB model inventory scan.
        var targets = IedBrowserRcbEnabledPresentation.VerifiedTargets(
            device.LiveDiscoveryModel);
        var connected = device.IsConnected && !device.LiveValues.IsOffline;

        // Right list objects can be visible while left LD/LN folders stay lazy.
        foreach (var detail in _iedBrowserDetails.Rows)
        {
            if (detail.NavigateTo is not IedBrowserTreeRow node ||
                node.Payload is not RcbExportRow rcb)
                continue;
            var verified = IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
                device.LiveValues, rcb, targets, connected, out var tooltip,
                _iedBrowserRcbEnabledMinReadUtc);
            if (node.SetRcbEnabled(verified, tooltip))
                detail.RefreshRcbUsage();
        }
        foreach (var node in _iedBrowserTree.Rows)
        {
            if (node.Payload is not RcbExportRow rcb) continue;
            var verified = IedBrowserRcbEnabledPresentation.TryVerifiedTrue(
                device.LiveValues, rcb, targets, connected, out var tooltip,
                _iedBrowserRcbEnabledMinReadUtc);
            node.SetRcbEnabled(verified, tooltip);
        }
    }

    private void RefreshIedBrowserVisibleValues()
    {
        if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            SelectedDevice is not { } device)
            return;
        using var valuesPerf = IedBrowserPerformanceProbe.Measure(
            "Browser.VisibleValues", device.Name, _iedBrowserDetails.Rows.Count);
        // RCB start/stop does not increment the process LiveValues ledger
        // version. Independently update just the already materialized RCB
        // row glyphs from runtime activation authority on the UI flush tick,
        // preserving the tree selection and every expanded LN/DO.
        var usageChanges = ReferenceEquals(_iedBrowserCachedDevice, device)
            ? IedBrowserRcbLiveOwnershipPresentation.Reconcile(
                _iedBrowserCachedRcbRows,
                _runtime.GetActiveBrowserRcbReferences(device.DeviceId),
                device.IsConnected && device.IsMonitoring)
            : 0;
        if (usageChanges > 0)
        {
            // Runtime ownership changes are not fresh Boolean read-back.
            _iedBrowserRcbEnabledMinReadUtc = DateTime.UtcNow;
            foreach (var treeRow in _iedBrowserTree.Rows)
                treeRow.RefreshRcbUsage();
            foreach (var detailRow in _iedBrowserDetails.Rows)
                detailRow.RefreshRcbUsage();
        }
        var version = device.LiveValues.Version;
        // RptEna is current-association MMS Read evidence, never RCB occupancy.
        // Update only current virtual rows when source authority changes.
        if (usageChanges > 0 || _iedBrowserLastLiveVersion != version ||
            !ReferenceEquals(_iedBrowserLastCfCanonicalModel, device.LiveCanonicalModel))
            RefreshIedBrowserRcbEnabledIndicators(device);
        if (_iedBrowserLastLiveVersion == version &&
            ReferenceEquals(_iedBrowserLastCfCanonicalModel, device.LiveCanonicalModel))
        {
            // Passive GOOSE TTL expiry and received RCB continuity are not
            // version increments of the MMS value ledger. Project the existing
            // evidence on the UI flush tick even when no MMS value changed.
            if (_iedBrowserRenderedPage?.Kind == "GoCB")
                UpdateIedBrowserGooseDetails();
            else if (_iedBrowserRenderedPage?.Kind == "RCB")
                UpdateIedBrowserRcbReportDetails();
            return;
        }
        _iedBrowserLastLiveVersion = version;
        _iedBrowserLastCfCanonicalModel = device.LiveCanonicalModel;
        // No MMS call occurs during navigation or refresh. Exact CF/DC
        // instance evidence is a labelled discovery snapshot, not a Report.
        var model = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        var cfEvidence = new IedBrowserCfEvidencePresentation(device.LiveValues,
            model, device.LiveCanonicalModel);
        // Read-only association-scoped acceptance counters are surfaced in
        // the EXISTING subtitle tooltip, without resizing any live panel or
        // adding another subscription. They do not claim consumer comparison
        // until distinct consumer projections have actually been compared.
        if (ReferenceEquals(model, device.LiveDiscoveryModel) && model is not null)
            QueueIedBrowserAcceptanceMetrics(device, model);
        else
            IedBrowserSubtitle.ToolTip =
                "Offline/SCL declarations are not proof of fresh IEC MMS observations.";
        using var unitsPerf = IedBrowserPerformanceProbe.Measure(
            "Browser.Units", device.Name, device.Signals.Count);
        var engineeringUnits = _iedBrowserUnitCache.Resolve(device, model, cfEvidence);
        foreach (var row in _iedBrowserDetails.Rows)
        {
            if (string.IsNullOrWhiteSpace(row.ValueReference)) continue;
            // An IEC NamPlt DO has DC children, not a fictional scalar under
            // NamPlt [DC]. Show the exact vendor/d/swRev leaf already observed
            // in the canonical association (or clearly labelled discovery).
            if (IedBrowserNameplatePresentation.TryProject(row, device.LiveValues,
                    cfEvidence, out var nameplate))
            {
                row.Value = nameplate.Value;
                row.ObservationSource = nameplate.Source;
                row.ObservationTooltip = nameplate.Provenance;
                continue;
            }
            if (cfEvidence.TryFormatUnitsParent(
                    row.ValueReference, row.FunctionalConstraint,
                    out var verifiedUnit, out var verifiedProvenance,
                    out var verifiedAuthority))
            {
                row.Value = verifiedAuthority.StartsWith("Stale ", StringComparison.Ordinal)
                    ? "Offline · " + verifiedUnit : verifiedUnit;
                row.ObservationSource = verifiedAuthority;
                row.ObservationTooltip = verifiedProvenance;
                continue;
            }
            // Single IEC composition entry point, with strict compatible
            // phasors and independently labelled extra vector components.
            if (IedBrowserEngineeringComposite.TryCompose(
                    row, device.LiveValues, engineeringUnits,
                    device.IsMonitoring, out var composite))
            {
                row.Value = composite.IsStale
                    ? "Offline · " + composite.Text : composite.Text;
                row.ObservationSource = composite.IsStale
                    ? "Stale derived " + composite.Source : "Derived " + composite.Source;
                row.ObservationTooltip = composite.Provenance;
                continue;
            }
            // A phase/intermediate DO may not have its own MMS scalar.
            // Reuse ONLY the exact cVal child with already received MX leaves,
            // so the parent no longer says Not read while its child is observed.
            if (IedBrowserNestedObservedPresentation.TryCompose(
                    row, device.LiveValues, engineeringUnits,
                    device.IsMonitoring, out var nested))
            {
                row.Value = nested.IsStale ? "Offline · " + nested.Text : nested.Text;
                row.ObservationSource = nested.IsStale
                    ? "Stale derived " + nested.Source : "Derived " + nested.Source;
                row.ObservationTooltip = nested.Provenance;
                continue;
            }
            // A model-proven compound ST DataSet member can contain two
            // independently received process leaves. Show both labels rather
            // than claiming the whole parent was unread or choosing one.
            if (IedBrowserMultiLeafPresentation.TryCompose(
                    row, device.LiveValues, device.IsMonitoring,
                    out var multi))
            {
                row.Value = multi.IsStale ? "Offline · " + multi.Text : multi.Text;
                row.ObservationSource = multi.IsStale
                    ? "Stale " + multi.Source : multi.Source;
                row.ObservationTooltip = multi.Provenance;
                continue;
            }
            var found = IedBrowserDetailEvidenceResolver.TryResolve(
                device.LiveValues, row, _iedBrowserValueSchemaIndex,
                device.IsMonitoring, out var live, out var observedReference);
            if (!found)
            {
                // Only exact model-bound, previously captured canonical
                // instance evidence may populate an unread CF/DC value.
                // It remains visibly a historical discovery snapshot.
                if (row.FunctionalConstraint is "CF" or "DC" &&
                    cfEvidence.TryGet(row.ValueReference, row.FunctionalConstraint,
                        out var snapshot, out var provenance, out _))
                {
                    row.Value = IedBrowserCfEvidencePresentation.RenderCfValue(
                        row.ValueReference, row.FunctionalConstraint, snapshot);
                    row.ObservationSource = "Discovery snapshot";
                    row.ObservationTooltip = provenance;
                }
                else if (IedBrowserObservedParentPresentation.TrySummarize(
                             row, device.LiveValues, device.IsMonitoring,
                             out var childSummary, out var childSource,
                             out var childProvenance))
                {
                    // Strictly a presence indicator for a non-scalar IEC DO.
                    // No claim that every FC/attribute is readable, no made-up
                    // parent value, no implicit MMS calls or refresh loop.
                    row.Value = childSummary;
                    row.ObservationSource = childSource;
                    row.ObservationTooltip = childProvenance;
                }
                else if (TryGetBrowserReadFailure(device, row,
                             out var readFailure, out var absenceKind))
                {
                    // No initial wire attempt is not an MMS rejection.
                    // Keep the small table calm; explain the cause in the
                    // existing per-row tooltip, without fake process values.
                    var deferred = absenceKind == IedBrowserReadAbsenceKind.Deferred;
                    row.Value = deferred ? "Not read" : "Unavailable";
                    row.ObservationSource = absenceKind switch
                    {
                        IedBrowserReadAbsenceKind.Deferred => "Initial read deferred",
                        IedBrowserReadAbsenceKind.Interrupted => "MMS Read interrupted",
                        _ => "MMS Read failed"
                    };
                    row.ObservationTooltip =
                        $"{row.ValueReference} [{row.FunctionalConstraint}] · {readFailure}";
                }
                else
                {
                    row.Value = IedBrowserReadPlan.UnobservedValue(
                        row.FunctionalConstraint);
                    row.ObservationSource = "";
                    row.ObservationTooltip = row.FunctionalConstraint == "CO"
                        ? "FC CO: command parameters are not scalar MMS Read targets. Use the explicit controlled Signal Testing workflow."
                        : "";
                }
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
            if (row.EvidenceKind == "Quality" ||
                (row.EvidenceField.Length == 0 &&
                 string.Equals(row.FunctionalConstraint, "ST", StringComparison.Ordinal) &&
                 IedBrowserLiveValues.CanonicalReference(row.ValueReference)
                     .EndsWith(".q", StringComparison.Ordinal)))
            {
                // An exact IEC .q attribute in a DataSet/RCB detail row can be
                // built from a member without EvidenceKind="Quality". Present
                // the typed 13-bit quality instead of the raw bits(0000,...).
                display = live.QualityBits?.FormatField(row.EvidenceField) ??
                    Iec61850QualityPresentation.FormatObserved(live.Value, row.EvidenceField);
                if (display == "Not exposed")
                    display = "Not decoded";
            }
            else if (row.EvidenceKind == "Timestamp" && row.EvidenceField.Length > 0)
                display = IedBrowserUtcTimeQualityPresentation.FormatField(
                    live.TimeBits, row.EvidenceField);
            else if (row.EvidenceField.Length == 0)
                display = IedBrowserCfEvidencePresentation.RenderCfValue(
                    row.ValueReference, row.FunctionalConstraint, live.Value);
            else
                display = "Not exposed";
            // An actually observed q/t leaf with no verified raw bit-level
            // metadata is not an unexposed IEC attribute. Report/MMS value
            // ingress succeeded; only the expansion into individual flags
            // is unavailable. Preserve this distinction to guide BCUGE
            // diagnosis without inventing false quality/timeQuality bits.
            if (row.EvidenceField.Length > 0 &&
                display == "Not exposed" &&
                row.EvidenceKind is "Quality" or "Timestamp")
            {
                display = "Not decoded";
                row.ObservationTooltip +=
                    " · Exact q/t leaf observed, but raw typed bit evidence " +
                    "was not verified; do not infer flags from text.";
            }
            // IEC UTC wire timestamps are UTC instants, unlike offsetless
            // vendor display strings that may already be operator-local.
            // Convert only when explicit UTC/offset evidence exists.
            if (row.EvidenceField.Length == 0 &&
                (row.EvidenceKind == "Timestamp" ||
                 row.ValueReference.EndsWith(".t", StringComparison.Ordinal) ||
                 row.ValueReference.EndsWith(".LActTm", StringComparison.Ordinal)))
                display = IedBrowserLocalTimePresentation.Format(display);
            // Data Model, DataSets and Report details are projections of the
            // SAME exact reference/FC canonical value. Use the existing IEC
            // operator formatter only when the current model declares DPC.
            // No name-only inference and no mutation of raw report evidence.
            if (row.EvidenceField.Length == 0)
            {
                display = IedBrowserDoublePointPresentation.Format(
                    display, observedReference, row.ValueReference,
                    row.FunctionalConstraint, _iedBrowserValueSchemaIndex);
                display = IedBrowserStandardStatusPresentation.Format(
                    observedReference, row.FunctionalConstraint, display,
                    _iedBrowserValueSchemaIndex);
                display = IedBrowserDoublePointPresentation.FormatSubstitutionQuality(
                    display, observedReference, row.FunctionalConstraint,
                    _iedBrowserValueSchemaIndex);
                // The exact verified SCL EnumType is the only authority
                // for vendor read-only SP/SG ordinal symbols (including
                // PTOC setCharact curves). SSOT retains raw MMS evidence.
                // Editing group FC=SE is NEVER projected as active setting.
                if (row.FunctionalConstraint is "SG" or "SP" &&
                    (observedReference.EndsWith(".setVal", StringComparison.Ordinal) ||
                     observedReference.EndsWith(".setCharact", StringComparison.Ordinal)))
                {
                    if (_iedBrowserSclEnumEvidence?.TryFormat(
                        observedReference, row.FunctionalConstraint, display,
                        out var symbol, out var enumProof) == true)
                    {
                        display = symbol;
                        row.ObservationTooltip += " · " + enumProof;
                    }
                    else
                    {
                        row.ObservationTooltip +=
                            " · IEC setting enum remains raw: " +
                            (_iedBrowserSclEnumEvidence is null
                                ? _iedBrowserSclEnumAbsenceReason.Length == 0
                                    ? "verified SCL EnumType is not loaded"
                                    : _iedBrowserSclEnumAbsenceReason
                                : "no exact matched verified EnumType or ordinal");
                    }
                }
            }
            // One dimensionally typed projector across Data Model, DataSet,
            // Report and Setting Group detail rows. DO parent and direct leaf
            // reuse the SAME observed reference + owner CF unit decision.
            if (row.EvidenceField.Length == 0)
                display = IedBrowserEngineeringValuePresentation.Format(
                    display, IedBrowserEngineeringValuePresentation.ResolveObservedUnit(
                        engineeringUnits, cfEvidence, observedReference,
                        row.EngineeringUnitReference.Length == 0
                            ? row.ValueReference : row.EngineeringUnitReference,
                        row.FunctionalConstraint, row.IsEngineeringValue));
            row.Value = live.IsStale ? "Offline · " + display : display;
        }
        UpdateIedBrowserGooseDetails();
        UpdateIedBrowserControlStateDetails();
        UpdateIedBrowserRcbReportDetails();
    }




    private void UpdateIedBrowserControlStateDetails()
    {
        if (_iedBrowserRenderedPage is not
            { Kind: "SGCB" or "GoCB", Payload: LiveIedControlBlockModel block } ||
            SelectedDevice is not { } device)
            return;

        var pageKind = _iedBrowserRenderedPage.Kind;
        var fieldRows = pageKind == "SGCB"
            ? new[] { ("NumOfSG", "Number of Setting Groups"),
                      ("ActSG", "Active Setting Group"),
                      ("LActTm", "Last changed") }
            : new[] { ("GoEna", "Enabled"),
                      ("NdsCom", "Needs commissioning") };
        foreach (var (field, label) in fieldRows)
        {
            var row = _iedBrowserDetails.Rows.FirstOrDefault(r =>
                r.Kind == "Property" && r.Name == label);
            if (row is null) continue;
            if (IedBrowserControlStateEvidence.TryRead(
                    device.LiveValues, block, pageKind, field,
                    out var observed, out var reference))
            {
                var display = IedBrowserControlStateEvidence.FormatObservedField(
                    pageKind, field, observed);
                if (field == "LActTm")
                    display = IedBrowserLocalTimePresentation.Format(display);
                row.Value = observed.IsStale ? "Offline · " + display : display;
                row.ObservationSource = observed.IsStale
                    ? "Stale MMS Read" : "MMS Read";
                row.ObservationTooltip = $"{reference} [{block.FunctionalConstraint}]" +
                    $" · {observed.Source} · {observed.ObservedUtc:yyyy-MM-dd HH:mm:ss} UTC" +
                    $" · association {observed.Association}" +
                    (field == "LActTm" && observed.TimeBits is
                        { IsDecoded: true, HasWireProvenance: true } time
                        ? $" · wire {time.RawHex} · time quality {time.QualityHex}"
                        : "");
            }
            else
            {
                if (IedBrowserControlStateEvidence.TryReadFailure(
                        device, block, pageKind, field,
                        _iedBrowserReadFailureEvidence, device.InitialHydrationFailures,
                        out var failure, out var source, out var failedReference))
                {
                    row.Value = "Unavailable";
                    row.ObservationSource = source;
                    row.ObservationTooltip =
                        $"{failedReference} [{block.FunctionalConstraint}] · {source} · {failure}";
                }
                else
                {
                    row.Value = pageKind == "SGCB" && field == "LActTm"
                        ? "Unknown" : "Not read";
                    row.ObservationSource = "";
                    row.ObservationTooltip =
                        IedBrowserControlStateEvidence.IsVerifiedInventory(block, pageKind)
                        ? $"{field}: not observed; no inferred control state"
                        : $"{field}: not authorized by exact live FC inventory";
                }
            }
        }
    }

    private void UpdateIedBrowserRcbReportDetails()
    {
        if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            _iedBrowserRenderedPage?.Payload is not RcbExportRow rcb ||
            SelectedDevice is not { } device)
            return;

        var fields = _iedBrowserDetails.Rows.Where(row => row.Kind == "Property")
            .ToDictionary(row => row.Name, StringComparer.Ordinal);
        void Put(string name, string value, string authority)
        {
            if (!fields.TryGetValue(name, out var row)) return;
            row.Value = value;
            row.ObservationSource = authority;
            row.ObservationTooltip = name + " · " + authority;
        }

        // A focused read-only MMS RCB attribute is admitted to the SAME
        // association-bound scalar ledger as Data Model / DataSets. Received
        // reports do not prove the current RptEna or reservation state.
        var focusedRcbTargets = IedBrowserRcbFocusedReadPlan.Build(
            device.LiveDiscoveryModel, rcb);
        foreach (var (name, field) in new[]
        {
            ("Enabled / RptEna", "RptEna"),
            ("Reservation", "Resv"),
            ("Reserve time (s)", "ResvTms")
        })
        {
            var target = focusedRcbTargets.FirstOrDefault(t =>
                t.Reference.EndsWith("." + field, StringComparison.Ordinal));
            if (target is not null && device.IsConnected &&
                !device.LiveValues.IsOffline &&
                device.LiveValues.TryGet(target.Reference, target.FunctionalConstraint,
                    out var actual, preferReport: false) &&
                !actual.IsStale &&
                actual.Association == device.LiveValues.Association &&
                string.Equals(actual.Source, "MMS Read", StringComparison.Ordinal))
                Put(name, actual.Value, "Verified MMS RCB field · current association");
        }

        // Association-scoped immutable metadata, never a synthetic report,
        // reservation state, RptEna, GI, or a newly opened MMS association.
        var received = device.IsConnected && device.IsMonitoring &&
                !device.LiveValues.IsOffline
            ? IedBrowserRcbReportEvidence.Select(device.ReportContinuityEvidence,
                rcb.Reference, rcb.DataSetReference, rcb.Inspection.ReportId)
            : null;
        if (received is null)
        {
            Put("Last Report status", device.IsConnected && device.IsMonitoring
                ? "No uniquely matched received Report" : "No active report monitoring",
                "Current association only");
            Put("Last Report received at", "—", "No current matching frame");
            Put("Received frame count", "—", "No current matching stream");
            Put("Last observed SqNum", "—", "No current matching stream");
            Put("Received RptID", "—", "No current matching stream");
            Put("Received DataSet", "—", "No current matching stream");
            return;
        }

        Put("Last Report status", "Received · exact RCB verified",
            "Association-scoped Report continuity evidence");
        Put("Last Report received at", received.LastFrameReceivedAtUtc!.Value.ToLocalTime()
            .ToString("yyyy-MM-dd HH:mm:ss.fff zzz",
                System.Globalization.CultureInfo.InvariantCulture),
            "Decoded Report receive time");
        Put("Received frame count", received.Frames.ToString(
            System.Globalization.CultureInfo.InvariantCulture),
            "Current Report stream only");
        Put("Last observed SqNum", received.LastSqNum is { } sq
            ? sq.ToString(System.Globalization.CultureInfo.InvariantCulture) : "Not supplied",
            "Last observed sequence number; not necessarily in latest frame");
        Put("Received RptID", received.ReportId, "Decoded received Report identifier");
        Put("Received DataSet", received.DataSet, "Decoded received Report DataSet");
        // The continuity summary contains aggregated GI/reason evidence; it
        // cannot prove reason for inclusion in the last individual frame.
    }

    private void PrepareIedBrowserGoose(Iec61850MonitorDevice device,
        LiveIedControlBlockModel block, LiveIedModelDiscoveryDocument? model)
    {
        // Only SCL metadata belonging to this selected device may enrich the GoCB.
        var source = device.SclWorkspace?.GooseStreams
            .Where(item => IedBrowserGooseInspection.Canonical(item.ControlBlockReference) ==
                IedBrowserGooseInspection.Canonical(block.Reference)).Take(2).ToArray();
        var scl = source is { Length: 1 } ? source[0] : null;
        var modelAppId = TryParseAppId(block.AppId);
        var modelConfRev = TryParseUInt(block.ConfRev);
        if (scl is not null &&
            ((!string.IsNullOrWhiteSpace(block.DataSetReference) &&
                IedBrowserGooseInspection.Canonical(block.DataSetReference) !=
                    IedBrowserGooseInspection.Canonical(scl.DataSetReference)) ||
             (!string.IsNullOrWhiteSpace(block.ControlId) &&
                !string.Equals(block.ControlId, scl.GoId, StringComparison.Ordinal)) ||
             (modelAppId.HasValue && scl.Address.AppId.HasValue &&
                modelAppId.Value != scl.Address.AppId.Value) ||
             (modelConfRev.HasValue && modelConfRev.Value != scl.ConfigurationRevision)))
            scl = null; // preserve conflicting authorities rather than blind SCL overlay

        var dataSet = !string.IsNullOrWhiteSpace(block.DataSetReference)
            ? block.DataSetReference : scl?.DataSetReference ?? "";
        // Exact selected-IED SCL DataSet is a directory fallback, never a
        // received subscriber frame or a substitute for live MMS state.
        var directoryModel = IedBrowserGooseDirectory.ModelForDataSet(
            model, device.SclWorkspace?.DesignModel, block);
        _iedBrowserGooseDirectory = directoryModel?.DataSets.FirstOrDefault(entry =>
            string.Equals(entry.Domain, block.Domain, StringComparison.Ordinal) &&
            IedBrowserGooseInspection.Canonical(entry.Reference) ==
                IedBrowserGooseInspection.Canonical(dataSet));
        _iedBrowserGooseExpected = new IedBrowserGooseExpectedIdentity(
            device.Name, block.Reference, dataSet,
            !string.IsNullOrWhiteSpace(block.ControlId) ? block.ControlId : scl?.GoId ?? "",
            TryParseAppId(block.AppId) ?? scl?.Address.AppId,
            TryParseUInt(block.ConfRev) ?? scl?.ConfigurationRevision,
            scl?.Address.DestinationMac?.ToString() ?? "",
            scl?.GoId ?? "", scl?.Address.AppId,
            scl?.Address.VlanId, scl?.Address.VlanPriority,
            scl?.MinTimeMilliseconds, scl?.MaxTimeMilliseconds);
    }

    private void UpdateIedBrowserGooseDetails()
    {
        if (MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
            _iedBrowserRenderedPage?.Kind != "GoCB" ||
            _iedBrowserGooseExpected is not { } identity)
            return;

        // One indexed projection per bounded UI coalescing tick. No tree rebuild.
        var fields = _iedBrowserDetails.Rows
            .Where(row => row.Kind == "Property")
            .ToDictionary(row => row.Name, StringComparer.Ordinal);
        void Put(string name, string value, string authority)
        {
            if (!fields.TryGetValue(name, out var row)) return;
            row.Value = value;
            row.ObservationSource = authority;
            row.ObservationTooltip = name + " · " + authority;
        }

        if (!string.IsNullOrWhiteSpace(identity.SclGoId))
            Put("GOOSE ID", identity.SclGoId, "Source SCL GSEControl");
        if (!string.IsNullOrWhiteSpace(identity.DataSetReference))
            Put("DataSet reference", identity.DataSetReference, "Model / SCL DataSet");
        if (identity.SclAppId is { } configuredAppId)
            Put("Application ID", configuredAppId.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "Source SCL GSE address");
        Put("Destination MAC", string.IsNullOrWhiteSpace(identity.SclDestinationMac)
            ? "Not read" : identity.SclDestinationMac,
            string.IsNullOrWhiteSpace(identity.SclDestinationMac)
                ? "No verified GSE address" : "Source SCL GSE address");
        foreach (var (name, value) in IedBrowserGooseInspection.SclNetworkProperties(identity))
            Put(name, value,
                name is "Minimum time (ms)" or "Maximum time (ms)"
                    ? "Source SCL GSEControl" : "Source SCL GSE address");

        // Never present old snapshots as currently received when capture is stopped.
        foreach (var name in new[] { "Frame received at", "Source MAC", "Frame test",
            "Frame ndsCom", "State number", "Sequence number",
            "Time allowed to live", "Frame DataSet entries" })
            Put(name, "—", "No currently verified passive reception");
        if (identity.SclVlanId is null)
            Put("VLAN ID", "Not read", "No verified SCL VLAN configuration or passive frame");
        if (identity.SclVlanPriority is null)
            Put("VLAN priority", "Not read",
                "No verified SCL VLAN configuration or passive frame");

        // Filter raw bounded cache by exact GoCB before building display leaves.
        // Two different publisher streams claiming the same identity are rejected.
        var nowUtc = DateTimeOffset.UtcNow;
        var controlReference = IedBrowserGooseInspection.Canonical(identity.GoCbRef);
        // Existing bounded subscriber cache; never open another capture or
        // promote any GOOSE sample to the MMS/Report value ledger. Filter by
        // *wire* TTL BEFORE choosing the two unique identity candidates.
        var found = IsGooseCapturing
            ? _latestGooseFrames.Values.Where(frame =>
                IedBrowserGooseInspection.Canonical(frame.Frame.Pdu.GoCbRef) ==
                    controlReference &&
                IedBrowserGooseInspection.IsFrameCurrent(
                    frame.CaptureTimestamp,
                    frame.Frame.Pdu.TimeAllowedToLiveMilliseconds, nowUtc))
                .Take(2)
                .Select(frame => (Raw: frame,
                    Display: BuildGooseStreamSnapshot(frame, _gooseBindingCatalog))).ToArray()
            : Array.Empty<(GooseSubscriberFrameSnapshot Raw, GooseStreamSnapshot Display)>();
        var expiredSameControl = IsGooseCapturing && found.Length == 0 &&
            _latestGooseFrames.Values.Any(frame =>
                IedBrowserGooseInspection.Canonical(frame.Frame.Pdu.GoCbRef) ==
                    controlReference &&
                !IedBrowserGooseInspection.IsFrameCurrent(
                    frame.CaptureTimestamp,
                    frame.Frame.Pdu.TimeAllowedToLiveMilliseconds, nowUtc));
        var frameView = IedBrowserGooseInspection.Select(identity, found.Select(x => x.Display));
        var received = frameView is null ? null : found
            .FirstOrDefault(x => ReferenceEquals(frameView, x.Display)).Raw;
        if (frameView is not null && received is not null)
        {
            Put("Capture status",
                string.IsNullOrWhiteSpace(frameView.ModelIedName)
                    ? "Receiving · exact wire GoCB, publisher model unbound"
                    : "Receiving · exact GoCB and publisher model matched",
                "Passive GOOSE; never implies MMS state or publisher authentication");
            Put("GOOSE ID", frameView.GoId, "Received GOOSE PDU GoID");
            Put("DataSet reference", frameView.DataSetReference,
                "Received GOOSE PDU datSet");
            Put("Application ID", frameView.AppIdText, "Received Ethernet APPID");
            Put("Configuration revision", frameView.ConfigurationRevisionText,
                "Received GOOSE PDU confRev");
            Put("Frame received at", received.CaptureTimestamp.ToLocalTime()
                .ToString("yyyy-MM-dd HH:mm:ss.fff zzz",
                    System.Globalization.CultureInfo.InvariantCulture), "Passive capture time");
            Put("Source MAC", frameView.SourceMac, "Received Ethernet frame");
            Put("Destination MAC", frameView.DestinationMac, "Received Ethernet frame");
            Put("VLAN ID", frameView.VlanId, "Received Ethernet frame");
            Put("VLAN priority", frameView.VlanPriority, "Received Ethernet frame");
            Put("Frame test", frameView.Test ? "true" : "false", "Received GOOSE PDU");
            Put("Frame ndsCom", frameView.NeedsCommissioning ? "true" : "false",
                "Received PDU; not MMS GoEna or control state");
            Put("State number", frameView.StateNumberText, "Received stNum");
            Put("Sequence number", frameView.SequenceNumberText, "Received sqNum");
            Put("Time allowed to live", frameView.TimeAllowedToLiveText,
                "Received timeAllowedToLive");
            Put("Frame DataSet entries", frameView.WireDataSetEntryCount.ToString(
                System.Globalization.CultureInfo.InvariantCulture), "Received allData");
            // Some devices expose GO/GoEna but omit GO/DatSet in the MMS
            // control inventory. Once a full matching wire GoCB is received,
            // resolve its *exact wire datSet* against the selected IED model.
            // Do not infer a link from GOOSE ID or the number of entries.
            if (_iedBrowserGooseDirectory is null &&
                _iedBrowserRenderedPage?.Payload is LiveIedControlBlockModel block &&
                SelectedDevice is { } selected)
            {
                var wireSet = IedBrowserGooseDirectory.ResolveWireDataSet(
                    selected.LiveDiscoveryModel, selected.SclWorkspace?.DesignModel,
                    block.Domain, frameView.DataSetReference);
                if (wireSet is { } resolved)
                    _iedBrowserGooseDirectory = resolved.Set;
            }
            var ordered = _iedBrowserGooseDirectory is null
                ? null : IedBrowserGooseInspection.VerifiedMembers(frameView,
                    _iedBrowserGooseDirectory);

            // Append only when the member shape changes. Existing flat
            // virtualized rows are updated in place each UI coalescing tick;
            // never rebuild the tree or mirror wire values into MMS LiveValues.
            var wireSlots = _iedBrowserDetails.Rows
                .Where(row => row.GooseSlot >= 0 && row.GooseReference.Length == 0)
                .ToArray();
            var limit = Math.Min(frameView.Leaves.Count, 128);
            if (ordered is not null)
            {
                foreach (var slot in wireSlots)
                    _iedBrowserDetails.Rows.Remove(slot);
                // The model directory may have become available only after
                // the wire datSet was observed. Attach the exact FCDA rows
                // once without replacing the whole virtualized Browser view.
                if (!_iedBrowserDetails.Rows.Any(row =>
                        row.GooseSlot >= 0 && row.GooseReference.Length != 0) &&
                    _iedBrowserGooseDirectory is { } directory &&
                    SelectedDevice is { } owner)
                {
                    var descriptors = directory.Members.OrderBy(m => m.Index).ToArray();
                    var sourceModel = owner.LiveDiscoveryModel ??
                        owner.SclWorkspace?.DesignModel;
                    for (var slot = 0; slot < descriptors.Length; slot++)
                    {
                        var member = descriptors[slot];
                        _iedBrowserDetails.Rows.Add(
                            IedBrowserStructuredDetails.Member(member, sourceModel) with
                            {
                                GooseSlot = slot, GooseReference = member.Reference,
                                GooseFc = member.FunctionalConstraint
                            });
                    }
                }
            }
            else if (wireSlots.Length != limit)
            {
                foreach (var slot in wireSlots)
                    _iedBrowserDetails.Rows.Remove(slot);
                for (var slot = 0; slot < limit; slot++)
                    _iedBrowserDetails.Rows.Add(new IedBrowserDetailRow(
                        "Data", $"allData[{slot + 1}] · unbound", "Not received",
                        Source: "GOOSE received wire order; FCDA is not verified")
                    {
                        GooseSlot = slot
                    });
            }
            foreach (var row in _iedBrowserDetails.Rows)
            {
                if (row.GooseSlot < 0) continue;
                if (row.GooseReference.Length == 0 &&
                    ordered is null && row.GooseSlot < frameView.Leaves.Count)
                {
                    row.Value = frameView.Leaves[row.GooseSlot].Value;
                    row.ObservationSource = "GOOSE raw allData · unbound";
                    row.ObservationTooltip =
                        $"allData[{row.GooseSlot + 1}] from {frameView.GoCbRef} " +
                        $"· received {received.CaptureTimestamp:O}; no FCDA claim";
                }
                else if (ordered is not null && row.GooseSlot < ordered.Count &&
                    IedBrowserGooseInspection.Canonical(row.GooseReference) ==
                        IedBrowserGooseInspection.Canonical(ordered[row.GooseSlot].SignalReference) &&
                    string.Equals(row.GooseFc, ordered[row.GooseSlot].FunctionalConstraint,
                        StringComparison.OrdinalIgnoreCase))
                {
                    row.Value = ordered[row.GooseSlot].Value;
                    row.ObservationSource = "GOOSE frame";
                    row.ObservationTooltip = row.GooseReference + " [" + row.GooseFc +
                        "] · passive received " + received.CaptureTimestamp.ToString("O",
                            System.Globalization.CultureInfo.InvariantCulture);
                }
                else if (row.ObservationSource is "GOOSE frame" or
                    "GOOSE raw allData · unbound")
                {
                    row.Value = "Not current";
                    row.ObservationSource = "";
                    row.ObservationTooltip = "";
                }
            }
        }
        else
        {
            Put("Capture status", !IsGooseCapturing
                ? "Subscriber not capturing"
                : expiredSameControl
                    ? "GOOSE frame expired · no current live value"
                    : "No uniquely verified matching current frame",
                "Passive capture · exact identity and received TTL required; not an MMS GoCB read");
            foreach (var row in _iedBrowserDetails.Rows)
            {
                if (row.GooseSlot < 0 ||
                    row.ObservationSource is not ("GOOSE frame" or
                        "GOOSE raw allData · unbound")) continue;
                row.Value = "Not current";
                row.ObservationSource = "";
                row.ObservationTooltip = "";
            }
        }
    }

    /// <summary>
    /// Capture a bounded, read-only diagnostic from the subscriber's existing
    /// last-frame index. Exact Browser identity and wire-TTL tests use the
    /// same projection as the GoCB page. Never subscribe, read MMS, decode
    /// frames again from Ethernet or create a shadow stream cache.
    /// </summary>
    private string CaptureIedBrowserGooseEvidence()
    {
        if (_iedBrowserRenderedPage?.Kind != "GoCB" ||
            _iedBrowserGooseExpected is not { } expected)
            return "";

        var now = DateTimeOffset.UtcNow;
        var key = IedBrowserGooseInspection.Canonical(expected.GoCbRef);
        var observed = _latestGooseFrames.Values
            .Where(frame => IedBrowserGooseInspection.Canonical(
                frame.Frame.Pdu.GoCbRef) == key)
            .OrderByDescending(frame => frame.CaptureTimestamp)
            .Take(8).ToArray();
        var lines = new List<string>
        {
            "",
            "GOOSE BROWSER ACCEPTANCE EVIDENCE (passive, non-MMS)",
            "--------------------------------------------------",
            $"Selected GoCB     : {key}",
            $"Expected DataSet  : {expected.DataSetReference}",
            $"Expected GoID     : {expected.GoId}",
            $"Expected AppID    : {(expected.AppId.HasValue ? $"0x{expected.AppId.Value:X4}" : "unspecified")}",
            $"Expected confRev  : {expected.ConfRev?.ToString() ?? "unspecified"}",
            $"Capture running   : {IsGooseCapturing}",
            $"Selected adapter  : {SelectedGooseAdapter?.DisplayText ?? "none"}",
            $"Exact GoCB sample : {observed.Length} bounded stream(s); at most 8 shown",
            $"Member directory  : {_iedBrowserGooseDirectory?.Members.Count.ToString() ?? "unavailable"}"
        };

        foreach (var raw in observed)
        {
            var fresh = IedBrowserGooseInspection.IsFrameCurrent(
                raw.CaptureTimestamp, raw.Frame.Pdu.TimeAllowedToLiveMilliseconds, now);
            try
            {
                var display = BuildGooseStreamSnapshot(raw, _gooseBindingCatalog);
                var identity = ReferenceEquals(
                    IedBrowserGooseInspection.Select(expected, [display]), display);
                var members = identity && _iedBrowserGooseDirectory is not null &&
                    IedBrowserGooseInspection.VerifiedMembers(
                        display, _iedBrowserGooseDirectory) is not null;
                var reason = !IsGooseCapturing ? "subscriber-stopped" :
                    !fresh ? "TTL-expired-or-invalid" :
                    !identity ? "identity-or-model-binding-mismatch" :
                    !members ? "ordered-member-directory-not-verified" : "verified-current";
                lines.Add($"  {raw.CaptureTimestamp:O} | {reason} | " +
                    $"TAL={raw.Frame.Pdu.TimeAllowedToLiveMilliseconds}ms | " +
                    $"DataSet={raw.Frame.Pdu.DataSetReference} | GoID={raw.Frame.Pdu.GoId} | " +
                    $"confRev={raw.Frame.Pdu.ConfigurationRevision} | " +
                    $"wireEntries={raw.Frame.Pdu.Values.Count}");
            }
            catch (Exception ex)
            {
                lines.Add($"  {raw.CaptureTimestamp:O} | projection-error=" +
                    ex.GetType().Name + " (no value accepted)");
            }
        }
        if (observed.Length == 0)
            lines.Add("  No passive frame found for selected exact GoCB reference.");
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
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
        _iedBrowserRcbEnabledMinReadUtc = DateTime.MinValue;
        _iedBrowserSnapshot = null;
        _iedBrowserSnapshotLease = null;
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
        {
            _iedBrowserForceRefresh = true;
            _iedBrowserRcbEnabledMinReadUtc = DateTime.UtcNow;
        }

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
            _iedBrowserReadFailureEvidence = null;
            _iedBrowserSnapshot = null;
            _iedBrowserSnapshotLease = null;
        }
        if (MainTabs.SelectedIndex == IedBrowserWorkspaceIndex)
            RefreshIedBrowser();
    }

    /// <summary>
    /// Metadata-only, one-shot asynchronous EnumType source verification.
    /// This never reads process values, changes the SG, or creates a second
    /// mutable observation cache. Stale device/model/source flights are fenced.
    /// </summary>
    private void QueueIedBrowserVerifiedSclEnums(Iec61850MonitorDevice device)
    {
        var design = device.SclWorkspace;
        var model = device.LiveDiscoveryModel;
        var sourcePath = device.SclSourcePath;
        var sourceSha = device.SclSourceSha256;
        if (design is null || model is null || !device.IsConnected ||
            string.IsNullOrWhiteSpace(sourcePath) ||
            string.IsNullOrWhiteSpace(sourceSha))
        {
            if (!ReferenceEquals(_iedBrowserSclEnumDevice, device) ||
                !ReferenceEquals(_iedBrowserSclEnumLiveModel, model) ||
                _iedBrowserSclEnumEvidence is not null ||
                _iedBrowserSclEnumSourceSha.Length != 0)
            {
                _iedBrowserSclEnumLoadTicket++;
                _iedBrowserSclEnumEvidence = null;
                _iedBrowserSclEnumDevice = device;
                _iedBrowserSclEnumLiveModel = model;
                _iedBrowserSclEnumSourceSha = "";
                _iedBrowserSclEnumAbsenceReason =
                    "No matching verified SCL source and online SG type.";
            }
            return;
        }

        // An on-disk edit invalidates previously verified declaration
        // symbols even if the device and imported source SHA did not change.
        // The async source loader then rejects any content/SHA mismatch.
        long sourceStamp;
        try { sourceStamp = System.IO.File.GetLastWriteTimeUtc(sourcePath).Ticks; }
        catch (Exception ex) when (ex is System.IO.IOException or
                                   UnauthorizedAccessException)
        {
            sourceStamp = -1;
        }
        var association = device.LiveValues.Association;
        var signature = sourcePath + "|" + sourceSha + "|" + sourceStamp +
            "|" + association;
        if (ReferenceEquals(_iedBrowserSclEnumDevice, device) &&
            ReferenceEquals(_iedBrowserSclEnumLiveModel, model) &&
            string.Equals(_iedBrowserSclEnumSourceSha, signature,
                StringComparison.Ordinal))
            return;
        _iedBrowserSclEnumEvidence = null;
        _iedBrowserSclEnumDevice = device;
        _iedBrowserSclEnumLiveModel = model;
        _iedBrowserSclEnumSourceSha = signature;
        _iedBrowserSclEnumAbsenceReason = "Verifying the matching SCL EnumType.";
        var ticket = ++_iedBrowserSclEnumLoadTicket;
        _ = LoadIedBrowserVerifiedSclEnumsAsync(
            device, model, design, sourcePath, sourceSha, signature,
            association, ticket);
    }

    private async Task LoadIedBrowserVerifiedSclEnumsAsync(
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument liveModel,
        AR.Iec61850.Scl.Workspace.SclIedWorkspace design,
        string sourcePath, string sourceSha, string signature,
        long association, int ticket)
    {
        try
        {
            var verified = await VerifiedSclSourceLoader.LoadAsync(
                sourcePath, sourceSha, _applicationCancellation.Token);
            var enums = await Task.Run(() =>
                IedBrowserSclEnumEvidence.Build(verified, design, liveModel),
                _applicationCancellation.Token);
            if (ticket != _iedBrowserSclEnumLoadTicket ||
                !ReferenceEquals(SelectedDevice, device) ||
                !ReferenceEquals(device.LiveDiscoveryModel, liveModel) ||
                device.LiveValues.Association != association ||
                !ReferenceEquals(device.SclWorkspace, design) ||
                !string.Equals(_iedBrowserSclEnumSourceSha, signature,
                    StringComparison.Ordinal) || !device.IsConnected)
                return;
            _iedBrowserSclEnumEvidence = enums;
            _iedBrowserSclEnumAbsenceReason = enums.BoundAttributeCount > 0
                ? "" : "SCL has no exact online-verified SG EnumType bindings.";
            _iedBrowserLastLiveVersion = -1;
            RefreshIedBrowserVisibleValues();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            if (ticket != _iedBrowserSclEnumLoadTicket ||
                !ReferenceEquals(SelectedDevice, device))
                return;
            _iedBrowserSclEnumEvidence = null;
            _iedBrowserSclEnumAbsenceReason =
                "Trusted SCL enum source rejected: " + ex.Message;
            _iedBrowserLastLiveVersion = -1;
            RefreshIedBrowserVisibleValues();
        }
    }

    private void RefreshIedBrowser()
    {
        if (IedBrowserTab == null || MainTabs.SelectedIndex != IedBrowserWorkspaceIndex)
            return;
        using var browserPerf = IedBrowserPerformanceProbe.Measure(
            "Browser.Refresh", SelectedDevice?.Name, SelectedDevice?.Signals.Count ?? 0);

        var device = SelectedDevice;
        ObserveIedBrowserDevice(device);
        // A captured probe is not live state across a new MMS association
        // even when the selected UI card never changed.
        if (_iedBrowserSnapshot != null &&
            (_iedBrowserSnapshotLease?.IsCurrent(device) != true))
        {
            _iedBrowserSnapshot = null;
            _iedBrowserSnapshotLease = null;
            _iedBrowserForceRefresh = true;
        }
        IedBrowserReadRcbButton.IsEnabled = device?.IsConnected == true &&
                                            device.IsBusy == false && _iedBrowserReadOperation == null;
        RefreshIedBrowserReadButtons();
        IedBrowserExportRcbButton.IsEnabled = device is { IsBusy: false, IsDemo: false };
        if (device == null)
        {
            _iedBrowserCachedDevice = null;
            _iedBrowserCachedAssociation = -1;
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

        QueueIedBrowserVerifiedSclEnums(device);

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

        // Heavy schema indexing must never block the WPF selection dispatcher.
        if (!TryActivateIedBrowserSchema(device, model))
            return;

        var changed = _iedBrowserForceRefresh ||
                      !ReferenceEquals(device, _iedBrowserCachedDevice) ||
                      device.LiveValues.Association != _iedBrowserCachedAssociation ||
                      !ReferenceEquals(model, _iedBrowserCachedModel) ||
                      !ReferenceEquals(_iedBrowserSnapshot, _iedBrowserCachedSnapshot) ||
                      !string.Equals(sourcePath, _iedBrowserCachedSourcePath, StringComparison.Ordinal) ||
                      stamp != _iedBrowserCachedSourceStamp;
        if (changed)
        {
            _iedBrowserForceRefresh = false;
            var sameDevice = ReferenceEquals(device, _iedBrowserCachedDevice) &&
                             device.LiveValues.Association == _iedBrowserCachedAssociation &&
                             ReferenceEquals(model, _iedBrowserCachedModel);
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
            // Initial RCB ownership is only a snapshot. The same projection
            // also runs on the UI's live refresh tick: monitor activation can
            // complete AFTER this lazy tree was built.
            IedBrowserRcbLiveOwnershipPresentation.Reconcile(
                rcbs, _runtime.GetActiveBrowserRcbReferences(device.DeviceId),
                device.IsConnected && device.IsMonitoring);
            _iedBrowserCachedRcbRows = rcbs;
            _iedBrowserCachedDevice = device;
            _iedBrowserCachedAssociation = device.LiveValues.Association;
            _iedBrowserCachedModel = model;
            _iedBrowserCachedSnapshot = _iedBrowserSnapshot;
            _iedBrowserCachedSourcePath = sourcePath;
            _iedBrowserCachedSourceStamp = stamp;

            var roots = IedBrowserPresentation.Roots(model, _iedBrowserCachedRcbRows,
                _iedBrowserRemoteFiles, EffectiveSclIedName(device),
                device.SclWorkspace?.DesignModel);
            _iedBrowserTree.Reset(roots, expandedKeys);
            // Fresh IED/association/model epoch: all six Objects categories
            // remain collapsed. Selecting Reports fills the right pane
            // without materializing left LD/LN/RCB descendants.

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
        else
            UpdateIedBrowserRcbReportDetails(); // metadata may arrive without a scalar update
    }

    private static bool IedBrowserClickOriginIsButton(
        object originalSource, DependencyObject container)
    {
        var node = originalSource as DependencyObject;
        while (node is not null && !ReferenceEquals(node, container))
        {
            if (node is System.Windows.Controls.Primitives.ButtonBase)
                return true;
            node = node is FrameworkContentElement content
                ? content.Parent
                : System.Windows.Media.VisualTreeHelper.GetParent(node);
        }
        return false;
    }

    private void IedBrowserTreeRow_MouseUp(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 ||
            sender is not DataGridRow { DataContext: IedBrowserTreeRow row } ||
            !row.HasChildren || IedBrowserClickOriginIsButton(e.OriginalSource,
                (DependencyObject)sender))
            return;
        // A virtual collection reset must not replace the clicked selection
        // with a transient null and erase the navigation trail.
        _iedBrowserSyncingSelection = true;
        try
        {
            _iedBrowserTree.Toggle(row);
            IedBrowserTreeGrid.SelectedItem = row;
        }
        finally { _iedBrowserSyncingSelection = false; }
        _iedBrowserNavigation.SelectFromTree(row);
        var owner = SelectedDevice;
        var ticket = ++_iedBrowserNavigationRenderTicket;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (ticket != _iedBrowserNavigationRenderTicket ||
                MainTabs.SelectedIndex != IedBrowserWorkspaceIndex ||
                !ReferenceEquals(SelectedDevice, owner) ||
                !ReferenceEquals(_iedBrowserNavigation.Current, row))
                return;
            var model = owner?.LiveDiscoveryModel ?? owner?.SclWorkspace?.DesignModel;
            RenderIedBrowserSelection(row, model, owner?.IsConnected == true);
        }));
        e.Handled = true;
    }

    private void IedBrowserDetailRow_MouseUp(
        object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.ClickCount != 1 ||
            sender is not ListViewItem { DataContext: IedBrowserDetailRow detail } ||
            IedBrowserClickOriginIsButton(e.OriginalSource,
                (DependencyObject)sender))
            return;
        if (detail.IsDataObject &&
            !string.IsNullOrEmpty(detail.ReadObjectReference) &&
            _iedBrowserNavigation.Current?.Payload is LiveIedLogicalNodeModel selectedLn &&
            IedBrowserExactDoFocus.IsDeclaredOwner(selectedLn,
                detail.ReadObjectReference))
        {
            _iedBrowserSelectedDoReference = detail.ReadObjectReference;
            RefreshIedBrowserReadButtons(); // expose separate Read DO and Read LN scope
            if (_iedBrowserNavigation.Current is { } lnPage)
                QueueIedBrowserFocusedRead(lnPage, detail.ReadObjectReference);
        }
        if (!detail.HasChildren) return;
        ToggleIedBrowserDetails(detail);
        e.Handled = true;
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
        // Expand-arrow navigation must acquire the same exact DO as row click.
        if (detail.IsDataObject &&
            !string.IsNullOrEmpty(detail.ReadObjectReference) &&
            _iedBrowserNavigation.Current?.Payload is LiveIedLogicalNodeModel selectedLn &&
            IedBrowserExactDoFocus.IsDeclaredOwner(selectedLn,
                detail.ReadObjectReference) &&
            _iedBrowserNavigation.Current is { } lnPage)
        {
            _iedBrowserSelectedDoReference = detail.ReadObjectReference;
            RefreshIedBrowserReadButtons();
            QueueIedBrowserFocusedRead(lnPage, detail.ReadObjectReference);
        }
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

        // Full-row single click handles expansion. Double-click retains only
        // genuine object navigation; never undo the first toggle here.
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
            if (row.IsExpanded)
            {
                QueueIedBrowserVisibleConfigurationRead(row);
                QueueIedBrowserExactCompanionRead(row);
            }
        }));
    }

    // Read an expanded IEC CF/DC subtree through the existing scoped,
    // association-fenced MMS Read pipeline; never shadow canonical values.
    private void QueueIedBrowserVisibleConfigurationRead(IedBrowserDetailRow row)
    {
        if (row.ReadObjectReference.Length == 0 ||
            SelectedDevice is not { IsConnected: true, IsDemo: false } device ||
            _iedBrowserNavigation.Current is not { Kind: "LN" } current ||
            !ReferenceEquals(_iedBrowserRenderedPage, current))
            return;
        var scope = row.ReadObjectReference;
        var association = device.LiveValues.Association;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(SelectedDevice, device) ||
                device.LiveValues.Association != association ||
                !_iedBrowserDetails.Rows.Contains(row) || !row.IsExpanded ||
                !ReferenceEquals(current, _iedBrowserRenderedPage) ||
                device.IsBusy || _iedBrowserReadOperation is not null) return;
            IedBrowserReadValues_Click(
                new Button { Tag = new IedBrowserVisibleConfigurationReadRequest(scope) },
                new RoutedEventArgs());
        }));
    }

    /// <summary>
    /// On expansion, request only one exact q/t leaf if the canonical ledger
    /// has no raw typed evidence. A summary such as "good" or an ISO timestamp
    /// does not satisfy IEC bit-level details.
    /// </summary>
    private void QueueIedBrowserExactCompanionRead(IedBrowserDetailRow row)
    {
        if (row.EvidenceKind is not ("Quality" or "Timestamp") ||
            string.IsNullOrWhiteSpace(row.ValueReference) ||
            SelectedDevice is not { IsConnected: true, IsDemo: false } device ||
            _iedBrowserNavigation.Current is not { Kind: "LN" } current ||
            !ReferenceEquals(_iedBrowserRenderedPage, current))
            return;
        var association = device.LiveValues.Association;
        var request = new IedBrowserExactCompanionReadRequest(
            row.ValueReference, row.FunctionalConstraint, row.EvidenceKind);
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(SelectedDevice, device) ||
                device.LiveValues.Association != association ||
                !ReferenceEquals(current, _iedBrowserRenderedPage) ||
                !_iedBrowserDetails.Rows.Contains(row) || !row.IsExpanded ||
                device.IsBusy || _iedBrowserReadOperation is not null)
                return;
            IedBrowserReadValues_Click(new Button { Tag = request },
                new RoutedEventArgs());
        }));
    }

    private void QueueFirstExpandedVisibleConfigurationRead()
    {
        if (_iedBrowserDetails.Rows.Where(x =>
            x.IsExpanded && x.ReadObjectReference.Length > 0 &&
            _iedBrowserAutoReadAdmission.CanStart(
                SelectedDevice!.DeviceId, SelectedDevice.LiveValues.Association,
                IedBrowserFocusedAdmission.PageKey(
                    _iedBrowserNavigation.Current!.Key, x.ReadObjectReference), 1))
            .OrderByDescending(x => x.Depth).FirstOrDefault()
            is { } row)
            QueueIedBrowserVisibleConfigurationRead(row);
    }

    private void QueueIedBrowserFocusedRead(IedBrowserTreeRow row,
        string? selectedDoReference = null)
    {
        if (SelectedDevice is not { IsConnected: true, IsDemo: false } device ||
            device.LiveDiscoveryModel is null ||
            row.Kind is not ("SGCB" or "LN" or "DO" or "DA" or "DataSet") ||
            row.Kind == "LN" && !row.Key.StartsWith("model/ln/", StringComparison.Ordinal))
            return;
        var association = device.LiveValues.Association;
        var selectedDo = row.Kind == "LN" ? selectedDoReference : null;
        var key = IedBrowserFocusedAdmission.PageKey(row.Key, selectedDo);
        var maxAttempts = row.Kind == "SGCB" ? 3 :
            row.Kind is "LN" or "DO" or "DataSet" ? 3 : 1;
        if (!_iedBrowserAutoReadAdmission.CanStart(
                device.DeviceId, association, key, maxAttempts))
            return;
        // Do not mark attempted here: selection, device busy state and model
        // can all change before this callback actually runs.
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!ReferenceEquals(SelectedDevice, device) ||
                device.LiveValues.Association != association ||
                !ReferenceEquals(_iedBrowserNavigation.Current, row) ||
                !ReferenceEquals(_iedBrowserRenderedPage, row) ||
                !device.IsConnected || device.IsBusy ||
                _iedBrowserReadOperation is not null ||
                !string.Equals(row.Kind == "LN" ? _iedBrowserSelectedDoReference : null,
                    selectedDo, StringComparison.Ordinal))
                return;
            var pending = IedBrowserReadPlan.BuildFocused(
                device.LiveDiscoveryModel, row, device.LiveValues,
                selectedDoReference: selectedDo);
            if (pending.Count == 0)
            {
                _iedBrowserAutoReadAdmission.Complete(key);
                return;
            }
            // Same association-fenced typed result inbox and isolated MMS Read
            // as manual Read; never trigger an automatic IED-wide Read All.
            IedBrowserReadValues_Click(new Button { Tag = "Auto" },
                new RoutedEventArgs());
        }));
    }

    private void RenderIedBrowserSelection(
        IedBrowserTreeRow? row, LiveIedModelDiscoveryDocument? model, bool connected)
    {
        if (!ReferenceEquals(_iedBrowserRenderedPage, row))
            _iedBrowserSelectedDoReference = null;
        _iedBrowserRenderedPage = row;
        _iedBrowserRenderedConnected = connected;
        _iedBrowserGooseExpected = null;
        _iedBrowserGooseDirectory = null;
        if (row is { Kind: "GoCB", Payload: LiveIedControlBlockModel gooseBlock } &&
            SelectedDevice is { } owner)
            PrepareIedBrowserGoose(owner, gooseBlock, model);
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
            : connected ? "Connected · Report values shown; use Read for CF/DC configuration values"
                        : "Offline · cached SCL/discovery model";
        ReplaceIedBrowserDetails(IedBrowserPresentation.Details(row, model,
            connected, SelectedDevice?.SclWorkspace?.DesignModel));
        _iedBrowserLastLiveVersion = -1;
        RefreshIedBrowserVisibleValues();
        RefreshIedBrowserReadButtons();
        QueueIedBrowserFocusedRead(row);
        QueueIedBrowserRcbFocusedRead(row);
        // Folder entry opens at its first child, regardless of the scroll
        // position in the previously inspected RCB, LN or DataSet.
        if (_iedBrowserDetails.Rows.Count > 0)
            IedBrowserDetailList.ScrollIntoView(_iedBrowserDetails.Rows[0]);
    }

}
