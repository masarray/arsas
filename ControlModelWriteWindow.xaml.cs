using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using AR.Iec61850.Control;

namespace ArIED61850Tester;

/// <summary>
/// One CF value editor. Read → select → Write → exact readback.
/// No typed phrases, extra checkboxes, command-service Operate, or retries.
/// </summary>
public partial class ControlModelWriteWindow : Window
{
    public sealed record ModelChoice(Iec61850ControlModelKind Model, string Label);
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly SignalDefinition _signal;
    private readonly CancellationTokenSource _cancellation = new();
    private bool _busy;
    public bool VerifiedWriteOccurred { get; private set; }
    public bool WriteAttempted { get; private set; }
    public string WriteStatus { get; private set; } = "";
    public long WriteAssociation { get; private set; }
    private bool _liveModelReady;
    private Iec61850ControlModelKind _liveModel = Iec61850ControlModelKind.Unknown;
    // Read/GVA evidence belongs to one live MMS association and one immutable
    // model instance. A reconnect cannot keep a previously enabled Write valid.
    private long _inspectedAssociation;
    private object? _inspectedLiveModel;

    private bool InspectionIsCurrent =>
        _inspectedLiveModel is not null &&
        ReferenceEquals(_device.LiveDiscoveryModel, _inspectedLiveModel) &&
        _device.LiveValues.Association == _inspectedAssociation &&
        _device.IsConnected && !_cancellation.IsCancellationRequested;

    public ObservableCollection<ModelChoice> Choices { get; } = new()
    {
        new(Iec61850ControlModelKind.StatusOnly, "0  [status-only]"),
        new(Iec61850ControlModelKind.DirectNormal, "1  [direct-with-normal-security]"),
        new(Iec61850ControlModelKind.SboNormal, "2  [sbo-with-normal-security]"),
        new(Iec61850ControlModelKind.DirectEnhanced, "3  [direct-with-enhanced-security]"),
        new(Iec61850ControlModelKind.SboEnhanced, "4  [sbo-with-enhanced-security]")
    };

