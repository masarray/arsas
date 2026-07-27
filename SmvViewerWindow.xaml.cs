using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class SmvViewerWindow : Window, INotifyPropertyChanged
{
    private static readonly Brush[] WaveformBrushes =
    [
        Brushes.DodgerBlue,
        Brushes.OrangeRed,
        Brushes.SeaGreen,
        Brushes.MediumPurple,
        Brushes.DeepPink,
        Brushes.DarkCyan,
        Brushes.Goldenrod,
        Brushes.SlateBlue
    ];

    private readonly SmvSnapshotCaptureService _snapshotService = new();
    private SmvStreamRow? _selectedStream;
    private GooseAdapterOption? _selectedAdapter;
    private CancellationTokenSource? _captureCancellation;
    private SmvSnapshotResult? _snapshot;
    private string _statusText = string.Empty;
    private string _captureStatusText = "Select an adapter and stream, then capture a bounded two-cycle snapshot.";
    private string _snapshotBadgeText = "No snapshot";
    private string _snapshotSummaryText = "No Sampled Values snapshot has been captured yet.";
    private string _snapshotEvidenceText = "The snapshot will report stream identity, payload shape, sample-counter continuity and a static raw waveform.";
    private string _snapshotBoundaryText = "Read-only capture. Raw lanes do not claim current, voltage, engineering units or IEC quality unless trusted semantic mapping is available.";

    public SmvViewerWindow(Iec61850MonitorDevice device)
    {
        ArgumentNullException.ThrowIfNull(device);
        InitializeComponent();

        DeviceId = device.DeviceId;
        DeviceName = device.Name;
        EndpointText = string.IsNullOrWhiteSpace(device.IpAddress)
            ? "MMS endpoint unassigned"
            : $"{device.IpAddress}:{device.Port}";

        foreach (var row in BuildRows(device))
            Streams.Add(row);

        SelectedStream = Streams.FirstOrDefault();
        StatusText = Streams.Count == 0
            ? "No Sampled Value control block is configured in the opened SCL model or discovered from this IED."
            : $"{Streams.Count:N0} Sampled Value stream definition(s) are available. Snapshot capture is passive and does not write to the IED.";
        DataContext = this;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public string DeviceId { get; }
    public string DeviceName { get; }
    public string EndpointText { get; }
    public ObservableCollection<SmvStreamRow> Streams { get; } = new();
    public ObservableCollection<GooseAdapterOption> AdapterOptions { get; } = new();
    public ObservableCollection<SmvSnapshotChannelRow> SnapshotChannels { get; } = new();
    public string StreamCountText => $"{Streams.Count:N0} stream(s)";

    public string StatusText
    {
        get => _statusText;
        private set => Set(ref _statusText, value);
    }

    public string CaptureStatusText
    {
        get => _captureStatusText;
        private set => Set(ref _captureStatusText, value);
    }

    public string SnapshotBadgeText
    {
        get => _snapshotBadgeText;
        private set => Set(ref _snapshotBadgeText, value);
    }

    public string SnapshotSummaryText
    {
        get => _snapshotSummaryText;
        private set => Set(ref _snapshotSummaryText, value);
    }

    public string SnapshotEvidenceText
    {
        get => _snapshotEvidenceText;
        private set => Set(ref _snapshotEvidenceText, value);
    }

    public string SnapshotBoundaryText
    {
        get => _snapshotBoundaryText;
        private set => Set(ref _snapshotBoundaryText, value);
    }

    public GooseAdapterOption? SelectedAdapter
    {
        get => _selectedAdapter;
        set => Set(ref _selectedAdapter, value);
    }

    public SmvStreamRow? SelectedStream
    {
        get => _selectedStream;
        set
        {
            if (ReferenceEquals(_selectedStream, value))
                return;
            _selectedStream = value;
            Raise();
            Raise(nameof(SelectedStreamDetail));
            ResetSnapshotPresentation();
        }
    }

    public string SelectedStreamDetail => SelectedStream == null
        ? "Select one stream before starting the bounded capture."
        : $"{SelectedStream.Source} • {SelectedStream.ControlReference} • DataSet {SelectedStream.DataSetReference} • " +
          $"APPID {SelectedStream.AppId} • {SelectedStream.MemberCount} member(s).";

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AdapterOptions.Clear();
            foreach (var adapter in _snapshotService.ListAdapters())
                AdapterOptions.Add(adapter);
            SelectedAdapter = AdapterOptions.FirstOrDefault();

            if (AdapterOptions.Count == 0)
            {
                CaptureStatusText = "No Npcap adapter is available. Install or repair Npcap before capturing SV.";
                CaptureButton.IsEnabled = false;
            }
        }
        catch (Exception ex)
        {
            CaptureStatusText = $"Npcap adapter discovery failed: {ex.Message}";
            CaptureButton.IsEnabled = false;
        }
    }

    private async void Capture_Click(object sender, RoutedEventArgs e)
    {
        if (SelectedStream is null)
        {
            MessageBox.Show(this, "Select an SV stream first.", "SV Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        if (SelectedAdapter is null)
        {
            MessageBox.Show(this, "Select a capture adapter first.", "SV Snapshot", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        var frequency = ReadSelectedFrequency();
        var appId = TryParseAppId(SelectedStream.AppId, out var parsedAppId) ? parsedAppId : null;
        var request = new SmvSnapshotRequest
        {
            AdapterSelector = SelectedAdapter.Selector,
            AppId = appId,
            StreamId = NormalizeOptionalIdentity(SelectedStream.StreamId),
            DestinationMac = NormalizeOptionalIdentity(SelectedStream.DestinationMac),
            DeclaredSampleRateHint = TryParseUShort(SelectedStream.SampleRate),
            DeclaredSampleModeHint = ParseSampleMode(SelectedStream.SampleMode),
            TrustedNominalFrequencyHz = frequency,
            CycleCount = 2,
            MaximumChannels = 8,
            Timeout = TimeSpan.FromSeconds(8)
        };

        _captureCancellation?.Cancel();
        _captureCancellation?.Dispose();
        _captureCancellation = new CancellationTokenSource();
        SetCaptureState(true);
        ResetSnapshotPresentation();
        CaptureStatusText = "Listening for the selected SV stream…";
        StatusText = $"Passive SV capture started on {SelectedAdapter.DisplayText}. No network frame is transmitted.";

        var progress = new Progress<SmvSnapshotProgress>(snapshotProgress =>
        {
            CaptureStatusText = snapshotProgress.Message;
            SnapshotProgress.IsIndeterminate = snapshotProgress.TargetSamples <= 0;
            SnapshotProgress.Maximum = Math.Max(1, snapshotProgress.TargetSamples);
            SnapshotProgress.Value = Math.Min(snapshotProgress.CapturedSamples, SnapshotProgress.Maximum);
        });

        try
        {
            var result = await _snapshotService.CaptureAsync(request, progress, _captureCancellation.Token);
            ApplySnapshot(result);
        }
        catch (OperationCanceledException)
        {
            CaptureStatusText = "Snapshot capture cancelled.";
            StatusText = "SV snapshot capture was cancelled without changing the selected IED or stream configuration.";
        }
        catch (Exception ex) when (ex is TimeoutException or InvalidDataException or InvalidOperationException or ArgumentException)
        {
            CaptureStatusText = "Snapshot not proven.";
            SnapshotBadgeText = "NOT PROVEN";
            SnapshotSummaryText = ex.Message;
            SnapshotEvidenceText = "No complete two-cycle evidence window was accepted.";
            SnapshotBoundaryText = "Review adapter selection, mirror-port/VLAN visibility, APPID/MAC/svID matching, publisher state and explicit 50/60 Hz context.";
            StatusText = $"SV snapshot failed: {ex.Message}";
            RenderWaveform();
        }
        catch (Exception ex)
        {
            CaptureStatusText = "Capture runtime error.";
            SnapshotBadgeText = "ERROR";
            SnapshotSummaryText = ex.Message;
            StatusText = $"Unexpected SV capture error: {ex.Message}";
            RenderWaveform();
        }
        finally
        {
            SetCaptureState(false);
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
        => _captureCancellation?.Cancel();

    private void ApplySnapshot(SmvSnapshotResult result)
    {
        _snapshot = result;
        SnapshotChannels.Clear();
        foreach (var channel in result.Channels)
            SnapshotChannels.Add(new SmvSnapshotChannelRow(channel));

        SnapshotProgress.IsIndeterminate = false;
        SnapshotProgress.Maximum = Math.Max(1, result.TargetSamples);
        SnapshotProgress.Value = result.CapturedSamples;

        var proof = result.IsCleanProof ? "PASS" : "REVIEW";
        SnapshotBadgeText = $"{proof} · {result.CycleCount} cycles";
        CaptureStatusText = result.IsCleanProof
            ? "Two-cycle SV snapshot received and decoded with continuous smpCnt."
            : "Two-cycle SV snapshot decoded, but continuity anomalies require review.";
        SnapshotSummaryText =
            $"{proof} — {result.CapturedSamples:N0}/{result.TargetSamples:N0} samples ({result.SamplesPerCycle:N0} samples/cycle at {result.NominalFrequencyHz:0} Hz), " +
            $"{result.CapturedFrames:N0} Ethernet frame(s), {result.ParsedAsdus:N0} ASDU(s), {result.Channels.Count:N0} plotted raw lane(s).";
        SnapshotEvidenceText =
            $"APPID 0x{result.AppId:X4} • {result.SourceMac} → {result.DestinationMac} • VLAN {result.VlanText} • " +
            $"svID {FirstReadable(result.StreamId, "-")} • DataSet {FirstReadable(result.DataSetReference, "-")} • confRev {result.ConfigurationRevision} • " +
            $"smpSynch {result.SampleSynchronization} • smpCnt {result.FirstSampleCount} → {result.LastSampleCount} • " +
            $"gaps {result.GapTransitions} / missing {result.MissingSamples} / duplicate {result.DuplicateTransitions} / out-of-order {result.OutOfOrderTransitions} • " +
            $"{result.PayloadShape}.";
        SnapshotBoundaryText =
            $"Timebase: {result.TimebaseReason} Capture duration {result.CaptureDuration.TotalMilliseconds:0} ms. " +
            "This proves bounded reception, IEC 61850-9-2 parsing, stable payload shape and sample-counter observability; it is not calibrated measurement or formal conformance evidence. " +
            "Raw lanes remain semantically unresolved until ordered SCL mapping and reviewed scaling evidence are bound.";
        StatusText = result.IsCleanProof
            ? "The selected SV stream produced a complete two-cycle proof window without detected smpCnt gaps, duplicates or out-of-order transitions."
            : "A complete two-cycle window was decoded, but counter continuity findings remain visible and must be reviewed.";

        Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(RenderWaveform));
    }

    private void ResetSnapshotPresentation()
    {
        _snapshot = null;
        SnapshotChannels.Clear();
        SnapshotBadgeText = "No snapshot";
        SnapshotSummaryText = "No Sampled Values snapshot has been captured for the selected stream.";
        SnapshotEvidenceText = "The snapshot will report stream identity, payload shape, sample-counter continuity and a static raw waveform.";
        SnapshotBoundaryText = "Read-only capture. Raw lanes do not claim current, voltage, engineering units or IEC quality unless trusted semantic mapping is available.";
        SnapshotProgress.IsIndeterminate = false;
        SnapshotProgress.Maximum = 1;
        SnapshotProgress.Value = 0;
        if (IsLoaded)
            RenderWaveform();
    }

    private void SetCaptureState(bool capturing)
    {
        CaptureButton.IsEnabled = !capturing && AdapterOptions.Count > 0;
        CancelButton.IsEnabled = capturing;
        AdapterCombo.IsEnabled = !capturing;
        FrequencyCombo.IsEnabled = !capturing;
        if (!capturing)
            SnapshotProgress.IsIndeterminate = false;
    }

    private void WaveformCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        => RenderWaveform();

    private void RenderWaveform()
    {
        if (WaveformCanvas is null)
            return;
        WaveformCanvas.Children.Clear();

        if (_snapshot is null || _snapshot.Channels.Count == 0)
        {
            AddCanvasText("Capture a two-cycle snapshot to display a static waveform proof.", 18, 18, Brushes.SlateGray, 12);
            return;
        }

        var width = Math.Max(760, WaveformCanvas.ActualWidth);
        var laneCount = _snapshot.Channels.Count;
        var height = Math.Max(300, laneCount * 48 + 48);
        WaveformCanvas.Height = height;

        const double left = 150;
        const double right = 18;
        const double top = 24;
        const double bottom = 24;
        var plotWidth = Math.Max(200, width - left - right);
        var plotHeight = height - top - bottom;
        var laneHeight = plotHeight / laneCount;

        for (var cycle = 0; cycle <= _snapshot.CycleCount; cycle++)
        {
            var x = left + plotWidth * cycle / _snapshot.CycleCount;
            WaveformCanvas.Children.Add(new Line
            {
                X1 = x,
                X2 = x,
                Y1 = top,
                Y2 = height - bottom,
                Stroke = cycle is 0 or 2 ? Brushes.LightSlateGray : Brushes.LightSteelBlue,
                StrokeThickness = cycle is 0 or 2 ? 1.1 : 0.8,
                StrokeDashArray = cycle is 0 or 2 ? null : new DoubleCollection { 4, 4 }
            });
            AddCanvasText($"{cycle} cycle", Math.Max(left, x - 20), 4, Brushes.SlateGray, 9.5);
        }

        for (var channelIndex = 0; channelIndex < laneCount; channelIndex++)
        {
            var channel = _snapshot.Channels[channelIndex];
            var centerY = top + laneHeight * (channelIndex + 0.5);
            WaveformCanvas.Children.Add(new Line
            {
                X1 = left,
                X2 = left + plotWidth,
                Y1 = centerY,
                Y2 = centerY,
                Stroke = Brushes.Gainsboro,
                StrokeThickness = 0.8
            });

            AddCanvasText(channel.Label, 8, centerY - 9, WaveformBrushes[channelIndex % WaveformBrushes.Length], 10.5, 136);
            if (channel.Samples.Count < 2)
                continue;

            var maxAbs = channel.Samples.Max(value => Math.Abs(value));
            if (maxAbs < 1)
                maxAbs = 1;
            var amplitude = laneHeight * 0.38;
            var points = new PointCollection(channel.Samples.Count);
            for (var sampleIndex = 0; sampleIndex < channel.Samples.Count; sampleIndex++)
            {
                var x = left + plotWidth * sampleIndex / Math.Max(1, channel.Samples.Count - 1);
                var normalized = Math.Clamp(channel.Samples[sampleIndex] / maxAbs, -1.0, 1.0);
                points.Add(new Point(x, centerY - normalized * amplitude));
            }

            WaveformCanvas.Children.Add(new Polyline
            {
                Points = points,
                Stroke = WaveformBrushes[channelIndex % WaveformBrushes.Length],
                StrokeThickness = 1.45,
                StrokeLineJoin = PenLineJoin.Round,
                SnapsToDevicePixels = true
            });
        }
    }

    private void AddCanvasText(string text, double left, double top, Brush foreground, double fontSize, double? width = null)
    {
        var block = new TextBlock
        {
            Text = text,
            Foreground = foreground,
            FontSize = fontSize,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        if (width.HasValue)
            block.Width = width.Value;
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        WaveformCanvas.Children.Add(block);
    }

    private double ReadSelectedFrequency()
    {
        if (FrequencyCombo.SelectedItem is ComboBoxItem item &&
            double.TryParse(item.Tag?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var frequency))
            return frequency;
        return 50.0;
    }

    private static bool TryParseAppId(string? value, out ushort? appId)
    {
        appId = null;
        var text = NormalizeOptionalIdentity(value);
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var cleaned = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ? text[2..] : text;
        var preferHex = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase) ||
                        cleaned.Any(character => character is >= 'A' and <= 'F' or >= 'a' and <= 'f') ||
                        cleaned.Length == 4;
        var style = preferHex ? NumberStyles.HexNumber : NumberStyles.Integer;
        if (ushort.TryParse(cleaned, style, CultureInfo.InvariantCulture, out var parsed) ||
            ushort.TryParse(cleaned, preferHex ? NumberStyles.Integer : NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed))
        {
            appId = parsed;
            return true;
        }
        return false;
    }

    private static ushort? TryParseUShort(string? value)
    {
        var text = NormalizeOptionalIdentity(value);
        return ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;
    }

    private static ushort? ParseSampleMode(string? value)
    {
        var text = NormalizeOptionalIdentity(value);
        if (ushort.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var numeric))
            return numeric;
        if (text.Contains("period", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("cycle", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (text.Contains("second", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("sec", StringComparison.OrdinalIgnoreCase))
            return 1;
        return null;
    }

    private static string NormalizeOptionalIdentity(string? value)
    {
        var text = value?.Trim() ?? string.Empty;
        return text == "-" ? string.Empty : text;
    }

    private static IReadOnlyList<SmvStreamRow> BuildRows(Iec61850MonitorDevice device)
    {
        var rows = new List<SmvStreamRow>();

        if (device.SclWorkspace is { } workspace)
        {
            rows.AddRange(workspace.SampledValuesStreams.Select(stream => new SmvStreamRow
            {
                Source = "SCL",
                ControlReference = stream.ControlBlockReference,
                StreamId = FirstNonEmpty(stream.SmvId, stream.SvId),
                DataSetReference = stream.DataSetReference,
                AppId = stream.Address.AppIdText,
                DestinationMac = stream.Address.DestinationMacText,
                Vlan = stream.Address.VlanId?.ToString() ?? "-",
                SampleRate = stream.SampleRate == 0 ? "-" : stream.SampleRate.ToString(CultureInfo.InvariantCulture),
                SampleMode = string.IsNullOrWhiteSpace(stream.SampleMode) ? "-" : stream.SampleMode,
                NumberOfAsdu = stream.NoAsdu.ToString(CultureInfo.InvariantCulture),
                MemberCount = stream.Entries.Count
            }));
        }

        if (device.LiveDiscoveryModel is { } liveModel)
        {
            rows.AddRange(liveModel.SampledValueControlBlocks.Select(control => new SmvStreamRow
            {
                Source = "Live discovery",
                ControlReference = control.Reference,
                StreamId = FirstNonEmpty(control.SmvId, control.ControlId),
                DataSetReference = control.DataSetReference,
                AppId = string.IsNullOrWhiteSpace(control.AppId) ? "-" : control.AppId,
                DestinationMac = "-",
                Vlan = "-",
                SampleRate = string.IsNullOrWhiteSpace(control.SampleRate) ? "-" : control.SampleRate,
                SampleMode = string.IsNullOrWhiteSpace(control.SampleMode) ? "-" : control.SampleMode,
                NumberOfAsdu = string.IsNullOrWhiteSpace(control.NumberOfAsdu) ? "-" : control.NumberOfAsdu,
                MemberCount = FindMemberCount(liveModel, control.DataSetReference)
            }));
        }

        return rows
            .Where(row => !string.IsNullOrWhiteSpace(row.ControlReference) || !string.IsNullOrWhiteSpace(row.StreamId))
            .GroupBy(
                row => $"{Normalize(row.ControlReference)}|{Normalize(row.StreamId)}|{Normalize(row.DataSetReference)}",
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group
                .OrderByDescending(row => row.Source.Equals("SCL", StringComparison.OrdinalIgnoreCase))
                .First())
            .OrderBy(row => row.ControlReference, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static int FindMemberCount(
        AR.Iec61850.Discovery.LiveIedModelDiscoveryDocument model,
        string dataSetReference)
    {
        if (string.IsNullOrWhiteSpace(dataSetReference))
            return 0;

        var normalized = Normalize(dataSetReference);
        return model.DataSets.FirstOrDefault(dataSet => Normalize(dataSet.Reference) == normalized)?.MemberCount ?? 0;
    }

    private static string FirstNonEmpty(params string[] values)
        => values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim() ?? "-";

    private static string FirstReadable(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;

    private static string Normalize(string? value)
        => (value ?? string.Empty).Trim().Replace('$', '.').ToLowerInvariant();

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closed(object? sender, EventArgs e)
    {
        _captureCancellation?.Cancel();
        _captureCancellation?.Dispose();
        _captureCancellation = null;
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value))
            return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}

public sealed class SmvStreamRow
{
    public string Source { get; init; } = string.Empty;
    public string ControlReference { get; init; } = string.Empty;
    public string StreamId { get; init; } = string.Empty;
    public string DataSetReference { get; init; } = string.Empty;
    public string AppId { get; init; } = string.Empty;
    public string DestinationMac { get; init; } = string.Empty;
    public string Vlan { get; init; } = string.Empty;
    public string SampleRate { get; init; } = string.Empty;
    public string SampleMode { get; init; } = string.Empty;
    public string NumberOfAsdu { get; init; } = string.Empty;
    public int MemberCount { get; init; }
}

public sealed class SmvSnapshotChannelRow
{
    public SmvSnapshotChannelRow(SmvSnapshotChannel channel)
    {
        Label = channel.Label;
        MinimumText = channel.Minimum.ToString("N0", CultureInfo.InvariantCulture);
        MaximumText = channel.Maximum.ToString("N0", CultureInfo.InvariantCulture);
        PeakToPeakText = channel.PeakToPeak.ToString("N0", CultureInfo.InvariantCulture);
    }

    public string Label { get; }
    public string MinimumText { get; }
    public string MaximumText { get; }
    public string PeakToPeakText { get; }
}