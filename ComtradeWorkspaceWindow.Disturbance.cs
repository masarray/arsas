using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const int MaxVisibleDisturbanceTracks = 16;
    private readonly HashSet<ComtradeSignalItem> _disturbanceVisibleSignals = new();
    private CancellationTokenSource? _disturbanceLoadCts;
    private bool _disturbanceInitialized;
    private bool _disturbanceVisibilityReady;
    private bool _disturbanceCheckboxSync;
    private ComtradeSourceViewport _disturbanceLoadedViewport;
    private ComtradeSourceViewport _disturbanceRequestedViewport;
    private uint[]? _disturbanceReferenceTimestamps;
    private bool _disturbanceReferenceExact;
    private bool _disturbanceSourcePanGesture;
    private Point _disturbanceSourcePanStartPoint;
    private ComtradeSourceViewport _disturbanceSourcePanStartViewport;

    private void InitializeDisturbanceWorkspace()
    {
        if (_disturbanceInitialized) return;
        _disturbanceInitialized = true;

        // The synchronized view replaces the old one-channel canvas as the primary waveform UX.
        // Keep the old renderer alive only as a compatibility implementation detail while P1D lands.
        WaveformView.NavigationChanged -= WaveformView_NavigationChanged;
        WaveformView.Visibility = Visibility.Collapsed;
        DisturbanceView.Visibility = _analysisMode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        DisturbanceView.NavigationChanged += DisturbanceView_NavigationChanged;
        DisturbanceView.PreviewMouseWheel += DisturbanceView_PreviewMouseWheel;
        DisturbanceView.PreviewMouseDown += DisturbanceView_PreviewMouseDown;
        DisturbanceView.PreviewMouseUp += DisturbanceView_PreviewMouseUp;
        Closed += DisturbanceWindow_Closed;

        BuildDefaultVisibleSignals();
        _disturbanceVisibilityReady = true;
        Dispatcher.BeginInvoke(SyncSignalVisibilityCheckboxes, DispatcherPriority.Loaded);

        var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
        _disturbanceRequestedViewport = full;
        _ = ReloadDisturbanceAsync(full, initialLoad: true);
    }

    private void DisturbanceWindow_Closed(object? sender, EventArgs e)
    {
        _disturbanceLoadCts?.Cancel();
        _disturbanceLoadCts?.Dispose();
        _disturbanceLoadCts = null;
        DisturbanceView.NavigationChanged -= DisturbanceView_NavigationChanged;
        DisturbanceView.PreviewMouseWheel -= DisturbanceView_PreviewMouseWheel;
        DisturbanceView.PreviewMouseDown -= DisturbanceView_PreviewMouseDown;
        DisturbanceView.PreviewMouseUp -= DisturbanceView_PreviewMouseUp;
    }

    private void BuildDefaultVisibleSignals()
    {
        _disturbanceVisibleSignals.Clear();
        if (SignalList.ItemsSource is not IEnumerable<ComtradeSignalItem> signals) return;
        var all = signals.ToArray();

        foreach (var signal in all.Where(item => item.IsAnalog).Take(6))
            _disturbanceVisibleSignals.Add(signal);

        var semanticDigital = all.Where(item => !item.IsAnalog && IsUsefulProtectionDigital(item.Title)).Take(6).ToArray();
        var digitalDefaults = semanticDigital.Length > 0
            ? semanticDigital
            : all.Where(item => !item.IsAnalog).Take(4).ToArray();
        foreach (var signal in digitalDefaults)
            _disturbanceVisibleSignals.Add(signal);
    }

    private void SignalVisibility_Loaded(object sender, RoutedEventArgs e)
    {
        if (!_disturbanceVisibilityReady || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        _disturbanceCheckboxSync = true;
        checkBox.IsChecked = _disturbanceVisibleSignals.Contains(signal);
        _disturbanceCheckboxSync = false;
    }

    private async void SignalVisibility_Checked(object sender, RoutedEventArgs e)
    {
        if (_disturbanceCheckboxSync || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        if (_disturbanceVisibleSignals.Contains(signal)) return;
        if (_disturbanceVisibleSignals.Count >= MaxVisibleDisturbanceTracks)
        {
            _disturbanceCheckboxSync = true;
            checkBox.IsChecked = false;
            _disturbanceCheckboxSync = false;
            StatusTextBlock.Text = $"Waveform workspace supports up to {MaxVisibleDisturbanceTracks} visible tracks at once. Hide another signal first.";
            return;
        }
        _disturbanceVisibleSignals.Add(signal);
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false).ConfigureAwait(true);
    }

    private async void SignalVisibility_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_disturbanceCheckboxSync || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        if (!_disturbanceVisibleSignals.Remove(signal)) return;
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false).ConfigureAwait(true);
    }

    private async void AutoSignals_Click(object sender, RoutedEventArgs e)
    {
        BuildDefaultVisibleSignals();
        SyncSignalVisibilityCheckboxes();
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false).ConfigureAwait(true);
    }

    private async void ClearSignals_Click(object sender, RoutedEventArgs e)
    {
        _disturbanceVisibleSignals.Clear();
        SyncSignalVisibilityCheckboxes();
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false).ConfigureAwait(true);
    }

    private void SyncSignalVisibilityCheckboxes()
    {
        if (!_disturbanceVisibilityReady) return;
        _disturbanceCheckboxSync = true;
        try
        {
            foreach (var item in SignalList.Items.Cast<object>())
            {
                if (item is not ComtradeSignalItem signal) continue;
                if (SignalList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem container) continue;
                var checkBox = FindVisualChild<CheckBox>(container);
                if (checkBox is not null)
                    checkBox.IsChecked = _disturbanceVisibleSignals.Contains(signal);
            }
        }
        finally
        {
            _disturbanceCheckboxSync = false;
        }
    }

    private static T? FindVisualChild<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T typed) return typed;
            var nested = FindVisualChild<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    private async Task ReloadDisturbanceAsync(ComtradeSourceViewport requestedViewport, bool initialLoad)
    {
        _disturbanceRequestedViewport = requestedViewport;
        _disturbanceLoadCts?.Cancel();
        _disturbanceLoadCts?.Dispose();
        _disturbanceLoadCts = new CancellationTokenSource();
        var token = _disturbanceLoadCts.Token;
        var selected = _disturbanceVisibleSignals.Take(MaxVisibleDisturbanceTracks).ToArray();

        if (selected.Length == 0)
        {
            DisturbanceView.ShowMessage("Select signals to display.");
            DigitalEventGrid.ItemsSource = Array.Empty<ComtradeDigitalEventRow>();
            StatusTextBlock.Text = "No waveform tracks selected • use the checkboxes in Signals or choose Auto.";
            NavigationTextBlock.Text = "Shared timeline • trigger = 0 ms • click a digital event to move Cursor A";
            return;
        }

        StatusTextBlock.Text = initialLoad
            ? $"Building synchronized disturbance view for {selected.Length} tracks…"
            : $"Refreshing {selected.Length} synchronized tracks…";

        try
        {
            var result = await LoadDisturbanceTracksAsync(selected, requestedViewport, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            _disturbanceLoadedViewport = result.SourceViewport;
            _disturbanceRequestedViewport = result.SourceViewport;
            _disturbanceReferenceTimestamps = result.ReferenceTimestamps;
            _disturbanceReferenceExact = result.ReferenceIsExact;
            var triggerMs = ResolveTriggerMilliseconds();
            DisturbanceView.ShowTracks(result.Tracks.Select(item => item.Track).ToArray(), _record.Info.TimeMultiplier, triggerMs);
            DigitalEventGrid.ItemsSource = BuildDigitalEventRows(result.Tracks, triggerMs);
            DigitalEventExpander.Visibility = result.Tracks.Any(item => item.Track.IsDigital) ? Visibility.Visible : Visibility.Collapsed;
            ResetViewButton.IsEnabled = result.Tracks.Any(item => item.Track.Timestamps.Length > 1);
            StatusTextBlock.Text = $"Synchronized disturbance view • {result.Tracks.Count} tracks • {result.SourceViewport.FrameCount:N0} source frames" +
                                   (result.ReferenceIsExact ? " • exact native samples" : " • bounded native overview");
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            DisturbanceView.ShowMessage(ex.Message);
            StatusTextBlock.Text = $"Synchronized waveform load failed: {ex.Message}";
        }
    }

    private async Task<DisturbanceLoadResult> LoadDisturbanceTracksAsync(
        IReadOnlyList<ComtradeSignalItem> signals,
        ComtradeSourceViewport requestedViewport,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => BuildDisturbanceTracks(signals, requestedViewport, token), token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private DisturbanceLoadResult BuildDisturbanceTracks(
        IReadOnlyList<ComtradeSignalItem> signals,
        ComtradeSourceViewport requestedViewport,
        CancellationToken token)
    {
        var viewport = ComtradeAbsoluteViewportMath.Normalize(requestedViewport, _record.Info.FrameCount);
        var loaded = new List<LoadedDisturbanceTrack>(signals.Count);
        uint[]? referenceTimestamps = null;
        var exact = viewport.FrameCount <= ExactSignalFrameLimit;

        if (exact)
        {
            var count = checked((int)viewport.FrameCount);
            referenceTimestamps = _record.ReadRawTimestamps(viewport.StartFrame, count);
            foreach (var signal in signals)
            {
                token.ThrowIfCancellationRequested();
                if (signal.IsAnalog)
                {
                    var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title,
                        BuildTrackSubtitle(metadata.Phase, metadata.Circuit),
                        metadata.Units,
                        false,
                        _record.ReadAnalog(signal.Index, viewport.StartFrame, count),
                        null,
                        referenceTimestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, false))));
                }
                else
                {
                    var metadata = _record.StatusChannels[checked((int)signal.Index)];
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title,
                        $"{BuildTrackSubtitle(metadata.Phase, metadata.Circuit)} • normal {metadata.NormalState}",
                        "",
                        true,
                        null,
                        _record.ReadStatus(signal.Index, viewport.StartFrame, count),
                        referenceTimestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, true))));
                }
            }
        }
        else
        {
            foreach (var signal in signals)
            {
                token.ThrowIfCancellationRequested();
                var preview = BuildSignalPreview(signal, viewport, token);
                referenceTimestamps ??= preview.Timestamps;
                if (signal.IsAnalog && preview.Analog is not null)
                {
                    var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title, BuildTrackSubtitle(metadata.Phase, metadata.Circuit), metadata.Units,
                        false, preview.Analog, null, preview.Timestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, false), preserveAllPoints: true)));
                }
                else if (!signal.IsAnalog && preview.Status is not null)
                {
                    var metadata = _record.StatusChannels[checked((int)signal.Index)];
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title, $"{BuildTrackSubtitle(metadata.Phase, metadata.Circuit)} • normal {metadata.NormalState}", "",
                        true, null, preview.Status, preview.Timestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, true))));
                }
            }
        }

        return new DisturbanceLoadResult(loaded, viewport, referenceTimestamps ?? Array.Empty<uint>(), exact);
    }

    private IReadOnlyList<ComtradeDigitalEventRow> BuildDigitalEventRows(IReadOnlyList<LoadedDisturbanceTrack> tracks, double? triggerMilliseconds)
    {
        var events = new List<(double Time, string Signal, string Event, string State)>();
        foreach (var loaded in tracks.Where(item => item.Track.IsDigital))
        {
            var track = loaded.Track;
            if (track.Digital is null) continue;
            var count = Math.Min(track.Digital.Length, track.Timestamps.Length);
            for (var i = 1; i < count; i++)
            {
                var before = track.Digital[i - 1] != 0;
                var after = track.Digital[i] != 0;
                if (before == after) continue;
                var time = ComtradeTimeMath.ToMilliseconds(track.Timestamps[i], _record.Info.TimeMultiplier);
                events.Add((time, track.Title, DescribeDigitalEvent(track.Title, after), $"{(before ? 1 : 0)}→{(after ? 1 : 0)}"));
            }
        }

        var ordered = events.OrderBy(item => item.Time).ToArray();
        var rows = new List<ComtradeDigitalEventRow>(ordered.Length);
        double? previous = null;
        foreach (var item in ordered)
        {
            var relative = item.Time - (triggerMilliseconds ?? 0.0);
            var delta = previous is { } previousTime ? item.Time - previousTime : (double?)null;
            rows.Add(new ComtradeDigitalEventRow(
                item.Time,
                FormatRelativeTime(relative),
                delta is { } d ? $"{d:G6} ms" : "—",
                item.Signal,
                item.Event,
                item.State));
            previous = item.Time;
        }
        return rows;
    }

    private void DigitalEventGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DigitalEventGrid.SelectedItem is not ComtradeDigitalEventRow row) return;
        DisturbanceView.SetCursorAFromAbsoluteMilliseconds(row.AbsoluteMilliseconds);
        StatusTextBlock.Text = $"Cursor A moved to {row.Signal} • {row.Event} • {row.TimeText}.";
    }

    private async void DisturbanceReset_Click(object sender, RoutedEventArgs e)
    {
        var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
        if (_record.Info.FrameCount > ExactSignalFrameLimit && _disturbanceRequestedViewport != full)
        {
            _disturbanceRequestedViewport = full;
            await ReloadDisturbanceAsync(full, initialLoad: false).ConfigureAwait(true);
            return;
        }
        DisturbanceView.ResetNavigation();
    }

    private void DisturbanceView_NavigationChanged(object? sender, ComtradeDisturbanceNavigationChangedEventArgs e)
    {
        if (_analysisMode != AnalysisMode.Waveform) return;
        NavigationTextBlock.Text = e.Summary + (_record.Info.FrameCount > ExactSignalFrameLimit
            ? "  |  wheel reloads native source range"
            : "  |  shared trigger-relative timeline");
        UpdateAnalysisAvailability();
    }

    private async void DisturbanceView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (_record.Info.FrameCount <= ExactSignalFrameLimit || _disturbanceLoadedViewport.FrameCount == 0)
            return;
        var current = _disturbanceRequestedViewport.FrameCount > 0 ? _disturbanceRequestedViewport : _disturbanceLoadedViewport;
        var fraction = DisturbanceView.PlotFractionAt(e.GetPosition(DisturbanceView).X);
        ComtradeSourceViewport target;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var step = Math.Max(1UL, current.FrameCount / 10);
            target = ComtradeAbsoluteViewportMath.Pan(current, _record.Info.FrameCount, ToSignedStep(step, e.Delta > 0 ? -1 : 1));
        }
        else
        {
            target = ComtradeAbsoluteViewportMath.Zoom(current, _record.Info.FrameCount, fraction, e.Delta > 0 ? 0.60 : 1.60, minimumFrames: 32);
        }
        e.Handled = true;
        if (target == current) return;
        _disturbanceRequestedViewport = target;
        await ReloadDisturbanceAsync(target, initialLoad: false).ConfigureAwait(true);
    }

    private void DisturbanceView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_record.Info.FrameCount <= ExactSignalFrameLimit || _disturbanceLoadedViewport.FrameCount == 0) return;
        var pan = e.ChangedButton == MouseButton.Middle ||
                  (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        if (!pan) return;
        _disturbanceSourcePanGesture = true;
        _disturbanceSourcePanStartPoint = e.GetPosition(DisturbanceView);
        _disturbanceSourcePanStartViewport = _disturbanceRequestedViewport.FrameCount > 0 ? _disturbanceRequestedViewport : _disturbanceLoadedViewport;
        DisturbanceView.CaptureMouse();
        DisturbanceView.Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private async void DisturbanceView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_disturbanceSourcePanGesture) return;
        _disturbanceSourcePanGesture = false;
        var end = e.GetPosition(DisturbanceView);
        if (DisturbanceView.IsMouseCaptured) DisturbanceView.ReleaseMouseCapture();
        DisturbanceView.Cursor = Cursors.Cross;
        e.Handled = true;
        var width = Math.Max(1.0, DisturbanceView.ActualWidth - 168.0);
        var fraction = -(end.X - _disturbanceSourcePanStartPoint.X) / width;
        var delta = ToSignedDelta(_disturbanceSourcePanStartViewport.FrameCount, fraction);
        var target = ComtradeAbsoluteViewportMath.Pan(_disturbanceSourcePanStartViewport, _record.Info.FrameCount, delta);
        if (target == _disturbanceRequestedViewport) return;
        _disturbanceRequestedViewport = target;
        await ReloadDisturbanceAsync(target, initialLoad: false).ConfigureAwait(true);
    }

    private ComtradeSourceViewport CurrentDisturbanceViewport()
        => _disturbanceRequestedViewport.FrameCount > 0
            ? _disturbanceRequestedViewport
            : ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);

    private double? ResolveTriggerMilliseconds()
    {
        return ComtradeTimeMath.TryGetTriggerOffsetMilliseconds(_record.Info.StartTime, _record.Info.TriggerTime, out var trigger)
            ? trigger
            : null;
    }

    private bool TryResolveDisturbanceCursorFrame(out ulong frame)
    {
        frame = 0;
        if (DisturbanceView.CursorAMilliseconds is not { } cursor || _record.Info.FrameCount == 0 || _disturbanceLoadedViewport.FrameCount == 0)
            return false;

        if (_disturbanceReferenceExact && _disturbanceReferenceTimestamps is { Length: > 0 } timestamps)
        {
            var targetRaw = cursor * 1000.0 / Math.Max(1e-12, _record.Info.TimeMultiplier);
            var index = NearestTimestampIndex(timestamps, targetRaw);
            frame = Math.Min(_record.Info.FrameCount - 1, _disturbanceLoadedViewport.StartFrame + checked((ulong)index));
            return true;
        }

        var start = DisturbanceView.ViewStartMilliseconds;
        var end = DisturbanceView.ViewEndMilliseconds;
        if (end <= start) return false;
        var fraction = Math.Clamp((cursor - start) / (end - start), 0.0, 1.0);
        var offset = _disturbanceLoadedViewport.FrameCount <= 1
            ? 0UL
            : checked((ulong)Math.Round((_disturbanceLoadedViewport.FrameCount - 1) * fraction, MidpointRounding.AwayFromZero));
        frame = Math.Min(_record.Info.FrameCount - 1, _disturbanceLoadedViewport.StartFrame + offset);
        return true;
    }

    private static int NearestTimestampIndex(uint[] timestamps, double target)
    {
        if (timestamps.Length <= 1) return 0;
        var lo = 0;
        var hi = timestamps.Length - 1;
        while (lo < hi)
        {
            var mid = lo + (hi - lo) / 2;
            if (timestamps[mid] < target) lo = mid + 1;
            else hi = mid;
        }
        if (lo == 0) return 0;
        var before = lo - 1;
        return Math.Abs(timestamps[lo] - target) < Math.Abs(timestamps[before] - target) ? lo : before;
    }

    private static string BuildTrackSubtitle(string phase, string circuit)
        => string.Join(" • ", new[] { string.IsNullOrWhiteSpace(phase) ? null : $"phase {phase}", circuit }
            .Where(value => !string.IsNullOrWhiteSpace(value)));

    private static Color ResolveSignalColor(string phase, string title, bool digital)
    {
        if (digital)
        {
            var upper = (title ?? string.Empty).ToUpperInvariant();
            if (upper.Contains("TRIP")) return Color.FromRgb(220, 88, 55);
            if (upper.Contains("PICK") || upper.Contains("START")) return Color.FromRgb(222, 142, 35);
            if (upper.Contains("OPEN") || upper.Contains("CLOSE") || upper.Contains("CB")) return Color.FromRgb(37, 151, 102);
            return Color.FromRgb(65, 139, 105);
        }

        var normalized = NormalizePhase(phase, title);
        return normalized switch
        {
            "L1" => Color.FromRgb(214, 66, 66),
            "L2" => Color.FromRgb(218, 157, 0),
            "L3" => Color.FromRgb(39, 118, 203),
            "N" or "E" => Color.FromRgb(106, 117, 130),
            _ => Color.FromRgb(48, 126, 213)
        };
    }

    private static bool IsUsefulProtectionDigital(string title)
    {
        var value = (title ?? string.Empty).ToUpperInvariant();
        return new[] { "TRIP", "PICK", "START", "OPER", "OPEN", "CLOSE", "BREAKER", "CB", "52", "87", "50", "51", "21" }
            .Any(value.Contains);
    }

    private static string DescribeDigitalEvent(string title, bool asserted)
    {
        var value = (title ?? string.Empty).ToUpperInvariant();
        if (value.Contains("TRIP")) return asserted ? "Trip" : "Trip reset";
        if (value.Contains("PICK") || value.Contains("START")) return asserted ? "Pickup" : "Dropoff";
        if (value.Contains("OPEN")) return asserted ? "Open" : "Open reset";
        if (value.Contains("CLOSE")) return asserted ? "Close" : "Close reset";
        return asserted ? "Assert" : "Deassert";
    }

    private static string FormatRelativeTime(double milliseconds)
        => Math.Abs(milliseconds) < 0.0005 ? "0 ms" : $"{milliseconds:+0.###;-0.###} ms";

    private sealed record LoadedDisturbanceTrack(ComtradeSignalItem Signal, ComtradeDisturbanceTrack Track);
    private sealed record DisturbanceLoadResult(
        IReadOnlyList<LoadedDisturbanceTrack> Tracks,
        ComtradeSourceViewport SourceViewport,
        uint[] ReferenceTimestamps,
        bool ReferenceIsExact);

    private sealed record ComtradeDigitalEventRow(
        double AbsoluteMilliseconds,
        string TimeText,
        string DeltaText,
        string Signal,
        string Event,
        string State);
}