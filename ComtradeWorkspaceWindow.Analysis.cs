using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private enum AnalysisMode
    {
        Waveform,
        Phasor,
        Harmonics
    }

    private AnalysisMode _analysisMode = AnalysisMode.Waveform;
    private double? _phasorCursorMilliseconds;
    private CancellationTokenSource? _analysisLoadCts = new();
    private bool _analysisEventsAttached;

    // P1D.4 scrub scheduler: UI cursor movement is immediate, while native analysis is latest-wins.
    // At most one native job is in flight and intermediate pointer positions are coalesced at the
    // WPF composition cadence. This prevents the old cancel/spawn/flicker storm during scrubbing.
    private bool _analysisRenderingHooked;
    private bool _analysisScrubDirty;
    private bool _analysisWorkerRunning;
    private bool _analysisFinalRequested;
    private int _analysisGeneration;
    private ulong _lastRenderedPhasorFrame = ulong.MaxValue;
    private HarmonicCacheKey? _lastRenderedHarmonicKey;
    private readonly Dictionary<ulong, ComtradePhasorWorkspaceResult> _phasorFrameCache = new();
    private readonly Dictionary<HarmonicCacheKey, ComtradeHarmonicSpectrum> _harmonicFrameCache = new();

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_analysisEventsAttached) return;
        _analysisEventsAttached = true;
        SignalList.SelectionChanged += SignalList_AnalysisSelectionChanged;
        Closed += AnalysisWindow_Closed;
        InitializeDisturbanceWorkspace();
        ApplyAnalysisModeVisuals();
        UpdateAnalysisAvailability();
    }

    private void AnalysisWindow_Closed(object? sender, EventArgs e)
    {
        StopAnalysisRenderingPump();
        _analysisLoadCts?.Cancel();
        _analysisLoadCts?.Dispose();
        _analysisLoadCts = null;
    }

    private void SignalList_AnalysisSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAnalysisAvailability();
        if (_analysisMode == AnalysisMode.Harmonics && _activeSignal is not { IsAnalog: true })
        {
            SetAnalysisMode(AnalysisMode.Waveform);
            return;
        }

        if (_analysisMode == AnalysisMode.Harmonics)
        {
            ResetAnalysisContext();
            QueueRealtimeAnalysisScrub(isFinal: true);
        }
    }

    // Retained for XAML/code-driven compatibility. Visible mode buttons route through the shell.
    private void WaveformMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Waveform);
    private void PhasorMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Phasor);
    private void HarmonicsMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Harmonics);

    // Legacy P1D.2D buttons remain hidden in P1D.4. If invoked by automation, seed the single P
    // cursor from the requested former global cursor instead of restoring dual-phasor behavior.
    private void PhasorCursor1_Click(object sender, RoutedEventArgs e)
    {
        _phasorCursorMilliseconds = DisturbanceView.Cursor1Milliseconds ?? _phasorCursorMilliseconds;
        SyncInvestigationTimeline();
        QueueRealtimeAnalysisScrub(isFinal: true);
    }

    private void PhasorCursor2_Click(object sender, RoutedEventArgs e)
    {
        _phasorCursorMilliseconds = DisturbanceView.Cursor2Milliseconds ?? _phasorCursorMilliseconds;
        SyncInvestigationTimeline();
        QueueRealtimeAnalysisScrub(isFinal: true);
    }

    private void SetAnalysisMode(AnalysisMode mode)
    {
        if (mode == AnalysisMode.Phasor && _record.Info.AnalogCount == 0)
            mode = AnalysisMode.Waveform;
        if (mode == AnalysisMode.Harmonics && _activeSignal is not { IsAnalog: true })
            mode = AnalysisMode.Waveform;

        var changed = _analysisMode != mode;
        _analysisMode = mode;
        if (changed)
            ResetAnalysisContext();

        WaveformWorkspaceHost.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        WaveformView.Visibility = Visibility.Collapsed;
        PhasorView.Visibility = mode == AnalysisMode.Phasor ? Visibility.Visible : Visibility.Collapsed;
        HarmonicsView.Visibility = mode == AnalysisMode.Harmonics ? Visibility.Visible : Visibility.Collapsed;
        TimeNavigationPanel.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        PhasorReferencePanel.Visibility = Visibility.Collapsed;
        ApplyAnalysisModeVisuals();

        if (mode == AnalysisMode.Waveform)
        {
            StopAnalysisRenderingPump();
            return;
        }

        if (mode == AnalysisMode.Phasor)
            EnsurePhasorCursor();
        else
            EnsureHarmonicCursor();
        SyncInvestigationTimeline();

        if (mode == AnalysisMode.Phasor && _lastRenderedPhasorFrame == ulong.MaxValue)
            PhasorView.ShowMessage("Phasor", "Preparing native Voltage and Current phasors…");
        else if (mode == AnalysisMode.Harmonics && _lastRenderedHarmonicKey is null)
            HarmonicsView.ShowMessage("Harmonics", "Preparing native harmonic spectrum…");

        QueueRealtimeAnalysisScrub(isFinal: true);
    }

    private void UpdateAnalysisAvailability()
    {
        var hasAnalogRecord = _record.Info.AnalogCount > 0;
        var selectedAnalog = _activeSignal is { IsAnalog: true };
        PhasorModeButton.IsEnabled = hasAnalogRecord;
        HarmonicsModeButton.IsEnabled = selectedAnalog;
        PhasorModeButton.ToolTip = hasAnalogRecord
            ? "Record-level Voltage and Current phasors with one dedicated P cursor on the shared timebase."
            : "This COMTRADE record contains no analog channels.";
        HarmonicsModeButton.ToolTip = selectedAnalog
            ? "Native ArdIrec harmonic spectrum at the dedicated H cursor on the shared timebase."
            : "Select an analog signal first. Harmonics uses one dedicated H cursor.";
        PhasorCursor1Button.IsEnabled = false;
        PhasorCursor2Button.IsEnabled = false;
    }

    private void ApplyAnalysisModeVisuals()
    {
        ApplyModeButton(WaveformModeButton, _analysisMode == AnalysisMode.Waveform);
        ApplyModeButton(PhasorModeButton, _analysisMode == AnalysisMode.Phasor);
        ApplyModeButton(HarmonicsModeButton, _analysisMode == AnalysisMode.Harmonics);
    }

    private static void ApplyModeButton(Button button, bool selected)
    {
        button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(35, 86, 153) : Color.FromRgb(93, 111, 133));
        button.Background = new SolidColorBrush(selected ? Color.FromRgb(234, 243, 255) : Colors.White);
        button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(140, 177, 221) : Color.FromRgb(203, 216, 231));
        button.BorderThickness = new Thickness(1);
    }

    /// <summary>
    /// Called for every shell scrub update. The cursor itself has already moved synchronously;
    /// native calculation is coalesced to one request per composition frame and latest frame wins.
    /// </summary>
    private void QueueRealtimeAnalysisScrub(bool isFinal)
    {
        if (_analysisMode == AnalysisMode.Waveform) return;
        _analysisScrubDirty = true;
        _analysisFinalRequested |= isFinal;
        EnsureAnalysisRenderingPump();
    }

    private Task RefreshNativeAnalysisAsync()
    {
        QueueRealtimeAnalysisScrub(isFinal: true);
        return Task.CompletedTask;
    }

    private void EnsureAnalysisRenderingPump()
    {
        if (_analysisRenderingHooked) return;
        CompositionTarget.Rendering += AnalysisCompositionFrame;
        _analysisRenderingHooked = true;
    }

    private void StopAnalysisRenderingPump()
    {
        if (!_analysisRenderingHooked) return;
        CompositionTarget.Rendering -= AnalysisCompositionFrame;
        _analysisRenderingHooked = false;
    }

    private void AnalysisCompositionFrame(object? sender, EventArgs e)
    {
        if (_analysisMode == AnalysisMode.Waveform)
        {
            StopAnalysisRenderingPump();
            return;
        }
        if (_analysisWorkerRunning || !_analysisScrubDirty)
            return;

        _analysisScrubDirty = false;
        var isFinal = _analysisFinalRequested;
        _analysisFinalRequested = false;
        if (!TryCreateAnalysisRequest(isFinal, out var request))
        {
            if (!_analysisScrubDirty)
                StopAnalysisRenderingPump();
            return;
        }

        if (!request.IsFinal && IsAlreadyRendered(request))
        {
            if (!_analysisScrubDirty)
                StopAnalysisRenderingPump();
            return;
        }

        _analysisWorkerRunning = true;
        _ = ExecuteAnalysisRequestAsync(request);
    }

    private bool TryCreateAnalysisRequest(bool isFinal, out AnalysisScrubRequest request)
    {
        request = default;
        if (_record.Info.FrameCount == 0) return false;

        if (_analysisMode == AnalysisMode.Phasor)
        {
            EnsurePhasorCursor();
            var cursorMs = _phasorCursorMilliseconds;
            if (cursorMs is null || !TryResolveDisturbanceFrameAtMilliseconds(cursorMs.Value, out var frame))
            {
                if (!TryResolveDisturbanceViewportCenterFrame(out frame)) return false;
            }
            request = new AnalysisScrubRequest(AnalysisMode.Phasor, frame, null, _analysisGeneration, isFinal);
            return true;
        }

        if (_activeSignal is not { IsAnalog: true } signal) return false;
        EnsureHarmonicCursor();
        var harmonicMs = _harmonicCursorMilliseconds;
        if (harmonicMs is null || !TryResolveDisturbanceFrameAtMilliseconds(harmonicMs.Value, out var harmonicFrame))
        {
            if (!TryResolveDisturbanceViewportCenterFrame(out harmonicFrame)) return false;
        }
        request = new AnalysisScrubRequest(AnalysisMode.Harmonics, harmonicFrame, signal.Index, _analysisGeneration, isFinal);
        return true;
    }

    private bool IsAlreadyRendered(AnalysisScrubRequest request)
        => request.Mode == AnalysisMode.Phasor
            ? request.ReferenceFrame == _lastRenderedPhasorFrame
            : request.ChannelIndex is { } channel &&
              _lastRenderedHarmonicKey == new HarmonicCacheKey(channel, request.ReferenceFrame);

    private async Task ExecuteAnalysisRequestAsync(AnalysisScrubRequest request)
    {
        try
        {
            var token = _analysisLoadCts?.Token ?? CancellationToken.None;
            if (request.Mode == AnalysisMode.Phasor)
            {
                if (!_phasorFrameCache.TryGetValue(request.ReferenceFrame, out var phasor))
                {
                    phasor = await LoadPhasorWorkspaceAsync(request.ReferenceFrame, token).ConfigureAwait(true);
                    RememberPhasor(request.ReferenceFrame, phasor);
                }
                var timeMs = await TryReadReferenceTimeMillisecondsAsync(request.ReferenceFrame, token).ConfigureAwait(true);
                if (!IsRequestCurrent(request)) return;
                PresentPhasor(request.ReferenceFrame, timeMs, phasor);
            }
            else if (request.ChannelIndex is { } channel && _activeSignal is { IsAnalog: true } signal)
            {
                var key = new HarmonicCacheKey(channel, request.ReferenceFrame);
                if (!_harmonicFrameCache.TryGetValue(key, out var spectrum))
                {
                    spectrum = await LoadHarmonicsAsync(signal, request.ReferenceFrame, token).ConfigureAwait(true);
                    RememberHarmonic(key, spectrum);
                }
                var timeMs = await TryReadReferenceTimeMillisecondsAsync(request.ReferenceFrame, token).ConfigureAwait(true);
                if (!IsRequestCurrent(request)) return;
                PresentHarmonics(signal, request.ReferenceFrame, timeMs, spectrum);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (IsRequestCurrent(request))
            {
                if (request.Mode == AnalysisMode.Phasor && _lastRenderedPhasorFrame == ulong.MaxValue)
                    PhasorView.ShowMessage("Phasor analysis failed", ex.Message);
                else if (request.Mode == AnalysisMode.Harmonics && _lastRenderedHarmonicKey is null)
                    HarmonicsView.ShowMessage("Harmonic analysis failed", ex.Message);
                StatusTextBlock.Text = $"Native COMTRADE analysis failed: {ex.Message}";
            }
        }
        finally
        {
            _analysisWorkerRunning = false;
            if (_analysisScrubDirty)
                EnsureAnalysisRenderingPump();
            else
                StopAnalysisRenderingPump();
        }
    }

    private bool IsRequestCurrent(AnalysisScrubRequest request)
    {
        if (request.Generation != _analysisGeneration || request.Mode != _analysisMode)
            return false;
        return request.Mode != AnalysisMode.Harmonics ||
               (_activeSignal is { IsAnalog: true } signal && request.ChannelIndex == signal.Index);
    }

    private void PresentPhasor(ulong referenceFrame, double? timeMs, ComtradePhasorWorkspaceResult result)
    {
        var referenceTimeText = FormatAnalysisReferenceTime(timeMs);
        AnalysisReferenceTextBlock.Text = $"Analysis reference: P • frame {referenceFrame:N0} • {referenceTimeText}";
        if (result.VoltageVectors.Count == 0 && result.CurrentVectors.Count == 0)
        {
            if (_lastRenderedPhasorFrame == ulong.MaxValue)
                PhasorView.ShowMessage("Phasor", "P does not contain a complete analyzable Voltage or Current cycle.");
            StatusTextBlock.Text = "Native ArdIrec phasor analysis • P • no valid Voltage/Current vectors.";
            return;
        }

        PhasorView.ShowPhasors(
            "P",
            $"{referenceTimeText} • frame {referenceFrame:N0} • full-cycle DFT • RMS magnitude • common phase reference",
            result.VoltageVectors,
            result.CurrentVectors);
        _lastRenderedPhasorFrame = referenceFrame;
        StatusTextBlock.Text = $"Native ArdIrec phasor workstation • P • frame {referenceFrame:N0} • " +
                               $"{result.VoltageVectors.Count} voltage + {result.CurrentVectors.Count} current vector(s)";
    }

    private void PresentHarmonics(
        ComtradeSignalItem signal,
        ulong referenceFrame,
        double? timeMs,
        ComtradeHarmonicSpectrum spectrum)
    {
        var key = new HarmonicCacheKey(signal.Index, referenceFrame);
        var referenceTimeText = FormatAnalysisReferenceTime(timeMs);
        AnalysisReferenceTextBlock.Text = $"Analysis reference: H • frame {referenceFrame:N0} • {referenceTimeText}";
        if (!spectrum.Valid || spectrum.Bins.Count == 0)
        {
            if (_lastRenderedHarmonicKey is null)
                HarmonicsView.ShowMessage("Harmonics", "H does not contain a valid full-cycle harmonic window.");
            return;
        }

        var metadata = _record.AnalogChannels[checked((int)signal.Index)];
        var display = new ComtradeHarmonicDisplaySpectrum(
            metadata.Id,
            metadata.Units,
            spectrum.FundamentalRms,
            spectrum.ThdPercent,
            spectrum.DominantOrder,
            spectrum.DominantRms,
            spectrum.DominantPercent,
            spectrum.EstimatedSampleRateHz,
            spectrum.MaximumResolvableOrder,
            spectrum.Bins.Select(bin => new ComtradeHarmonicDisplayBin(
                bin.Order, bin.MagnitudeRms, bin.PercentOfFundamental, bin.AngleDegrees)).ToArray());
        HarmonicsView.ShowSpectrum(
            "Harmonic spectrum",
            BuildAnalysisSubtitle(metadata, referenceFrame, spectrum.Bins.Count, $"H cursor • orders H1…H{spectrum.Bins[^1].Order}"),
            display);
        _lastRenderedHarmonicKey = key;
        StatusTextBlock.Text = $"Native ArdIrec harmonics • H • {referenceTimeText} • THD {spectrum.ThdPercent:G5}% • " +
                               (spectrum.DominantOrder > 1
                                   ? $"dominant H{spectrum.DominantOrder} {spectrum.DominantPercent:G4}%"
                                   : "no meaningful distortion harmonic");
    }

    private string FormatAnalysisReferenceTime(double? timeMs)
    {
        var triggerMs = DisturbanceView.EffectiveTriggerMilliseconds ?? ResolveTriggerMilliseconds();
        if (timeMs is { } absolute && triggerMs is { } trigger)
            return ComtradeDisturbanceTimelineMath.FormatRelativeTime(absolute - trigger);
        return timeMs is { } value ? $"{value:G7} ms" : "time unavailable";
    }

    private void ResetAnalysisContext()
    {
        _analysisGeneration++;
        _analysisScrubDirty = false;
        _analysisFinalRequested = false;
        _analysisLoadCts?.Cancel();
        _analysisLoadCts?.Dispose();
        _analysisLoadCts = new CancellationTokenSource();
    }

    private void RememberPhasor(ulong frame, ComtradePhasorWorkspaceResult result)
    {
        if (_phasorFrameCache.Count >= 64) _phasorFrameCache.Clear();
        _phasorFrameCache[frame] = result;
    }

    private void RememberHarmonic(HarmonicCacheKey key, ComtradeHarmonicSpectrum result)
    {
        if (_harmonicFrameCache.Count >= 64) _harmonicFrameCache.Clear();
        _harmonicFrameCache[key] = result;
    }

    private async Task<double?> TryReadReferenceTimeMillisecondsAsync(ulong referenceFrame, CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            var timestamp = await Task.Run(() => _record.ReadRawTimestamps(referenceFrame, 1)[0], token).ConfigureAwait(false);
            return ComtradeTimeMath.ToMilliseconds(timestamp, _record.Info.TimeMultiplier);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private async Task<ComtradePhasorWorkspaceResult> LoadPhasorWorkspaceAsync(
        ulong referenceFrame,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var descriptors = new List<ComtradePhasorChannelDescriptor>(_record.AnalogChannels.Count);
                for (var index = 0; index < _record.AnalogChannels.Count; index++)
                {
                    token.ThrowIfCancellationRequested();
                    var channel = _record.AnalogChannels[index];
                    var hasSemantics = _record.TryReadAnalogSemantics(checked((uint)index), out var semantics) && semantics is not null;
                    var fallbackPhase = NormalizePhase(channel.Phase, channel.Id);
                    var role = hasSemantics
                        ? semantics!.Role
                        : ResolveAnalogSection(channel.Units, null) switch
                        {
                            "Voltage" => ComtradePhasorWorkspaceMath.RoleVoltage,
                            "Current" => ComtradePhasorWorkspaceMath.RoleCurrent,
                            _ => 0
                        };
                    var phaseRole = hasSemantics
                        ? semantics!.PhaseRole
                        : ComtradePhasorWorkspaceMath.PhaseRoleFromCanonicalName(fallbackPhase);
                    descriptors.Add(new ComtradePhasorChannelDescriptor(
                        checked((uint)index), role, phaseRole, channel.Id,
                        ComtradePhasorWorkspaceMath.CanonicalPhaseName(phaseRole, fallbackPhase),
                        channel.Circuit, channel.Units));
                }

                var voltageChannels = ComtradePhasorWorkspaceMath.SelectRoleSet(descriptors, ComtradePhasorWorkspaceMath.RoleVoltage);
                var currentChannels = ComtradePhasorWorkspaceMath.SelectRoleSet(descriptors, ComtradePhasorWorkspaceMath.RoleCurrent);
                return new ComtradePhasorWorkspaceResult(
                    ReadPhasorVectors(voltageChannels, referenceFrame, token),
                    ReadPhasorVectors(currentChannels, referenceFrame, token));
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private IReadOnlyList<ComtradePhasorVector> ReadPhasorVectors(
        IReadOnlyList<ComtradePhasorChannelDescriptor> channels,
        ulong referenceFrame,
        CancellationToken token)
    {
        var vectors = new List<ComtradePhasorVector>(channels.Count);
        foreach (var channel in channels)
        {
            token.ThrowIfCancellationRequested();
            var phasor = _record.ReadPhasor(channel.Index, referenceFrame);
            if (!phasor.Valid) continue;
            vectors.Add(new ComtradePhasorVector(
                channel.Label,
                ComtradePhasorWorkspaceMath.CanonicalPhaseName(channel.PhaseRole, channel.Phase),
                channel.Units,
                phasor.MagnitudeRms,
                phasor.AngleDegrees));
        }
        return vectors;
    }

    private async Task<ComtradeHarmonicSpectrum> LoadHarmonicsAsync(
        ComtradeSignalItem signal,
        ulong referenceFrame,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() => _record.ReadHarmonicSpectrum(signal.Index, referenceFrame, 25), token)
                .ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private static string BuildAnalysisSubtitle(
        ComtradeAnalogChannelInfo metadata,
        ulong referenceFrame,
        int itemCount,
        string suffix)
    {
        var context = string.Join(" • ", new[] { metadata.Id, metadata.Phase, metadata.Circuit, metadata.Units }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
        return $"{context} • reference frame {referenceFrame:N0} • {itemCount} {suffix}";
    }

    private static string NormalizePhase(string phase, string id)
    {
        var direct = (phase ?? string.Empty).Trim().ToUpperInvariant();
        if (direct is "A" or "L1") return "L1";
        if (direct is "B" or "L2") return "L2";
        if (direct is "C" or "L3") return "L3";
        if (direct is "N" or "E") return direct;

        var name = new string((id ?? string.Empty).ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray());
        if (name.Contains("L1") || name.EndsWith("AN") || name.EndsWith("IA") || name.EndsWith("VA") || name.EndsWith("UA")) return "L1";
        if (name.Contains("L2") || name.EndsWith("BN") || name.EndsWith("IB") || name.EndsWith("VB") || name.EndsWith("UB")) return "L2";
        if (name.Contains("L3") || name.EndsWith("CN") || name.EndsWith("IC") || name.EndsWith("VC") || name.EndsWith("UC")) return "L3";
        if (name.Contains("3I0") || name.Contains("3V0") || name.Contains("3U0") || name.Contains("RES") || name.Contains("NEUTRAL")) return "E";
        return "Other";
    }

    private readonly record struct AnalysisScrubRequest(
        AnalysisMode Mode,
        ulong ReferenceFrame,
        uint? ChannelIndex,
        int Generation,
        bool IsFinal);

    private readonly record struct HarmonicCacheKey(uint ChannelIndex, ulong ReferenceFrame);

    private sealed record ComtradePhasorWorkspaceResult(
        IReadOnlyList<ComtradePhasorVector> VoltageVectors,
        IReadOnlyList<ComtradePhasorVector> CurrentVectors);
}
