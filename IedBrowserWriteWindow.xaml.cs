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
    // A ctlModel Write may reach the wire even if cancellation hides its reply.
    // Invalidate stale Command Dock permission on modal close in that case.
    public HashSet<string> PotentiallyChangedControlModels { get; } =
        new(StringComparer.OrdinalIgnoreCase);
    public long WriteAssociation => _association;
    public List<(string Reference, string Fc)> AttemptedLeaves { get; } = new();
    public string WriteStatus { get; private set; } = "";
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
        foreach (var row in Rows)
            row.PropertyChanged += (_, _) => UpdateWriteEnabled();
        InitializeComponent();
        DataContext = this;
        IdentityText.Text = _device.Name + " · " + _device.IpAddress +
            " · association " + _association + " · " + Rows.Count +
            " live-verified configuration DA(s)";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Height = Math.Max(MinHeight, Math.Min(520, work.Height - 26));
        Width = Math.Max(MinWidth, Math.Min(940, work.Width - 26));
        // operator-friendly editor: preload bounded current values from this live
        // association. Read-only traffic only; never write on opening.
        await ReadRowsAsync(Rows.Take(24).ToArray());
    }

    private void UpdateWriteEnabled()
    {
        if (WriteButton is null) return;
        WriteButton.IsEnabled = !_busy && CurrentAssociation &&
            Rows.Any(row => row.Selected && row.HasVerifiedRead && row.HasEdited);
    }

    private bool CurrentAssociation =>
        _device.IsConnected && !_device.IsDemo &&
        !_lifetime.IsCancellationRequested &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model) &&
        _device.LiveValues.Association == _association;

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ReadButton.IsEnabled = !busy;
        UpdateWriteEnabled();
        WriteRowsGrid.IsEnabled = !busy;
    }

    private async void Read_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !CurrentAssociation) return;
        WriteRowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        WriteRowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        var selected = Rows.Where(row => row.Selected).ToArray();
        if (selected.Length == 0)
        {
            StatusText.Text = "Select the Data Attributes you want to read.";
            return;
        }
        await ReadRowsAsync(selected);
    }

    private async Task ReadRowsAsync(IedBrowserWriteRow[] selected)
    {
        if (_busy || !CurrentAssociation || selected.Length == 0) return;
        SetBusy(true);
        try
        {
            var i = 0;
            foreach (var row in selected)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed. Reopen Write.");
                StatusText.Text = "Reading " + (++i) + "/" + selected.Length + " · " + row.ShortName;
                var observation = await _runtime.ReadBrowserConfigurationAsync(
                    _device.DeviceId, _model, _association, row, _lifetime.Token);
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed. Reopen Write.");
                if (observation.IsSuccess && observation.Value is not null)
                {
                    row.ApplyLiveRead(observation.Value);
                    row.Result = "Ready to edit";
                }
                else
                {
                    row.ExpireLiveRead();
                    row.Current = "Read unavailable";
                    row.Result = "Read failed: " + observation.Message;
                }
            }
            StatusText.Text = "Choose a value, tick its row and click Write. No Write sent.";
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
        if (selected.Any(row => !row.HasVerifiedRead || !row.HasEdited ||
                              row.LiveBeforeValue is null))
        {
            StatusText.Text = "Read the selected DAs and choose a different value before Write.";
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
            {
                var candidate = IedBrowserWritePlan.EncodeValue(
                    row.DataType, row.RequestedValue, row.Reference);
                if (MmsDataCodec.Encode(candidate).AsSpan().SequenceEqual(
                        MmsDataCodec.Encode(row.LiveBeforeValue!)))
                {
                    StatusText.Text = row.ShortName + ": unchanged value. No Write sent.";
                    return;
                }
                staged.Add((row, candidate));
            }
        }
        catch (Exception ex) when (ex is FormatException or OverflowException or NotSupportedException)
        {
            StatusText.Text = "Typed validation failed; zero Writes sent: " + ex.Message;
            return;
        }

        // The enabled Write button is explicit operator intent. Native
        // configuration admission still verifies the same association,
        // typed before-value and readback. No second modal prompt.
        if (!CurrentAssociation)
        {
            StatusText.Text = "IED connection changed; no Write sent.";
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
                row.ExpireLiveRead(); // each attempt requires a NEW explicit Read
                if (row.IsControlModel)
                    PotentiallyChangedControlModels.Add(row.Reference[..^".ctlModel".Length]);
                AttemptedLeaves.Add((row.Reference, row.FunctionalConstraint));
                var result = await _runtime.WriteBrowserConfigurationAsync(
                    _device.DeviceId, _model, _association, row, requested, _lifetime.Token);
                row.Current = result.ReadbackVerified ? result.After : result.Before;
                row.Result = result.NoChange ? "Unchanged · verified" :
                    result.Succeeded ? "Write + readback PASS" :
                    "NOT VERIFIED: " + result.Detail;
                if (!result.Succeeded)
                {
                    var reason = string.IsNullOrWhiteSpace(result.Detail)
                        ? "IED refused the requested value." : result.Detail;
                    StatusText.Text = "Write ditolak: " +
                        (reason.Length > 130 ? reason[..127] + "…" : reason) +
                        $" · {passed}/{staged.Count} verified. No automatic retry.";
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 242, 228));
                    return;
                }
                passed++;
            }
            StatusText.Text = "Verified " + passed + "/" + staged.Count +
                " configuration DA(s) using native MMS readback. No IEC Operate issued.";
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(232, 248, 237));
        }
        catch (Exception ex)
        {
            StatusText.Text = "Write unresolved: " + ex.Message +
                " · no automatic retry.";
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 242, 228));
        }
        finally
        {
            WriteStatus = StatusText.Text;
            SetBusy(false);
            if (AttemptedLeaves.Count > 0 && IsLoaded) Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
        => _lifetime.Cancel();
}
