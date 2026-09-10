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
    private CancellationTokenSource? _disturbanceCursorSnapCts;
    private bool _disturbanceInitialized;
    private bool _disturbanceVisibilityReady;
    private bool _disturbanceCheckboxSync;
    private bool _disturbanceInitialFocusApplied;
    private ComtradeSourceViewport _disturbanceLoadedViewport;
    private ComtradeSourceViewport _disturbanceRequestedViewport;
    private uint[]? _disturbanceReferenceTimestamps;
    private ulong[]? _disturbanceReferenceSourceFrames;

    private void InitializeDisturbanceWorkspace()
    {
        if (_disturbanceInitialized) return;
        _disturbanceInitialized = true;

        WaveformView.Visibility = Visibility.Collapsed;
        DisturbanceView.Visibility = _analysisMode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        DisturbanceView.NavigationChanged += DisturbanceView_NavigationChanged;
        DisturbanceView.CursorChanged += DisturbanceView_CursorChanged;
        DisturbanceView.PanRequested += DisturbanceView_PanRequested;
        DisturbanceView.PreviewMouseWheel += DisturbanceView_PreviewMouseWheel;
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
        _disturbanceCursorSnapCts?.Cancel();
        _disturbanceCursorSnapCts?.Dispose();
        _disturbanceCursorSnapCts = null;
        DisturbanceView.NavigationChanged -= DisturbanceView_NavigationChanged;
        DisturbanceView.CursorChanged -= DisturbanceView_CursorChanged;
        DisturbanceView.PanRequested -= DisturbanceView_PanRequested;
        DisturbanceView.PreviewMouseWheel -= DisturbanceView_PreviewMouseWheel;
    }

    private void BuildDefaultVisibleSignals()
    {
        _disturbanceVisibleSignals.Clear();
        if (SignalList.ItemsSource is not IEnumerable<ComtradeSignalItem> signals) return;
        var all = signals.ToArray();

        var preferredAnalog = all
            .Where(item => item.IsAnalog && item.Section == "Voltage")
            .OrderBy(item => item.PhaseOrder)
            .Take(3)
            .Concat(all.Where(item => item.IsAnalog && item.Section == "Current")
                .OrderBy(item => item.PhaseOrder)
                .Take(3))
            .Distinct()
            .ToList();
        foreach (var signal in all.Where(item => item.IsAnalog).OrderBy(item => item.SectionOrder).ThenBy(item => item.PhaseOrder))
        {
            if (preferredAnalog.Count >= 6) break;
            if (!preferredAnalog.Contains(signal)) preferredAnalog.Add(signal);
        }
        foreach (var signal in preferredAnalog.Take(6))
            _disturbanceVisibleSignals.Add(signal);

        var semanticDigital = all
            .Where(item => !item.IsAnalog && ComtradeDisturbanceTimelineMath.IsUsefulProtectionDigital(item.Title))
            .Take(6)
            .ToArray();
        var digitalDefaults = semanticDigital.Length > 0
            ? semanticDigital
            : all.Where(item => !item.IsAnalog).Take(4).ToArray();
        foreach (var signal in digitalDefaults)
        {
            if (_disturbanceVisibleSignals.Count >= MaxVisibleDisturbanceTracks) break;
            _disturbanceVisibleSignals.Add(signal);
        }
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
            StatusTextBlock.Text = $"Time Signals supports up to {MaxVisibleDisturbanceTracks} visible tracks at once. Hide another signal first.";
            return;
        }
        _disturbanceVisibleSignals.Add(signal);
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false, preserveLocalView: true).ConfigureAwait(true);
    }

    private async void SignalVisibility_Unchecked(object sender, RoutedEventArgs e)
    {
        if (_disturbanceCheckboxSync || sender is not CheckBox checkBox || checkBox.DataContext is not ComtradeSignalItem signal)
            return;
        if (!_disturbanceVisibleSignals.Remove(signal)) return;
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false, preserveLocalView: true).ConfigureAwait(true);
    }

    private async void AutoSignals_Click(object sender, RoutedEventArgs e)
    {
        BuildDefaultVisibleSignals();
        SyncSignalVisibilityCheckboxes();
        await ReloadDisturbanceAsync(CurrentDisturbanceViewport(), initialLoad: false, preserveLocalView: true).ConfigureAwait(true);
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

    private async Task ReloadDisturbanceAsync(
        ComtradeSourceViewport requestedViewport,
        bool initialLoad,
        bool preserveLocalView = false)
    {
        var previousView = new ComtradeTimeWindow(DisturbanceView.ViewStartMilliseconds, DisturbanceView.ViewEndMilliseconds);
        var previousSourceViewport = _disturbanceLoadedViewport;
        _disturbanceRequestedViewport = requestedViewport;
        _disturbanceLoadCts?.Cancel();
        _disturbanceLoadCts?.Dispose();
        _disturbanceLoadCts = new CancellationTokenSource();
        var token = _disturbanceLoadCts.Token;
        var selected = _disturbanceVisibleSignals
            .OrderBy(item => item.IsAnalog ? 0 : 1)
            .ThenBy(item => item.SectionOrder)
            .ThenBy(item => item.PhaseOrder)
            .ThenBy(item => item.Index)
            .Take(MaxVisibleDisturbanceTracks)
            .ToArray();

        if (selected.Length == 0)
        {
            DisturbanceView.ShowMessage("Select signals to display.");
            CursorReadoutCanvas.Children.Clear();
            _p1d5VisibleTrackOrder = Array.Empty<ComtradeSignalItem>();
            DigitalEventGrid.ItemsSource = Array.Empty<ComtradeDigitalEventRow>();
            StatusTextBlock.Text = "No Time Signals tracks selected • use the checkboxes in Signals or choose Auto.";
            NavigationTextBlock.Text = "Wheel scrolls signals • Ctrl+wheel zooms time • drag plot pans • drag C1/C2 measures";
            return;
        }

        StatusTextBlock.Text = initialLoad
            ? $"Building Time Signals workstation for {selected.Length} tracks…"
            : $"Refreshing {selected.Length} synchronized tracks…";

        try
        {
            var result = await LoadDisturbanceTracksAsync(selected, requestedViewport, token).ConfigureAwait(true);
            if (token.IsCancellationRequested) return;

            _disturbanceLoadedViewport = result.SourceViewport;
            _disturbanceRequestedViewport = result.SourceViewport;
            _disturbanceReferenceTimestamps = result.ReferenceTimestamps;
            _disturbanceReferenceSourceFrames = result.ReferenceSourceFrames;
            P1D5RememberTrackOrder(result.Tracks);
            var triggerMs = ResolveTriggerMilliseconds();
            DisturbanceView.ShowTracks(result.Tracks.Select(item => item.Track).ToArray(), _record.Info.TimeMultiplier, triggerMs);

            if (initialLoad && !_disturbanceInitialFocusApplied)
            {
                DisturbanceView.ApplyTriggerFocusedDefault(_record.Info.NominalFrequency);
                _disturbanceInitialFocusApplied = true;

                if (result.SourceViewport.FrameCount > ExactSignalFrameLimit &&
                    TryBuildSourceViewportForTimeWindow(
                        DisturbanceView.ViewStartMilliseconds,
                        DisturbanceView.ViewEndMilliseconds,
                        out var triggerViewport) &&
                    triggerViewport.FrameCount > 0 &&
                    triggerViewport.FrameCount < result.SourceViewport.FrameCount)
                {
                    StatusTextBlock.Text = "Refining trigger neighborhood from source frames…";
                    await ReloadDisturbanceAsync(triggerViewport, initialLoad: false).ConfigureAwait(true);
                    DisturbanceView.ApplyTriggerFocusedDefault(_record.Info.NominalFrequency);
                    return;
                }
            }
            else if (preserveLocalView && previousSourceViewport == result.SourceViewport && previousView.SpanMilliseconds > 0)
            {
                DisturbanceView.SetViewWindow(previousView.StartMilliseconds, previousView.EndMilliseconds);
            }

            DigitalEventGrid.ItemsSource = BuildDigitalEventRows(result.Tracks, triggerMs);
            DigitalEventExpander.Visibility = result.Tracks.Any(item => item.Track.IsDigital) ? Visibility.Visible : Visibility.Collapsed;
            ResetViewButton.IsEnabled = result.Tracks.Any(item => item.Track.Timestamps.Length > 1);
            FullRecordButton.IsEnabled = result.Tracks.Any(item => item.Track.Timestamps.Length > 1);
            var traceMode = P1D5IsRmsTrace ? "RMS" : "instantaneous";
            StatusTextBlock.Text = $"Time Signals • {result.Tracks.Count} tracks • {traceMode} • {P1D5RepresentationLabel} • {result.SourceViewport.FrameCount:N0} source frames" +
                                   (result.SourceViewport.FrameCount <= ExactSignalFrameLimit ? " • exact samples" : " • bounded overview");
            QueueP1D5CursorMeasurements();
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
            StatusTextBlock.Text = $"Time Signals load failed: {ex.Message}";
            ComtradeDiagnosticQueue.TryEnqueue("P1D5.TimeSignals", "TIMESIGNALS_LOAD_FAILURE", $"viewport={requestedViewport}", ex);
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
        var exact = viewport.FrameCount <= ExactSignalFrameLimit;

        if (exact)
        {
            var count = checked((int)viewport.FrameCount);
            var timestamps = _record.ReadRawTimestamps(viewport.StartFrame, count);
            var sourceFrames = BuildSequentialFrames(viewport.StartFrame, count);
            foreach (var signal in signals)
            {
                token.ThrowIfCancellationRequested();
                if (signal.IsAnalog)
                {
                    var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                    var recorded = _record.ReadAnalog(signal.Index, viewport.StartFrame, count);
                    var values = P1D5IsRmsTrace
                        ? ComtradeRmsSeriesBuilder.BuildExact(
                            recorded,
                            timestamps,
                            sourceFrames,
                            _record.Info.TimeMultiplier,
                            _record.Info.NominalFrequency,
                            P1D5DisplayScale(signal.Index),
                            token).Values
                        : P1D5ScaleInstantaneous(recorded, signal.Index);
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title,
                        BuildP1D5TrackSubtitle(metadata),
                        metadata.Units,
                        false,
                        values,
                        null,
                        timestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, false),
                        SourceFrames: sourceFrames)));
                }
                else
                {
                    var metadata = _record.StatusChannels[checked((int)signal.Index)];
                    var states = _record.ReadStatus(signal.Index, viewport.StartFrame, count);
                    var edges = BuildExactDigitalEdges(states, timestamps, sourceFrames, metadata.NormalState);
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title,
                        $"{BuildTrackSubtitle(metadata.Phase, metadata.Circuit)} • normal {metadata.NormalState}",
                        "",
                        true,
                        null,
                        states,
                        timestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, true),
                        SourceFrames: sourceFrames,
                        DigitalNormalState: metadata.NormalState,
                        DigitalEdges: edges)));
                }
            }
        }
        else
        {
            var source = new ArdIrecRangeSource(_record);
            foreach (var signal in signals)
            {
                token.ThrowIfCancellationRequested();
                if (signal.IsAnalog)
                {
                    var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                    if (P1D5IsRmsTrace)
                    {
                        var rms = ComtradeRmsSeriesBuilder.BuildBounded(
                            source,
                            signal.Index,
                            viewport.StartFrame,
                            viewport.FrameCount,
                            _record.Info.TimeMultiplier,
                            _record.Info.NominalFrequency,
                            P1D5DisplayScale(signal.Index),
                            FullRecordAnalogBuckets * 2,
                            cancellationToken: token);
                        loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                            signal.Title,
                            BuildP1D5TrackSubtitle(metadata),
                            metadata.Units,
                            false,
                            rms.Values,
                            null,
                            rms.Timestamps,
                            ResolveSignalColor(metadata.Phase, signal.Title, false),
                            PreserveAllPoints: true,
                            SourceFrames: rms.SourceFrames)));
                    }
                    else
                    {
                        var envelope = ComtradeRangeDecimator.BuildAnalogEnvelope(
                            source,
                            signal.Index,
                            viewport.StartFrame,
                            viewport.FrameCount,
                            FullRecordAnalogBuckets,
                            cancellationToken: token);
                        var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);
                        var values = P1D5ScaleInstantaneous(series.Values, signal.Index);
                        loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                            signal.Title,
                            BuildP1D5TrackSubtitle(metadata),
                            metadata.Units,
                            false,
                            values,
                            null,
                            series.Timestamps,
                            ResolveSignalColor(metadata.Phase, signal.Title, false),
                            PreserveAllPoints: true,
                            SourceFrames: series.SourceFrames)));
                    }
                }
                else
                {
                    var transitionSet = ComtradeRangeDecimator.BuildDigitalTransitions(
                        source,
                        signal.Index,
                        viewport.StartFrame,
                        viewport.FrameCount,
                        FullRecordDigitalTransitionCap,
                        cancellationToken: token);
                    var series = ComtradeDecimatedSeriesBuilder.BuildDigital(transitionSet);
                    var metadata = _record.StatusChannels[checked((int)signal.Index)];
                    var edges = BuildReducedDigitalEdges(transitionSet, metadata.NormalState);
                    loaded.Add(new LoadedDisturbanceTrack(signal, new ComtradeDisturbanceTrack(
                        signal.Title,
                        $"{BuildTrackSubtitle(metadata.Phase, metadata.Circuit)} • normal {metadata.NormalState}",
                        "",
                        true,
                        null,
                        series.States,
                        series.Timestamps,
                        ResolveSignalColor(metadata.Phase, signal.Title, true),
                        SourceFrames: series.SourceFrames,
                        DigitalNormalState: metadata.NormalState,
                        DigitalEdges: edges,
                        DigitalIsLossy: series.IsTruncated)));
                }
            }
        }

        var reference = loaded
            .Select(item => item.Track)
            .Where(track => track.Timestamps.Length > 0 && track.SourceFrames is { Length: > 0 })
            .OrderByDescending(track => track.Timestamps.Length)
            .FirstOrDefault();
        return new DisturbanceLoadResult(
            loaded,
            viewport,
            reference?.Timestamps ?? Array.Empty<uint>(),
            reference?.SourceFrames ?? Array.Empty<ulong>());
    }

    private string BuildP1D5TrackSubtitle(ComtradeAnalogChannelInfo metadata)
    {
        var baseSubtitle = BuildTrackSubtitle(metadata.Phase, metadata.Circuit);
        var trace = P1D5IsRmsTrace ? "RMS" : "instant";
        return string.IsNullOrWhiteSpace(baseSubtitle)
            ? $"{trace} • {P1D5RepresentationLabel}"
            : $"{baseSubtitle} • {trace} • {P1D5RepresentationLabel}";
    }

    private static ulong[] BuildSequentialFrames(ulong startFrame, int count)
    {
        var frames = new ulong[count];
        for (var i = 0; i < count; i++)
            frames[i] = startFrame + checked((ulong)i);
        return frames;
    }

    private static IReadOnlyList<ComtradeDisturbanceDigitalEdge> BuildExactDigitalEdges(
        IReadOnlyList<byte> states,
        IReadOnlyList<uint> timestamps,
        IReadOnlyList<ulong> sourceFrames,
        int normalState)
    {
        var count = Math.Min(states.Count, Math.Min(timestamps.Count, sourceFrames.Count));
        var edges = new List<ComtradeDisturbanceDigitalEdge>();
        for (var i = 1; i < count; i++)
        {
            var before = states[i - 1] == 0 ? (byte)0 : (byte)1;
            var after = states[i] == 0 ? (byte)0 : (byte)1;
            if (before == after) continue;
            edges.Add(new ComtradeDisturbanceDigitalEdge(timestamps[i], sourceFrames[i], before, after, normalState));
        }
        return edges;
    }

    private static IReadOnlyList<ComtradeDisturbanceDigitalEdge> BuildReducedDigitalEdges(
        ComtradeDigitalTransitionSet transitionSet,
        int normalState)
    {
        if (transitionSet.Transitions.Count <= 1)
            return Array.Empty<ComtradeDisturbanceDigitalEdge>();
        return transitionSet.Transitions
            .Skip(1)
            .Select(transition =>
            {
                var after = transition.State == 0 ? (byte)0 : (byte)1;
                var before = after == 0 ? (byte)1 : (byte)0;
                return new ComtradeDisturbanceDigitalEdge(
                    transition.Timestamp,
                    transition.Frame,
                    before,
                    after,
                    normalState);
            })
            .ToArray();
    }

    private IReadOnlyList<ComtradeDigitalEventRow> BuildDigitalEventRows(
        IReadOnlyList<LoadedDisturbanceTrack> tracks,
        double? triggerMilliseconds)
    {
        var events = new List<(double Time, ulong SourceFrame, string Signal, string Event, string State)>();
        foreach (var loaded in tracks.Where(item => item.Track.IsDigital))
        {
            var track = loaded.Track;
            if (track.DigitalEdges is not { Count: > 0 }) continue;
            foreach (var edge in track.DigitalEdges)
            {
                var time = ComtradeTimeMath.ToMilliseconds(edge.Timestamp, _record.Info.TimeMultiplier);
                var active = (edge.AfterState != 0 ? 1 : 0) != edge.NormalState;
                events.Add((
                    time,
                    edge.SourceFrame,
                    track.Title,
                    ComtradeDisturbanceTimelineMath.DescribeDigitalEvent(track.Title, active),
                    $"{edge.BeforeState}→{edge.AfterState} • {(active ? "active" : "normal")}"));
            }
        }

        var ordered = events.OrderBy(item => item.Time).ThenBy(item => item.Signal, StringComparer.OrdinalIgnoreCase).ToArray();
        var rows = new List<ComtradeDigitalEventRow>(ordered.Length);
        double? previous = null;
        foreach (var item in ordered)
        {
            var relative = item.Time - (triggerMilliseconds ?? 0.0);
            var delta = previous is { } previousTime ? item.Time - previousTime : (double?)null;
            rows.Add(new ComtradeDigitalEventRow(
                item.Time,
                item.SourceFrame,
                ComtradeDisturbanceTimelineMath.FormatRelativeTime(relative),
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
        DisturbanceView.SetCursorFromHost(ComtradeDisturbanceCursor.Cursor1, row.AbsoluteMilliseconds);
        QueueP1D5CursorMeasurements();
        StatusTextBlock.Text = $"C1 moved to {row.Signal} • {row.Event} • {row.TimeText}.";
    }

    private async void DisturbanceReset_Click(object sender, RoutedEventArgs e)
    {
        var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
        if (_disturbanceRequestedViewport != full || _disturbanceLoadedViewport != full)
        {
            await ReloadDisturbanceAsync(full, initialLoad: false).ConfigureAwait(true);
            if (_record.Info.FrameCount > ExactSignalFrameLimit &&
                TryBuildSourceViewportForTimeWindow(
                    DisturbanceView.ViewStartMilliseconds,
                    DisturbanceView.ViewEndMilliseconds,
                    out var triggerViewport) &&
                triggerViewport.FrameCount > 0 && triggerViewport != full)
            {
                await ReloadDisturbanceAsync(triggerViewport, initialLoad: false).ConfigureAwait(true);
            }
        }
        DisturbanceView.ApplyTriggerFocusedDefault(_record.Info.NominalFrequency);
        _disturbanceInitialFocusApplied = true;
        QueueP1D5CursorMeasurements();
    }

    private async void DisturbanceFullRecord_Click(object sender, RoutedEventArgs e)
    {
        var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
        if (_disturbanceRequestedViewport != full || _disturbanceLoadedViewport != full)
            await ReloadDisturbanceAsync(full, initialLoad: false).ConfigureAwait(true);
        DisturbanceView.ResetNavigation();
    }

    private void DisturbanceView_NavigationChanged(object? sender, ComtradeDisturbanceNavigationChangedEventArgs e)
    {
        if (_analysisMode != AnalysisMode.Waveform) return;
        NavigationTextBlock.Text = e.Summary + "  |  wheel scrolls tracks • Ctrl+wheel zooms • drag pans";
        UpdateAnalysisAvailability();
    }

    private async void DisturbanceView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
            return;
        if (_record.Info.FrameCount <= ExactSignalFrameLimit || _disturbanceLoadedViewport.FrameCount == 0)
            return;

        var current = _disturbanceRequestedViewport.FrameCount > 0 ? _disturbanceRequestedViewport : _disturbanceLoadedViewport;
        var plotFraction = DisturbanceView.PlotFractionAt(e.GetPosition(DisturbanceView).X);
        var visibleSpan = DisturbanceView.ViewEndMilliseconds - DisturbanceView.ViewStartMilliseconds;
        var anchorMilliseconds = DisturbanceView.ViewStartMilliseconds + visibleSpan * plotFraction;
        var sourceFraction = plotFraction;
        if (TryResolveDisturbanceFrameAtMilliseconds(anchorMilliseconds, out var anchorFrame) && current.FrameCount > 1 &&
            anchorFrame >= current.StartFrame && anchorFrame < current.EndExclusive)
        {
            sourceFraction = (anchorFrame - current.StartFrame) / (double)(current.FrameCount - 1);
        }

        var target = ComtradeAbsoluteViewportMath.Zoom(
            current,
            _record.Info.FrameCount,
            Math.Clamp(sourceFraction, 0.0, 1.0),
            e.Delta > 0 ? 0.60 : 1.60,
            minimumFrames: 32);
        e.Handled = true;
        if (target == current) return;
        _disturbanceRequestedViewport = target;
        await ReloadDisturbanceAsync(target, initialLoad: false).ConfigureAwait(true);
    }

    private async void DisturbanceView_PanRequested(object? sender, ComtradeDisturbancePanRequestedEventArgs e)
    {
        if (_record.Info.FrameCount <= ExactSignalFrameLimit || _disturbanceLoadedViewport.FrameCount == 0)
            return;
        const double epsilon = 1e-6;
        if (DisturbanceView.ViewStartMilliseconds > DisturbanceView.FullStartMilliseconds + epsilon &&
            DisturbanceView.ViewEndMilliseconds < DisturbanceView.FullEndMilliseconds - epsilon)
            return;

        var current = _disturbanceRequestedViewport.FrameCount > 0 ? _disturbanceRequestedViewport : _disturbanceLoadedViewport;
        var delta = ToSignedDelta(current.FrameCount, e.DeltaFraction);
        var target = ComtradeAbsoluteViewportMath.Pan(current, _record.Info.FrameCount, delta);
        if (target == current) return;
        _disturbanceRequestedViewport = target;
        await ReloadDisturbanceAsync(target, initialLoad: false).ConfigureAwait(true);
    }

    private async void DisturbanceView_CursorChanged(object? sender, ComtradeDisturbanceCursorChangedEventArgs e)
    {
        if (!e.IsFinal || e.SnapToleranceMilliseconds <= 0 ||
            !_record.Supports(ArdIrecNativeBridge.CapDigitalEdgeSnap) ||
            !TryResolveDisturbanceFrameAtMilliseconds(e.AbsoluteMilliseconds, out var sourceFrame))
        {
            if (e.IsFinal) QueueP1D5CursorMeasurements();
            return;
        }

        _disturbanceCursorSnapCts?.Cancel();
        _disturbanceCursorSnapCts?.Dispose();
        _disturbanceCursorSnapCts = new CancellationTokenSource();
        var token = _disturbanceCursorSnapCts.Token;
        try
        {
            await _nativeGate.WaitAsync(token).ConfigureAwait(false);
            ComtradeStatusEdge? edge;
            try
            {
                token.ThrowIfCancellationRequested();
                var toleranceSeconds = e.SnapToleranceMilliseconds / 1000.0;
                edge = await Task.Run(() =>
                {
                    _record.TryFindNearestStatusEdge(sourceFrame, toleranceSeconds, out var nativeEdge);
                    return nativeEdge;
                }, token).ConfigureAwait(false);
            }
            finally
            {
                _nativeGate.Release();
            }

            if (token.IsCancellationRequested || edge is not { Valid: true })
            {
                await Dispatcher.InvokeAsync(QueueP1D5CursorMeasurements);
                return;
            }
            var snappedMilliseconds = ComtradeTimeMath.ToMilliseconds(edge.RawTimestamp, _record.Info.TimeMultiplier);
            await Dispatcher.InvokeAsync(() =>
            {
                DisturbanceView.SetCursorFromHost(e.Cursor, snappedMilliseconds);
                QueueP1D5CursorMeasurements();
                var signal = edge.ChannelIndex < _record.StatusChannels.Count
                    ? _record.StatusChannels[checked((int)edge.ChannelIndex)].Id
                    : $"digital {edge.ChannelIndex + 1}";
                StatusTextBlock.Text = $"{(e.Cursor == ComtradeDisturbanceCursor.Cursor1 ? "C1" : "C2")} snapped to {signal} • " +
                                       $"{edge.BeforeState}→{edge.AfterState} • {(edge.BecameActive ? "active" : "normal")}.";
            });
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private ComtradeSourceViewport CurrentDisturbanceViewport()
        => _disturbanceRequestedViewport.FrameCount > 0
            ? _disturbanceRequestedViewport
            : ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);

    private double? ResolveTriggerMilliseconds()
        => ComtradeTimeMath.TryGetTriggerOffsetMilliseconds(_record.Info.StartTime, _record.Info.TriggerTime, out var trigger)
            ? trigger
            : null;

    private bool TryResolveDisturbanceCursorFrame(out ulong frame)
    {
        frame = 0;
        return DisturbanceView.Cursor1Milliseconds is { } cursor &&
               TryResolveDisturbanceFrameAtMilliseconds(cursor, out frame);
    }

    private bool TryResolveDisturbanceFrameAtMilliseconds(double milliseconds, out ulong frame)
    {
        frame = 0;
        if (!double.IsFinite(milliseconds) || _record.Info.FrameCount == 0 ||
            _disturbanceReferenceTimestamps is not { Length: > 0 } timestamps ||
            _disturbanceReferenceSourceFrames is not { Length: > 0 } sourceFrames)
            return false;

        var count = Math.Min(timestamps.Length, sourceFrames.Length);
        if (count <= 0) return false;
        var targetRaw = milliseconds * 1000.0 / Math.Max(1e-12, _record.Info.TimeMultiplier);
        var index = ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, count, targetRaw);
        if (index < 0 || index >= count) return false;
        frame = Math.Min(_record.Info.FrameCount - 1, sourceFrames[index]);
        return true;
    }

    private bool TryBuildSourceViewportForTimeWindow(
        double startMilliseconds,
        double endMilliseconds,
        out ComtradeSourceViewport viewport)
    {
        viewport = default;
        if (!double.IsFinite(startMilliseconds) || !double.IsFinite(endMilliseconds) || endMilliseconds <= startMilliseconds ||
            _disturbanceReferenceTimestamps is not { Length: > 1 } timestamps ||
            _disturbanceReferenceSourceFrames is not { Length: > 1 } sourceFrames)
            return false;

        var count = Math.Min(timestamps.Length, sourceFrames.Length);
        var targetStartRaw = startMilliseconds * 1000.0 / Math.Max(1e-12, _record.Info.TimeMultiplier);
        var targetEndRaw = endMilliseconds * 1000.0 / Math.Max(1e-12, _record.Info.TimeMultiplier);
        var startIndex = ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, count, targetStartRaw);
        var endIndex = ComtradeDisturbanceTimelineMath.NearestTimestampIndex(timestamps, count, targetEndRaw);
        if (startIndex < 0 || endIndex < 0) return false;

        var lowIndex = Math.Max(0, Math.Min(startIndex, endIndex) - 2);
        var highIndex = Math.Min(count - 1, Math.Max(startIndex, endIndex) + 2);
        var startFrame = Math.Min(sourceFrames[lowIndex], sourceFrames[highIndex]);
        var endFrame = Math.Max(sourceFrames[lowIndex], sourceFrames[highIndex]);
        var frameCount = endFrame >= startFrame ? endFrame - startFrame + 1 : 0;
        if (frameCount == 0) return false;

        const ulong minimumFrames = 32;
        if (frameCount < minimumFrames)
        {
            var center = startFrame + frameCount / 2;
            var half = minimumFrames / 2;
            startFrame = center > half ? center - half : 0;
            frameCount = minimumFrames;
        }

        viewport = ComtradeAbsoluteViewportMath.Normalize(
            new ComtradeSourceViewport(startFrame, frameCount),
            _record.Info.FrameCount);
        return viewport.FrameCount > 0;
    }

    private bool TryResolveDisturbanceViewportCenterFrame(out ulong frame)
    {
        var center = DisturbanceView.ViewStartMilliseconds +
                     (DisturbanceView.ViewEndMilliseconds - DisturbanceView.ViewStartMilliseconds) * 0.5;
        return TryResolveDisturbanceFrameAtMilliseconds(center, out frame);
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

    private sealed record LoadedDisturbanceTrack(ComtradeSignalItem Signal, ComtradeDisturbanceTrack Track);

    private sealed record DisturbanceLoadResult(
        IReadOnlyList<LoadedDisturbanceTrack> Tracks,
        ComtradeSourceViewport SourceViewport,
        uint[] ReferenceTimestamps,
        ulong[] ReferenceSourceFrames);

    private sealed record ComtradeDigitalEventRow(
        double AbsoluteMilliseconds,
        ulong SourceFrame,
        string TimeText,
        string DeltaText,
        string Signal,
        string Event,
        string State);
}
