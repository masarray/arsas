using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using ArIED61850Tester.Services;
using ArIED61850Tester.Models;
using ProcessBus.Core.Models;
using ProcessBus.App.Wpf.Controls;

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
    public ObservableCollection<SmvDisplayMeasurement> Measurements { get; } = [];
    public ObservableCollection<SmvPowerMeasurements.Phase> PowerPhases { get; } = [];
    private SmvPowerMeasurements.Phase? _powerTotal;
    public string PowerP => _powerTotal?.PText ?? "—";
    public string PowerQ => _powerTotal?.QText ?? "—";
    public string PowerS => _powerTotal?.SText ?? "—";
    public string PowerPf => _powerTotal?.PfText ?? "—";
    public string PowerStatus => _powerTotal is null
        ? "Power unavailable · requires clean 9-2LE U/I sample pairing"
        : "P true · Q₁ fundamental · S true RMS · PF true (operator-selected U/I)";
    public string PowerStatusShort => _powerTotal is null
        ? "9-2LE required" : "True P · Q₁ fundamental";
    public string PowerDetails => _powerTotal is null
        ? "No power calculation for unverified RAW channels or incomplete snapshots."
        : PowerStatus + "\n" + string.Join("\n", PowerPhases.Select(phase =>
            $"{phase.Name}: P {phase.PText}  Q₁ {phase.QText}  S {phase.SText}  PF {phase.PfText}"));
    public string SyncLabel => SmvSyncPresentation.Label(_visibleSnapshot);
    public string SyncDetail => SmvSyncPresentation.Detail(_visibleSnapshot);
    public Brush SyncBackground => SmvSyncPresentation.Background(_visibleSnapshot);
    public Brush SyncForeground => SmvSyncPresentation.Foreground(_visibleSnapshot);

    // Two lightweight live views of the original bounded eight-row collection;
    // no duplicate samples, DSP calculations or packet observations.
    public ICollectionView VoltageMeasurements { get; }
    public ICollectionView CurrentMeasurements { get; }

    public string VoltageGroupHeading => ActiveUnits == SmvDisplayUnits.NineTwoLe
        ? "Voltage · Ch5–8 (V)" : "RAW Group A · Ch1–4 (counts)";
    public string CurrentGroupHeading => ActiveUnits == SmvDisplayUnits.NineTwoLe
        ? "Current · Ch1–4 (A)" : "RAW Group B · Ch5–8 (counts)";

    // The original DigSub WPF controls repaint immutable models, not packet work.
    public WaveformSnapshot? CurrentWaveform { get; private set; }
    public IReadOnlyList<PhasorDisplayItem> CurrentPhasors { get; private set; } = [];
    public string CurrentStreamKey { get; private set; } = string.Empty;
    public bool IsLiveSnapshot { get; private set; }

    private SmvDisplayUnits RequestedUnits
        => (UnitsModeCombo?.SelectedItem as ComboBoxItem)?.Tag?.ToString() == "NineTwoLe"
            ? SmvDisplayUnits.NineTwoLe : SmvDisplayUnits.RawCounts;

    private SmvDisplayUnits ActiveUnits
        => SmvEngineeringDisplay.Effective(_visibleSnapshot, RequestedUnits);

    public string TimeAxisCaption
    {
        get
        {
            if (CurrentWaveform is not { } wave || wave.SampleRateHz <= 0)
                return "2 cycles · timebase pending";
            var count = Math.Min(2 * Math.Max(1, wave.SamplesPerCycle),
                SmvWaveformTimeAxis.AvailableSamples(wave));
            return ActiveUnits == SmvDisplayUnits.NineTwoLe &&
                SmvWaveformTimeAxis.ReferenceSample(wave, count) > 0.01
                ? "2 cycles · Ua↑ = 0 ms"
                : "2 cycles · first sample = 0 ms";
        }
    }

    public string PhaseReferenceText => ActiveUnits == SmvDisplayUnits.NineTwoLe
        ? "Ua (Ch5) · ∠0° reference" : "Ch1 · ∠0° reference";

    public string UnitStatus => RequestedUnits switch
    {
        SmvDisplayUnits.RawCounts => "RAW = original INT32 counts; 9-2LE maps Ch1–4 ×0.001 A and Ch5–8 ×0.01 V (operator-selected, unverified)",
        _ when ActiveUnits == SmvDisplayUnits.NineTwoLe
            => "9-2LE user profile · 1 mA / 10 mV per count · verify publisher DataSet",
        _ => "9-2LE selected · need eight clean channels; displaying RAW counts"
    };

    public string StreamCountText => $"{Streams.Count:N0} detected";
    public string CaptureStatus { get => _captureStatus; private set => Change(ref _captureStatus, value, nameof(CaptureStatus)); }
    public string WaveformStatus { get => _waveformStatus; private set => Change(ref _waveformStatus, value, nameof(WaveformStatus)); }
    public string StreamDetail { get => _streamDetail; private set => Change(ref _streamDetail, value, nameof(StreamDetail)); }

    public SmvInlineViewer()
    {
        InitializeComponent();
        VoltageMeasurements = new ListCollectionView(Measurements)
        {
            Filter = item => item is SmvDisplayMeasurement row &&
                SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage
        };
        CurrentMeasurements = new ListCollectionView(Measurements)
        {
            Filter = item => item is SmvDisplayMeasurement row &&
                !SmvEngineeringDisplay.Describe(row.ChannelIndex, ActiveUnits).IsVoltage
        };
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
        TimebaseCombo.IsEnabled = false;
        _freshness.Start();

        var hz = FrequencyCombo.SelectedItem is ComboBoxItem item &&
                 double.TryParse(item.Tag?.ToString(), out var v) ? v : 50;
        var options = new SmvLiveCaptureOptions
        {
            AdapterSelector = adapter.Selector,
            NominalFrequencyHz = hz,
            Cycles = 2,
            DisplaySamplesPerCycleOverride =
                int.TryParse((TimebaseCombo.SelectedItem as ComboBoxItem)?.Tag?.ToString(), out var chosenSpc)
                    ? chosenSpc : null,
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
        TimebaseCombo.IsEnabled = true;
                IsLiveSnapshot = false;
                Notify(nameof(IsLiveSnapshot));
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
            IsLiveSnapshot = false;
            Notify(nameof(IsLiveSnapshot));
            CaptureStatus = $"HOLD • {summary.SvId} stale; no new packets for {age.TotalSeconds:0.0}s.";
            return;
        }

        CaptureStatus = $"LIVE • {update.Streams.Count} stream(s) • {_frameCount:N0} frames • " +
            $"gap {summary.Gaps} • missing {summary.Missing} • duplicate {summary.Duplicates} • out-of-order {summary.OutOfOrder}";

        // Only publish samples for the exact currently selected publisher;
        // no mixing old results after stream selection or captured-source changes.
        if (update.SelectedKey != selectedKey || update.SelectedSnapshot is null)
        {
            // A buffer in its initial warm-up window must not repeatedly blank
            // every pane 5 times/second. Stream changes clear only once in the
            // SelectionChanged handler. Hold the last coherent state otherwise.
            if (_visibleSnapshot is null && Measurements.Count == 0)
                WaveformStatus = summary.Diagnostic;
            return;
        }

        BeginAnalysis(update.SelectedSnapshot, selectedKey, generation);
    }

    private async void BeginAnalysis(SmvSnapshotResult snapshot, string? selectedKey, int generation)
    {
        var analysis = Interlocked.Increment(ref _analysisGeneration);
        var profile = RequestedUnits;
        try
        {
            // One bounded immutable publication: do not render waveform from
            // snapshot N while measurements/phasors still belong to N-1.
            // Both DSP and the eight-channel projection stay off the UI thread.
            var rendered = await Task.Run(() =>
            {
                var vectors = SmvRawVectorMath.Analyze(snapshot);
                var waveform = SmvDigSubPresentationAdapter.BuildWaveform(snapshot, profile);
                return (vectors, waveform);
            });
            if (generation != _generation || analysis != _analysisGeneration ||
                !IsLoaded || selectedKey != Volatile.Read(ref _selectedKey))
                return;

            // The WPF dispatcher paints after this synchronous update returns,
            // ensuring all panes represent one matching selected SV snapshot.
            _visibleSnapshot = snapshot;
            _visibleVectors = SmvDigSubPresentationAdapter.RelativeMeasurements(rendered.vectors);
            CurrentWaveform = rendered.waveform;
            Notify(nameof(CurrentWaveform));
            IsLiveSnapshot = true;
            Notify(nameof(IsLiveSnapshot));
            UpdateUnitPresentation(redrawWaveform: profile != RequestedUnits);
            if (profile == RequestedUnits)
                WaveformStatus =
                    (snapshot.TimebaseReason.StartsWith("ASSUMED", StringComparison.OrdinalIgnoreCase)
                        ? "ASSUMED timebase · rate/PTP unverified · " : string.Empty) +
                    $"Received {snapshot.CapturedSamples:N0} coherent samples • {snapshot.SamplesPerCycle} samples/cycle • " +
                    (ActiveUnits == SmvDisplayUnits.NineTwoLe
                        ? "9-2LE operator-selected engineering display"
                        : "raw INT32 counts");
        }
        catch (Exception)
        {
            if (generation != _generation || analysis != _analysisGeneration ||
                selectedKey != Volatile.Read(ref _selectedKey))
                return;
            // Do not erase the last coherent snapshot because a later
            // calculation failed; notify the operator instead.
            WaveformStatus = "New SV analysis unavailable; displaying last coherent snapshot.";
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
        PowerPhases.Clear();
        _powerTotal = null;
        NotifyPowerStatus();
        WaveformStatus = message;
        CurrentWaveform = null;
        Notify(nameof(CurrentWaveform));
        CurrentStreamKey = Volatile.Read(ref _selectedKey) ?? string.Empty;
        Notify(nameof(CurrentStreamKey));
        IsLiveSnapshot = false;
        Notify(nameof(IsLiveSnapshot));
        UpdateUnitPresentation(redrawWaveform: false);
    }

    private void UpdateDigSubPhasors()
    {
        CurrentPhasors = SmvDigSubPresentationAdapter.BuildPhasors(_visibleVectors, ActiveUnits);
        Notify(nameof(CurrentPhasors));
    }

    private void UnitsMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (WaveformView is null || UnitsModeCombo is null)
            return; // SelectionChanged also fires while InitializeComponent constructs the UI.
        UpdateUnitPresentation(redrawWaveform: true);
    }

    private void UpdateUnitPresentation(bool redrawWaveform)
    {
        var profile = ActiveUnits;
        if (redrawWaveform && _visibleSnapshot is { } snapshot)
        {
            CurrentWaveform = SmvDigSubPresentationAdapter.BuildWaveform(snapshot, profile);
            Notify(nameof(CurrentWaveform));
            WaveformStatus =
                (snapshot.TimebaseReason.StartsWith("ASSUMED", StringComparison.OrdinalIgnoreCase)
                    ? "ASSUMED timebase · rate/PTP unverified · " : string.Empty) +
                $"Received {snapshot.CapturedSamples:N0} coherent samples • {snapshot.SamplesPerCycle} samples/cycle • " +
                (profile == SmvDisplayUnits.NineTwoLe
                    ? "9-2LE operator-selected engineering display"
                    : "raw INT32 counts");
        }

        // The immutable wire/DSP vectors remain raw. Scaling is presentation-only.
        // WPF collections remain bounded to at most the 8 displayed channels.
        // Preserve the same bounded DataGrid collection/view and replace only
        // changed rows; Clear/Add resets WPF row containers and visibly flickers.
        // The phase reference is selected at PRESENTATION time; no wire value
        // or DFT result is overwritten.
        var presentationVectors = SmvDigSubPresentationAdapter.RelativeMeasurements(_visibleVectors, profile);
        var rowIndex = 0;
        foreach (var raw in presentationVectors)
        {
            var next = SmvEngineeringDisplay.Present(raw, profile);
            if (rowIndex < Measurements.Count)
            {
                if (!Equals(Measurements[rowIndex], next))
                    Measurements[rowIndex] = next;
            }
            else
                Measurements.Add(next);
            rowIndex++;
        }
        while (Measurements.Count > rowIndex)
            Measurements.RemoveAt(Measurements.Count - 1);

        // A single two-cycle immutable snapshot is the only source of power
        // calculations; never pair samples across different publishers/frames.
        var power = SmvPowerMeasurements.Analyze(_visibleSnapshot, _visibleVectors, profile);
        var phaseCount = 0;
        if (power is { } actual)
            foreach (var phase in actual.Phases)
            {
                if (phaseCount < PowerPhases.Count)
                {
                    if (!Equals(PowerPhases[phaseCount], phase))
                        PowerPhases[phaseCount] = phase;
                }
                else
                    PowerPhases.Add(phase);
                phaseCount++;
            }
        while (PowerPhases.Count > phaseCount)
            PowerPhases.RemoveAt(PowerPhases.Count - 1);
        _powerTotal = power?.Total;
        NotifyPowerStatus();
        UpdateDigSubPhasors();
        Notify(nameof(UnitStatus));
        Notify(nameof(VoltageGroupHeading));
        Notify(nameof(CurrentGroupHeading));
        Notify(nameof(PhaseReferenceText));
        Notify(nameof(TimeAxisCaption));
        Notify(nameof(SyncLabel));
        Notify(nameof(SyncDetail));
        Notify(nameof(SyncBackground));
        Notify(nameof(SyncForeground));
    }

    private void NotifyPowerStatus()
    {
        Notify(nameof(PowerP));
        Notify(nameof(PowerQ));
        Notify(nameof(PowerS));
        Notify(nameof(PowerPf));
        Notify(nameof(PowerStatus));
        Notify(nameof(PowerStatusShort));
        Notify(nameof(PowerDetails));
    }

    private void WaveformOptionsChanged(object sender, SelectionChangedEventArgs e)
    {
        // Controls may raise selection events during InitializeComponent.
        if (WaveformView is null || ShowModeCombo is null ||
            LayoutModeCombo is null || ScaleModeCombo is null)
            return;
        var mode = (ShowModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Both";
        WaveformView.ShowMode = mode switch
        {
            "Group A" => "Voltage",
            "Group B" => "Current",
            _ => "Both"
        };
        WaveformView.LayoutMode =
            (LayoutModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Overlay";
        var scale = (ScaleModeCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Auto Peak Hold";
        WaveformView.VoltageScale = scale;
        WaveformView.CurrentScale = scale;
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
