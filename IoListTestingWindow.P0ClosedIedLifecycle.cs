using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ArIED61850Tester;

/// <summary>
/// P0 Close IED / re-import continuity.
///
/// Close is a deliberate transaction: persist the current project, copy the selected IED's
/// proven snapshot into an independent checkpoint, seal only that IED's evidence session,
/// stop/remove the shared Engineering runtime device, remove the IED from the active FAT
/// scope, then persist the reduced project. Re-imported IED objects may restore historical
/// evidence only through IoFatClosedIedCheckpointService identity/configuration matching.
/// Existing IEDs present when the FAT window opens are never auto-restored from a closed-IED
/// checkpoint, preventing an old checkpoint from overriding normal project snapshot restore.
/// </summary>
public partial class IoListTestingWindow
{
    private static readonly bool P0ClosedIedLifecycleRegistered = RegisterP0ClosedIedLifecycle();

    private readonly HashSet<IoTestIedPlan> _p0ClosedIedRestoreAttempted = new();
    private bool _p0ClosedIedLifecycleInstalled;
    private bool _p0CloseIedInProgress;
    private Button? _p0CloseIedButton;

    private static bool RegisterP0ClosedIedLifecycle()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(P0ClosedIedLifecycle_Loaded),
            handledEventsToo: true);
        return true;
    }

    private static void P0ClosedIedLifecycle_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is not IoListTestingWindow window || window._p0ClosedIedLifecycleInstalled)
            return;

        window._p0ClosedIedLifecycleInstalled = true;
        foreach (var existing in window.Project.Ieds)
            window._p0ClosedIedRestoreAttempted.Add(existing);

        window.PropertyChanged += window.P0ClosedIedLifecycle_PropertyChanged;
        window.Closed += window.P0ClosedIedLifecycle_Closed;
        window.Dispatcher.BeginInvoke(
            new Action(window.InstallP0CloseIedButton),
            DispatcherPriority.Loaded);
    }

    private void InstallP0CloseIedButton()
    {
        if (_p0CloseIedButton != null)
            return;

        var stopButton = FindP0ClosedIedVisualDescendants<Button>(this)
            .FirstOrDefault(button => string.Equals(button.Content?.ToString(), "Stop", StringComparison.OrdinalIgnoreCase));
        if (stopButton?.Parent is not Panel actionPanel)
            return;

        var button = new Button
        {
            Style = TryFindResource("SoftButton") as Style,
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(6, 0, 0, 0),
            MinWidth = 30,
            MinHeight = 29,
            ToolTip = "Close selected IED · preserve FAT evidence for safe re-import"
        };

        if (TryFindResource("LucideX") is Geometry geometry)
        {
            button.Content = new Viewbox
            {
                Width = 13,
                Height = 13,
                Child = new System.Windows.Shapes.Path
                {
                    Data = geometry,
                    Stroke = new SolidColorBrush(Color.FromRgb(178, 62, 72)),
                    StrokeThickness = 2,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round
                }
            };
        }
        else
        {
            button.Content = new TextBlock
            {
                Text = "×",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(178, 62, 72))
            };
        }

        button.Click += P0CloseSelectedIed_Click;
        actionPanel.Children.Add(button);
        _p0CloseIedButton = button;
    }

    private async void P0CloseSelectedIed_Click(object sender, RoutedEventArgs e)
    {
        if (_p0CloseIedInProgress || sender is not Button button || SelectedIed is not { } ied)
            return;

        if (ied.IsPreparing)
        {
            MessageBox.Show(
                this,
                $"{ied.IedName} is still preparing. Finish or stop its connection workflow before closing the IED.",
                "IED preparation in progress",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        if (Storage == null)
        {
            MessageBox.Show(
                this,
                "This FAT workspace has no durable project storage. Close IED is blocked because evidence continuity cannot be checkpointed safely.",
                "Close IED unavailable",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        var activeText = Session.IsIedSessionActive(ied)
            ? " Its active evidence session will be stopped and hash-chain sealed first."
            : string.Empty;
        var answer = MessageBox.Show(
            this,
            $"Close {ied.IedName} ({ied.IpAddress})?{activeText}\n\nFAT evidence, result state, attempts, COMTRADE metadata and journal files will be preserved for a matching re-import. Live association will require a fresh connection/baseline.",
            "Close IED",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);
        if (answer != MessageBoxResult.Yes)
            return;

        _p0CloseIedInProgress = true;
        var originalOpacity = button.Opacity;
        button.IsHitTestVisible = false;
        button.Opacity = 0.62;
        await Dispatcher.Yield(DispatcherPriority.Render);

        try
        {
            // Snapshot first: the checkpoint service copies the proven persisted payload,
            // never a half-mutated in-memory IED.
            await Task.Run(Storage.SaveNow);
            var checkpointPath = await Task.Run(() =>
                IoFatClosedIedCheckpointService.SaveFromCurrentSnapshot(Storage, ied));

            Session.SelectContext(ied);
            if (Session.IsIedSessionActive(ied))
            {
                IoTestSessionActionResult stop;
                using (IoTestEvidenceJournal.BeginDeferredSealScope())
                {
                    stop = Session.Stop("IED closed by operator; evidence preserved for matching re-import.");
                }

                if (!stop.Succeeded)
                    throw new InvalidOperationException(stop.Message);
                await IoTestEvidenceJournal.AwaitDeferredSealsAsync();
            }

            if (Owner is MainWindow engineeringWindow)
                await engineeringWindow.CloseIoFatEngineeringIedAsync(this, ied);

            var oldIndex = Project.Ieds.IndexOf(ied);
            Project.Ieds.Remove(ied);
            _p0ClosedIedRestoreAttempted.Remove(ied);
            _preparationDisplayStates.Remove(ied);

            SelectedIed = Project.Ieds.Count == 0
                ? null
                : Project.Ieds[Math.Clamp(oldIndex, 0, Project.Ieds.Count - 1)];
            FatIedList.Items.Refresh();
            RaiseStatusProperties();

            await Task.Run(Storage.SaveNow);
            PreparationStatusText = $"{ied.IedName} closed · evidence checkpoint {Path.GetFileName(checkpointPath)} retained for matching re-import";
            RaiseStatusProperties();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            MessageBox.Show(
                this,
                ex.Message,
                "Close IED failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            button.Opacity = originalOpacity;
            button.IsHitTestVisible = true;
            _p0CloseIedInProgress = false;
        }
    }

    private void P0ClosedIedLifecycle_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SelectedIed) || SelectedIed == null)
            return;

        RestoreP0ClosedIedCheckpointIfApplicable(SelectedIed);
    }

    private void RestoreP0ClosedIedCheckpointIfApplicable(IoTestIedPlan ied)
    {
        // Initial project IEDs were marked attempted during Loaded. Only an object introduced
        // later by Add IED / SCL re-import reaches the checkpoint reader.
        if (!_p0ClosedIedRestoreAttempted.Add(ied))
            return;

        try
        {
            var restore = IoFatClosedIedCheckpointService.TryRestore(Storage, ied);
            if (!restore.Found)
                return;

            foreach (var point in ied.TestPoints)
                point.Runtime.CurrentIedTimestamp = "—";

            Storage?.ScheduleSave();
            PreparationStatusText = restore.Message;
            RaiseStatusProperties();

            MessageBox.Show(
                this,
                restore.Message + "\n\nContinue FAT will use fresh live acquisition; restored historical evidence is not treated as a live association.",
                "Previous FAT evidence restored",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException or InvalidOperationException)
        {
            MessageBox.Show(
                this,
                $"A previous closed-IED checkpoint exists but could not be restored safely. The re-import remains fresh and no historical evidence was applied.\n\n{ex.Message}",
                "Historical evidence not restored",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void P0ClosedIedLifecycle_Closed(object? sender, EventArgs e)
    {
        PropertyChanged -= P0ClosedIedLifecycle_PropertyChanged;
        Closed -= P0ClosedIedLifecycle_Closed;
        if (_p0CloseIedButton != null)
        {
            _p0CloseIedButton.Click -= P0CloseSelectedIed_Click;
            _p0CloseIedButton = null;
        }
        _p0ClosedIedRestoreAttempted.Clear();
    }

    private static IEnumerable<T> FindP0ClosedIedVisualDescendants<T>(DependencyObject root)
        where T : DependencyObject
    {
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(root, index);
            if (child is T typed)
                yield return typed;
            foreach (var nested in FindP0ClosedIedVisualDescendants<T>(child))
                yield return nested;
        }
    }
}
