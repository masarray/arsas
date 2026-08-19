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
            e.Key != Key.Q ||
            Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift))
            return;

        e.Handled = true;

        var device = window.SelectedDevice;
        if (device is null)
        {
            MessageBox.Show(
                window,
                "Select one IEC 61850 IED first. G2 qualification is intentionally scoped to one explicit IED at a time.",
                "G2 Dynamic Reporting Qualification",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _qualificationBusy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "Another G2 qualification operation is already running.",
                "G2 Dynamic Reporting Qualification",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var selectedCount = device.Signals.Count(signal => signal.IsSelected);
            var answer = MessageBox.Show(
                window,
                $"Run G2 dynamic reporting qualification for {device.Name} ({device.EndpointText})?\n\n" +
                $"Selected signals: {selectedCount}\n\n" +
                "This is an explicit COMMISSIONING operation. ARSAS will open a separate auxiliary MMS association, validate exact Class-A scalar points by direct MMS read, then temporarily Define/GetAttributes/Delete bounded DataSets using the 1 → 4 → 8 → 16 → 32 ladder.\n\n" +
                "It will NOT enable an RCB, will NOT write RptEna/GI, will NOT change the production monitoring planner, and will stop if association continuity or cleanup is not proven.\n\n" +
                "Continue?",
                "G2 Dynamic Reporting Qualification",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            window.LastStatusText = $"G2 qualification: opening isolated auxiliary MMS association to {device.Name}…";
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
        catch (Exception ex)
        {
            window.LastStatusText = "G2 qualification failed locally; production monitoring policy was not changed.";
            MessageBox.Show(
                window,
                "G2 qualification stopped before any production dynamic-report enablement.\n\n" + ex,
                "G2 Dynamic Reporting Qualification",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _qualificationBusy, 0);
        }
    }
}
