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
            return IoTestSessionActionResult.Failure($"{ied.IedName} is already busy with another connection or discovery workflow.");

        if (!device.IpAddress.Equals(ied.IpAddress, StringComparison.OrdinalIgnoreCase))
        {
            if (device.IsConnected || device.IsMonitoring)
            {
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
            progress?.Report($"Connecting {ied.IedName} · {ied.IpAddress}:102");
            var usedSavedModel = false;
            if (!device.IsConnected)
            {
                var canUseSavedModel = device.HasDiscoveryCache && device.Signals.Count > 0;
                var connected = canUseSavedModel
                    ? await ConnectUsingSavedModelAsync(device, selectDevice: false)
                    : await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false);
                usedSavedModel = connected && canUseSavedModel;
                if (!connected)
                {
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"ARSAS could not connect to {ied.IedName} at {ied.IpAddress}:102. Open Diagnostics for the MMS association or discovery error.");
                }
            }
            else if (device.Signals.Count == 0)
            {
                progress?.Report($"{ied.IedName} connected · discovering live model");
                await StopDeviceConnectionAsync(device);
                if (!await ConnectAndConfigureDeviceAsync(device, openWizard: false, selectDevice: false))
                {
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure($"Full live-model discovery failed for {ied.IedName}.");
                }
            }

            progress?.Report($"Matching {requestedPoints.Count} workbook signal(s)");
            var selection = _ioTestSignalSelectionService.Resolve(ied, device);
            if (!selection.Succeeded && selection.CanRetryWithFreshDiscovery)
            {
                progress?.Report("Refreshing live model once · saved model missed workbook points");
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
                progress?.Report("Refreshing acquisition for the workbook scope");
                await StopDeviceMonitorAsync(device);
            }

            if (!device.IsMonitoring)
            {
                progress?.Report("Arming configured RCB · dynamic URCB fallback if coverage is missing");
                if (!await StartDeviceMonitorAsync(device, navigateToExplorer: false))
                {
                    _ioTestLiveBindingService.Bind(project, Devices);
                    return IoTestSessionActionResult.Failure(
                        $"{ied.IedName} connected, but ARSAS could not start live acquisition for the imported FAT scope.");
                }
            }

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
            var message = $"{ied.IedName} · {liveCount}/{requestedPoints.Count} live · {device.AcquisitionMode}";
            SetStatus(message);
            AddLog(
                "INFO",
                "IO Testing",
                $"{message}. Acquisition policy: configured RCB → temporary dynamic DataSet/URCB → bounded MMS verification/fallback. No process control commands are enabled. Project live-bound={binding.LivePointCount}; model={modelText}.");
            progress?.Report(message);
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
            if (createdFromWorkbook)
                RaiseWorkspaceCounts();
        }
    }
}
