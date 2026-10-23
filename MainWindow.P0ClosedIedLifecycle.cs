using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Engineering-side half of the P0 Close IED transaction.
///
/// FAT owns evidence persistence. Engineering owns the IEC 61850 runtime/model. Closing an
/// IED therefore stops only that shared Engineering device and removes its presentation/model
/// rows after FAT has durably checkpointed and sealed evidence. ARIEC61850 is untouched.
/// </summary>
public partial class MainWindow
{
    internal async Task CloseIoFatEngineeringIedAsync(
        IoListTestingWindow fatWindow,
        IoTestIedPlan ied)
    {
        ArgumentNullException.ThrowIfNull(fatWindow);
        ArgumentNullException.ThrowIfNull(ied);

        if (!ReferenceEquals(_loadedIoFatWindow, fatWindow) || !fatWindow.IsLoaded)
            throw new InvalidOperationException("The target FAT workspace is no longer active.");

        var device = ResolveIoTestDevice(ied.LiveDeviceId)
                     ?? ResolveIoTestDevice(ied.IpAddress)
                     ?? ResolveIoTestDevice(ied.IedName);

        // Selection/evidence handlers were attached when the IED entered the shared FAT
        // workspace. Detach them even when the Engineering device is already absent.
        foreach (var point in ied.TestPoints)
            point.PropertyChanged -= IoFatSelectionPoint_PropertyChanged;

        if (device == null)
        {
            ClearP0FatPointIndex();
            SetStatus($"{ied.IedName}: FAT IED closed; no Engineering runtime device remained to stop.");
            return;
        }

        SaveSignalSelectionMemory(device);
        await _runtime.StopDeviceAsync(device.DeviceId);

        device.HasReportStream = false;
        device.ReportPulseActive = false;
        _reportPulseUntil.Remove(device.DeviceId);
        RemoveDeviceHighlights(device.DeviceId);
        RemoveDevicePoints(device.DeviceId);
        DetachSignalHandlers(device.Signals);
        Devices.Remove(device);
        _pendingProjectSelections.Remove(device.DeviceId);
        _sharedSclSelectionAuthorityDeviceIds.Remove(device.DeviceId);
        _sharedSclStaticDataSetAuthorityDeviceIds.Remove(device.DeviceId);
        RemoveControlFeedbackIndex(device.DeviceId);

        if (ReferenceEquals(SelectedDevice, device))
            SelectedDevice = Devices.FirstOrDefault();

        ClearP0FatPointIndex();
        RaiseWorkspaceCounts();
        SetStatus($"{ied.IedName}: Engineering runtime stopped and IED removed. FAT evidence checkpoint remains available for re-import.");
    }
}
