using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Explicit destructive BRCB action in IED Browser. No write on open/read,
/// exact typed confirmation, no retry, and no separate MMS connection.
/// </summary>
public partial class BrowserRcbPurgeWindow : Window
{
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly LiveIedModelDiscoveryDocument _model;
    private readonly string _rcbReference;
    private readonly long _association;
    private bool _busy;

    public bool WriteAttempted { get; private set; }
    public bool WriteAccepted { get; private set; }
    public bool ReadbackVerified { get; private set; }
    public string WriteStatus { get; private set; } = "No MMS Write sent.";

    private bool IsCurrent => _device.IsConnected && !_device.IsDemo &&
        !_device.IsBusy && !_device.IsMonitoring &&
        !_device.LiveValues.IsOffline &&
        _device.LiveValues.Association == _association &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model);

    public BrowserRcbPurgeWindow(
        Iec61850MonitorRuntime runtime, Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model, string rcbReference)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        if (!BrowserPurgeBufPolicy.TryResolve(model, rcbReference, out _rcbReference))
            throw new InvalidOperationException("Only an exact current live BRCB permits a manual PurgeBuf action.");
        _association = device.LiveValues.Association;
        InitializeComponent();
        RcbReferenceText.Text = _rcbReference + " [BR] · IED: " + device.Name;
        RefreshAdmission();
    }

    private void ConfirmationBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshAdmission();

    private void RefreshAdmission()
    {
        if (PurgeButton is null || ConfirmationBox is null) return;
        PurgeButton.IsEnabled = !_busy && !WriteAttempted && IsCurrent &&
            string.Equals(ConfirmationBox.Text, "PURGE", StringComparison.Ordinal);
    }

    private async void Purge_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || WriteAttempted || !IsCurrent ||
            !string.Equals(ConfirmationBox.Text, "PURGE", StringComparison.Ordinal))
        {
            WriteStatus = "Live association or explicit confirmation is missing; no Write sent.";
            StatusText.Text = WriteStatus;
            RefreshAdmission();
            return;
        }

        _busy = true;
        WriteAttempted = true;
        RefreshAdmission();
        ConfirmationBox.IsEnabled = false;
        CloseButton.IsEnabled = false;
        StatusText.Text = "Checking the live BRCB and sending at most one confirmed MMS Write…";
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        try
        {
            var outcome = await _runtime.WriteBrowserPurgeBufAsync(
                _device.DeviceId, _model, _association, _rcbReference,
                explicitOperatorConfirmation: true, cts.Token);
            WriteAccepted = outcome.WriteAccepted;
            ReadbackVerified = outcome.ReadbackVerified;
            WriteStatus = outcome.Detail;
            StatusText.Text = WriteStatus;
        }
        catch (OperationCanceledException)
        {
            WriteStatus = "PurgeBuf response unresolved after cancellation/timeout. Do not retry automatically.";
            StatusText.Text = WriteStatus;
        }
        catch (Exception ex)
        {
            WriteStatus = "PurgeBuf not verified: " + ex.Message + ". Do not retry automatically.";
            StatusText.Text = WriteStatus;
        }
        finally
        {
            _busy = false;
            CloseButton.IsEnabled = true;
            CloseButton.Content = "Close";
            RefreshAdmission();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        if (_busy)
        {
            e.Cancel = true;
            StatusText.Text = "MMS Write still in flight; close after the outcome is resolved.";
        }
    }
}
