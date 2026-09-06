using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P0 operator-action responsiveness for the FAT bench.
///
/// Start/Continue paints a local busy state before the existing safe preparation workflow
/// begins. Resume can emit one evidence record per active point; production journal writes
/// remain ordered and hash-chained, but their visible StreamWriter flush is coalesced into
/// one flush for the complete rebaseline transaction. Stop detaches the evidence journal on
/// the Dispatcher and performs the expensive durable disk barrier + full read-back on a
/// worker. Only the pressed button is muted while work is in flight; the DataGrid, search,
/// IED explorer and window chrome remain interactive.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0RuntimeActionsRegistered = RegisterP0RuntimeActions();

    private bool _p0RuntimeActionsInstalled;
    private bool _p0StartInProgress;
    private bool _p0ResumeInProgress;
    private bool _p0StopInProgress;
    private Button? _p0StartButton;
    private Button? _p0ResumeButton;
    private Button? _p0StopButton;

    private static bool RegisterP0RuntimeActions()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0RuntimeActions_Loaded));
        return true;
    }

    private static void P0RuntimeActions_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0RuntimeActionsInstalled)
            return;

        window._p0RuntimeActionsInstalled = true;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallP0RuntimeActionHandlers),
            DispatcherPriority.Loaded);
    }

    private void InstallP0RuntimeActionHandlers()
    {
        _p0StartButton = FindButtonByContentBindingPath(this, nameof(SelectedStartWorkflowText));
        if (_p0StartButton != null)
        {
            // Preserve the existing safe Start/Continue workflow, but insert one rendered
            // frame before it begins. This avoids the Windows "not responding" impression
            // even if the first preparation phase has synchronous setup before its await.
            _p0StartButton.Click -= StartSelectedIedSafely_Click;
            _p0StartButton.Click += P0StartSelectedIedSafely_Click;
        }

        _p0ResumeButton = FindButtonByContent(this, "Resume");
        if (_p0ResumeButton != null)
        {
            // XAML attached the legacy synchronous handler during InitializeComponent.
            // Replace only this edge and leave Session/controller ownership unchanged.
            _p0ResumeButton.Click -= ResumeSession_Click;
            _p0ResumeButton.Click += P0ResumeSession_Click;
        }

        _p0StopButton = FindButtonByContent(this, "Stop");
        if (_p0StopButton != null)
        {
            _p0StopButton.Click -= StopSession_Click;
            _p0StopButton.Click += P0StopSession_Click;
        }

        Closed += P0RuntimeActions_Closed;
    }

    private void P0RuntimeActions_Closed(object? sender, EventArgs e)
    {
        Closed -= P0RuntimeActions_Closed;
        if (_p0StartButton != null)
        {
            _p0StartButton.Click -= P0StartSelectedIedSafely_Click;
            _p0StartButton = null;
        }
        if (_p0ResumeButton != null)
        {
            _p0ResumeButton.Click -= P0ResumeSession_Click;
            _p0ResumeButton = null;
        }
        if (_p0StopButton != null)
        {
            _p0StopButton.Click -= P0StopSession_Click;
            _p0StopButton = null;
        }
    }

    private async void P0StartSelectedIedSafely_Click(object sender, RoutedEventArgs e)
    {
        if (_p0StartInProgress || sender is not Button button)
            return;

        var targetIed = SelectedIed;
        _p0StartInProgress = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;

        // Content stays bound to SelectedStartWorkflowText. SetPreparingIed in the existing
        // workflow therefore changes the same button naturally to "Connecting …" without
        // replacing/breaking its binding.
        await Dispatcher.Yield(DispatcherPriority.Render);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            StartSelectedIedSafely_Click(sender, e);
            await WaitForP0StartWorkflowCompletionAsync(targetIed);
            Trace.WriteLine(
                $"[IO FAT P0] Start/Continue workflow completed in {stopwatch.ElapsedMilliseconds} ms; " +
                $"ied={targetIed?.IedName ?? "<none>"}; state={Session.State}; active={Session.IsSessionActive}.");
        }
        finally
        {
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0StartInProgress = false;
            RaiseSelectedIedContextProperties();
        }
    }

    private async Task WaitForP0StartWorkflowCompletionAsync(IoTestIedPlan? targetIed)
    {
        // The legacy handler is async void. Yield once so it can enter SetPreparingIed and
        // reach its first asynchronous acquisition await. If preflight returned early there
        // is nothing to wait for.
        await Dispatcher.Yield(DispatcherPriority.Background);
        if (targetIed == null || !targetIed.IsPreparing)
            return;

        var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        PropertyChangedEventHandler? handler = null;
        handler = (_, args) =>
        {
            if (args.PropertyName == nameof(IoTestIedPlan.IsPreparing) && !targetIed.IsPreparing)
                completion.TrySetResult(true);
        };

        targetIed.PropertyChanged += handler;
        try
        {
            if (!targetIed.IsPreparing)
                return;
            await completion.Task;
        }
        finally
        {
            targetIed.PropertyChanged -= handler;
        }
    }

    private async void P0ResumeSession_Click(object sender, RoutedEventArgs e)
    {
        if (_p0ResumeInProgress || sender is not Button button)
            return;

        _p0ResumeInProgress = true;
        var originalContent = button.Content;
        var originalOpacity = button.Opacity;
        button.Content = "Continuing…";
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;

        // Paint the busy state before any controller/project PropertyChanged burst starts.
        await Dispatcher.Yield(DispatcherPriority.Render);
        var stopwatch = Stopwatch.StartNew();
        try
        {
            IoTestSessionActionResult result;
            using (IoTestEvidenceJournal.BeginCoalescedVisibleFlushScope())
                result = Session.Resume();

            ShowActionResult(result, "FAT session could not resume");
            if (result.Succeeded)
                Storage?.ScheduleSave();

            RaiseStatusProperties();
            RaiseSelectedIedContextProperties();
            Trace.WriteLine($"[IO FAT P0] Resume completed in {stopwatch.ElapsedMilliseconds} ms; succeeded={result.Succeeded}.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Trace.WriteLine($"[IO FAT P0] Resume failed after {stopwatch.ElapsedMilliseconds} ms: {ex}");
            MessageBox.Show(
                this,
                ex.Message,
                "FAT session could not resume",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0ResumeInProgress = false;
            RaiseSelectedIedContextProperties();
        }
    }

    private async void P0StopSession_Click(object sender, RoutedEventArgs e)
    {
        if (_p0StopInProgress || sender is not Button button)
            return;

        _p0StopInProgress = true;
        var originalContent = button.Content;
        var originalOpacity = button.Opacity;
        button.Content = "Stopping…";
        button.IsHitTestVisible = false;
        button.Opacity = 0.68;

        // Paint immediately. Do not disable the Window: scrolling and inspection remain
        // available while the detached evidence file is being durably sealed/read back.
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
                // Stop() has already made the controller/session state immutable. Await the
                // physical disk barrier and complete hash-chain read-back off Dispatcher.
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
            MessageBox.Show(
                this,
                ex.Message,
                "FAT evidence could not be sealed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Content = originalContent;
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0StopInProgress = false;
            RaiseSelectedIedContextProperties();
        }
    }

    private static Button? FindButtonByContentBindingPath(DependencyObject root, string path)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button)
            {
                var binding = BindingOperations.GetBinding(button, ContentControl.ContentProperty);
                if (string.Equals(binding?.Path?.Path, path, StringComparison.Ordinal))
                    return button;
            }

            var nested = FindButtonByContentBindingPath(child, path);
            if (nested != null)
                return nested;
        }

        return null;
    }

    private static Button? FindButtonByContent(DependencyObject root, string content)
    {
        var childCount = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < childCount; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is Button button &&
                string.Equals(button.Content?.ToString(), content, StringComparison.Ordinal))
            {
                return button;
            }

            var nested = FindButtonByContent(child, content);
            if (nested != null)
                return nested;
        }

        return null;
    }
}