    public ControlModelWriteWindow(
        Iec61850MonitorRuntime runtime, Iec61850MonitorDevice device, SignalDefinition signal)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));
        InitializeComponent();
        DataContext = this;
        ReferenceLabel.Text = _signal.ObjectReference + ".ctlModel";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
        => await ReadModelAsync();

    private void ModelPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => UpdateWriteButton();

    private void UpdateWriteButton()
    {
        ReadButton.IsEnabled = !_busy;
        WriteButton.IsEnabled = !_busy && _liveModelReady && InspectionIsCurrent &&
            ModelPicker.SelectedItem is ModelChoice choice && choice.Model != _liveModel;
    }

    private async Task ReadModelAsync()
    {
        _busy = true;
        _liveModelReady = false;
        _inspectedLiveModel = null;
        ModelPicker.IsEnabled = false;
        UpdateWriteButton();
        StatusLabel.Text = "Reading current ctlModel…";
        var association = _device.LiveValues.Association;
        var model = _device.LiveDiscoveryModel;
        ResultBanner.Background = System.Windows.Media.Brushes.Transparent;
        try
        {
            if (!_device.IsConnected || model is null)
                throw new InvalidOperationException("A connected IED with verified live model is required.");
            var capabilities = await _runtime.InspectControlAsync(
                _device.DeviceId, _signal, _cancellation.Token);
            if (_cancellation.IsCancellationRequested || !IsLoaded)
                return;
            if (!_device.IsConnected || _device.LiveValues.Association != association ||
                !ReferenceEquals(_device.LiveDiscoveryModel, model))
            {
                StatusLabel.Text = "IED connection or live model changed. Read again before Write.";
                return;
            }
            _inspectedAssociation = association;
            _inspectedLiveModel = model;
            _liveModel = capabilities.ControlModel;
            _liveModelReady = _liveModel != Iec61850ControlModelKind.Unknown &&
                capabilities.EngineControlServiceAvailable;
            if (_liveModelReady)
            {
                ModelPicker.SelectedItem = Choices.FirstOrDefault(x => x.Model == _liveModel);
                ModelPicker.IsEnabled = true;
                StatusLabel.Text = $"Current: {(int)_liveModel} · {capabilities.ControlModelText}";
            }
            else
            {
                StatusLabel.Text = "ctlModel could not be read. Write disabled; check Diagnostics.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Read cancelled.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = $"Read failed: {ex.Message}";
        }
        finally
        {
            _busy = false;
            UpdateWriteButton();
        }
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (!_busy) await ReadModelAsync();
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_liveModelReady || !InspectionIsCurrent ||
            ModelPicker.SelectedItem is not ModelChoice selected || selected.Model == _liveModel)
            return;

        // Clicking Write is the operator's explicit action. No second modal
        // confirmation, no automatic retry and no IEC Control Operate.
        // Revalidate the exact association/model/selection at dispatch time.
        var expected = _liveModel;
        if (!InspectionIsCurrent || ModelPicker.SelectedItem is not ModelChoice current ||
            current.Model != selected.Model || _liveModel != expected)
        {
            _liveModelReady = false;
            UpdateWriteButton();
            ShowWriteFailure("IED connection or selection changed. Read again.");
            return;
        }

        _busy = true;
        _liveModelReady = false;
        ModelPicker.IsEnabled = false;
        UpdateWriteButton();
        StatusLabel.Text = "Writing ctlModel…";
        ResultBanner.Background = System.Windows.Media.Brushes.Transparent;
        string? failure = null;
        bool verified = false;
        WriteAttempted = true;
        WriteAssociation = _inspectedAssociation;
        try
        {
            // The pinned native dispatcher checks expected-old value and
            // executes exactly one CF Write followed by MMS readback.
            var result = await ConfigureExactModelAsync(expected, selected.Model, _cancellation.Token);
            if (_cancellation.IsCancellationRequested || !IsLoaded) return;
            verified = result.WriteAccepted && result.ReadbackVerified &&
                (int)result.AfterModel == (int)selected.Model;
            if (verified)
                VerifiedWriteOccurred = true;
            else
                failure = string.IsNullOrWhiteSpace(result.Detail)
                    ? "IED rejected the value or readback did not match."
                    : result.Detail;
        }
        catch (OperationCanceledException)
        {
            failure = "Connection interrupted; Write outcome is unknown.";
        }
        catch (Exception ex)
        {
            failure = ex.Message;
        }
        finally
        {
            WriteStatus = verified
                ? "ctlModel Write verified; refreshing Data Model…"
                : "ctlModel Write ditolak / unknown: " +
                  (string.IsNullOrWhiteSpace(failure) ? "IED did not verify Write." : failure);
            _busy = false;
            // Browser owns the post-Write MMS Read and SSOT publication.
            // Close the editor immediately, including on rejected Writes.
            if (IsLoaded) Close();
        }
    }

    private void ShowWriteFailure(string? detail)
    {
        if (_cancellation.IsCancellationRequested || !IsLoaded) return;
        var reason = string.IsNullOrWhiteSpace(detail) ? "IED did not verify Write." : detail.Trim();
        StatusLabel.Text = "Write ditolak / belum terverifikasi: " +
            (reason.Length > 150 ? reason[..147] + "…" : reason);
        ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(255, 242, 228));
    }

    // Compile-time transport bridge; no reflection or ad-hoc MMS Write.
    private Task<Iec61850ControlModelConfigurationResult> ConfigureExactModelAsync(
        Iec61850ControlModelKind expected, Iec61850ControlModelKind selected,
        CancellationToken cancellationToken)
        => Iec61850ControlModelWriteDispatcher.ConfigureAsync(
            _runtime, _device.DeviceId, _signal, expected, selected, cancellationToken);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closing(object? sender, CancelEventArgs e) => _cancellation.Cancel();
}
