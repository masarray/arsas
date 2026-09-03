using System.Threading;
using System.Windows;
using System.Windows.Input;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Explicit zero-control P1.7 commissioning entry point.
/// Ctrl+Shift+B never dispatches a breaker/process command. It only runs the existing
/// guarded G2.3 -> G2.4 -> G2.5 Dynamic RCB qualification ladder for the selected IED.
/// </summary>
internal static class DynamicReportPerIedBootstrapUiBehavior
{
    private static int _installed;
    private static int _busy;

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
            e.Key != Key.B)
        {
            return;
        }

        e.Handled = true;
        var device = window.SelectedDevice;
        if (device is null)
        {
            MessageBox.Show(
                window,
                "Select the physical IEC 61850 IED to qualify first. P1.7 is identity-bound and will never copy another IED's Dynamic RCB witness.",
                "G2.7 Per-IED Dynamic RCB Capability Bootstrap",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "A G2.7 per-IED Dynamic RCB bootstrap is already running.",
                "G2.7 Per-IED Dynamic RCB Capability Bootstrap",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var answer = MessageBox.Show(
                window,
                $"Bootstrap native Dynamic RCB field capability for {device.Name} ({device.EndpointText})?\n\n" +
                "P1.7 PER-IED EXPLICIT COMMISSIONING\n\n" +
                "This action never copies the old AA1C1F08R4 witness and never assumes that Write/DefineNVL/free URCB advertisement alone proves safe runtime mutation. It qualifies THIS exact IED identity.\n\n" +
                "For a new IED, the coordinator reuses the existing guarded ladder: G2.3 bounded exact Dynamic DataSet envelope -> G2.4 transactional URCB activation + actual InformationReport -> G2.5 strict dchg-only physical proof + cleanup.\n\n" +
                "This action issues ZERO automatic control commands. During the final G2.5 phase, wait until status says the dchg path is READY, then cause exactly ONE already-approved safe process/status change affecting a member in the proven envelope.\n\n" +
                "The native capability profile + sidecar are persisted only after actual NO-GI data-change mapping, association health, monitor cleanup, proof-field restore, and fresh-association cleanup closure all PASS.\n\n" +
                "Even after PASS, ProductionEligible remains OFF. Disconnect/reconnect (or restart monitoring) is required before normal Start Monitor loads the new P1.7 authorization and attempts bounded general Dynamic RCB groups.\n\n" +
                "Continue?",
                "G2.7 Per-IED Dynamic RCB Capability Bootstrap",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            window.LastStatusText =
                $"G2.7 P1.7 bootstrap starting for {device.Name}: exact identity qualification only; zero automatic process/control commands…";
            var progress = new Progress<string>(text => window.LastStatusText = text);
            var result = await new DynamicReportPerIedFieldCapabilityBootstrapService()
                .RunAsync(
                    device,
                    device.Signals.ToArray(),
                    progress,
                    CancellationToken.None);

            window.LastStatusText = result.Summary;
            MessageBox.Show(
                window,
                result.Summary +
                (result.IsSuccess
                    ? "\n\nNEXT: Disconnect -> Connect -> Start Monitor. Then inspect Diagnostics for P1.7 native authorization, Dynamic groups, dynamic signals, per-group RCB/DataSet AR_HYB_<hash>, and genuine MMS residual only."
                    : $"\n\nStopped at stage: {result.Stage}. No normal-runtime Dynamic RCB authorization was granted by this failed bootstrap."),
                "G2.7 Per-IED Dynamic RCB Capability Bootstrap",
                MessageBoxButton.OK,
                result.IsSuccess ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex)
        {
            window.LastStatusText =
                "G2.7 per-IED Dynamic RCB capability bootstrap stopped fail-closed. Normal monitoring remains static/MMS fallback; ProductionEligible remains OFF.";
            MessageBox.Show(
                window,
                "G2.7 per-IED bootstrap stopped fail-closed. The coordinator never auto-operates a breaker/process object. A new IED is advanced only through the existing G2.3 -> G2.4 -> G2.5 commissioning ladder, and native general Dynamic RCB runtime stays locked unless both the exact DataChange profile and cleanup-bound sidecar are durable. ProductionEligible remains OFF.\n\n" + ex,
                "G2.7 Per-IED Dynamic RCB Capability Bootstrap",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
