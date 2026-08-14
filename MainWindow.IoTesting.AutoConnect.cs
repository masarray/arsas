using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
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

        var requestedPoints = (requestedPointsOverride ?? ied.TestPoints)
            .Where(point => point.TestEnabled && point.ImportReady)
            .Distinct()
            .ToList();
        if (requestedPoints.Count == 0)
            return IoTestSessionActionResult.Failure("No import-ready IO-list signal is enabled for this IED.");

        void ReportProgress(string message)
        {
            ied.SetPreparationState(true, message);
            progress?.Report(message);
        }

        ied.SetPreparationState(true, $"Connecting {ied.IedName} · {ied.IpAddress}:102");
        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);
        var createdFromWorkbook = device == null;
        if (device == null)
        {
            device = new Iec61850MonitorDevice
            {
                Name = ied.IedName,
                SclIedName = ied.IedName,
                IdentitySource = "IO List workbook",
                IpAddress = ied.IpAddress,
                Port = 102,
                AllowDynamicDataSetWrites = true,
                Status = "IO FAT ready to connect",
                Detail = "Connect will discover the live model and arm report-first acquisition for this IED's imported FAT scope."
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
                    $"The loaded {ied.IedName} workspace is connected to {device.IpAddress}, but the IO list requires {ied.IpAddress}. Stop that engineering session or correct the workbook endpoint before FAT.");
            }

            device.IpAddress = ied.IpAddress;
            device.Port = 102;
        }

        if (string.IsNullOrWhiteSpace(device.Name) || device.Name.Equals(device.IpAddress, StringComparison.OrdinalIgnoreCase))
            device.Name = ied.IedName;
        if (string.IsNullOrWhiteSpace(device.SclIedName))
            device.SclIedName = ied.IedName;

        // Monitoring-only toward the process: no control commands are executed. FAT uses
        // deterministic fast MMS acquisition for digital commissioning points while the
        // normal engineering workspace keeps report-first behavior for ordinary intervals.
        device.AllowDynamicDataSetWrites = true;

        try
        {
            var usedSavedModel = false;
            if (!device.IsConnected)
            {
                var canUseSavedModel = device.HasDiscoveryCache && device.Signals.Count > 0;
                var connected = false;
                if (canUseSavedModel)
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

            ReportProgress($"Matching {requestedPoints.Count} workbook signal(s)");
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
            if (!selection.Succeeded && selection.CanRetryWithFreshDiscovery)
            {
                ReportProgress("Refreshing live model once · saved model missed workbook points");
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

            if (!selection.Succeeded)
            {
                _ioTestLiveBindingService.BindIed(ied, Devices);
                return IoTestSessionActionResult.Failure(
                    $"ARSAS could not prepare the imported FAT scope safely. {selection.Message}");
            }

            var selectionChanged = false;
            foreach (var match in selection.Matches)
            {
                // Preserve the unique reference proven by the preparation pass. This is
                // essential for legacy weak rows such as `.Op.general`: the live-binding
                // phase must follow the proven exact model object rather than re-guess it.
                match.TestPoint.ApplyLiveBinding(
                    match.UsedNormalizedIedPrefix
                        ? IoTestLiveBindingState.BoundNormalized
                        : IoTestLiveBindingState.BoundExact,
                    match.UsedNormalizedIedPrefix
                        ? "FAT preparation resolved one unique canonical IEC 61850 model reference."
                        : "FAT preparation matched the exact imported IEC 61850 model reference.",
                    device.DeviceId,
                    match.Signal.ObjectReference);

                if (match.Signal.IsSelected)
                    continue;
                match.Signal.IsSelected = true;
                selectionChanged = true;
            }

            // Time synchronization is device-level FAT evidence rather than an ON/OFF
            // test point. If the live model exposes an explicit status (for example
            // SIPROTEC TimeSynchrnz or MiCOM LLN0.SyncSt), arm that one extra read-only
            // signal so the FAT window can capture the real IED value automatically.
            var timeSyncArmed = IoFatSupplementalEvidenceService.EnsureTimeSyncSignalSelected(device);
            if (timeSyncArmed)
                selectionChanged = true;

            device.RecountSelectedSignals();
            device.RefreshComputed();
            RaiseWorkspaceCounts();

            // Reconciliation belongs to the connection/discovery lifecycle, not to the
            // synchronous UI binding loop. P1.2 remains probe:null inside the cache;
            // P1.3 can later replace that producer with the engine-owned connected facade.
            ReportProgress("Reconciling SCL design with authoritative live model");
            await IoTestReconciliationCache.RefreshAsync(device, _applicationCancellation.Token);

            _ioTestLiveBindingService.BindIed(ied, Devices);
            var allRequestedPointsLive = requestedPoints.All(point =>
                point.LiveBindingState == IoTestLiveBindingState.LivePointReady);
            var fatPollingNotActive = device.IsMonitoring &&
                                      device.Points.Any(point => point.PollingIntervalMs > 500);

            if (device.IsMonitoring && (selectionChanged || !allRequestedPointsLive || fatPollingNotActive))
            {
                ReportProgress("Refreshing FAT acquisition · deterministic fast digital polling");
                await StopDeviceMonitorAsync(device);
            }

            if (!device.IsMonitoring)
            {
                ReportProgress("Starting FAT live acquisition · fast MMS verification");
                if (!await StartDeviceMonitorAsync(device, navigateToExplorer: false))
                {
                    _ioTestLiveBindingService.BindIed(ied, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"{ied.IedName} connected, but ARSAS could not start live acquisition for the imported FAT scope.");
                }
            }

            // Do not hold the operator behind an 8–18 second report-settling phase. FAT
            // readiness is the first usable live image; report planning for non-fast points
            // can continue in the monitor loop after the card is already responsive.
            var acquisition = await SettleIoFatReportPriorityAsync(
                ied,
                requestedPoints,
                device,
                ReportProgress);

            var binding = _ioTestLiveBindingService.BindIed(ied, Devices);
            var liveCount = requestedPoints.Count(point =>
                point.LiveBindingState == IoTestLiveBindingState.LivePointReady);
            if (liveCount != requestedPoints.Count)
            {
                var unresolved = requestedPoints
                    .Where(point => point.LiveBindingState != IoTestLiveBindingState.LivePointReady)
                    .Take(4)
                    .Select(point => $"{point.TestPointId} ({point.ObjectReference})");
                return IoTestSessionActionResult.Failure(
                    $"{ied.IedName} is connected and monitoring, but only {liveCount}/{requestedPoints.Count} imported signal(s) became live. Unresolved: {string.Join(", ", unresolved)}.");
            }

            SaveSignalSelectionMemory(device);
            var modelText = usedSavedModel ? "saved model" : "live model";
            var acquisitionText = acquisition.PollingCount == 0
                ? $"report-backed {acquisition.ReportCount}/{requestedPoints.Count}"
                : $"fast MMS {acquisition.PollingCount} · report-backed {acquisition.ReportCount}";
            var timeSyncText = IoFatSupplementalEvidenceService.FindTimeSyncSignal(device) == null
                ? " · time-sync fallback ready"
                : " · time-sync status armed";
            var message = $"{ied.IedName} · {liveCount}/{requestedPoints.Count} live · {acquisitionText}{timeSyncText}";
            SetStatus(message);
            AddLog(
                acquisition.PollingCount == 0 ? "INFO" : "INFO",
                "IO Testing",
                $"{message}. FAT acquisition prioritizes deterministic fast MMS for digital commissioning points; normal engineering monitoring retains report-first behavior. No process control commands are enabled. IED live-bound={binding.LivePointCount}; model={modelText}; mode={device.AcquisitionMode}.");
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
            if (createdFromWorkbook)
                RaiseWorkspaceCounts();
        }
    }

    private async Task<IoFatAcquisitionSummary> SettleIoFatReportPriorityAsync(
        IoTestIedPlan ied,
        IReadOnlyCollection<IoTestPointPlan> requestedPoints,
        Iec61850MonitorDevice device,
        Action<string> reportProgress)
    {
        return await ObserveIoFatAcquisitionAsync(
            ied,
            requestedPoints,
            device,
            reportProgress,
            TimeSpan.FromMilliseconds(2500));
    }

    private async Task<IoFatAcquisitionSummary> ObserveIoFatAcquisitionAsync(
        IoTestIedPlan ied,
        IReadOnlyCollection<IoTestPointPlan> requestedPoints,
        Iec61850MonitorDevice device,
        Action<string> reportProgress,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = ReadIoFatAcquisitionSummary(requestedPoints, device);
        var announced = false;
        var nextBindingRefreshUtc = DateTime.MinValue;

        while (DateTime.UtcNow < deadline && device.IsMonitoring)
        {
            await Task.Delay(100);
            var nowUtc = DateTime.UtcNow;
            if (nowUtc >= nextBindingRefreshUtc)
            {
                _ioTestLiveBindingService.BindIed(ied, Devices);
                nextBindingRefreshUtc = nowUtc.AddMilliseconds(250);
            }
            last = ReadIoFatAcquisitionSummary(requestedPoints, device);

            if (!announced)
            {
                reportProgress("Waiting for first live FAT image · report optimization continues in background");
                announced = true;
            }

            var liveImageReady = requestedPoints.All(HasUsableFatLiveValue);
            if (last.LiveCount == requestedPoints.Count && liveImageReady && last.UnknownCount == 0)
                break;
        }

        _ioTestLiveBindingService.BindIed(ied, Devices);
        return ReadIoFatAcquisitionSummary(requestedPoints, device);
    }

    private static bool HasUsableFatLiveValue(IoTestPointPlan point)
    {
        var value = (point.Runtime.CurrentValue ?? string.Empty).Trim();
        return value.Length > 0 && value != "-" && value != "—" &&
               !value.Equals("Unknown", StringComparison.OrdinalIgnoreCase) &&
               !value.Contains("pending", StringComparison.OrdinalIgnoreCase);
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
