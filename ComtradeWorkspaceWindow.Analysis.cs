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
    private CancellationTokenSource? _analysisLoadCts;
    private bool _analysisEventsAttached;

    protected override void OnContentRendered(EventArgs e)
    {
        base.OnContentRendered(e);
        if (_analysisEventsAttached) return;
        _analysisEventsAttached = true;
        SignalList.SelectionChanged += SignalList_AnalysisSelectionChanged;
        Closed += AnalysisWindow_Closed;
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
        if (_activeSignal is { IsAnalog: false } && _analysisMode != AnalysisMode.Waveform)
        {
            SetAnalysisMode(AnalysisMode.Waveform);
            return;
        }

        if (_analysisMode != AnalysisMode.Waveform)
            _ = Dispatcher.InvokeAsync(async () => await RefreshNativeAnalysisAsync().ConfigureAwait(true));
    }

    private void WaveformMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Waveform);
    private void PhasorMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Phasor);
    private void HarmonicsMode_Click(object sender, RoutedEventArgs e) => SetAnalysisMode(AnalysisMode.Harmonics);

    private void SetAnalysisMode(AnalysisMode mode)
    {
        if (mode != AnalysisMode.Waveform && _activeSignal is not { IsAnalog: true })
            mode = AnalysisMode.Waveform;

        _analysisMode = mode;
        WaveformView.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        PhasorView.Visibility = mode == AnalysisMode.Phasor ? Visibility.Visible : Visibility.Collapsed;
        HarmonicsView.Visibility = mode == AnalysisMode.Harmonics ? Visibility.Visible : Visibility.Collapsed;
        ResetViewButton.Visibility = mode == AnalysisMode.Waveform ? Visibility.Visible : Visibility.Collapsed;
        NavigationTextBlock.Text = mode switch
        {
            AnalysisMode.Phasor => "Fundamental RMS phasors • native one-cycle DFT • cosine-referenced phase",
            AnalysisMode.Harmonics => "Full-cycle native DFT • magnitude shown as % of fundamental • click a bar for details",
            _ => NavigationHint
        };
        ApplyAnalysisModeVisuals();

        if (mode != AnalysisMode.Waveform)
            _ = RefreshNativeAnalysisAsync();
    }

    private void UpdateAnalysisAvailability()
    {
        var analog = _activeSignal is { IsAnalog: true };
        PhasorModeButton.IsEnabled = analog;
        HarmonicsModeButton.IsEnabled = analog;
        if (!analog)
        {
            PhasorModeButton.ToolTip = "Phasor analysis requires an analog channel.";
            HarmonicsModeButton.ToolTip = "Harmonic analysis requires an analog channel.";
        }
        else
        {
            PhasorModeButton.ToolTip = "Show same-unit fundamental RMS phasors at the current analysis reference.";
            HarmonicsModeButton.ToolTip = "Show native ArdIrec harmonic spectrum for the selected analog channel.";
        }
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

    private async Task RefreshNativeAnalysisAsync()
    {
        if (_analysisMode == AnalysisMode.Waveform || _activeSignal is not { IsAnalog: true } signal)
            return;

        _analysisLoadCts?.Cancel();
        _analysisLoadCts?.Dispose();
        _analysisLoadCts = new CancellationTokenSource();
        var token = _analysisLoadCts.Token;
        var referenceFrame = ResolveAnalysisReferenceFrame();
        var timeMs = await TryReadReferenceTimeMillisecondsAsync(referenceFrame, token).ConfigureAwait(true);
        if (token.IsCancellationRequested) return;
        AnalysisReferenceTextBlock.Text = timeMs is { } ms
            ? $"Analysis reference: frame {referenceFrame:N0} • {ms:G7} ms"
            : $"Analysis reference: frame {referenceFrame:N0}";

        try
        {
            if (_analysisMode == AnalysisMode.Phasor)
            {
                PhasorView.ShowMessage("Phasor", "Calculating native one-cycle phasors…");
                var result = await LoadPhasorGroupAsync(signal, referenceFrame, token).ConfigureAwait(true);
                if (token.IsCancellationRequested || _analysisMode != AnalysisMode.Phasor) return;
                if (result.Count == 0)
                {
                    PhasorView.ShowMessage("Phasor", "The selected reference does not contain a complete analyzable cycle.");
                    return;
                }

                var selectedMetadata = _record.AnalogChannels[checked((int)signal.Index)];
                var subtitle = BuildAnalysisSubtitle(selectedMetadata, referenceFrame, result.Count,
                    "fundamental RMS • same-unit channels");
                PhasorView.ShowPhasors("Phasor diagram", subtitle, result);
                StatusTextBlock.Text = $"Native ArdIrec phasor analysis • frame {referenceFrame:N0} • {result.Count} vector(s)";
                return;
            }

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
            StatusTextBlock.Text = $"Native ArdIrec harmonics • THD {spectrum.ThdPercent:G5}% • " +
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

    private ulong ResolveAnalysisReferenceFrame()
    {
        var total = _record.Info.FrameCount;
        if (total == 0) return 0;
        var viewport = _requestedSourceViewport.FrameCount > 0
            ? _requestedSourceViewport
            : _loadedSourceViewport.FrameCount > 0
                ? _loadedSourceViewport
                : ComtradeAbsoluteViewportMath.Full(total);
        viewport = ComtradeAbsoluteViewportMath.Normalize(viewport, total);
        if (viewport.FrameCount == 0) return Math.Min(total - 1, viewport.StartFrame);
        var offset = (viewport.FrameCount - 1) / 2;
        return Math.Min(total - 1, viewport.StartFrame + offset);
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

    private async Task<IReadOnlyList<ComtradePhasorVector>> LoadPhasorGroupAsync(
        ComtradeSignalItem signal,
        ulong referenceFrame,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var selected = _record.AnalogChannels[checked((int)signal.Index)];
                var sameUnits = _record.AnalogChannels
                    .Select((channel, index) => (channel, index))
                    .Where(item => string.Equals(item.channel.Units?.Trim(), selected.Units?.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var sameCircuit = sameUnits
                    .Where(item => !string.IsNullOrWhiteSpace(selected.Circuit) &&
                                   string.Equals(item.channel.Circuit?.Trim(), selected.Circuit?.Trim(), StringComparison.OrdinalIgnoreCase))
                    .ToArray();
                var candidates = sameCircuit.Length >= 2 ? sameCircuit : sameUnits;
                if (candidates.Length == 0)
                    candidates = new[] { (selected, checked((int)signal.Index)) };

                var vectors = new List<ComtradePhasorVector>();
                foreach (var item in candidates.Take(8))
                {
                    token.ThrowIfCancellationRequested();
                    var phasor = _record.ReadPhasor(checked((uint)item.index), referenceFrame);
                    if (!phasor.Valid) continue;
                    vectors.Add(new ComtradePhasorVector(
                        item.channel.Id,
                        NormalizePhase(item.channel.Phase, item.channel.Id),
                        item.channel.Units,
                        phasor.MagnitudeRms,
                        phasor.AngleDegrees));
                }
                return (IReadOnlyList<ComtradePhasorVector>)vectors;
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
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
}
