using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

internal static class DynamicReportCommandBoundWitnessUiBehavior
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

        // Legacy observer-only fallback for the dedicated ControlCommandWindow path.
        // V3/A3 command authority is the already-existing runtime Diagnostic event; this
        // routed observer is retained only as non-authoritative fallback evidence.
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(OnAnyButtonClick),
            handledEventsToo: true);
    }

    private static void OnAnyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || Window.GetWindow(button) is not ControlCommandWindow commandWindow)
            return;

        var label = button.Content?.ToString()?.Trim() ?? string.Empty;
        if (!label.Equals("Send Command", StringComparison.OrdinalIgnoreCase) &&
            !label.Equals("Send Test", StringComparison.OrdinalIgnoreCase))
            return;

        if (!commandWindow.CanSend)
            return;

        DynamicReportCommandIntentObservation.Publish(new DynamicReportObservedCommandIntent(
            commandWindow.A21WitnessDevice,
            commandWindow.A21WitnessSignal,
            commandWindow.SelectedValue,
            "ControlCommandWindow.RoutedButtonClick",
            DateTimeOffset.UtcNow));
    }

    private static async void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not MainWindow window ||
            Keyboard.Modifiers != (ModifierKeys.Control | ModifierKeys.Shift) ||
            (e.Key != Key.F && e.Key != Key.A && e.Key != Key.S))
            return;

        e.Handled = true;
        var device = window.SelectedDevice;
        var a3 = e.Key == Key.A;
        var shadow = e.Key == Key.S;
        var title = shadow
            ? "G2.6 Physical Shadow Verification"
            : a3
                ? "G2.6-P1 Q0 Target-Locked Auto A3"
                : "G2.5-A2.1 Command-Bound Witness";
        if (device is null)
        {
            MessageBox.Show(
                window,
                shadow
                    ? "Select the exact identity-compatible InformationReportProven IEC 61850 IED first. G2.6 shadow is bound to the persisted proven RCB/member envelope and will not guess another target."
                    : a3
                        ? "Select the qualified AA1C1F08R4 IEC 61850 IED first. Q0 Auto A3 is hard-bound to the exact proven field identity and AA1C1F08R4Q0/CSWI1.Pos."
                        : "Select one IEC 61850 IED first. G2.5-A2.1 is intentionally bound to one explicit IED and one explicit ARSAS command.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "A G2 commissioning witness/shadow action is already armed or running.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (shadow)
                await RunPhysicalShadowAsync(window, device);
            else if (a3)
                await RunDeterministicA3Async(window, device);
            else
                await RunA21Async(window, device);
        }
        catch (Exception ex)
        {
            window.LastStatusText = shadow
                ? "G2.6 physical shadow stopped fail-closed. Cleanup evidence is retained; profile remains InformationReportProven and ProductionEligible remains OFF."
                : a3
                    ? "G2.6-P1 Q0 Auto A3 stopped fail-closed. No retry/CLOSE/toggle is issued; ProductionEligible remains OFF."
                    : "G2.5-A2.1 V3 stopped locally; production dynamic reporting remains OFF.";
            MessageBox.Show(
                window,
                (shadow
                    ? "G2.6 physical shadow stopped. The collector issues zero automatic control commands and cannot promote the profile. Inspect cleanup/reconnect evidence before retry. Production automatic dynamic reporting remains OFF.\n\n"
                    : a3
                        ? "G2.6-P1 Q0 target-locked Auto A3 stopped. Ctrl+Shift+A is the explicit commissioning action, but the one-shot OPEN is dispatched only after exact identity, Q0 status, Closed-state, command-focus, dchg-arm and final-baseline gates close. A blocked/ambiguous command is never retried and no CLOSE/toggle/auto-restore is issued. Recovery remains transactional and A3 cleanup remains mandatory. ProductionEligible stays OFF.\n\n"
                        : "G2.5-A2.1 V3 stopped. The witness did not change production reporting policy.\n\n") + ex,
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    private static async Task RunA21Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Arm G2.5-A2.1 V3 command-bound high-speed stimulus witness for {device.Name} ({device.EndpointText})?\n\n" +
            "READ-ONLY MMS WITNESS + ONE EXISTING ARSAS CONTROL COMMAND\n\n" +
            "V3 captures the exact command from the ALREADY-EXISTING Iec61850MonitorRuntime Diagnostic event 'Control execution requested:' that is emitted before native control execution. It does not add a hook to the SBOw/Operate transaction.\n\n" +
            "After the status shows 'G2.5-A2.1 READY — ISSUE ONE ARSAS COMMAND', issue exactly ONE already-proven safe OPEN or CLOSE using the normal ARSAS control UI you normally use. Do NOT use an external/manual stimulus for this phase.\n\n" +
            "Once the runtime diagnostic identifies the exact control object, the isolated MMS witness narrows read-only sampling to at most six points around the exact ControlStatusReference. Existing ExecuteControlAsync, SBOw, Operate and CommandTermination behavior is NOT modified, delayed, wrapped or re-issued.\n\n" +
            "Once 'G2.5-A2.1 COMMAND CAPTURED' appears, do not issue another command. If a transition is seen, A2.1 samples briefly to classify persistent/latched versus momentary/pulse behavior.\n\n" +
            "The witness does not access/mutate RCB or DataSet state, does not send GI, does not save/advance the qualification profile, and production dynamic reporting remains OFF. Do not run another G2 hotkey while A2.1 is armed.\n\n" +
            "Continue?",
            "G2.5-A2.1 V3 Runtime-Diagnostic Witness",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.5-A2.1 V3: opening isolated read-only MMS witness for {device.Name} and preparing pre-command baseline…";
        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportCommandBoundStimulusWitnessServiceV3();
        var result = await service.RunAsync(
            window.A21WitnessRuntime,
            device,
            device.Signals.ToArray(),
            progress,
            CancellationToken.None);
        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunDeterministicA3Async(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        // Ctrl+Shift+A itself is the explicit commissioning action. There are deliberately
        // no modal arm/recovery/command dialogs in the successful path: the coordinator is
        // hard-bound to the already-proven field identity and Q0 CSWI1.Pos, performs all
        // read-only/transactional gates first, and dispatches one OPEN only after the A3
        // final baseline is ready. Any failed gate sends zero commands.
        window.LastStatusText =
            $"G2.6-P1 Q0 AUTO starting for {device.Name}: exact target {DynamicReportQ0TargetLockedAutoA3CommissioningService.TargetControlReference}; one-shot OPEN only from Closed; no retry/CLOSE/toggle/auto-restore…";

        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportQ0TargetLockedAutoA3CommissioningService();
        var result = await service.RunAsync(
            window.A21WitnessRuntime,
            device,
            device.Signals.ToArray(),
            progress,
            CancellationToken.None);

        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }

    private static async Task RunPhysicalShadowAsync(MainWindow window, Models.Iec61850MonitorDevice device)
    {
        var answer = MessageBox.Show(
            window,
            $"Start G2.6 physical shadow verification for {device.Name} ({device.EndpointText})?\n\n" +
            "TWO PHYSICAL PHASES + ONE DELIBERATE RECONNECT\n\n" +
            "The collector will use the exact persisted InformationReportProven URCB/member envelope. Each phase opens an independent READ-ONLY MMS polling association plus one transactional dchg-only report association.\n\n" +
            "When the status shows 'G2.6 SHADOW PHASE 1 READY — CAUSE ONE SAFE CHANGE', cause exactly ONE already-approved safe process/status change affecting the proven envelope. After cleanup, the collector deliberately reconnects both paths and will show a second READY marker for one more safe change.\n\n" +
            "Ctrl+Shift+S issues ZERO automatic control commands. It sends no GI, never edits the persisted qualification profile, never marks ProductionEligible, and retains mandatory monitor/proof-field/fresh-association cleanup.\n\n" +
            "Quality/timestamp evidence is never invented. If the scalar report envelope does not physically carry paired q/t, the strict PR #99 shadow gate will remain BLOCKED/FAIL and that field finding is intentional.\n\n" +
            "Independent Smart Control/static-report regressions are NOT assumed by this action, so even a shadow PASS is not an automatic production promotion.\n\n" +
            "Continue?",
            "G2.6 Physical Shadow Verification",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.6 physical shadow starting for {device.Name}: validating exact profile, opening read-only polling reference and transactional dchg report path…";
        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportShadowVerificationCommissioningService();
        var result = await service.RunAsync(
            device,
            device.Signals.ToArray(),
            progress,
            controlRegressionPassed: false,
            staticReportingRegressionPassed: false,
            CancellationToken.None);

        window.LastStatusText = result.Summary;
        var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
        evidenceWindow.ShowDialog();
    }
}
