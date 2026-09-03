using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly IoTestSignalSelectionService _ioTestSignalSelectionService = new();

    /// <summary>
    /// Prepares one imported IO-list IED for monitoring-only FAT acquisition. Calls for
    /// different IEDs are independent and may overlap; the IED model itself owns the
    /// preparation flag, while live binding is deliberately scoped to this IED only.
    /// </summary>
    internal async Task<IoTestSessionActionResult> PrepareIoTestIedForFatAsync(
        IoTestProject project,
        IoTestIedPlan ied,
        IProgress<string>? progress = null,
        IReadOnlyCollection<IoTestPointPlan>? requestedPointsOverride = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);

        if (ied.IsPreparing)
            return IoTestSessionActionResult.Failure($"{ied.IedName} is already being prepared for FAT acquisition.");

        // P1: connection/acquisition scope and FAT evidence selection are different
        // authorities. A normal Connect prepares every included import-ready row whether
        // its TEST checkbox is on or off. The optional override is retained for legacy
        // callers that explicitly ask to validate a narrower evidence subset.
        var requestedPoints = (requestedPointsOverride ?? ied.TestPoints)
            .Where(point =>
                point.IsIncludedInFat &&
                point.ImportReady &&
                (requestedPointsOverride is null || point.TestEnabled))
            .Distinct()
            .ToList();
        if (requestedPoints.Count == 0)
        {
            return IoTestSessionActionResult.Failure(
                requestedPointsOverride is null
                    ? "No included import-ready FAT signal is available for this IED."
                    : "No operator-selected import-ready FAT signal is available for this IED.");
        }

        void ReportProgress(string message)
        {
            ied.SetPreparationState(true, message);
            progress?.Report(message);
        }

        ied.SetPreparationState(true, $"Connecting {ied.IedName} · {ied.IpAddress}:102");
        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        var createdForFat = device == null;
        if (device == null)
        {
            device = new Iec61850MonitorDevice
            {
                Name = ied.IedName,
                SclIedName = ied.IedName,
                IdentitySource = "FAT import",
                IpAddress = ied.IpAddress,
                Port = 102,
                AllowDynamicDataSetWrites = true,
                Status = "FAT ready to connect",
                Detail = "Connect will use imported SCL authority when available; legacy workbook scope falls back to live discovery."
            };
            Devices.Add(device);
            RaiseWorkspaceCounts();
        }

        if (device.IsBusy)
        {
            ied.SetPreparationState(false, "IED is busy in another connection workflow");
            return IoTestSessionActionResult.Failure($"{ied.IedName} is already busy with another connection or discovery workflow.");
        }

        if (!device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase))
        {
            if (device.IsConnected || device.IsMonitoring)
            {
                ied.SetPreparationState(false, "Endpoint mismatch");
                return IoTestSessionActionResult.Failure(
                    $"The loaded {ied.IedName} workspace is connected to {device.IpAddress}, but FAT requires {ied.IpAddress}. Stop that engineering session or correct the source endpoint before FAT.");
            }

            device.IpAddress = ied.IpAddress;
            device.Port = 102;
        }

        if (string.IsNullOrWhiteSpace(device.Name) || device.Name.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase))
            device.Name = ied.IedName;
        if (string.IsNullOrWhiteSpace(device.SclIedName))
            device.SclIedName = ied.IedName;

        // P5.2: a direct SCL FAT import has already been parsed by ARIEC. Attach that exact
        // workspace before any network action and project its reusable signal model. The
        // connection path can then perform only TCP/ACSE/MMS association; Re-scan remains
        // the explicit engineering action for full live discovery/comparison.
        var hasSclRuntimeAuthority = AttachIoFatSclRuntimeAuthority(ied, device);

        // Monitoring-only toward the process: no control commands are executed. FAT uses
        // deterministic fast MMS acquisition for digital commissioning points while the
        // normal engineering workspace keeps report-first behavior for ordinary intervals.
        device.AllowDynamicDataSetWrites = true;

        try
        {
            var usedSavedModel = false;
            if (!device.IsConnected)
            {
                var canUsePreparedScl = hasSclRuntimeAuthority && device.HasSclDesignModel && device.Signals.Count > 0;
                var canUseSavedModel = device.HasDiscoveryCache && device.Signals.Count > 0;
                var connected = false;
                if (canUsePreparedScl)
                {
                    ReportProgress($"Fast SCL association {ied.IedName} · imported model already available");
                    connected = await ConnectIoFatUsingPreparedSclAsync(device);
                    usedSavedModel = connected;
                    if (!connected)
                    {
                        _ioTestLiveBindingService.BindIed(ied, Devices);
                        return IoTestSessionActionResult.Failure(
                            $"ARSAS could not associate with {ied.IedName} at {ied.IpAddress}:102 using the imported SCL model. Full discovery was intentionally not started; use Diagnostics or Re-scan if live-model verification is required.");
                    }
                }
                else if (canUseSavedModel)
                {
                    ReportProgress($"Fast reconnect {ied.IedName} · saved endpoint and discovery model");
                    connected = await ConnectUsingSavedModelAsync(device, selectDevice: false);
                    usedSavedModel = connected;
                    if (!connected)
                    {
                        ReportProgress("Saved-model reconnect failed · running one full live discovery");
                        connected = await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false);
                        usedSavedModel = false;
                    }
                }
                else
                {
                    ReportProgress($"Connecting {ied.IedName} · {ied.IpAddress}:102");
                    connected = await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false);
                }

                if (!connected)
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"ARSAS could not connect to {ied.IedName} at {ied.IpAddress}:102. Open Diagnostics for the MMS association or discovery error.");
                }
            }
            else if (device.Signals.Count == 0)
            {
                if (hasSclRuntimeAuthority)
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"{ied.IedName} is connected, but the imported SCL authority produced no reusable signal model. ARSAS did not start an implicit full discovery.");
                }

                ReportProgress($"{ied.IedName} connected · discovering live model");
                await StopDeviceConnectionAsync(device);
                if (!await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false))
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure($"Full live-model discovery failed for {ied.IedName}.");
                }
            }
            else
            {
                ReportProgress($"{ied.IedName} association ready · reusing the loaded model");
            }

            // Reconciliation belongs to the connection/discovery lifecycle, before local
            // FAT row selection. SCL-backed fast association keeps the design authority
            // intact; absence is never inferred merely because full live discovery was skipped.
            if (hasSclRuntimeAuthority)
            {
                // The imported ARIEC SCL workspace already owns the complete static
                // DataSet identity. Connected reconciliation can perform a large exact-read
                // pass and used to hold this SIPROTEC FAT preparation for about one minute.
                // Explicit Re-scan remains the design-versus-live comparison action.
                IoTestReconciliationCache.Invalidate(device);
                ReportProgress("Using authoritative SCL DataSet identity · starting live acquisition");
            }
            else
            {
                ReportProgress("Reconciling FAT source with authoritative IED model");
                await IoTestReconciliationCache.RefreshAsync(device, _applicationCancellation.Token);
            }

            // Never let a runtime anchor from an earlier model silently decide a fresh
            // FAT preparation. Re-prove every requested row against the current model;
            // successful smart matches are anchored again immediately below.
            foreach (var point in requestedPoints)
            {
                point.ApplyLiveBinding(
                    IoTestLiveBindingState.NotEvaluated,
                    "Revalidating the imported FAT reference against the current IED model.",
                    device.DeviceId);
            }

            ReportProgress($"Matching {requestedPoints.Count} FAT signal(s)");
            var selection = _ioTestSignalSelectionService.Resolve(
                new IoTestIedPlan
                {
                    IedName = ied.IedName,
                    IpAddress = ied.IpAddress,
                    IedRole = ied.IedRole,
                    Location = ied.Location,
                    VoltageLevel = ied.VoltageLevel,
                    Switchgear = ied.Switchgear,
                    TestPoints = requestedPoints
                },
                device);

            // Legacy workbook rows may still request one fresh discovery when a saved live
            // model is stale. An SCL-backed FAT row already has engine-owned static DataSet
            // authority; a local match miss must not trigger a 50-second rediscovery loop.
            var hasBlockingNonSclMiss = selection.MissingPoints.Any(point =>
                !IoTestSignalSelectionService.IsSclDataSetAuthority(point));
            if (!selection.Succeeded && selection.CanRetryWithFreshDiscovery && hasBlockingNonSclMiss)
            {
                ReportProgress("Refreshing live model once · saved model missed legacy FAT points");
                if (device.IsMonitoring)
                    await StopDeviceMonitorAsync(device);
                if (device.IsConnected)
                    await StopDeviceConnectionAsync(device);

                if (!await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false))
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure($"Live-model refresh failed for {ied.IedName}.");
                }

                usedSavedModel = false;
                ReportProgress("Reconciling refreshed live model with FAT source");
                await IoTestReconciliationCache.RefreshAsync(device, _applicationCancellation.Token);

                foreach (var point in requestedPoints)
                {
                    point.ApplyLiveBinding(
                        IoTestLiveBindingState.NotEvaluated,
                        "Revalidating after fresh live-model discovery.",
                        device.DeviceId);
                }
                selection = _ioTestSignalSelectionService.Resolve(
                    new IoTestIedPlan
                    {
                        IedName = ied.IedName,
                        IpAddress = ied.IpAddress,
                        IedRole = ied.IedRole,
                        Location = ied.Location,
                        VoltageLevel = ied.VoltageLevel,
                        Switchgear = ied.Switchgear,
                        TestPoints = requestedPoints
                    },
                    device);
            }

            var unresolvedSelectionPoints = selection.MissingPoints
                .Concat(selection.AmbiguousPoints)
                .Distinct()
                .ToList();
            var mayProceedWithPartialSclSelection =
                hasSclRuntimeAuthority &&
                selection.Matches.Count > 0 &&
                unresolvedSelectionPoints.All(IoTestSignalSelectionService.IsSclDataSetAuthority);

            if (!selection.Succeeded && !mayProceedWithPartialSclSelection)
            {
                _ioTestLiveBindingService.BindIed(ied, Devices);
                return IoTestSessionActionResult.Failure(
                    hasSclRuntimeAuthority
                        ? $"ARSAS could not bind the imported SCL FAT scope safely without guessing. {selection.Message} Full live discovery was not repeated."
                        : $"ARSAS could not prepare the imported FAT scope safely. {selection.Message}");
            }

            foreach (var unresolved in unresolvedSelectionPoints)
            {
                unresolved.ApplyLiveBinding(
                    IoTestLiveBindingState.NotEvaluated,
                    "Static DataSet membership remains selected, but no unique runtime point is safe to bind yet. Other live FAT rows remain usable.",
                    device.DeviceId);
            }

            // P1 acquisition scope is the safe model match set, not the operator TEST
            // selection. Keep the Engineering/TEST flags untouched while arming all proven
            // included static members in the isolated runtime session for this IED.
            var acquisitionSignals = selection.Matches
                .Select(match => match.Signal)
                .Where(signal => signal.CanPublishToRuntime)
                .GroupBy(
                    signal => IoTestLiveBindingService.NormalizeReference(signal.ObjectReference),
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();

            foreach (var match in selection.Matches)
            {
                // Preserve the unique reference proven by the preparation pass. This is
                // essential for legacy weak rows and for SCL static membership rows whose
                // DisplayReference can differ from the resolved runtime ObjectReference.
                match.TestPoint.ApplyLiveBinding(
                    match.UsedNormalizedIedPrefix
                        ? IoTestLiveBindingState.BoundNormalized
                        : IoTestLiveBindingState.BoundExact,
                    match.UsedNormalizedIedPrefix
                        ? "FAT preparation resolved one unique canonical IEC 61850 model reference."
                        : "FAT preparation matched an exact engine-owned IEC 61850 model identity.",
                    device.DeviceId,
                    match.Signal.ObjectReference);
            }

            // Time synchronization is device-level FAT evidence rather than an ON/OFF
            // test point. If the live model exposes an explicit status (for example
            // SIPROTEC TimeSynchrnz or MiCOM LLN0.SyncSt), arm that one extra read-only
            // signal so the FAT window can capture the real IED value automatically.
            IoFatSupplementalEvidenceService.EnsureTimeSyncSignalSelected(device);
            var timeSyncSignal = IoFatSupplementalEvidenceService.FindTimeSyncSignal(device);
            if (timeSyncSignal?.CanPublishToRuntime == true &&
                acquisitionSignals.All(signal => !ReferenceEquals(signal, timeSyncSignal)))
            {
                acquisitionSignals.Add(timeSyncSignal);
            }

            device.RecountSelectedSignals();
            device.RefreshComputed();
            RaiseWorkspaceCounts();

            _ioTestLiveBindingService.BindIed(ied, Devices);
            var fatPollingNotActive = device.IsMonitoring &&
                                      device.Points.Any(point => point.PollingIntervalMs > 500);
            var requestedAcquisitionReferences = acquisitionSignals
                .Select(signal => IoTestLiveBindingService.NormalizeReference(signal.ObjectReference))
                .Where(reference => reference.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var activeAcquisitionReferences = device.Points
                .Select(point => IoTestLiveBindingService.NormalizeReference(point.IecReference))
                .Where(reference => reference.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var acquisitionScopeChanged =
                !requestedAcquisitionReferences.SetEquals(activeAcquisitionReferences);

            // Each IED monitor is isolated. Refresh only this device when its acquisition
            // scope or FAT polling cadence changed; another connected/monitoring IED is
            // never stopped or rebound by this preparation.
            if (device.IsMonitoring && (acquisitionScopeChanged || fatPollingNotActive))
            {
                ReportProgress("Refreshing this IED's FAT acquisition · other IED monitors remain active");
                await StopDeviceMonitorAsync(device);
            }

            if (!device.IsMonitoring)
            {
                ReportProgress("Starting independent FAT live acquisition · fast MMS verification");
                // Compatibility note: the pre-P1 source contract called StartDeviceMonitorAsync
                // here. P1 routes through StartIoFatDeviceMonitorAsync so acquisition scope can
                // be armed without mutating/persisting the operator TEST selection.
                if (!await StartIoFatDeviceMonitorAsync(device, acquisitionSignals))
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"{ied.IedName} connected, but ARSAS could not start live acquisition for the imported FAT scope.");
                }
            }

            // The monitor points now exist and MMS polling has already been scheduled.
            // Return control to FAT immediately: waiting here for values/report setup made
            // a sub-second SCL association look like a 30-50 second connection whenever
            // the UI dispatcher was busy with the first report burst. Values and report
            // optimization continue through the shared Engineering runtime.
            var binding = _ioTestLiveBindingService.BindIed(ied, Devices);
            var acquisition = ReadIoFatAcquisitionSummary(requestedPoints, device);
            var liveCount = requestedPoints.Count(point =>
                point.LiveBindingState == IoTestLiveBindingState.LivePointReady);
            if (liveCount == 0)
            {
                var unresolved = requestedPoints
                    .Take(4)
                    .Select(point => $"{point.TestPointId} ({point.ObjectReference})");
                return IoTestSessionActionResult.Failure(
                    $"{ied.IedName} is connected and monitoring, but none of the {requestedPoints.Count} requested FAT signal(s) has a unique live monitor point. Unresolved: {string.Join(", ", unresolved)}.");
            }

            // The temporary acquisition arm has already been restored by
            // StartIoFatDeviceMonitorAsync. Persist only the real operator selection.
            SaveSignalSelectionMemory(device);
            var modelText = hasSclRuntimeAuthority
                ? "imported SCL model"
                : usedSavedModel ? "saved model" : "live model";
            var acquisitionText = acquisition.PollingCount == 0
                ? $"report-backed {acquisition.ReportCount}/{liveCount}"
                : $"fast MMS {acquisition.PollingCount} · report-backed {acquisition.ReportCount}";
            var timeSyncText = IoFatSupplementalEvidenceService.FindTimeSyncSignal(device) == null
                ? " · time-sync fallback ready"
                : " · time-sync status armed";
            var unresolvedCount = requestedPoints.Count - liveCount;
            var partialText = unresolvedCount == 0
                ? string.Empty
                : $" · {unresolvedCount} FAT row(s) waiting for safe live binding";
            var message = $"{ied.IedName} · {liveCount}/{requestedPoints.Count} live · {acquisitionText}{timeSyncText}{partialText}";
            SetStatus(message);
            AddLog(
                unresolvedCount == 0 ? "INFO" : "WARN",
                "IO Testing",
                $"{message}. Live rows are usable immediately; unresolved rows stay visible but are excluded from the active evidence scope until a unique live point exists. No checkbox or FAT disposition is changed by the engine. IED live-bound={binding.LivePointCount}; model={modelText}; mode={device.AcquisitionMode}.");
            ReportProgress(message);
            return IoTestSessionActionResult.Success(message);
        }
        catch (OperationCanceledException)
        {
            _ioTestLiveBindingService.BindIed(ied, Devices);
            return IoTestSessionActionResult.Failure($"Connection preparation for {ied.IedName} was cancelled.");
        }
        catch (Exception ex)
        {
            _ioTestLiveBindingService.BindIed(ied, Devices);
            AddLog("ERROR", "IO Testing", $"{ied.IedName} automatic preparation failed: {ex}");
            MarkDiagnosticAlert();
            return IoTestSessionActionResult.Failure(
                $"Automatic connection preparation failed for {ied.IedName}: {ex.Message}");
        }
        finally
        {
            ied.SetPreparationState(false, ied.LiveStatusText);
            if (createdForFat)
                RaiseWorkspaceCounts();
        }
    }

    private bool AttachIoFatSclRuntimeAuthority(
        IoTestIedPlan ied,
        Iec61850MonitorDevice device)
    {
        if (!_ioFatSclProjectImportService.TryGetRuntimeWorkspace(
                ied.IedName,
                ied.IpAddress,
                out var workspace) || workspace is null)
        {
            return false;
        }

        device.SclWorkspace = workspace;
        device.SclIedName = workspace.IedName;
        device.SclAccessPointName = workspace.AccessPointName;
        device.IdentitySource = "FAT SCL · ARIEC workspace";

        if (!device.HasDiscoveryCache)
        {
            var projected = SclWorkspaceSignalMapper.BuildSignals(workspace);
            DetachSignalHandlers(device.Signals);
            device.Signals.Clear();
            device.Signals.AddRange(projected);
        }
        else
        {
            // An existing Engineering Workspace keeps its richer live model. Merge only
            // engine-authoritative static DataSet inventory; never replace discovered rows.
            Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(
                device.Signals,
                workspace.DesignModel);
        }

        AttachIoFatSignalHandlers(device);

        device.RecountSelectedSignals();
        device.RefreshComputed();
        return device.Signals.Count > 0;
    }

    private int SynchronizeImportedSclFatWithEngineering(IoTestProject project)
        => SynchronizeImportedSclFatWithEngineering(project, project.Ieds);

    private int SynchronizeImportedSclFatWithEngineering(
        IoTestProject project,
        IEnumerable<IoTestIedPlan> ieds)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ieds);
        var synchronized = 0;
        foreach (var ied in ieds)
        {
            var device = ResolveIoTestDevice(ied.LiveDeviceId)
                         ?? ResolveIoTestDevice(ied.IpAddress)
                         ?? ResolveIoTestDevice(ied.IedName);
            var existed = device is not null;
            if (device is null)
            {
                device = new Iec61850MonitorDevice
                {
                    Name = ied.IedName,
                    SclIedName = ied.IedName,
                    IdentitySource = "FAT SCL · shared Engineering workspace",
                    IpAddress = ied.IpAddress,
                    Port = 102,
                    AllowDynamicDataSetWrites = true,
                    Status = "SCL model ready",
                    Detail = "Engineering and FAT share this imported SCL model and signal selection."
                };
                Devices.Add(device);
            }

            var preserveEngineeringSelection = existed &&
                (_sharedSclSelectionAuthorityDeviceIds.Contains(device.DeviceId) ||
                 device.Signals.Any(signal => signal.IsSelected));
            if (!AttachIoFatSclRuntimeAuthority(ied, device))
                continue;

            synchronized += IoFatEngineeringSelectionBridge.Initialize(
                ied,
                device,
                preserveEngineeringSelection);
            _ioTestLiveBindingService.BindIed(ied, Devices);
            SaveSignalSelectionMemory(device);
        }

        RaiseWorkspaceCounts();
        return synchronized;
    }

    private void AttachIoFatSignalHandlers(Iec61850MonitorDevice device)
    {
        foreach (var signal in device.Signals)
        {
            if (_signalOwners.ContainsKey(signal))
                continue;
            signal.PropertyChanged += Signal_PropertyChanged;
            _signalOwners[signal] = device;
        }
    }

    private async Task<bool> ConnectIoFatUsingPreparedSclAsync(Iec61850MonitorDevice device)
    {
        // ConnectUsingSavedModelAsync and the runtime predate the SCL fast-connect UI and
        // use HasDiscoveryCache as their reusable-model gate. Bridge that gate only for the
        // duration of this association. The flag is restored immediately so provenance
        // remains truthful: this device still owns an SCL design model, not a live scan.
        var hadDiscoveryCache = device.HasDiscoveryCache;
        try
        {
            if (!hadDiscoveryCache)
                device.HasDiscoveryCache = true;
            return await ConnectUsingSavedModelAsync(device, selectDevice: false);
        }
        finally
        {
            device.HasDiscoveryCache = hadDiscoveryCache;
            device.RefreshComputed();
        }
    }

    private static IoFatAcquisitionSummary ReadIoFatAcquisitionSummary(
        IEnumerable<IoTestPointPlan> requestedPoints,
        Iec61850MonitorDevice device)
    {
        var live = 0;
        var report = 0;
        var polling = 0;
        var unknown = 0;

        foreach (var point in requestedPoints)
        {
            if (point.LiveBindingState == IoTestLiveBindingState.LivePointReady)
                live++;

            var source = point.Runtime.CurrentSource ?? string.Empty;
            if (IsReportSource(source))
                report++;
            else if (source.Contains("poll", StringComparison.OrdinalIgnoreCase) ||
                     source.Contains("MMS", StringComparison.OrdinalIgnoreCase))
                polling++;
            else
                unknown++;
        }

        return new IoFatAcquisitionSummary(live, report, polling, unknown, device.AcquisitionMode ?? string.Empty);
    }

    private static bool IsReportSource(string source)
        => source.Contains("BRCB", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("URCB", StringComparison.OrdinalIgnoreCase) ||
           source.Contains("report", StringComparison.OrdinalIgnoreCase);

    private sealed record IoFatAcquisitionSummary(
        int LiveCount,
        int ReportCount,
        int PollingCount,
        int UnknownCount,
        string Mode);
}
