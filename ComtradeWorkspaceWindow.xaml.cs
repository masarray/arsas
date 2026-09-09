using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Controls;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow : Window
{
    private const int ExactSignalFrameLimit = 500_000;
    private const int FullRecordAnalogBuckets = 8_000;
    private const int FullRecordDigitalTransitionCap = 100_000;
    private const string NavigationHint = "Wheel zoom • Shift+wheel pan • Click Cursor A • Ctrl/right-click Cursor B";
    private readonly ArdIrecNativeRecord _record;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private CancellationTokenSource? _signalLoadCts;
    private bool _isReducedView;

    internal ComtradeWorkspaceWindow(ArdIrecNativeRecord record)
    {
        _record = record;
        InitializeComponent();
        WaveformView.NavigationChanged += WaveformView_NavigationChanged;
        ConfigureTriggerReference();
        ResetViewButton.IsEnabled = false;
        PopulateHeader();
        PopulateSignals();
        Closed += ComtradeWorkspaceWindow_Closed;
    }

    private async void ComtradeWorkspaceWindow_Closed(object? sender, EventArgs e)
    {
        // A selected-channel copy may still be executing on a worker thread. Cancel any queued
        // load, then wait for the native gate before freeing the opaque record handle.
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

        _signalLoadCts?.Cancel();
        _signalLoadCts?.Dispose();
        _signalLoadCts = new CancellationTokenSource();
        var token = _signalLoadCts.Token;

        _isReducedView = false;
        ResetViewButton.IsEnabled = false;
        NavigationTextBlock.Text = NavigationHint;
        StatusTextBlock.Text = $"Loading {signal.Title} from native ArdIrec core…";
        WaveformView.ShowMessage(signal.Title, "Loading signal samples…");

        try
        {
            var preview = await LoadSignalAsync(signal, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

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
                    _record.Info.TimeMultiplier);
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
            // Window shutdown can dispose the native lifetime after the load was cancelled.
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

    private async Task<SignalPreview> LoadSignalAsync(ComtradeSignalItem signal, CancellationToken token)
    {
        await _nativeGate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            token.ThrowIfCancellationRequested();
            return await Task.Run(() => BuildSignalPreview(signal, token), token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private SignalPreview BuildSignalPreview(ComtradeSignalItem signal, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var frameCount = _record.Info.FrameCount;

        if (frameCount <= ExactSignalFrameLimit)
        {
            var count = checked((int)frameCount);
            var timestamps = _record.ReadRawTimestamps(0, count);
            if (signal.IsAnalog)
            {
                return new SignalPreview(
                    _record.ReadAnalog(signal.Index, 0, count),
                    null,
                    timestamps,
                    frameCount,
                    false,
                    false,
                    "full record • exact samples");
            }

            return new SignalPreview(
                null,
                _record.ReadStatus(signal.Index, 0, count),
                timestamps,
                frameCount,
                false,
                false,
                "full record • exact samples");
        }

        var source = new ArdIrecRangeSource(_record);
        if (signal.IsAnalog)
        {
            var envelope = ComtradeRangeDecimator.BuildAnalogEnvelope(
                source,
                signal.Index,
                0,
                frameCount,
                FullRecordAnalogBuckets,
                cancellationToken: token);
            var series = ComtradeDecimatedSeriesBuilder.BuildAnalog(envelope);
            return new SignalPreview(
                series.Values,
                null,
                series.Timestamps,
                frameCount,
                true,
                false,
                "full record • bounded min/max envelope");
        }

        var transitions = ComtradeRangeDecimator.BuildDigitalTransitions(
            source,
            signal.Index,
            0,
            frameCount,
            FullRecordDigitalTransitionCap,
            cancellationToken: token);
        var digitalSeries = ComtradeDecimatedSeriesBuilder.BuildDigital(transitions);
        return new SignalPreview(
            null,
            digitalSeries.States,
            digitalSeries.Timestamps,
            frameCount,
            true,
            digitalSeries.IsTruncated,
            digitalSeries.IsTruncated
                ? "full record • adaptively sampled transitions"
                : "full record • exact transitions");
    }

    private static string BuildLoadStatus(SignalPreview preview)
    {
        if (!preview.IsReduced)
            return $"{preview.SourceFrameCount:N0} frames • exact native samples • trigger/cursor time from COMTRADE timestamps";

        var detail = preview.IsLossy
            ? "adaptive transition sampling active"
            : preview.Analog is not null
                ? "min/max envelope preserves bucket extrema"
                : "all digital transitions preserved";
        return $"Full record {preview.SourceFrameCount:N0} frames scanned in bounded native chunks • " +
               $"{preview.Timestamps.Length:N0} plot points • {detail}";
    }

    private void WaveformView_NavigationChanged(object? sender, ComtradeNavigationChangedEventArgs e)
    {
        NavigationTextBlock.Text = _isReducedView
            ? e.Summary + "  |  reduced full-record overview"
            : e.Summary;
    }

    private void ResetView_Click(object sender, RoutedEventArgs e)
    {
        WaveformView.ResetNavigation();
    }

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
        ulong SourceFrameCount,
        bool IsReduced,
        bool IsLossy,
        string DisplayMode);
}
