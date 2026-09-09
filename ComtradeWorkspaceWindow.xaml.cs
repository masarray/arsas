using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ComtradeWorkspaceWindow : Window
{
    private const int MaxPreviewFrames = 500_000;
    private readonly ArdIrecNativeRecord _record;
    private readonly SemaphoreSlim _nativeGate = new(1, 1);
    private CancellationTokenSource? _signalLoadCts;

    internal ComtradeWorkspaceWindow(ArdIrecNativeRecord record)
    {
        _record = record;
        InitializeComponent();
        PopulateHeader();
        PopulateSignals();
        Closed += (_, _) =>
        {
            _signalLoadCts?.Cancel();
            _signalLoadCts?.Dispose();
            _nativeGate.Dispose();
            _record.Dispose();
        };
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
            WaveformView.ShowMessage("No channels", "The COMTRADE record contains no analog or digital channels.");
    }

    private async void SignalList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (SignalList.SelectedItem is not ComtradeSignalItem signal)
            return;

        _signalLoadCts?.Cancel();
        _signalLoadCts?.Dispose();
        _signalLoadCts = new CancellationTokenSource();
        var token = _signalLoadCts.Token;

        StatusTextBlock.Text = $"Loading {signal.Title} from native ArdIrec core…";
        WaveformView.ShowMessage(signal.Title, "Loading signal samples…");

        try
        {
            var preview = await LoadSignalAsync(signal, token).ConfigureAwait(true);
            if (token.IsCancellationRequested)
                return;

            if (preview.Analog is not null)
            {
                var metadata = _record.AnalogChannels[checked((int)signal.Index)];
                WaveformView.ShowAnalog(
                    signal.Title,
                    BuildSignalSubtitle(metadata.Phase, metadata.Circuit, preview.IsTruncated),
                    metadata.Units,
                    preview.Analog,
                    preview.Timestamps);
            }
            else if (preview.Status is not null)
            {
                var metadata = _record.StatusChannels[checked((int)signal.Index)];
                WaveformView.ShowStatus(
                    signal.Title,
                    BuildSignalSubtitle(metadata.Phase, metadata.Circuit, preview.IsTruncated),
                    preview.Status,
                    preview.Timestamps);
            }

            StatusTextBlock.Text = preview.IsTruncated
                ? $"Showing the first {preview.Timestamps.Length:N0} of {_record.Info.FrameCount:N0} frames • P1A preview limit"
                : $"{preview.Timestamps.Length:N0} frames • in-process native bridge • no Qt child process";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
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
            var count = checked((int)Math.Min(_record.Info.FrameCount, (ulong)MaxPreviewFrames));
            var isTruncated = _record.Info.FrameCount > (ulong)count;
            return await Task.Run(() =>
            {
                token.ThrowIfCancellationRequested();
                var timestamps = _record.ReadRawTimestamps(0, count);
                if (signal.IsAnalog)
                    return new SignalPreview(_record.ReadAnalog(signal.Index, 0, count), null, timestamps, isTruncated);
                return new SignalPreview(null, _record.ReadStatus(signal.Index, 0, count), timestamps, isTruncated);
            }, token).ConfigureAwait(false);
        }
        finally
        {
            _nativeGate.Release();
        }
    }

    private static string BuildSignalSubtitle(string phase, string circuit, bool truncated)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(phase)) parts.Add($"phase {phase}");
        if (!string.IsNullOrWhiteSpace(circuit)) parts.Add(circuit);
        parts.Add(truncated ? "preview" : "full record");
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
    private sealed record SignalPreview(double[]? Analog, byte[]? Status, uint[] Timestamps, bool IsTruncated);
}
