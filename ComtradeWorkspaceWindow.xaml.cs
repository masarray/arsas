using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow : Window
{
    private const int ExactSignalFrameLimit = 500_000;
    private const int FullRecordAnalogBuckets = 4_096;
    private const int FullRecordDigitalTransitionCap = 100_000;
    private const string NavigationHint = "Wheel zoom • Shift+wheel pan • Alt/middle-drag pan • Click Cursor A • Ctrl/right-click Cursor B";
    private readonly ArdIrecNativeRecord _record;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private CancellationTokenSource? _signalLoadCts;
    private ComtradeSignalItem? _activeSignal;
    private ComtradeSourceViewport _loadedSourceViewport;
    private bool _sourceNavigationEnabled;
    private bool _isReducedView;
    private bool _sourcePanGesture;
    private Point _sourcePanStartPoint;
    private ComtradeSourceViewport _sourcePanStartViewport;

    internal ComtradeWorkspaceWindow(ArdIrecNativeRecord record)
    {
        _record = record;
        InitializeComponent();
        WaveformView.NavigationChanged += WaveformView_NavigationChanged;
        WaveformView.PreviewMouseWheel += WaveformView_PreviewMouseWheel;
        WaveformView.PreviewMouseDown += WaveformView_PreviewMouseDown;
        WaveformView.PreviewMouseUp += WaveformView_PreviewMouseUp;
        ConfigureTriggerReference();
        ResetViewButton.IsEnabled = false;
        PopulateHeader();
        PopulateSignals();
        Closed += ComtradeWorkspaceWindow_Closed;
    }

    private async void ComtradeWorkspaceWindow_Closed(object? sender, EventArgs e)
    {
        _signalLoadCts?.Cancel();
        try
        {
            await _nativeGate.WaitAsync().ConfigureAwait(true);
            try
            {
                _record.Dispose();
            }
            finally
            {
                _nativeGate.Release();
            }
        }
        finally
        {
            WaveformView.NavigationChanged -= WaveformView_NavigationChanged;
            WaveformView.PreviewMouseWheel -= WaveformView_PreviewMouseWheel;
            WaveformView.PreviewMouseDown -= WaveformView_PreviewMouseDown;
            WaveformView.PreviewMouseUp -= WaveformView_PreviewMouseUp;
            _signalLoadCts?.Dispose();
            _signalLoadCts = null;
            _nativeGate.Dispose();
        }
    }

    private void ConfigureTriggerReference()
    {
        var info = _record.Info;
        if (ComtradeTimeMath.TryGetTriggerOffsetMilliseconds(info.StartTime, info.TriggerTime, out var triggerOffsetMilliseconds))
            WaveformView.SetTriggerOffsetMilliseconds(triggerOffsetMilliseconds);
        else
            WaveformView.SetTriggerOffsetMilliseconds(null);
    }

    private void PopulateHeader()
    {
        var info = _record.Info;
        var fileName = Path.GetFileNameWithoutExtension(_record.CfgPath);
        RecordIdentityText.Text = $"{fileName}  •  COMTRADE {info.RevisionYear}  •  {FormatDataFormat(info.DataFormat)}  •  {info.StartTime}";
        StationText.Text = string.IsNullOrWhiteSpace(info.RecorderId)
            ? info.StationName
            : $"{info.StationName}  •  {info.RecorderId}";
        ChannelsText.Text = $"{info.AnalogCount} analog  •  {info.StatusCount} digital";
        FrequencyText.Text = info.NominalFrequency > 0 ? $"{info.NominalFrequency:G5} Hz" : "—";
        FramesText.Text = $"{info.FrameCount:N0}";
        StatusTextBlock.Text = $"Loaded natively from {Path.GetFileName(_record.CfgPath)} • trigger {info.TriggerTime}";
    }

    private void PopulateSignals()
    {
        var analogAccent = new SolidColorBrush(Color.FromRgb(42, 120, 223));
        var digitalAccent = new SolidColorBrush(Color.FromRgb(31, 145, 94));
        analogAccent.Freeze();
        digitalAccent.Freeze();

        var signals = new List<ComtradeSignalItem>(_record.AnalogChannels.Count + _record.StatusChannels.Count);
        for (var i = 0; i < _record.AnalogChannels.Count; i++)
        {
            var channel = _record.AnalogChannels[i];
            var context = string.Join(" • ", new[] { channel.Phase, channel.Circuit, channel.Units }.Where(value => !string.IsNullOrWhiteSpace(value)));
            signals.Add(new ComtradeSignalItem(true, checked((uint)i), channel.Id, context, analogAccent));
        }

        for (var i = 0; i < _record.StatusChannels.Count; i++)
        {
            var channel = _record.StatusChannels[i];
            var context = string.Join(" • ", new[] { channel.Phase, channel.Circuit, $"normal {channel.NormalState}" }.Where(value => !string.IsNullOrWhiteSpace(value)));
            signals.Add(new ComtradeSignalItem(false, checked((uint)i), channel.Id, context, digitalAccent));
        }

        SignalList.ItemsSource = signals;
        SignalCountText.Text = $"{signals.Count} total";
        if (signals.Count > 0)
            SignalList.SelectedIndex = 0;
        else
        {
            NavigationTextBlock.Text = NavigationHint;
            ResetViewButton.IsEnabled = false;
            WaveformView.ShowMessage("No channels", "The COMTRADE record contains no analog or digital channels.");
        }
    }

    private async void SignalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignalList.SelectedItem is not ComtradeSignalItem signal)
            return;

        _activeSignal = signal;
        _sourceNavigationEnabled = _record.Info.FrameCount > ExactSignalFrameLimit;
        var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
        await LoadAndDisplaySignalAsync(signal, full, initialSelection: true).ConfigureAwait(true);
    }

    private async Task LoadAndDisplaySignalAsync(
        ComtradeSignalItem signal,
        ComtradeSourceViewport sourceViewport,
        bool initialSelection)
    {
        _signalLoadCts?.Cancel();
        _signalLoadCts?.Dispose();
        _signalLoadCts = new CancellationTokenSource();
        var token = _signalLoadCts.Token;

        _isReducedView = false;
        ResetViewButton.IsEnabled = false;
        NavigationTextBlock.Text = NavigationHint;
        StatusTextBlock.Text = initialSelection
            ? $"Loading {signal.Title} from native ArdIrec core…"
            : $"Refining {signal.Title} from native frames {sourceViewport.StartFrame:N0}…{SourceEndFrame(sourceViewport):N0}…";
        if (initialSelection)
            WaveformView.ShowMessage(signal.Title, "Loading signal samples…");

        try
        {
            var preview = await LoadSignalAsync(signal, sourceViewport, token).ConfigureAwait(true);
            if (token.IsCancellationRequested || !Equals(_activeSignal, signal))
                return;

            _loadedSourceViewport = preview.SourceViewport;
            _isReducedView = preview.IsReduced;
            if (preview.Analog is not null)
            {
                var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                WaveformView.ShowAnalog(
                    signal.Title,
                    BuildSignalSubtitle(metadata.Phase, metadata.Circuit, preview.DisplayMode),
                    metadata.Units,
                    preview.Analog,
                    preview.Timestamps,
                    _record.Info.TimeMultiplier,
                    preserveAllPoints: preview.IsReduced);
            }
            else if (preview.Status is not null)
            {
                var metadata = _record.StatusChannels[checked((int)signal.Index)];
                WaveformView.ShowStatus(
                    signal.Title,
                    BuildSignalSubtitle(metadata.Phase, metadata.Circuit, preview.DisplayMode),
                    preview.Status,
                    preview.Timestamps,
                    _record.Info.TimeMultiplier);
            }

            ResetViewButton.IsEnabled = preview.Timestamps.Length > 1;
            StatusTextBlock.Text = BuildLoadStatus(preview);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (Exception ex)
        {
            _isReducedView = false;
            ResetViewButton.IsEnabled = false;
            NavigationTextBlock.Text = NavigationHint;
            WaveformView.ShowMessage("Signal load failed", ex.Message);
            StatusTextBlock.Text = $"Native signal load failed: {ex.Message}";
        }
    }

    private async Task<SignalPreview> LoadSignalAsync(
        ComtradeSignalItem signal,
        ComtradeSourceViewport requestedViewport,
        CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            return await Task.Run(
                () => BuildSignalPreview(signal, requestedViewport, token),
                token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private SignalPreview BuildSignalPreview(
        ComtradeSignalItem signal,
        ComtradeSourceViewport requestedViewport,
        CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var totalFrameCount = _record.Info.FrameCount;
        var viewport = ComtradeAbsoluteViewportMath.Normalize(requestedViewport, totalFrameCount);
        if (viewport.FrameCount == 0)
        {
            return new SignalPreview(
                null, null, Array.Empty<uint>(), totalFrameCount, viewport,
                false, false, "empty range");
        }

        if (viewport.FrameCount <= ExactSignalFrameLimit)
        {
            var count = checked((int)viewport.FrameCount);
            var timestamps = _record.ReadRawTimestamps(viewport.StartFrame, count);
            var mode = viewport.FrameCount == totalFrameCount
                ? "full record • exact samples"
                : $"native detail • exact {viewport.FrameCount:N0} frames";
            if (signal.IsAnalog)
            {
                return new SignalPreview(
                    _record.ReadAnalog(signal.Index, viewport.StartFrame, count),
                    null,
                    timestamps,
                    totalFrameCount,
                    viewport,
                    false,
                    false,
                    mode);
            }

            return new SignalPreview(
                null,
                _record.ReadStatus(signal.Index, viewport.StartFrame, count),
                timestamps,
                totalFrameCount,
                viewport,
                false,
                false,
                mode);
        }

        var source = new ArdIrecRangeSource(_record);
        if (signal.IsAnalog)
        {
            var envelope = ComtradeRangeDecimator.BuildAnalogEnvelope(
                source,
                signal.Index,
                viewport.StartFrame,
                viewport.FrameCount,
                FullRecordAnalogBuckets,
                cancellationToken: token);
            var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);
            return new SignalPreview(
                series.Values,
                null,
                series.Timestamps,
                totalFrameCount,
                viewport,
                true,
                false,
                viewport.FrameCount == totalFrameCount
                    ? "full record • bounded min/max envelope"
                    : $"native detail • bounded envelope • {viewport.FrameCount:N0} source frames");
        }

        var transitions = ComtradeRangeDecimator.BuildDigitalTransitions(
            source,
            signal.Index,
            viewport.StartFrame,
            viewport.FrameCount,
            FullRecordDigitalTransitionCap,
            cancellationToken: token);
        var digitalSeries = ComtradeDecimatedSeriesBuilder.BuildDigital(transitions);
        return new SignalPreview(
            null,
            digitalSeries.States,
            digitalSeries.Timestamps,
            totalFrameCount,
            viewport,
            true,
            digitalSeries.IsTruncated,
            digitalSeries.IsTruncated
                ? "native range • adaptively sampled transitions"
                : "native range • exact transitions");
    }

    private static string BuildLoadStatus(SignalPreview preview)
    {
        var fullRange = preview.SourceViewport.StartFrame == 0 &&
                        preview.SourceViewport.FrameCount == preview.TotalSourceFrameCount;
        var rangeText = fullRange
            ? $"Full record {preview.TotalSourceFrameCount:N0} frames"
            : $"Native frames {preview.SourceViewport.StartFrame:N0}…{SourceEndFrame(preview.SourceViewport):N0} " +
              $"of {preview.TotalSourceFrameCount:N0}";

        if (!preview.IsReduced)
            return $"{rangeText} • exact native samples • {preview.Timestamps.Length:N0} plot points";

        var detail = preview.IsLossy
            ? "adaptive transition sampling active"
            : preview.Analog is not null
                ? "min/max envelope preserves bucket extrema"
                : "all digital transitions preserved";
        return $"{rangeText} scanned in bounded native chunks • {preview.Timestamps.Length:N0} plot points • {detail}";
    }

    private void WaveformView_NavigationChanged(object? sender, ComtradeNavigationChangedEventArgs e)
    {
        var suffix = _sourceNavigationEnabled
            ? _isReducedView
                ? "  |  wheel reloads higher-resolution native range"
                : _loadedSourceViewport.FrameCount < _record.Info.FrameCount
                    ? "  |  exact native detail range"
                    : string.Empty
            : _isReducedView
                ? "  |  reduced full-record overview"
                : string.Empty;
        NavigationTextBlock.Text = e.Summary + suffix;
    }

    private async void WaveformView_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (!_sourceNavigationEnabled || _activeSignal is null || _loadedSourceViewport.FrameCount == 0)
            return;

        var fraction = PlotFraction(e.GetPosition(WaveformView).X);
        ComtradeSourceViewport target;
        if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            var step = Math.Max(1UL, _loadedSourceViewport.FrameCount / 10);
            var signedStep = ToSignedStep(step, e.Delta > 0 ? -1 : 1);
            target = ComtradeAbsoluteViewportMath.Pan(
                _loadedSourceViewport,
                _record.Info.FrameCount,
                signedStep);
        }
        else
        {
            target = ComtradeAbsoluteViewportMath.Zoom(
                _loadedSourceViewport,
                _record.Info.FrameCount,
                fraction,
                e.Delta > 0 ? 0.60 : 1.60,
                minimumFrames: 32);
        }

        e.Handled = true;
        if (target == _loadedSourceViewport)
            return;

        await LoadAndDisplaySignalAsync(_activeSignal, target, initialSelection: false).ConfigureAwait(true);
    }

    private void WaveformView_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (!_sourceNavigationEnabled || _activeSignal is null || _loadedSourceViewport.FrameCount == 0)
            return;

        var panGesture = e.ChangedButton == MouseButton.Middle ||
                         (e.ChangedButton == MouseButton.Left && (Keyboard.Modifiers & ModifierKeys.Alt) != 0);
        if (!panGesture)
            return;

        _sourcePanGesture = true;
        _sourcePanStartPoint = e.GetPosition(WaveformView);
        _sourcePanStartViewport = _loadedSourceViewport;
        WaveformView.CaptureMouse();
        WaveformView.Cursor = Cursors.SizeWE;
        e.Handled = true;
    }

    private async void WaveformView_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_sourcePanGesture || _activeSignal is null)
            return;

        _sourcePanGesture = false;
        var endPoint = e.GetPosition(WaveformView);
        if (WaveformView.IsMouseCaptured)
            WaveformView.ReleaseMouseCapture();
        WaveformView.Cursor = Cursors.Cross;
        e.Handled = true;

        var plotWidth = Math.Max(1.0, WaveformView.ActualWidth - 82.0);
        var deltaFraction = -(endPoint.X - _sourcePanStartPoint.X) / plotWidth;
        var deltaFrames = ToSignedDelta(_sourcePanStartViewport.FrameCount, deltaFraction);
        var target = ComtradeAbsoluteViewportMath.Pan(
            _sourcePanStartViewport,
            _record.Info.FrameCount,
            deltaFrames);
        if (target == _loadedSourceViewport)
            return;

        await LoadAndDisplaySignalAsync(_activeSignal, target, initialSelection: false).ConfigureAwait(true);
    }

    private async void ResetView_Click(object sender, RoutedEventArgs e)
    {
        if (_sourceNavigationEnabled && _activeSignal is not null)
        {
            var full = ComtradeAbsoluteViewportMath.Full(_record.Info.FrameCount);
            if (_loadedSourceViewport != full)
            {
                await LoadAndDisplaySignalAsync(_activeSignal, full, initialSelection: false).ConfigureAwait(true);
                return;
            }
        }

        WaveformView.ResetNavigation();
    }

    private double PlotFraction(double x)
    {
        var plotWidth = Math.Max(1.0, WaveformView.ActualWidth - 82.0);
        return Math.Clamp((x - 62.0) / plotWidth, 0.0, 1.0);
    }

    private static long ToSignedStep(ulong magnitude, int direction)
    {
        var bounded = Math.Min(magnitude, checked((ulong)long.MaxValue));
        var value = checked((long)bounded);
        return direction < 0 ? -value : value;
    }

    private static long ToSignedDelta(ulong frameCount, double fraction)
    {
        if (!double.IsFinite(fraction) || fraction == 0 || frameCount == 0)
            return 0;
        var magnitude = Math.Abs(fraction) * frameCount;
        if (!double.IsFinite(magnitude) || magnitude >= long.MaxValue)
            return fraction < 0 ? long.MinValue + 1 : long.MaxValue;
        var rounded = checked((long)Math.Round(magnitude, MidpointRounding.AwayFromZero));
        return fraction < 0 ? -rounded : rounded;
    }

    private static ulong SourceEndFrame(ComtradeSourceViewport viewport)
        => viewport.FrameCount == 0 ? viewport.StartFrame : viewport.EndExclusive - 1;

    private async void FullAnalysis_Click(object sender, RoutedEventArgs e)
    {
        var cfgPath = _record.CfgPath;
        FullAnalysisButton.IsEnabled = false;
        var originalContent = FullAnalysisButton.Content;
        FullAnalysisButton.Content = "Opening…";
        StatusTextBlock.Text = "Opening the complete COMTRADE analysis workspace…";

        try
        {
            if (!ArdIrecViewerLauncher.TryLaunch(cfgPath, out var process, out var launchError) || process is null)
            {
                StatusTextBlock.Text = "Full analysis could not be started.";
                MessageBox.Show(
                    this,
                    launchError,
                    "COMTRADE full analysis",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            using (process)
            {
                var activated = false;
                for (var attempt = 0; attempt < 6; attempt++)
                {
                    await Task.Delay(attempt == 0 ? 350 : 220).ConfigureAwait(true);
                    process.Refresh();

                    if (process.HasExited)
                    {
                        var earlyExitError = ArdIrecViewerLauncher.DescribeEarlyExit(process, cfgPath);
                        StatusTextBlock.Text = "Full analysis closed during startup.";
                        MessageBox.Show(
                            this,
                            earlyExitError,
                            "COMTRADE full analysis startup failed",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                        return;
                    }

                    if (!activated)
                        activated = ArdIrecViewerLauncher.TryActivateViewerWindow(process);
                }
            }

            StatusTextBlock.Text = "Full COMTRADE analysis opened • native workspace remains available.";
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            StatusTextBlock.Text = "Full analysis startup failed.";
            MessageBox.Show(
                this,
                ex.Message,
                "COMTRADE full analysis startup failed",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            FullAnalysisButton.Content = originalContent;
            FullAnalysisButton.IsEnabled = true;
        }
    }

    private static string BuildSignalSubtitle(string phase, string circuit, string displayMode)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(phase)) parts.Add($"phase {phase}");
        if (!string.IsNullOrWhiteSpace(circuit)) parts.Add(circuit);
        parts.Add(displayMode);
        return string.Join(" • ", parts);
    }

    private static string FormatDataFormat(int format) => format switch
    {
        0 => "ASCII",
        1 => "BINARY",
        2 => "BINARY32",
        3 => "FLOAT32",
        _ => "UNKNOWN"
    };

    private sealed record ComtradeSignalItem(bool IsAnalog, uint Index, string Title, string Subtitle, Brush Accent);
    private sealed record SignalPreview(
        double[]? Analog,
        byte[]? Status,
        uint[] Timestamps,
        ulong TotalSourceFrameCount,
        ComtradeSourceViewport SourceViewport,
        bool IsReduced,
        bool IsLossy,
        string DisplayMode);
}
