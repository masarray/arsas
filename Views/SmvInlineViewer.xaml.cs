using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Services;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Views;

/// <summary>
/// Native, IED-independent main-tab SV viewer. One passive engine reader,
/// one selected publisher, bounded snapshot presentation, no synthetic data.
/// </summary>
public partial class SmvInlineViewer : UserControl, INotifyPropertyChanged
{
    private static readonly Brush[] ChannelBrushes =
    [
        Brushes.Crimson, Brushes.Goldenrod, Brushes.DodgerBlue,
        Brushes.MediumSeaGreen, Brushes.MediumPurple, Brushes.DarkOrange,
        Brushes.DeepPink, Brushes.Teal
    ];

    private readonly SmvLiveCaptureService _service = new();
    private readonly DispatcherTimer _freshness;
    private CancellationTokenSource? _captureCancellation;
    private SmvLiveUpdate? _pendingUpdate;
    private SmvSnapshotResult? _visibleSnapshot;
    private IReadOnlyList<SmvRawVectorMeasurement> _visibleVectors = [];
    private string? _selectedKey;
    private string _captureStatus = "Select an Ethernet adapter and press Start. No IED or SCL is required.";
    private string _waveformStatus = "Waiting for received samples; no test sine waves are generated.";
    private string _streamDetail = "Streams are discovered directly from wire traffic.";
    private DateTimeOffset _lastReceived;
    private int _generation;
    private int _analysisGeneration;
    private int _uiQueued;
    private bool _capturing;
    private bool _loadingAdapters;
    private bool _selectingProgrammatically;
    private long _frameCount;

    public event PropertyChangedEventHandler? PropertyChanged;
    public ObservableCollection<GooseAdapterOption> Adapters { get; } = [];
    public ObservableCollection<SmvLiveStreamSummary> Streams { get; } = [];
    public ObservableCollection<SmvRawVectorMeasurement> Measurements { get; } = [];

    public string StreamCountText => $"{Streams.Count:N0} detected";
    public string CaptureStatus { get => _captureStatus; private set => Change(ref _captureStatus, value, nameof(CaptureStatus)); }
    public string WaveformStatus { get => _waveformStatus; private set => Change(ref _waveformStatus, value, nameof(WaveformStatus)); }
    public string StreamDetail { get => _streamDetail; private set => Change(ref _streamDetail, value, nameof(StreamDetail)); }

    public SmvInlineViewer()
    {
        InitializeComponent();
        DataContext = this;
        _freshness = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _freshness.Tick += Freshness_Tick;
    }

    private async void Viewer_Loaded(object sender, RoutedEventArgs e)
    {
        if (Adapters.Count == 0 && !_loadingAdapters)
            await LoadAdaptersAsync();
    }

    private void Viewer_Unloaded(object sender, RoutedEventArgs e)
    {
        // Main tab changes and window shutdown never leave a hidden NIC reader
        // owned by the view; cancellation remains idempotent.
        StopCapture();
    }

    private async void RefreshAdapters_Click(object sender, RoutedEventArgs e)
        => await LoadAdaptersAsync();

