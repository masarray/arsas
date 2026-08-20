using System.Threading;
using System.Windows;
using System.Windows.Input;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal static class DynamicReportQualificationUiBehavior
{
    private static int _installed;
    private static int _qualificationBusy;

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) != 0)
            return;

        EventManager.RegisterClassHandler(
            typeof(MainWindow),
            Keyboard.PreviewKeyDownEvent,
            new KeyEventHandler(OnPreviewKeyDown),
            handledEventsToo: true);
    }

    private static async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window ||
            Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) ||
            (e.Key != Key.Q && e.Key != Key.R))
            return;

        e.Handled = true;

        var device = window.SelectedDevice;
        if (device is null)
        {
            MessageBox.Show(
                window,
                "Select one IEC 61850 IED first. G2 commissioning is intentionally scoped to one explicit IED at a time.",
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _qualificationBusy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "Another G2 commissioning operation is already running.",
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (e.Key == Key.Q)
                await RunG23Async(window, device);
            else
                await RunG24Async(window, device);
        }
        catch (Exception ex)
        {
            window.LastStatusText = "G2 commissioning stopped locally; production monitoring policy was not changed.";
            MessageBox.Show(
                window,
                "G2 commissioning stopped. Production automatic dynamic reporting remains OFF.\n\n" + ex,
                "G2 Dynamic Reporting Commissioning",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _qualificationBusy, 0);
        }
    }

    private static async Task RunG23Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var selectedCount = device.Signals.Count(signal => signal.IsSelected);
        var answer = MessageBox.Show(
            window,
            $"Run G2.3 dynamic reporting qualification for {device.Name} ({device.EndpointText})?\n\n" +
            $"Selected signals: {selectedCount}\n\n" +
            "This is an explicit COMMISSIONING operation. ARSAS will open a separate auxiliary MMS association, validate exact Class-A scalar points by direct MMS read, then temporarily Define/GetAttributes/Delete bounded DataSets using the 1 → 4 → 8 → 16 → 32 ladder.\n\n" +
            "It will NOT enable an RCB, will NOT write RptEna/GI, will NOT change the production monitoring planner, and will stop if association continuity or cleanup is not proven.\n\n" +
            "Continue?",
            "G2.3 Dynamic Reporting Qualification",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.3 qualification: opening isolated auxiliary MMS association to {device.Name}…";
        var service = new DynamicReportQualificationCommissioningService();
        var result = await service.RunAsync(
            device,
            device.Signals.ToArray(),
            CancellationToken.None);

        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result)
        {
            Owner = window
        };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunG24Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Run G2.4 one-URCB InformationReport proof for {device.Name} ({device.EndpointText})?\n\n" +
            "ACTIVE COMMISSIONING WRITE WARNING\n\n" +
            "ARSAS will use the identity-bound G2.3 EnvelopeQualified profile, open a separate auxiliary MMS association, force-read live URCB DatSet/RptEna/Resv/Owner state, and choose exactly ONE proven-empty/free URCB.\n\n" +
            "For that one URCB only, ARSAS will capture the exact original TrgOps and OptFlds values, temporarily enable dchg+GI and reason-for-inclusion+data-set-name with exact readback, then create ONE temporary dynamic DataSet with no more than 8 already-qualified members, bind it, reserve the URCB when supported, write RptEna=true, register report routing, request GI, and wait for an ACTUAL strictly mapped InformationReport.\n\n" +
            "RptEna/GI acceptance alone is NOT success. After the proof attempt ARSAS will disable the URCB, restore its prior DatSet binding, delete the temporary DataSet, release reservation, then restore the exact original OptFlds and TrgOps values and verify exact readback. The profile advances only when actual report proof AND all cleanup/restore evidence pass.\n\n" +
            "Production monitoring and automatic production dynamic reporting remain OFF/untouched.\n\n" +
            "Continue?",
            "G2.4 One-URCB InformationReport Proof",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.4: opening isolated auxiliary MMS association to {device.Name} for transactional one-URCB proof…";
        var service = new DynamicReportActivationCommissioningServiceV2();
        var result = await service.RunAsync(
            device,
            device.Signals.ToArray(),
            CancellationToken.None);

        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result)
        {
            Owner = window
        };
        evidenceWindow.ShowDialog();
    }
}
