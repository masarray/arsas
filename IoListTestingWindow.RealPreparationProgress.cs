// Copyright 2026 Ari Sulistiono
// SPDX-License-Identifier: Apache-2.0

using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ArIED61850Tester.Models.IoTesting;

namespace ArIED61850Tester;

public partial class IoListTestingWindow
{
    private static readonly bool RealPreparationProgressRegistered = RegisterRealPreparationProgress();
    private readonly Dictionary<IoTestIedPlan, PreparationDisplayState> _preparationDisplayStates = new();
    private DispatcherTimer? _preparationProgressTimer;

    private static bool RegisterRealPreparationProgress()
    {
        EventManager.RegisterClassHandler(
            typeof(IoListTestingWindow),
            FrameworkElement.LoadedEvent,
            new RoutedEventHandler(RealPreparationProgress_Loaded));
        return true;
    }

    private static void RealPreparationProgress_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is IoListTestingWindow window)
            window.InstallRealPreparationProgress();
    }

    private void InstallRealPreparationProgress()
    {
        if (_preparationProgressTimer != null)
            return;

        foreach (var ied in Project.Ieds)
            _preparationDisplayStates[ied] = new PreparationDisplayState();

        _preparationProgressTimer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(50)
        };
        _preparationProgressTimer.Tick += PreparationProgressTimer_Tick;
        _preparationProgressTimer.Start();
        Closed += RealPreparationProgress_Closed;

        Dispatcher.BeginInvoke(
            new Action(ApplyDeterminateCardProgressBars),
            DispatcherPriority.Loaded);
    }

    private void PreparationProgressTimer_Tick(object? sender, EventArgs e)
    {
        foreach (var ied in Project.Ieds)
        {
            if (!_preparationDisplayStates.TryGetValue(ied, out var state))
            {
                state = new PreparationDisplayState();
                _preparationDisplayStates[ied] = state;
            }

            var active = ied.IsPreparing;
            if (active && !state.WasActive)
                state.Reset();

            state.WasActive = active;
            if (!active)
                continue;

            var snapshot = Owner is MainWindow engineeringWindow
                ? engineeringWindow.GetIoFatPreparationProgressSnapshot(ied)
                : BuildFallbackPreparationSnapshot(ied);
            state.Target = Math.Max(state.Target, Math.Clamp(snapshot.Percent, 0d, 100d));
            state.Message = snapshot.Message;
            state.StepText = snapshot.StepText;
            state.AdvanceDisplay();
        }

        ApplyDeterminateCardProgressBars();
    }

    private void ApplyDeterminateCardProgressBars()
    {
        foreach (var progressBar in VisualDescendants<ProgressBar>(this)
                     .Where(progress => string.Equals(progress.Name, "CardProgress", StringComparison.Ordinal)))
        {
            // The XAML fallback is indeterminate so old project binaries remain safe.
            // Once this behavior is installed every instantiated IED-card bar becomes
            // determinate and is driven by real connection/discovery/acquisition state.
            progressBar.IsIndeterminate = false;
            progressBar.Minimum = 0d;
            progressBar.Maximum = 100d;

            if (progressBar.DataContext is not IoTestIedPlan ied ||
                !_preparationDisplayStates.TryGetValue(ied, out var state))
            {
                progressBar.Value = 0d;
                continue;
            }

            progressBar.Value = state.Display;
            progressBar.ToolTip = string.Join(
                " · ",
                new[]
                {
                    state.Message,
                    state.StepText,
                    $"{state.Display:0}%"
                }.Where(value => !string.IsNullOrWhiteSpace(value)));
        }
    }

    private static IoFatPreparationProgressSnapshot BuildFallbackPreparationSnapshot(IoTestIedPlan ied)
    {
        var message = string.IsNullOrWhiteSpace(ied.PreparationStatusText)
            ? $"Preparing {ied.IedName}"
            : ied.PreparationStatusText;
        var percent = message.Contains("live", StringComparison.OrdinalIgnoreCase)
            ? 100d
            : message.Contains("validating", StringComparison.OrdinalIgnoreCase)
                ? 90d
                : message.Contains("arming", StringComparison.OrdinalIgnoreCase)
                    ? 80d
                    : message.Contains("matching", StringComparison.OrdinalIgnoreCase)
                        ? 68d
                        : 8d;
        return new IoFatPreparationProgressSnapshot(message, percent, "Preparing IED");
    }

    private void RealPreparationProgress_Closed(object? sender, EventArgs e)
    {
        Closed -= RealPreparationProgress_Closed;
        if (_preparationProgressTimer == null)
            return;

        _preparationProgressTimer.Stop();
        _preparationProgressTimer.Tick -= PreparationProgressTimer_Tick;
        _preparationProgressTimer = null;
        _preparationDisplayStates.Clear();
    }

    private sealed class PreparationDisplayState
    {
        public double Target { get; set; }
        public double Display { get; private set; }
        public string Message { get; set; } = string.Empty;
        public string StepText { get; set; } = string.Empty;
        public bool WasActive { get; set; }

        public void Reset()
        {
            Target = 0d;
            Display = 0d;
            Message = string.Empty;
            StepText = string.Empty;
        }

        public void AdvanceDisplay()
        {
            var remaining = Target - Display;
            if (remaining <= 0.04d)
            {
                if (remaining > 0d)
                    Display = Target;
                return;
            }

            // Same visual principle as Engineering discovery cards: 20 FPS,
            // ease-out movement, bounded minimum speed, and no artificial loop.
            var completing = Target >= 99.9d;
            var movement = Math.Clamp(
                remaining * (completing ? 0.16d : 0.115d),
                0.10d,
                completing ? 2.4d : 1.15d);
            Display = Math.Min(Target, Display + movement);
        }
    }
}