    private void Adapter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (AdapterCombo.SelectedItem is GooseAdapterOption chosen && !_capturing)
            CaptureStatus = $"Selected {chosen.DisplayText}. Press Start to sniff SMV.";
    }

    private async Task LoadAdaptersAsync()
    {
        if (_capturing || _loadingAdapters)
            return;
        _loadingAdapters = true;
        var previous = (AdapterCombo.SelectedItem as GooseAdapterOption)?.Selector;
        try
        {
            var adapters = await Task.Run(() => new SmvSnapshotCaptureService().ListAdapters());
            if (!IsLoaded)
                return;
            Adapters.Clear();
            foreach (var adapter in adapters)
                Adapters.Add(adapter);
            AdapterCombo.SelectedItem = Adapters.FirstOrDefault(a =>
                a.Selector.Equals(previous, StringComparison.OrdinalIgnoreCase));
            CaptureStatus = Adapters.Count == 0
                ? "No capture adapters found. Install Npcap and refresh; IED/SCL is not required."
                : $"{Adapters.Count} Ethernet adapter(s) available. Choose one, then press Start.";
        }
        catch (Exception ex)
        {
            CaptureStatus = $"Ethernet adapter discovery failed: {ex.Message}";
        }
        finally
        {
            _loadingAdapters = false;
        }
    }

    private async void Start_Click(object sender, RoutedEventArgs e)
    {
        if (_capturing)
            return;
        if (AdapterCombo.SelectedItem is not GooseAdapterOption adapter)
        {
            CaptureStatus = Adapters.Count == 0
                ? "No Ethernet adapter available. Install Npcap, then click refresh."
                : "Choose an Ethernet adapter in the dropdown above before pressing Start.";
            AdapterCombo.Focus();
            AdapterCombo.IsDropDownOpen = Adapters.Count > 0;
            return;
        }

        var cts = new CancellationTokenSource();
        _captureCancellation = cts;
        var generation = Interlocked.Increment(ref _generation);
        _capturing = true;
        _selectedKey = null;
        _lastReceived = DateTimeOffset.UtcNow;
        _frameCount = 0;
        _selectingProgrammatically = true;
        StreamList.SelectedItem = null;
        Streams.Clear();
        _selectingProgrammatically = false;
        Notify(nameof(StreamCountText));
        ClearPresentation("Listening for first coherent SV window.");
        CaptureStatus = $"LIVE • {adapter.DisplayText} • waiting for SV packets; no IED/SCL needed.";
        StartButton.IsEnabled = false;
        StopButton.IsEnabled = true;
        AdapterCombo.IsEnabled = false;
        FrequencyCombo.IsEnabled = false;
        _freshness.Start();

        var hz = FrequencyCombo.SelectedItem is ComboBoxItem item &&
                 double.TryParse(item.Tag?.ToString(), out var v) ? v : 50;
        var options = new SmvLiveCaptureOptions
        {
            AdapterSelector = adapter.Selector,
            NominalFrequencyHz = hz,
            Cycles = 2,
            MaximumChannels = 8,
            MaximumStreams = 32,
            RefreshMilliseconds = 200
        };

        try
        {
            await _service.RunAsync(options,
                () => Volatile.Read(ref _selectedKey),
                update => QueueUpdate(update, generation), cts.Token);
            if (generation == _generation)
                CaptureStatus = "Capture completed.";
        }
        catch (OperationCanceledException) when (cts.IsCancellationRequested)
        {
            if (generation == _generation)
                CaptureStatus = "SMV capture stopped.";
        }
        catch (Exception ex)
        {
            if (generation == _generation)
                CaptureStatus = $"SMV capture failed: {ex.Message}. Check Npcap/NIC/VLAN access.";
        }
        finally
        {
            if (generation == _generation)
            {
                _capturing = false;
                _freshness.Stop();
                StartButton.IsEnabled = true;
                StopButton.IsEnabled = false;
                AdapterCombo.IsEnabled = true;
                FrequencyCombo.IsEnabled = true;
                WaveformStatus = _visibleSnapshot is null
                    ? "Stopped • no complete continuous window captured."
                    : "HOLD • last valid observed window (raw counts, not calibrated V/A).";
            }
            if (ReferenceEquals(_captureCancellation, cts))
                _captureCancellation = null;
            cts.Dispose();
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e) => StopCapture();

    private void StopCapture()
    {
        if (!_capturing)
            return;
        StopButton.IsEnabled = false;
        CaptureStatus = "Stopping passive SV capture…";
        _freshness.Stop();
        _captureCancellation?.Cancel();
    }

    private void Stream_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_selectingProgrammatically)
            return;
        var stream = StreamList.SelectedItem as SmvLiveStreamSummary;
        Volatile.Write(ref _selectedKey, stream?.Key);
        Interlocked.Increment(ref _analysisGeneration);
        ClearPresentation(stream is null ? "Select a publisher to display samples."
            : $"Acquiring coherent samples from {stream.SvId}.");
        UpdateStreamDetail(stream);
    }

    private void QueueUpdate(SmvLiveUpdate update, int generation)
    {
        if (generation != Volatile.Read(ref _generation))
            return;
        Interlocked.Exchange(ref _pendingUpdate, update);
        if (Interlocked.CompareExchange(ref _uiQueued, 1, 0) != 0)
            return;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            Interlocked.Exchange(ref _uiQueued, 0);
            var newest = Interlocked.Exchange(ref _pendingUpdate, null);
            if (!IsLoaded || !_capturing || generation != _generation || newest is null)
                return;
            ApplyUpdate(newest, generation);
        }));
    }

    private void ApplyUpdate(SmvLiveUpdate update, int generation)
    {
        _frameCount = update.TotalFrames;
        if (Streams.Count != update.Streams.Count ||
            !Streams.Select(s => s.Key).SequenceEqual(update.Streams.Select(s => s.Key)))
        {
            var currentKey = Volatile.Read(ref _selectedKey);
            _selectingProgrammatically = true;
            Streams.Clear();
            foreach (var discovered in update.Streams)
                Streams.Add(discovered);
            _selectingProgrammatically = false;
            Notify(nameof(StreamCountText));

            var next = Streams.FirstOrDefault(s => s.Key == currentKey) ??
                       (currentKey is null ? Streams.FirstOrDefault() : null);
            if (next is not null)
            {
                StreamList.SelectedItem = next;
                Volatile.Write(ref _selectedKey, next.Key);
            }
            else
            {
                StreamList.SelectedItem = null;
                Volatile.Write(ref _selectedKey, null);
            }
        }

        var selectedKey = Volatile.Read(ref _selectedKey);
        var summary = update.Streams.FirstOrDefault(s => s.Key == selectedKey);
        if (summary is null)
        {
            CaptureStatus = update.Streams.Count == 0
                ? $"LIVE • {_frameCount:N0} SV frame(s). No publisher observed; verify VLAN/TAP/mirror."
                : "LIVE • choose an observed SV publisher in the list.";
            return;
        }

        _lastReceived = summary.LastSeen;
        UpdateStreamDetail(summary);
        var age = DateTimeOffset.UtcNow - summary.LastSeen;
        if (age > TimeSpan.FromSeconds(2))
        {
            CaptureStatus = $"HOLD • {summary.SvId} stale; no new packets for {age.TotalSeconds:0.0}s.";
            return;
        }

        CaptureStatus = $"LIVE • {update.Streams.Count} stream(s) • {_frameCount:N0} frames • " +
            $"gap {summary.Gaps} • missing {summary.Missing} • duplicate {summary.Duplicates} • out-of-order {summary.OutOfOrder}";

        // Only publish samples for the exact currently selected publisher;
        // no mixing old results after stream selection or captured-source changes.
        if (update.SelectedKey != selectedKey || update.SelectedSnapshot is null)
        {
            ClearPresentation(summary.Diagnostic);
            return;
        }

        var snapshot = update.SelectedSnapshot;
        _visibleSnapshot = snapshot;
        WaveformStatus = $"Received {snapshot.CapturedSamples:N0} coherent samples • {snapshot.SamplesPerCycle} samples/cycle • unscaled INT32";
        DrawWaveform();
        BeginAnalysis(snapshot, selectedKey, generation);
    }

    private async void BeginAnalysis(SmvSnapshotResult snapshot, string? selectedKey, int generation)
    {
        var analysis = Interlocked.Increment(ref _analysisGeneration);
        try
        {
            var vectors = await Task.Run(() => SmvRawVectorMath.Analyze(snapshot));
            if (generation != _generation || analysis != _analysisGeneration ||
                !ReferenceEquals(_visibleSnapshot, snapshot) || !IsLoaded ||
                selectedKey != Volatile.Read(ref _selectedKey))
                return;
            _visibleVectors = vectors;
            Measurements.Clear();
            foreach (var row in vectors)
                Measurements.Add(row);
            DrawPhasors();
        }
        catch (Exception)
        {
            if (generation != _generation || analysis != _analysisGeneration)
                return;
            _visibleVectors = [];
            Measurements.Clear();
            DrawPhasors();
            WaveformStatus = "Raw phasor calculation unavailable; no substitute values shown.";
        }
    }

    private void Freshness_Tick(object? sender, EventArgs e)
    {
        if (!_capturing)
            return;
        if (DateTimeOffset.UtcNow - _lastReceived > TimeSpan.FromSeconds(2))
            CaptureStatus = _frameCount == 0
                ? "LIVE • no SV packets yet. Check adapter, publisher, capture permissions and VLAN."
                : "HOLD • selected publisher has stopped delivering packets. Check network.";
    }

    private void UpdateStreamDetail(SmvLiveStreamSummary? summary)
    {
        StreamDetail = summary is null
            ? "Passive discovery • no selected publisher. No MMS connection needed."
            : $"{summary.SvId} • APPID 0x{summary.AppId:X4}\n" +
              $"{summary.SourceMac} → {summary.DestinationMac}\n" +
              $"VLAN {summary.Vlan} • gap {summary.Gaps} • restart {summary.Restarts}";
    }

    private void ClearPresentation(string message)
    {
        _visibleSnapshot = null;
        _visibleVectors = [];
        Measurements.Clear();
        WaveformStatus = message;
        WaveformCanvas.Children.Clear();
        DrawPhasors();
    }

    private void Waveform_Resized(object sender, SizeChangedEventArgs e) => DrawWaveform();
    private void Phasor_Resized(object sender, SizeChangedEventArgs e) => DrawPhasors();

    private void DrawWaveform()
    {
        var canvas = WaveformCanvas;
        canvas.Children.Clear();
        var snapshot = _visibleSnapshot;
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (snapshot is null || width < 80 || height < 80)
            return;
        for (var k = 1; k < 5; k++)
        {
            var y = k * height / 5;
            canvas.Children.Add(new Line
            {
                X1 = 0, X2 = width, Y1 = y, Y2 = y,
                Stroke = Brushes.LightSteelBlue, StrokeThickness = 0.6,
                StrokeDashArray = [3, 5]
            });
        }
        var channels = snapshot.Channels.Take(8).ToArray();
        var bins = Math.Max(64, Math.Min((int)width, 850));
        for (var lane = 0; lane < channels.Length; lane++)
        {
            var samples = channels[lane].Samples;
            if (samples.Count == 0) continue;
            var peak = samples.Max(v => Math.Abs(v));
            if (peak < 1) peak = 1;
            var geometry = new StreamGeometry();
            using (var context = geometry.Open())
            {
                for (var bin = 0; bin < bins; bin++)
                {
                    var from = (int)((long)bin * samples.Count / bins);
                    var to = Math.Max(from + 1, (int)((long)(bin + 1) * samples.Count / bins));
                    to = Math.Min(to, samples.Count);
                    var low = double.PositiveInfinity;
                    var high = double.NegativeInfinity;
                    for (var index = from; index < to; index++)
                    {
                        var value = samples[index];
                        if (value < low) low = value;
                        if (value > high) high = value;
                    }
                    var x = bin * width / (bins - 1);
                    var lowPoint = new Point(x, height / 2 - low / peak * (height * 0.41));
                    var highPoint = new Point(x, height / 2 - high / peak * (height * 0.41));
                    if (bin == 0) context.BeginFigure(lowPoint, false, false);
                    else context.LineTo(lowPoint, true, false);
                    context.LineTo(highPoint, true, false);
                }
            }
            geometry.Freeze();
            canvas.Children.Add(new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = ChannelBrushes[lane % ChannelBrushes.Length],
                StrokeThickness = 1.35,
                Opacity = 0.93
            });
        }
        for (var lane = 0; lane < channels.Length; lane++)
        {
            var caption = new TextBlock
            {
                Text = $"● {channels[lane].Label}",
                FontSize = 10, Foreground = ChannelBrushes[lane % ChannelBrushes.Length],
                Background = new SolidColorBrush(Color.FromArgb(220, 248, 251, 255))
            };
            Canvas.SetLeft(caption, 8 + (lane % 4) * Math.Max(70, width / 4));
            Canvas.SetTop(caption, 5 + lane / 4 * 17);
            canvas.Children.Add(caption);
        }
    }

    private void DrawPhasors()
    {
        var canvas = PhasorCanvas;
        canvas.Children.Clear();
        var width = canvas.ActualWidth;
        var height = canvas.ActualHeight;
        if (width < 120 || height < 110)
            return;
        var cx = width / 2;
        var cy = height / 2;
        var radius = Math.Max(25, Math.Min(width - 95, height - 30) * 0.46);
        for (var index = 1; index <= 4; index++)
        {
            var r = radius * index / 4;
            var ellipse = new Ellipse
            {
                Width = 2 * r, Height = 2 * r, Stroke = Brushes.LightSteelBlue,
                StrokeThickness = index == 4 ? 1.1 : 0.65
            };
            Canvas.SetLeft(ellipse, cx - r);
            Canvas.SetTop(ellipse, cy - r);
            canvas.Children.Add(ellipse);
        }
        canvas.Children.Add(new Line { X1 = cx - radius, X2 = cx + radius, Y1 = cy, Y2 = cy, Stroke = Brushes.LightSteelBlue });
        canvas.Children.Add(new Line { X1 = cx, X2 = cx, Y1 = cy - radius, Y2 = cy + radius, Stroke = Brushes.LightSteelBlue });
        var vectors = _visibleVectors.Where(v => v.HasFundamental).ToArray();
        if (vectors.Length == 0)
        {
            var notice = new TextBlock { Text = "Waiting for coherent samples", FontSize = 10.5, Foreground = Brushes.SlateGray };
            Canvas.SetLeft(notice, Math.Max(0, cx - 82));
            Canvas.SetTop(notice, cy + 8);
            canvas.Children.Add(notice);
            return;
        }
        var max = Math.Max(1e-9, vectors.Max(v => v.FundamentalRms));
        foreach (var vector in vectors)
        {
            var radians = vector.PhaseDegrees * Math.PI / 180;
            var distance = radius * vector.FundamentalRms / max;
            var end = new Point(cx + distance * Math.Cos(radians), cy - distance * Math.Sin(radians));
            var start = new Point(cx, cy);
            var d = end - start;
            if (d.Length <= 3) continue;
            d.Normalize();
            var orthogonal = new Vector(-d.Y, d.X);
            var brush = ChannelBrushes[Math.Max(0, vector.ChannelIndex - 1) % ChannelBrushes.Length];
            canvas.Children.Add(new Line { X1 = cx, Y1 = cy, X2 = end.X, Y2 = end.Y, Stroke = brush, StrokeThickness = 2 });
            foreach (var tip in new[] { end - d * 9 + orthogonal * 4, end - d * 9 - orthogonal * 4 })
                canvas.Children.Add(new Line { X1 = end.X, Y1 = end.Y, X2 = tip.X, Y2 = tip.Y, Stroke = brush, StrokeThickness = 1.6 });
            var name = new TextBlock { Text = $"Ch {vector.ChannelIndex}", FontSize = 10, Foreground = brush };
            Canvas.SetLeft(name, Math.Clamp(end.X + 4, 0, Math.Max(0, width - 42)));
            Canvas.SetTop(name, Math.Clamp(end.Y - 17, 0, Math.Max(0, height - 16)));
            canvas.Children.Add(name);
        }
    }

    private bool Change(ref string field, string next, string name)
    {
        if (field == next) return false;
        field = next;
        Notify(name);
        return true;
    }

    private void Notify(string propertyName)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

}
