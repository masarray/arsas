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
        FailureDetails.Visibility = Visibility.Collapsed;
        FailureDetails.Text = string.Empty;
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
        // Explicit operator confirmation: changing ctlModel affects later
        // command admission and must never be a single accidental click.
        var expected = _liveModel;
        var decision = MessageBox.Show(
            this,
            $"IED: {_device.Name}\n" +
            $"DA: {_signal.ObjectReference}.ctlModel [CF]\n" +
            $"Current: {(int)expected}\n" +
            $"Requested: {selected.Label}\n\n" +
            "Send one configuration Write and verify its exact readback?\n" +
            "No Operate or automatic retry will be sent.",
            "Confirm IEC 61850 ctlModel Write",
            MessageBoxButton.OKCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (decision != MessageBoxResult.OK)
            return;
        // An operator may spend minutes in the confirmation dialog. Recheck
        // live identity AFTER confirmation and BEFORE entering the native call.
        if (!InspectionIsCurrent || ModelPicker.SelectedItem is not ModelChoice current ||
            current.Model != selected.Model || _liveModel != expected)
        {
            _liveModelReady = false;
            UpdateWriteButton();
            ShowWriteFailure("IED association or selected ctlModel changed. Read again; no Write was sent.");
            return;
        }
        // Native ARIEC will additionally compare expected ctlModel, dispatch
        // at most one CF Write, and independently verify MMS readback.
        _busy = true;
        _liveModelReady = false;
        ModelPicker.IsEnabled = false;
        UpdateWriteButton();
        StatusLabel.Text = "Writing ctlModel and checking readback…";
        try
        {
            var result = await ConfigureExactModelAsync(expected, selected.Model, _cancellation.Token);
            if (!result.WriteAccepted || !result.ReadbackVerified ||
                (int)result.AfterModel != (int)selected.Model)
            {
                ShowWriteFailure(result.Detail, (int)selected.Model, (int)result.AfterModel, result.ReadbackVerified);
                return;
            }

            // The engine has completed the exact CF write and authoritative MMS readback.
            // Do not issue a second inspection or keep the commissioning dialog open.
            if (!_cancellation.IsCancellationRequested && IsLoaded)
                DialogResult = true;
        }
        catch (OperationCanceledException)
        {
            ShowWriteFailure("Connection interrupted or operation cancelled. Read actual ctlModel before retrying.");
        }
        catch (Exception ex)
        {
            ShowWriteFailure($"Write not verified: {ex.Message}. Read actual ctlModel.");
        }
        finally
        {
            _busy = false;
            UpdateWriteButton();
        }
    }

    private void ShowWriteFailure(string? detail,
        int? expected = null,
        int? actual = null, bool readbackVerified = false)
    {
        if (_cancellation.IsCancellationRequested || !IsLoaded)
            return;

        var reason = string.IsNullOrWhiteSpace(detail)
            ? "IED did not provide a verified write result."
            : detail.Trim();
        if (readbackVerified && expected.HasValue && actual.HasValue && expected != actual)
            reason = $"Readback mismatch • Expected {expected.Value}, actual {actual.Value}. {reason}";

        FailureDetails.Text = reason;
        FailureDetails.Visibility = Visibility.Visible;
        StatusLabel.Text = "Write not verified • no further write will be sent automatically.";
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
