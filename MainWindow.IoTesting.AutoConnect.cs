using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

public partial class MainWindow
{
    private readonly IoTestSignalSelectionService _ioTestSignalSelectionService = new();

    /// <summary>
    /// Prepares one imported IO-list IED for a monitoring-only FAT session. The workbook
    /// supplies the endpoint and exact signal scope, so the operator does not need to
    /// duplicate Add IED and signal-selection work in the engineering window.
    /// </summary>
    internal async Task<IoTestSessionActionResult> PrepareIoTestIedForFatAsync(
        IoTestProject project,
        IoTestIedPlan ied,
        IProgress<string>? progress = null)
    {
        ArgumentNullException.ThrowIfNull(project);
        ArgumentNullException.ThrowIfNull(ied);

        var requestedPoints = ied.TestPoints
            .Where(point => point.TestEnabled && point.ImportReady)
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
                Detail = "Connect & Start will discover the live model and arm report-first acquisition for the imported FAT scope."
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

        // Monitoring-only toward the process: no control commands are executed. Use
        // configured RCB/DataSet coverage first, then an association-scoped temporary
        // dynamic DataSet/URCB when exact coverage is missing, with bounded MMS
        // verification/fallback last. Temporary report resources are released when the
        // native monitoring session stops.
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
                    _ioTestLiveBindingService.Bind(project, Devices);
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
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure($"Full live-model discovery failed for {ied.IedName}.");
                }
            }
            else
            {
                ReportProgress($"{ied.IedName} association ready · reusing the loaded model");
            }

            ReportProgress($"Matching {requestedPoints.Count} workbook signal(s)");
            var selection = _ioTestSignalSelectionService.Resolve(ied, device);
            if (!selection.Succeeded && selection.CanRetryWithFreshDiscovery)
            {
                ReportProgress("Refreshing live model once · saved model missed workbook points");
                if (device.IsMonitoring)
                    await StopDeviceMonitorAsync(device);
                if (device.IsConnected)
                    await StopDeviceConnectionAsync(device);

                if (!await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false))
                {
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure($"Live-model refresh failed for {ied.IedName}.");
                }

                usedSavedModel = false;
                selection = _ioTestSignalSelectionService.Resolve(ied, device);
            }

            if (!selection.Succeeded &&
                selection.AmbiguousPoints.Count == 0 &&
                selection.Matches.Count > 0 &&
                selection.MissingPoints.Count > 0)
            {
                // A missing read-only/status tag is a configuration finding, not a
                // reason to block the rest of the FAT scope. The canonical resolver
                // has already exhausted exact, wrapper, vendor-LD, and weak-reference
                // forms. Keep the evidence trail, uncheck only proven-missing rows,
                // and continue with the unique live matches.
                var missingFindingCount = selection.MissingPoints.Count;
                _ioTestLiveBindingService.Bind(project, Devices);
                foreach (var missingPoint in selection.MissingPoints)
                {
                    missingPoint.TestEnabled = false;
                    AddLog(
                        "WARN",
                        "IO Testing",
                        $"FAT finding: {ied.IedName} {missingPoint.TestPointId} was not found in the discovered IED model and was disabled for this run. {missingPoint.LiveBindingDiagnostics}");
                }

                requestedPoints = requestedPoints
                    .Where(point => point.TestEnabled && point.ImportReady)
                    .ToList();
                selection = new IoTestSignalSelectionResult(
                    selection.Matches,
                    Array.Empty<IoTestPointPlan>(),
                    selection.AmbiguousPoints,
                    $"Resolved {selection.Matches.Count} signal(s); {missingFindingCount} missing row(s) were disabled as FAT findings.");
                ReportProgress($"{missingFindingCount} workbook row(s) are findings · continuing with {requestedPoints.Count} live signal(s)");
            }

            if (!selection.Succeeded)
            {
                _ioTestLiveBindingService.Bind(project, Devices);
                return IoTestSessionActionResult.Failure(
                    $"ARSAS could not prepare the imported FAT scope safely. {selection.Message}");
            }

            var selectionChanged = false;
            foreach (var match in selection.Matches)
            {
                if (match.Signal.IsSelected)
                    continue;
                match.Signal.IsSelected = true;
                selectionChanged = true;
            }
            device.RecountSelectedSignals();
            device.RefreshComputed();
            RaiseWorkspaceCounts();

            _ioTestLiveBindingService.Bind(project, Devices);
            var allRequestedPointsLive = requestedPoints.All(point =>
                point.LiveBindingState == IoTestLiveBindingState.LivePointReady);

            if (device.IsMonitoring && (selectionChanged || !allRequestedPointsLive))
            {
                ReportProgress("Refreshing report acquisition for the workbook scope");
                await StopDeviceMonitorAsync(device);
            }

            if (!device.IsMonitoring)
            {
                ReportProgress("Arming static RCB first · dynamic DataSet/URCB for uncovered points");
                if (!await StartDeviceMonitorAsync(device, navigateToExplorer: false))
                {
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"{ied.IedName} connected, but ARSAS could not start live acquisition for the imported FAT scope.");
                }
            }

            var acquisition = await SettleIoFatReportPriorityAsync(
                project,
                requestedPoints,
                device,
                ReportProgress,
                allowSingleRestart: true);

            var binding = _ioTestLiveBindingService.Bind(project, Devices);
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
                : $"report-backed {acquisition.ReportCount}/{requestedPoints.Count} · MMS fallback {acquisition.PollingCount}";
            var message = $"{ied.IedName} · {liveCount}/{requestedPoints.Count} live · {acquisitionText}";
            SetStatus(message);
            AddLog(
                acquisition.PollingCount == 0 ? "INFO" : "WARN",
                "IO Testing",
                $"{message}. Acquisition policy: configured RCB → temporary dynamic DataSet/URCB → bounded MMS verification/fallback. No process control commands are enabled. Project live-bound={binding.LivePointCount}; model={modelText}; mode={device.AcquisitionMode}.");
            ReportProgress(message);
            return IoTestSessionActionResult.Success(message);
        }
        catch (OperationCanceledException)
        {
            _ioTestLiveBindingService.Bind(project, Devices);
            return IoTestSessionActionResult.Failure($"Connection preparation for {ied.IedName} was cancelled.");
        }
        catch (Exception ex)
        {
            _ioTestLiveBindingService.Bind(project, Devices);
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
        IoTestProject project,
        IReadOnlyCollection<IoTestPointPlan> requestedPoints,
        Iec61850MonitorDevice device,
        Action<string> reportProgress,
        bool allowSingleRestart)
    {
        var first = await ObserveIoFatAcquisitionAsync(
            project,
            requestedPoints,
            device,
            reportProgress,
            TimeSpan.FromSeconds(8));

        if (!allowSingleRestart ||
            first.PollingCount == 0 ||
            !device.AllowDynamicDataSetWrites ||
            !device.IsMonitoring)
        {
            return first;
        }

        reportProgress($"{first.PollingCount} point(s) still on MMS fallback · rebuilding the report plan once");
        await StopDeviceMonitorAsync(device);
        await Task.Delay(180);
        if (!await StartDeviceMonitorAsync(device, navigateToExplorer: false))
            return first;

        return await ObserveIoFatAcquisitionAsync(
            project,
            requestedPoints,
            device,
            reportProgress,
            TimeSpan.FromSeconds(10));
    }

    private async Task<IoFatAcquisitionSummary> ObserveIoFatAcquisitionAsync(
        IoTestProject project,
        IReadOnlyCollection<IoTestPointPlan> requestedPoints,
        Iec61850MonitorDevice device,
        Action<string> reportProgress,
        TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var last = ReadIoFatAcquisitionSummary(requestedPoints, device);
        var announced = false;

        while (DateTime.UtcNow < deadline && device.IsMonitoring)
        {
            await Task.Delay(120);
            _ioTestLiveBindingService.Bind(project, Devices);
            last = ReadIoFatAcquisitionSummary(requestedPoints, device);

            if (!announced)
            {
                reportProgress("Validating static/dynamic report coverage · MMS is fallback only");
                announced = true;
            }

            var plannerSettled = !ContainsPlannerStage(device.AcquisitionMode);
            if (last.LiveCount == requestedPoints.Count &&
                plannerSettled &&
                last.UnknownCount == 0 &&
                (last.PollingCount == 0 || DateTime.UtcNow >= deadline - TimeSpan.FromSeconds(2)))
            {
                break;
            }
        }

        return last;
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

    private static bool ContainsPlannerStage(string? mode)
        => (mode ?? string.Empty).Contains("arming", StringComparison.OrdinalIgnoreCase) ||
           (mode ?? string.Empty).Contains("live start", StringComparison.OrdinalIgnoreCase) ||
           (mode ?? string.Empty).Contains("preparing", StringComparison.OrdinalIgnoreCase) ||
           (mode ?? string.Empty).Contains("settling", StringComparison.OrdinalIgnoreCase);

    private sealed record IoFatAcquisitionSummary(
        int LiveCount,
        int ReportCount,
        int PollingCount,
        int UnknownCount,
        string Mode);
}
