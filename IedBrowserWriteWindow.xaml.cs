using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Operator-initiated IEC 61850 MMS Write dialog, not an Operate command.
/// UI stages edits, runtime validates current association and ARIEC performs
/// the single native wire Write with independent before/after typed MMS reads.
/// </summary>
public partial class IedBrowserWriteWindow : Window
{
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly LiveIedModelDiscoveryDocument _model;
    private readonly long _association;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    public ObservableCollection<IedBrowserWriteRow> Rows { get; }

    public IedBrowserWriteWindow(
        Iec61850MonitorRuntime runtime, Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model, IReadOnlyList<IedBrowserWriteRow> rows)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(rows);
        _association = device.LiveValues.Association;
        Rows = new ObservableCollection<IedBrowserWriteRow>(rows);
        InitializeComponent();
        DataContext = this;
        IdentityText.Text = _device.Name + " · " + _device.IpAddress +
            " · association " + _association + " · " + Rows.Count +
            " live-verified configuration DA(s)";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
        => StatusText.Text = "Select only the DA(s) you intend to change. Read selected to inspect current live values.";

    private bool CurrentAssociation =>
        _device.IsConnected && !_device.IsDemo &&
        !_lifetime.IsCancellationRequested &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model) &&
        _device.LiveValues.Association == _association;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ReadButton.IsEnabled = !busy;
        WriteButton.IsEnabled = !busy;
        WriteRowsGrid.IsEnabled = !busy;
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CurrentAssociation) return;
        WriteRowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        var selected = Rows.Where(row => row.Selected).ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select one or more DA rows for an explicit live MMS Read.";
            return;
        }

        SetBusy(true);
        try
        {
            var i = 0;
            foreach (var row in selected)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation)
                    throw new InvalidOperationException("The association changed. Close and reopen the editor.");
                StatusText.Text = "Reading " + (++i) + "/" + selected.Length + " · " + row.Reference;
                var observation = await _runtime.ReadBrowserConfigurationAsync(
                    _device.DeviceId, _model, _association, row, _lifetime.Token);
                row.Current = observation.IsSuccess && observation.Value is not null
                    ? MmsDataCodec.ToDisplayString(observation.Value)
                    : "Read failed";
                row.Result = observation.IsSuccess ? "Live Read OK" :
                    "Read rejected: " + observation.Message;
            }
            StatusText.Text = "Live MMS Read completed; no configuration was changed.";
        }
        catch (OperationCanceledException)
        {
            StatusText.Text = "Read cancelled; no Write attempted.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Read stopped: " + ex.Message;
        }
        finally { SetBusy(false); }
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CurrentAssociation) return;
        WriteRowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        WriteRowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = Rows.Where(row => row.Selected).ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select at least one DA; no Write was sent.";
            return;
        }
        if (selected.Length > 16)
        {
            StatusText.Text = "At most 16 explicitly staged DA(s) per supervised Write. Refine the selection.";
            return;
        }

        var staged = new List<(IedBrowserWriteRow Row, MmsDataValue Value)>();
        try
        {
            foreach (var row in selected)
                staged.Add((row, IedBrowserWritePlan.EncodeValue(
                    row.DataType, row.RequestedValue, row.Reference)));
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or NotSupportedException)
        {
            StatusText.Text = "Typed validation failed; zero Writes sent: " + ex.Message;
            return;
        }

        var review = string.Join(Environment.NewLine,
            selected.Select(row => "  " + row.Reference + " [" + row.FunctionalConstraint +
                                   "] = " + row.RequestedValue));
        var notice = "IED: " + _device.Name + " (" + _device.IpAddress + ")" +
            Environment.NewLine + Environment.NewLine +
            "Change these " + selected.Length + " configuration DA(s)?" +
            Environment.NewLine + review + Environment.NewLine + Environment.NewLine +
            "These are real MMS Writes. A ctlModel change may change permitted IEC control modes." +
            Environment.NewLine +
            "The operation is sequential, NOT ATOMIC. Stops on the first failure; no automatic retry." +
            Environment.NewLine + "Proceed only with authorized, isolated commissioning.";
        if (MessageBox.Show(this, notice, "Confirm live MMS configuration Write",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) !=
            MessageBoxResult.Yes)
        {
            StatusText.Text = "Operator cancelled. Zero Writes sent.";
            return;
        }

        SetBusy(true);
        var passed = 0;
        try
        {
            foreach (var (row, requested) in staged)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation)
                    throw new InvalidOperationException("Live MMS association/model changed. No further Write.");
                StatusText.Text = "Writing " + (passed + 1) + "/" + staged.Count +
                    " · " + row.Reference;
                row.Result = "Write / readback pending…";
                var result = await _runtime.WriteBrowserConfigurationAsync(
                    _device.DeviceId, _model, _association, row, requested, _lifetime.Token);
                row.Current = result.ReadbackVerified ? result.After : result.Before;
                row.Result = result.NoChange ? "Unchanged · verified" :
                    result.Succeeded ? "Write + readback PASS" :
                    "NOT VERIFIED: " + result.Detail;
                if (!result.Succeeded)
                {
                    StatusText.Text = "STOP: " + passed + " of " + staged.Count +
                        " completed; " + row.Reference + ": " + result.Detail +
                        " Later rows were NOT sent. Verify on IED before retry.";
                    return;
                }
                passed++;
            }
            StatusText.Text = "Verified " + passed + "/" + staged.Count +
                " configuration DA(s) using native MMS readback. No IEC Operate issued.";
        }
        catch (Exception ex)
        {
            StatusText.Text = "STOP after " + passed + " completed · " + ex.Message +
                " Outcome of interrupted Write may be UNKNOWN. Never retry automatically.";
        }
        finally { SetBusy(false); }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
        => _lifetime.Cancel();
}
