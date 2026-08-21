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

        // Observer-only bridge for the dedicated ControlCommandWindow path. WPF class
        // handlers execute before the window's existing SendCommand_Click instance
        // handler. We only publish immutable intent context; the control handler and
        // its ExecuteControlAsync/SBOw/Operate sequence remain untouched.
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
            e.Key != Key.F)
            return;

        e.Handled = true;
        var device = window.SelectedDevice;
        if (device is null)
        {
            MessageBox.Show(
                window,
                "Select one IEC 61850 IED first. G2.5-A2.1 is intentionally bound to one explicit IED and one explicit ARSAS command.",
                "G2.5-A2.1 Command-Bound Witness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Interlocked.Exchange(ref _busy, 1) != 0)
        {
            MessageBox.Show(
                window,
                "G2.5-A2.1 is already armed/running.",
                "G2.5-A2.1 Command-Bound Witness",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        try
        {
            var answer = MessageBox.Show(
                window,
                $"Arm G2.5-A2.1 command-bound high-speed stimulus witness for {device.Name} ({device.EndpointText})?\n\n" +
                "READ-ONLY WITNESS + ONE EXISTING ARSAS CONTROL COMMAND\n\n" +
                "A2.1 V2 opens one isolated read-only MMS association and captures a PRE-COMMAND baseline. It can observe BOTH the fast Command Panel and the dedicated Control Command dialog without changing either control transaction.\n\n" +
                "After the status shows 'G2.5-A2.1 READY — ISSUE ONE ARSAS COMMAND', issue exactly ONE already-proven safe OPEN or CLOSE using the normal ARSAS Command Panel or dedicated Control Command dialog you normally use. Do NOT use an external/manual stimulus for this phase.\n\n" +
                "The observer then narrows read-only sampling to at most six points around the exact ControlStatusReference. Existing ExecuteControlAsync, SBOw, Operate and CommandTermination behavior is NOT modified, delayed, wrapped or re-issued.\n\n" +
                "Once 'G2.5-A2.1 COMMAND CAPTURED' appears, do not issue another command. If a transition is seen, A2.1 samples briefly to classify persistent/latched versus momentary/pulse behavior.\n\n" +
                "The witness does not access/mutate RCB or DataSet state, does not send GI, does not save/advance the qualification profile, and production dynamic reporting remains OFF. Do not run another G2 hotkey while A2.1 is armed.\n\n" +
                "Continue?",
                "G2.5-A2.1 Command-Bound High-Speed Witness",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning,
                MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes)
                return;

            window.LastStatusText = $"G2.5-A2.1 V2: opening isolated read-only MMS witness for {device.Name} and preparing pre-command baseline…";
            var progress = new Progress<string>(text => window.LastStatusText = text);
            var service = new DynamicReportCommandBoundStimulusWitnessServiceV2();
            var result = await service.RunAsync(device, device.Signals.ToArray(), progress, CancellationToken.None);
            window.LastStatusText = result.Summary;
            var evidenceWindow = new DynamicReportQualificationResultWindow(result) { Owner = window };
            evidenceWindow.ShowDialog();
        }
        catch (Exception ex)
        {
            window.LastStatusText = "G2.5-A2.1 V2 stopped locally; production dynamic reporting remains OFF.";
            MessageBox.Show(
                window,
                "G2.5-A2.1 V2 stopped. The witness did not change production reporting policy.\n\n" + ex,
                "G2.5-A2.1 Command-Bound Witness",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }
}
