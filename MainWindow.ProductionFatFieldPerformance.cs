using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Threading;

namespace ArIED61850Tester;

/// <summary>
/// Field guard for the embedded Static DataSet FAT path.
/// The legacy standalone FAT workspace temporarily tightened MainWindow polling to 250 ms.
/// That is unnecessary when FAT is consuming the already-running Engineering Static RCB/URCB
/// report-only session, and keeping it active after embedding adds avoidable UI/runtime load
/// while the operator navigates to other Engineering tabs.
/// </summary>
public partial class MainWindow
{
    [ModuleInitializer]
    internal static void RegisterProductionFatFieldPerformanceGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(ProductionFatFieldPerformance_IoWindowLoaded),
            handledEventsToo: true);
    }

    private static void ProductionFatFieldPerformance_IoWindowLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window ||
            !ReferenceEquals(e.OriginalSource, window) ||
            window.Owner is not MainWindow owner ||
            !owner.ProductionFatTabReady)
        {
            return;
        }

        // EmbeddedEngineeringHost mounts at Loaded priority. Run after that short operation,
        // then release only the legacy cyclic-polling acceleration. FAT evidence continues to
        // arrive from the exact shared Engineering report session and Runtime.PointUpdated.
        owner.Dispatcher.BeginInvoke(
            DispatcherPriority.ContextIdle,
            new Action(() => owner.ReleaseLegacyPollingOverrideForStaticEmbeddedFat(window)));
    }

    private void ReleaseLegacyPollingOverrideForStaticEmbeddedFat(IoListTestingWindow window)
    {
        if (!window.IsLoaded ||
            !ReferenceEquals(_loadedIoFatWindow, window) ||
            !_pollingIntervalBeforeIoFat.HasValue)
        {
            return;
        }

        var liveDevices = window.Project.Ieds
            .Select(ied => ResolveIoTestDevice(ied.LiveDeviceId)
                           ?? ResolveIoTestDevice(ied.IpAddress)
                           ?? ResolveIoTestDevice(ied.IedName))
            .Where(device => device is not null)
            .Distinct()
            .ToArray();

        // Workbook/manual FAT can still legitimately use the legacy faster polling cadence.
        // Automatic Engineering FAT must not: every participating live IED must already own
        // Static DataSet report-only authority before this override is released.
        if (liveDevices.Length == 0 || liveDevices.Any(device => !IsSharedStaticDataSetAuthority(device!)))
            return;

        PollingIntervalMs = _pollingIntervalBeforeIoFat.Value;
        _pollingIntervalBeforeIoFat = null;

        AddLog(
            "INFO",
            "FAT",
            $"Embedded Static DataSet FAT preserved Engineering polling cadence ({PollingIntervalMs} ms); no cyclic MMS acceleration remains active.");
    }
}
