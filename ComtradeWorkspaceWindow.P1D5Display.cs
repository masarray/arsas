using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow
{
    private const int P1D5RepresentationSecondary = 1;
    private const int P1D5RepresentationPrimary = 2;

    private enum P1D5WaveformTraceMode
    {
        Instantaneous,
        Rms
    }

    private readonly Dictionary<uint, ComtradeAnalogSemantics?> _p1d5SemanticsCache = new();
    private P1D5WaveformTraceMode _p1d5WaveformTraceMode = P1D5WaveformTraceMode.Instantaneous;
    private int _p1d5ValueRepresentation = P1D5RepresentationSecondary;
    private bool _p1d5PresentationRefreshRunning;

    private bool P1D5IsRmsTrace => _p1d5WaveformTraceMode == P1D5WaveformTraceMode.Rms;
    private string P1D5RepresentationLabel => _p1d5ValueRepresentation == P1D5RepresentationPrimary ? "Primary" : "Secondary";

    private void P1D5Workspace_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        if (WindowState == WindowState.Normal)
        {
            Width = Math.Min(Math.Max(MinWidth, 1360.0), Math.Max(MinWidth, work.Width - 20.0));
            Height = Math.Min(Math.Max(MinHeight, 960.0), Math.Max(MinHeight, work.Height - 20.0));
        }

        InitializeP1D5LocusUi();
        RefreshP1D5PresentationButtons();
        DisturbanceView.SetAnalogRepresentationLabel(P1D5RepresentationLabel);
        QueueP1D5CursorMeasurements();
    }

    private async void InstantTrace_Click(object sender, RoutedEventArgs e)
        => await SetP1D5WaveformTraceModeAsync(P1D5WaveformTraceMode.Instantaneous).ConfigureAwait(true);

    private async void RmsTrace_Click(object sender, RoutedEventArgs e)
        => await SetP1D5WaveformTraceModeAsync(P1D5WaveformTraceMode.Rms).ConfigureAwait(true);

    private async void SecondaryValue_Click(object sender, RoutedEventArgs e)
        => await SetP1D5ValueRepresentationAsync(P1D5RepresentationSecondary).ConfigureAwait(true);

    private async void PrimaryValue_Click(object sender, RoutedEventArgs e)
        => await SetP1D5ValueRepresentationAsync(P1D5RepresentationPrimary).ConfigureAwait(true);

    private async Task SetP1D5WaveformTraceModeAsync(P1D5WaveformTraceMode mode)
    {
        if (_p1d5WaveformTraceMode == mode || _p1d5PresentationRefreshRunning)
            return;

        _p1d5WaveformTraceMode = mode;
        RefreshP1D5PresentationButtons();
        await RefreshP1D5PresentationAsync(reloadWaveform: true).ConfigureAwait(true);
    }

    private async Task SetP1D5ValueRepresentationAsync(int representation)
    {
        representation = representation == P1D5RepresentationPrimary
            ? P1D5RepresentationPrimary
            : P1D5RepresentationSecondary;
        if (_p1d5ValueRepresentation == representation || _p1d5PresentationRefreshRunning)
            return;

        _p1d5ValueRepresentation = representation;
        RefreshP1D5PresentationButtons();
        await RefreshP1D5RepresentationAsync().ConfigureAwait(true);
    }

    /// <summary>
    /// PRI/SEC is a positive per-channel engineering scale. Because every Time Signals lane is
    /// independently auto-ranged, multiplying all samples in one lane by that scale cannot change
    /// its normalized waveform geometry. Re-reading/rebuilding every source frame was therefore
    /// pure latency. Keep the retained waveform data/geometry, update only frame metadata and
    /// representation-dependent native readouts/analysis.
    /// </summary>
    private async Task RefreshP1D5RepresentationAsync()
    {
        if (_p1d5PresentationRefreshRunning)
            return;
        _p1d5PresentationRefreshRunning = true;
        try
        {
            InvalidateP1D5AnalysisPresentationCaches();
            DisturbanceView.SetAnalogRepresentationLabel(P1D5RepresentationLabel);

            if (_p1d5LocusActive)
            {
                await RefreshP1D5LocusStaticAsync(forceReopen: false).ConfigureAwait(true);
                QueueP1D5LocusCursorRefresh();
            }
            else
            {
                QueueP1D5CursorMeasurements();
                if (_analysisMode != AnalysisMode.Waveform)
                    QueueP1D4LiveAnalysisScrub(isFinal: true);
            }

            if (_analysisMode == AnalysisMode.Waveform && _disturbanceLoadedViewport.FrameCount > 0)
            {
                var traceMode = P1D5IsRmsTrace ? "RMS" : "instantaneous";
                StatusTextBlock.Text = $"Time Signals • {_disturbanceVisibleSignals.Count} tracks • {traceMode} • " +
                                       $"{P1D5RepresentationLabel} • {_disturbanceLoadedViewport.FrameCount:N0} source frames • retained waveform";
            }
        }
        finally
        {
            _p1d5PresentationRefreshRunning = false;
        }
    }

    private async Task RefreshP1D5PresentationAsync(bool reloadWaveform)
    {
        if (_p1d5PresentationRefreshRunning)
            return;
        _p1d5PresentationRefreshRunning = true;
        try
        {
            InvalidateP1D5AnalysisPresentationCaches();

            if (reloadWaveform && _disturbanceInitialized)
            {
                await ReloadDisturbanceAsync(
                    CurrentDisturbanceViewport(),
                    initialLoad: false,
                    preserveLocalView: true).ConfigureAwait(true);
                DisturbanceView.SetAnalogRepresentationLabel(P1D5RepresentationLabel);
            }

            if (_p1d5LocusActive)
            {
                await RefreshP1D5LocusStaticAsync(forceReopen: false).ConfigureAwait(true);
                QueueP1D5LocusCursorRefresh();
            }
            else
            {
                QueueP1D5CursorMeasurements();
                if (_analysisMode != AnalysisMode.Waveform)
                    QueueP1D4LiveAnalysisScrub(isFinal: true);
            }
        }
        finally
        {
            _p1d5PresentationRefreshRunning = false;
        }
    }

    private void InvalidateP1D5AnalysisPresentationCaches()
    {
        _p1d4PhasorFrameCache.Clear();
        _phasorFrameCache.Clear();
        _lastRenderedPhasorFrame = ulong.MaxValue;
        _p1d4LastRenderedHarmonicOverviewFrame = ulong.MaxValue;
        _p1d4LastRenderedHarmonicOverviewSignature = string.Empty;
    }

    private void RefreshP1D5PresentationButtons()
    {
        ApplyP1D5ToggleButton(InstantTraceButton, !P1D5IsRmsTrace);
        ApplyP1D5ToggleButton(RmsTraceButton, P1D5IsRmsTrace);
        ApplyP1D5ToggleButton(SecondaryValueButton, _p1d5ValueRepresentation == P1D5RepresentationSecondary);
        ApplyP1D5ToggleButton(PrimaryValueButton, _p1d5ValueRepresentation == P1D5RepresentationPrimary);

        var hasConvertibleAnalog = false;
        for (var index = 0; index < _record.AnalogChannels.Count; index++)
        {
            var semantics = P1D5AnalogSemantics(checked((uint)index));
            if (semantics is { HasValidTransformerRatio: true })
            {
                hasConvertibleAnalog = true;
                break;
            }
        }
        PrimaryValueButton.IsEnabled = hasConvertibleAnalog;
        PrimaryValueButton.ToolTip = hasConvertibleAnalog
            ? "Display analog values in primary engineering quantities using COMTRADE transformer ratios."
            : "No valid primary/secondary transformer ratio is declared by this COMTRADE record.";
    }

    private static void ApplyP1D5ToggleButton(Button button, bool selected)
    {
        button.Foreground = new SolidColorBrush(selected ? Color.FromRgb(35, 86, 153) : Color.FromRgb(93, 111, 133));
        button.Background = new SolidColorBrush(selected ? Color.FromRgb(234, 243, 255) : Colors.White);
        button.BorderBrush = new SolidColorBrush(selected ? Color.FromRgb(140, 177, 221) : Color.FromRgb(203, 216, 231));
        button.BorderThickness = new Thickness(1);
    }

    private ComtradeAnalogSemantics? P1D5AnalogSemantics(uint channelIndex)
    {
        if (_p1d5SemanticsCache.TryGetValue(channelIndex, out var cached))
            return cached;

        ComtradeAnalogSemantics? semantics = null;
        if (_record.Supports(ArdIrecNativeBridge.CapChannelSemantics))
            _record.TryReadAnalogSemantics(channelIndex, out semantics);
        _p1d5SemanticsCache[channelIndex] = semantics;
        return semantics;
    }

    private double P1D5DisplayScale(uint channelIndex)
    {
        var semantics = P1D5AnalogSemantics(channelIndex);
        if (semantics is not { HasValidTransformerRatio: true })
            return 1.0;

        var scale = _p1d5ValueRepresentation == P1D5RepresentationPrimary
            ? semantics.ScaleToPrimary
            : semantics.ScaleToSecondary;
        return double.IsFinite(scale) && Math.Abs(scale) > 1e-15 ? scale : 1.0;
    }

    private double[] P1D5ScaleInstantaneous(IReadOnlyList<double> values, uint channelIndex)
        => ComtradeRmsSeriesBuilder.ApplyScale(values, P1D5DisplayScale(channelIndex));
}
