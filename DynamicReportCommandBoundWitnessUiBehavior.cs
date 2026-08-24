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
            (e.Key != Key.F && e.Key != Key.A))
            return;

        e.Handled = true;
        var device = window.SelectedDevice;
        var a3 = e.Key == Key.A;
        var title = a3 ? "G2.6-P1 Deterministic A3" : "G2.5-A2.1 Command-Bound Witness";
        if (device is null)
        {
            MessageBox.Show(
                window,
                a3
                    ? "Select one IEC 61850 IED first. Deterministic A3 is intentionally bound to one explicit IED, its exact persisted G2.4 envelope, and one explicit existing ARSAS command."
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
                "A command-bound G2 commissioning witness is already armed/running.",
                title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            if (a3)
                await RunDeterministicA3Async(window, device);
            else
                await RunA21Async(window, device);
        }
        catch (Exception ex)
        {
            window.LastStatusText = a3
                ? "G2.6-P1 A3 stopped locally; persisted qualification and production reporting policy remain unchanged."
                : "G2.5-A2.1 V3 stopped locally; production dynamic reporting remains OFF.";
            MessageBox.Show(
                window,
                (a3
                    ? "G2.6-P1 deterministic A3 stopped. Cleanup remains owned by the core G2.5-A transaction; this action cannot mark ProductionEligible.\n\n"
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
        var answer = MessageBox.Show(
            window,
            $"Arm G2.6-P1 deterministic A3 command-bound dchg proof for {device.Name} ({device.EndpointText})?\n\n" +
            "ONE G2.4-PROVEN URCB + DCHG ONLY + ONE EXISTING ARSAS COMMAND\n\n" +
            "A3 first opens a READ-ONLY witness association and refuses to arm the report path unless at least one existing ARSAS control object's A2.1 status chain intersects the exact persisted G2.4 member envelope. This avoids spending a breaker operation on a stimulus the A3 DataSet cannot prove.\n\n" +
            "The core report transaction temporarily configures ONLY the exact G2.4-proven URCB with dchg enabled, GI/integrity/qchg/dupd disabled, and reason-for-inclusion + DataSet-name enabled. It creates one bounded temporary DataSet and performs mandatory monitor/field/fresh-association cleanup.\n\n" +
            "After the status shows 'G2.6-P1 A3 READY — ISSUE ONE ARSAS COMMAND', issue exactly ONE already-proven safe OPEN/CLOSE using the normal ARSAS control UI. A3 observes the existing runtime 'Control execution requested:' diagnostic; it does NOT call, wrap, delay, duplicate or re-issue ExecuteControlAsync/SBOw/Operate.\n\n" +
            "PASS requires the post-command read-only witness to see a transition on a qualified command-focus member AND the dchg InformationReport to include the same exact DataSet index, followed by complete cleanup.\n\n" +
            "A3 never saves/advances the profile and can never mark ProductionEligible. Production automatic dynamic reporting remains OFF after this test. Do not run another G2 hotkey while A3 is armed.\n\n" +
            "Continue?",
            "G2.6-P1 Deterministic A3",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        window.LastStatusText = $"G2.6-P1 A3: preflighting exact G2.4 envelope and command-bound status intersection for {device.Name}…";
        var progress = new Progress<string>(text => window.LastStatusText = text);
        var service = new DynamicReportCommandBoundDataChangeCommissioningService();
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
}
