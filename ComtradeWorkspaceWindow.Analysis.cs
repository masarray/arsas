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
    private ComtradeDisturbanceCursor _phasorReferenceCursor = ComtradeDisturbanceCursor.Cursor1;
    private CancellationTokenSource? _analysisLoadCts;
    private bool _analysisEventsAttached;

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
        _analysisLoadCts?.Cancel();
        _analysisLoadCts?.Dispose();
        _analysisLoadCts = null;
    }

    private void SignalList_AnalysisSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateAnalysisAvailability();

        // P1D.2D Phasor is a record-level Voltage + Current workstation and no longer depends on
        // the selected signal row. Harmonics remains a selected-analog-channel workflow until its
        // own parity slice lands.
        if (_analysisMode == AnalysisMode.Harmonics && _activeSignal is not { IsAnalog: true })
        {
            SetAnalysisMode(AnalysisMode.Waveform);
            return;
        }

        if (_analysisMode == AnalysisMode.Harmonics)
            _ = Dispatcher.InvokeAsync(async () => await RefreshNativeAnalysisAsync().ConfigureAwait(true));
    }

    private void WaveformMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Waveform);
    private void PhasorMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Phasor);
    private void HarmonicsMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Harmonics);
    private void PhasorCursor1_Click(object sender, RoutedEventArgs e) => SetPhasorReferenceCursor(ComtradeDisturbanceCursor.Cursor1);
    private void PhasorCursor2_Click(object sender, RoutedEventArgs e) => SetPhasorReferenceCursor(ComtradeDisturbanceCursor.Cursor2);

    private void SetPhasorReferenceCursor(ComtradeDisturbanceCursor cursor)
    {
        _phasorReferenceCursor = cursor;
        ApplyPhasorCursorVisuals();
        if (_analysisMode == AnalysisMode.Phasor)
            _ = RefreshNativeAnalysisAsync();
    }

    private void SetAnalysisMode(AnalysisMode mode)
    {
        if (mode == AnalysisMode.Phasor && _record.Info.AnalogCount == 0)
            mode = AnalysisMode.Waveform;
        if (mode == AnalysisMode.Harmonics && _activeSignal is not { IsAnalog: true })
            mode = AnalysisMode.Waveform;

        _analysisMode = mode;
        WaveformWorkspaceHost.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        WaveformView.Visibility = Visibility.Collapsed;
        PhasorView.Visibility = mode == AnalysisMode.Phasor ? Visibility.Visible : Visibility.Collapsed;
        HarmonicsView.Visibility = mode == AnalysisMode.Harmonics ? Visibility.Visible : Visibility.Collapsed;
        TimeNavigationPanel.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        PhasorReferencePanel.Visibility = mode == AnalysisMode.Phasor ? Visibility.Visible : Visibility.Collapsed;
        NavigationTextBlock.Text = mode switch
        {
            AnalysisMode.Phasor => "Dual Voltage / Current diagrams • choose C1 or C2 • native one-cycle DFT • common phase reference",
            AnalysisMode.Harmonics => "C1 = harmonic reference • full-cycle native DFT • click a bar for details",
            _ => "Wheel scrolls tracks • Ctrl+wheel zooms time • drag plot pans • drag C1/C2 measures • right-click places C2"
        };
        ApplyAnalysisModeVisuals();

        if (mode != AnalysisMode.Waveform)
            _ = RefreshNativeAnalysisAsync();
    }

    private void UpdateAnalysisAvailability()
    {
        var hasAnalogRecord = _record.Info.AnalogCount > 0;
        var selectedAnalog = _activeSignal is { IsAnalog: true };
        PhasorModeButton.IsEnabled = hasAnalogRecord;
        HarmonicsModeButton.IsEnabled = selectedAnalog;

        PhasorModeButton.ToolTip = hasAnalogRecord
            ? "Show record-level Voltage and Current fundamental RMS phasors at global C1 or C2."
            : "This COMTRADE record contains no analog channels.";
        HarmonicsModeButton.ToolTip = selectedAnalog
            ? "Show native ArdIrec harmonic spectrum at C1 (or the visible Time Signals center when C1 is unset)."
            : "Select an analog signal first. C1 on Time Signals becomes the harmonic reference.";

        PhasorCursor1Button.IsEnabled = DisturbanceView.Cursor1Milliseconds.HasValue;
        PhasorCursor2Button.IsEnabled = DisturbanceView.Cursor2Milliseconds.HasValue;
        ApplyPhasorCursorVisuals();
    }

    private void ApplyAnalysisModeVisuals()
    {
        ApplyModeButton(WaveformModeButton, _analysisMode == AnalysisMode.Waveform);
        ApplyModeButton(PhasorModeButton, _analysisMode == AnalysisMode.Phasor);
        ApplyModeButton(HarmonicsModeButton, _analysisMode == AnalysisMode.Harmonics);
        ApplyPhasorCursorVisuals();
    }

    private static void ApplyModeButton(Button button, bool selected)
    {
        button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(35, 86, 153) : Color.FromRgb(93, 111, 133));
        button.Background = new SolidColorBrush(selected ? Color.FromRgb(234, 243, 255) : Colors.White);
        button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(140, 177, 221) : Color.FromRgb(203, 216, 231));
        button.BorderThickness = new Thickness(1);
    }

    private void ApplyPhasorCursorVisuals()
    {
        ApplyCursorButton(
            PhasorCursor1Button,
            _phasorReferenceCursor == ComtradeDisturbanceCursor.Cursor1,
            Color.FromRgb(221, 142, 32),
            Color.FromRgb(255, 247, 232));
        ApplyCursorButton(
            PhasorCursor2Button,
            _phasorReferenceCursor == ComtradeDisturbanceCursor.Cursor2,
            Color.FromRgb(36, 172, 211),
            Color.FromRgb(235, 249, 253));
    }

    private static void ApplyCursorButton(Button button, bool selected, Color accent, Color selectedBackground)
    {
        button.Foreground = new SolidColorBrush(selected ? accent : Color.FromRgb(94, 111, 132));
        button.Background = new SolidColorBrush(selected ? selectedBackground : Colors.White);
        button.BorderBrush = new SolidColorBrush(selected ? accent : Color.FromRgb(203, 216, 231));
        button.BorderThickness = new Thickness(1);
    }

    private async Task RefreshNativeAnalysisAsync()
    {
        if (_analysisMode == AnalysisMode.Waveform)
            return;
        if (_analysisMode == AnalysisMode.Harmonics && _activeSignal is not { IsAnalog: true })
            return;

        _analysisLoadCts?.Cancel();
        _analysisLoadCts?.Dispose();
        _analysisLoadCts = new CancellationTokenSource();
        var token = _analysisLoadCts.Token;
        var preferredCursor = _analysisMode == AnalysisMode.Phasor
            ? _phasorReferenceCursor
            : ComtradeDisturbanceCursor.Cursor1;
        var cursorReference = TryResolveAnalysisCursorFrame(preferredCursor, out var cursorFrame);
        var referenceFrame = cursorReference ? cursorFrame : ResolveAnalysisReferenceFrame(preferredCursor);
        var timeMs = await TryReadReferenceTimeMillisecondsAsync(referenceFrame, token).ConfigureAwait(true);
        if (token.IsCancellationRequested) return;
        var triggerMs = ResolveTriggerMilliseconds();
        var relative = timeMs is { } absolute && triggerMs is { } trigger ? absolute - trigger : (double?)null;
        var referenceName = cursorReference ? CursorName(preferredCursor) : "visible center";
        var referenceTimeText = relative is { } relativeMs
            ? ComtradeDisturbanceTimelineMath.FormatRelativeTime(relativeMs)
            : timeMs is { } absoluteMs
                ? $"{absoluteMs:G7} ms"
                : "time unavailable";
        AnalysisReferenceTextBlock.Text = $"Analysis reference: {referenceName} • frame {referenceFrame:N0} • {referenceTimeText}";

        try
        {
            if (_analysisMode == AnalysisMode.Phasor)
            {
                PhasorView.ShowMessage("Phasor", "Calculating native Voltage and Current one-cycle phasors…");
                var result = await LoadPhasorWorkspaceAsync(referenceFrame, token).ConfigureAwait(true);
                if (token.IsCancellationRequested || _analysisMode != AnalysisMode.Phasor) return;
                if (result.VoltageVectors.Count == 0 && result.CurrentVectors.Count == 0)
                {
                    PhasorView.ShowMessage("Phasor", "The selected reference does not contain a complete analyzable Voltage or Current cycle.");
                    StatusTextBlock.Text = $"Native ArdIrec phasor analysis • {referenceName} • no valid Voltage/Current vectors.";
                    return;
                }

                var detail = $"{referenceTimeText} • frame {referenceFrame:N0} • full-cycle DFT • RMS magnitude • common phase reference";
                PhasorView.ShowPhasors(
                    referenceName.ToUpperInvariant(),
                    detail,
                    result.VoltageVectors,
                    result.CurrentVectors);
                StatusTextBlock.Text = $"Native ArdIrec phasor workstation • {referenceName} • frame {referenceFrame:N0} • " +
                                       $"{result.VoltageVectors.Count} voltage + {result.CurrentVectors.Count} current vector(s)";
                return;
            }

            var signal = _activeSignal!;
            HarmonicsView.ShowMessage("Harmonics", "Calculating native harmonic spectrum…");
            var spectrum = await LoadHarmonicsAsync(signal, referenceFrame, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || _analysisMode != AnalysisMode.Harmonics) return;
            if (!spectrum.Valid || spectrum.Bins.Count == 0)
            {
                HarmonicsView.ShowMessage("Harmonics", "The selected reference does not contain a valid full-cycle harmonic window.");
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
            var harmonicSubtitle = BuildAnalysisSubtitle(metadata, referenceFrame, spectrum.Bins.Count,
                $"orders H1…H{spectrum.Bins[^1].Order}");
            HarmonicsView.ShowSpectrum("Harmonic spectrum", harmonicSubtitle, display);
            StatusTextBlock.Text = $"Native ArdIrec harmonics • {referenceName} • THD {spectrum.ThdPercent:G5}% • " +
                                   (spectrum.DominantOrder > 1
                                       ? $"dominant H{spectrum.DominantOrder} {spectrum.DominantPercent:G4}%"
                                       : "no meaningful distortion harmonic");
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            if (_analysisMode == AnalysisMode.Phasor)
                PhasorView.ShowMessage("Phasor analysis failed", ex.Message);
            else if (_analysisMode == AnalysisMode.Harmonics)
                HarmonicsView.ShowMessage("Harmonic analysis failed", ex.Message);
            StatusTextBlock.Text = $"Native COMTRADE analysis failed: {ex.Message}";
        }
    }

    private bool TryResolveAnalysisCursorFrame(ComtradeDisturbanceCursor cursor, out ulong frame)
    {
        frame = 0;
        var milliseconds = cursor == ComtradeDisturbanceCursor.Cursor1
            ? DisturbanceView.Cursor1Milliseconds
            : DisturbanceView.Cursor2Milliseconds;
        return milliseconds is { } value && TryResolveDisturbanceFrameAtMilliseconds(value, out frame);
    }

    private ulong ResolveAnalysisReferenceFrame(ComtradeDisturbanceCursor preferredCursor)
    {
        if (TryResolveAnalysisCursorFrame(preferredCursor, out var cursorFrame))
            return cursorFrame;
        if (TryResolveDisturbanceViewportCenterFrame(out var centerFrame))
            return centerFrame;

        var total = _record.Info.FrameCount;
        if (total == 0) return 0;
        return (total - 1) / 2;
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
                        checked((uint)index),
                        role,
                        phaseRole,
                        channel.Id,
                        ComtradePhasorWorkspaceMath.CanonicalPhaseName(phaseRole, fallbackPhase),
                        channel.Circuit,
                        channel.Units));
                }

                var voltageChannels = ComtradePhasorWorkspaceMath.SelectRoleSet(
                    descriptors,
                    ComtradePhasorWorkspaceMath.RoleVoltage);
                var currentChannels = ComtradePhasorWorkspaceMath.SelectRoleSet(
                    descriptors,
                    ComtradePhasorWorkspaceMath.RoleCurrent);
                var voltageVectors = ReadPhasorVectors(voltageChannels, referenceFrame, token);
                var currentVectors = ReadPhasorVectors(currentChannels, referenceFrame, token);
                return new ComtradePhasorWorkspaceResult(voltageVectors, currentVectors);
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

    private static string CursorName(ComtradeDisturbanceCursor cursor)
        => cursor == ComtradeDisturbanceCursor.Cursor1 ? "C1" : "C2";

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

    private sealed record ComtradePhasorWorkspaceResult(
        IReadOnlyList<ComtradePhasorVector> VoltageVectors,
        IReadOnlyList<ComtradePhasorVector> CurrentVectors);
}
