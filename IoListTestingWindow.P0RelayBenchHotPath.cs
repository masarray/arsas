using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// Relay-bench hot-path guard. This class handler runs before ordinary Button.Click handlers
/// and owns only the FAT operations that must never enter the legacy synchronous lifecycle.
/// It keeps an already-running Engineering acquisition session untouched and treats FAT as
/// a lightweight evidence consumer.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0RelayBenchButtonGuardRegistered = RegisterP0RelayBenchButtonGuard();

    private bool _p0HotPathStartRunning;
    private bool _p0HotPathResumeRunning;
    private bool _p0HotPathStopRunning;
    private bool _p0HotPathCommandRunning;

    private static bool RegisterP0RelayBenchButtonGuard()
    {
        EventManager.RegisterClassHandler(
            typeof(Button),
            Button.ClickEvent,
            new RoutedEventHandler(P0RelayBenchButton_Click),
            handledEventsToo: true);
        return true;
    }

    private static void P0RelayBenchButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || FindOwningFatWindow(button) is not IoListTestingWindow window)
            return;

        var text = ButtonText(button);

        if (IsFastStartLabel(text) &&
            window.SelectedIed?.IsLiveMonitoring == true &&
            window.Session.CanStart)
        {
            e.Handled = true;
            _ = window.StartFatFromSharedLiveSessionAsync(button);
            return;
        }

        if (text.Equals("Resume", StringComparison.OrdinalIgnoreCase) && window.Session.CanResume)
        {
            e.Handled = true;
            _ = window.ResumeFatWithoutBlockingDispatcherAsync(button);
            return;
        }

        if (text.Equals("Stop", StringComparison.OrdinalIgnoreCase) && window.Session.CanStop)
        {
            e.Handled = true;
            _ = window.StopFatWithoutBlockingDispatcherAsync(button);
            return;
        }

        // FAT position Confirm buttons inherit SignalDefinition as DataContext from the row.
        // Intercept only those buttons; Engineering command controls are untouched.
        if (text.Equals("Confirm", StringComparison.OrdinalIgnoreCase) &&
            button.DataContext is SignalDefinition signal &&
            signal.IsPositionControl &&
            signal.ControlConfirmationPending)
        {
            e.Handled = true;
            _ = window.ConfirmFatPositionWithoutUiBlockAsync(button, signal);
        }
    }

    private async Task StartFatFromSharedLiveSessionAsync(Button button)
    {
        if (_p0HotPathStartRunning)
            return;

        var ied = SelectedIed;
        if (ied == null)
            return;

        _p0HotPathStartRunning = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;
        await Dispatcher.Yield(DispatcherPriority.Render);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var preflight = IoTestSessionPreflight.Validate(ied);
            if (!preflight.Succeeded)
            {
                ShowActionResult(preflight, "FAT session scope is not ready");
                return;
            }

            // Critical fast path: Engineering is already connected + monitoring. Do NOT run
            // PrepareIoTestIedForFatAsync again, do NOT reconcile/reselect/restart reports,
            // and do NOT touch the acquisition cadence. FAT only arms evidence on the live
            // rows that are already proven by the shared process image.
            var requested = ied.TestPoints
                .Where(point =>
                    point.WorkspaceSelected &&
                    point.IsIncludedInFat &&
                    point.TestEnabled &&
                    point.ImportReady)
                .ToList();
            var live = requested
                .Where(point => point.LiveBindingState == IoTestLiveBindingState.LivePointReady)
                .ToList();

            if (live.Count == 0)
            {
                var failure = IoTestSessionActionResult.Failure(
                    $"{ied.IedName} is monitoring, but none of the {requested.Count} selected FAT row(s) has a proven live point.");
                ShowActionResult(failure, "FAT evidence session could not start");
                return;
            }

            var result = Session.Start(ied, live);
            ShowActionResult(result, "FAT evidence session could not start");
            if (result.Succeeded)
            {
                var waiting = requested.Count - live.Count;
                PreparationStatusText = waiting == 0
                    ? $"{ied.IedName} FAT active · attached directly to shared Engineering live data · {live.Count} row(s) armed"
                    : $"{ied.IedName} FAT active · {live.Count}/{requested.Count} proven live row(s) armed · {waiting} waiting for binding";
                Storage?.ScheduleSave();
            }
            else
            {
                PreparationStatusText = result.Message;
            }

            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            Trace.WriteLine(
                $"[IO FAT P0] shared-live Start/Continue completed in {stopwatch.ElapsedMilliseconds} ms; " +
                $"ied={ied.IedName}; requested={requested.Count}; live={live.Count}; succeeded={result.Succeeded}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            Trace.WriteLine($"[IO FAT P0] shared-live Start/Continue failed after {stopwatch.ElapsedMilliseconds} ms: {ex}");
            MessageBox.Show(this, ex.Message, "FAT session could not start", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0HotPathStartRunning = false;
            RaiseSelectedIedContextProperties();
        }
    }

    private async Task ResumeFatWithoutBlockingDispatcherAsync(Button button)
    {
        if (_p0HotPathResumeRunning)
            return;

        _p0HotPathResumeRunning = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;
        await Dispatcher.Yield(DispatcherPriority.Render);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            var result = Session.Resume();
            ShowActionResult(result, "FAT session could not resume");
            if (result.Succeeded)
                Storage?.ScheduleSave();
            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            Trace.WriteLine($"[IO FAT P0] Resume completed in {stopwatch.ElapsedMilliseconds} ms; succeeded={result.Succeeded}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            MessageBox.Show(this, ex.Message, "FAT session could not resume", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0HotPathResumeRunning = false;
        }
    }

    private async Task StopFatWithoutBlockingDispatcherAsync(Button button)
    {
        if (_p0HotPathStopRunning)
            return;

        _p0HotPathStopRunning = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;
        await Dispatcher.Yield(DispatcherPriority.Render);

        var stopwatch = Stopwatch.StartNew();
        try
        {
            IoTestSessionActionResult result;
            using (IoTestEvidenceJournal.BeginDeferredSealScope())
                result = Session.Stop();

            ShowActionResult(result, "FAT session could not stop");
            if (result.Succeeded)
            {
                // Queue drain, durable disk barrier and full hash-chain verification never
                // run on WPF Dispatcher. Engineering/FAT remain repaintable while sealing.
                await IoTestEvidenceJournal.AwaitDeferredSealsAsync();
                if (Storage != null)
                    await Task.Run(Storage.SaveNow);
            }

            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            Trace.WriteLine($"[IO FAT P0] Stop completed in {stopwatch.ElapsedMilliseconds} ms; succeeded={result.Succeeded}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Trace.WriteLine($"[IO FAT P0] Stop failed after {stopwatch.ElapsedMilliseconds} ms: {ex}");
            MessageBox.Show(this, ex.Message, "FAT evidence could not be sealed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0HotPathStopRunning = false;
            RaiseSelectedIedContextProperties();
        }
    }

    private async Task ConfirmFatPositionWithoutUiBlockAsync(Button button, SignalDefinition signal)
    {
        if (_p0HotPathCommandRunning || Owner is not MainWindow engineeringWindow)
            return;

        if (!signal.TryClaimControlConfirmation(out var claim, out var rejectionReason) || claim == null)
        {
            signal.ControlLastResult = $"Command rejected: {rejectionReason}.";
            return;
        }

        _p0HotPathCommandRunning = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;
        RefreshFatCommandActions(signal);
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            await engineeringWindow.ExecuteIoFatControlClaimAsync(signal, claim);

            // Command-service feedback is not allowed to overwrite the shared Engineering
            // process image. Re-project the newest actual status point after command release,
            // then once more after the CSWI stability guard has had time to publish.
            engineeringWindow.ReconcileIoFatCommandValueFromSharedProcessImage(signal);
            await Task.Delay(450);
            engineeringWindow.ReconcileIoFatCommandValueFromSharedProcessImage(signal);
        }
        finally
        {
            RefreshFatCommandActions(signal);
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0HotPathCommandRunning = false;
        }
    }

    private static bool IsFastStartLabel(string text)
        => text.Equals("Start FAT", StringComparison.OrdinalIgnoreCase) ||
           text.Equals("Continue FAT", StringComparison.OrdinalIgnoreCase) ||
           text.Equals("Retest FAT", StringComparison.OrdinalIgnoreCase);

    private static string ButtonText(Button button)
        => button.Content switch
        {
            string text => text.Trim(),
            TextBlock textBlock => textBlock.Text?.Trim() ?? string.Empty,
            _ => button.Content?.ToString()?.Trim() ?? string.Empty
        };

    private static IoListTestingWindow? FindOwningFatWindow(DependencyObject start)
    {
        DependencyObject? current = start;
        while (current != null)
        {
            if (current is IoListTestingWindow window)
                return window;

            current = VisualTreeHelper.GetParent(current) ?? LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
