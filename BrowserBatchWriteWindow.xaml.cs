using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Explicit, bounded same-DO multi-DA editor. Only user-checked and changed
/// scalar leaves enter the immutable native transaction plan; no DO overwrite.
/// All network access is through the existing association and runtime.
/// </summary>
public partial class BrowserBatchWriteWindow : Window
{
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly LiveIedModelDiscoveryDocument _model;
    private readonly long _association;
    private readonly CancellationTokenSource _lifetime = new();
    private bool _busy;
    private bool _prepared;

    public ObservableCollection<BrowserBatchWriteRow> Rows { get; } = new();

    public BrowserBatchWriteWindow(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        LiveIedModelDiscoveryDocument model,
        LiveIedDataObjectModel owner)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(owner);
        if (owner.Attributes.Count is 0 or > 256)
            throw new InvalidOperationException("The focused DO needs 1–256 explicit attributes.");

        _association = device.LiveValues.Association;
        var duplicated = owner.Attributes
            .GroupBy(a => a.FunctionalConstraint + "|" + a.MmsReference, StringComparer.Ordinal)
            .Where(g => g.Count() > 1)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);

        foreach (var attr in owner.Attributes)
        {
            MmsObjectReference? reference = null;
            try { reference = MmsObjectReference.Parse(attr.MmsReference, attr.FunctionalConstraint); }
            catch (Exception ex) when (ex is ArgumentException or FormatException) { }

            var parts = reference?.Item.Split('$') ?? [];
            var sameOwner = reference is not null && parts.Length >= 4 &&
                string.Equals(parts[1], reference.Value.FunctionalConstraint, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(reference.Value.Domain + "/" + parts[0] + "." + parts[2],
                    owner.Reference, StringComparison.Ordinal);
            var fcOk = reference is not null && reference.Value.FunctionalConstraint.ToUpperInvariant() is "CF" or "SP" or "DC";
            var isControlMember = parts.Skip(2).Any(p => p is "Oper" or "SBO" or "SBOw" or "Cancel" or "ctlModel");
            var isDuplicate = duplicated.Contains(attr.FunctionalConstraint + "|" + attr.MmsReference);
            var eligible = sameOwner && fcOk && !isControlMember && !isDuplicate;
            var row = new BrowserBatchWriteRow(
                reference, attr.MmsReference, attr.FunctionalConstraint, eligible,
                !sameOwner ? "Not an exact live DA of this DO" :
                isDuplicate ? "Ambiguous duplicate in live model" :
                isControlMember ? "Use IEC Control / dedicated ctlModel service" :
                !fcOk ? "FC not approved for generic Write" : "Select, edit and inspect");
            row.PropertyChanged += Row_PropertyChanged;
            Rows.Add(row);
        }

        InitializeComponent();
        DataContext = this;
        IdentityLabel.Text = device.Name + " · " + owner.Reference +
            " · association " + _association;
    }

    private bool CurrentAssociation =>
        !_lifetime.IsCancellationRequested && _device.IsConnected &&
        !_device.IsDemo && !_device.IsBusy && !_device.IsMonitoring &&
        !_device.LiveValues.IsOffline &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model) &&
        _device.LiveValues.Association == _association;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserBatchWriteRow.Selected) or
            nameof(BrowserBatchWriteRow.NewValue))
        {
            _prepared = false;
            if (WriteButton is not null) WriteButton.IsEnabled = false;
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RowsGrid.IsEnabled = !busy;
        InspectButton.IsEnabled = !busy;
        WriteButton.IsEnabled = !busy && _prepared;
    }

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Height = Math.Max(MinHeight, Math.Min(580, work.Height - 26));
        Width = Math.Max(MinWidth, Math.Min(760, work.Width - 26));
    }

    private BrowserBatchWriteRow[] CheckedRows()
    {
        RowsGrid.CommitEdit(DataGridEditingUnit.Cell, true);
        RowsGrid.CommitEdit(DataGridEditingUnit.Row, true);
        return Rows.Where(r => r.Selected).ToArray();
    }

    private async void Inspect_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        _prepared = false;
        WriteButton.IsEnabled = false;
        var selected = CheckedRows();
        if (selected.Length is 0 or > IedBrowserTypedWriteBatch.MaxSelectedAttributes ||
            selected.Any(r => !r.Eligible || !r.HasEdited) || !CurrentAssociation)
        {
            StatusLabel.Text =
                "Select 1–64 eligible DAs, explicitly edit each new value and keep the same online association. No Write sent.";
            return;
        }

        SetBusy(true);
        try
        {
            for (var i = 0; i < selected.Length; i++)
            {
                var row = selected[i];
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed during inspection.");
                StatusLabel.Text = $"Read-only inspect {i + 1}/{selected.Length}: {row.ReferenceText}";
                row.Preview = null;
                var preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                    _device, row.Reference!.Value, _lifetime.Token);
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed during inspection.");
                row.Preview = preview.IsAvailable ? preview : null;
                row.Type = preview.Type?.MmsType ?? "unverified";
                row.Old = preview.CurrentText;
                row.Status = preview.Message;
                if (row.Preview is null)
                {
                    StatusLabel.Text = "One DA has no valid live typed Read/GVA proof. No Write sent.";
                    return;
                }
            }

            if (!TryFreezePlan(selected, out _, out var rejection))
            {
                StatusLabel.Text = rejection + " No Write sent.";
                return;
            }
            _prepared = true;
            WriteButton.IsEnabled = true;
            StatusLabel.Text = $"{selected.Length} changed DA(s) inspected. Review all old/new values before confirming; zero Writes so far.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Inspection cancelled; no Write requested.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Inspection stopped (no Write sent): " + ex.Message;
        }
        finally { SetBusy(false); }
    }

    private static bool TryFreezePlan(
        IReadOnlyList<BrowserBatchWriteRow> selected,
        out IReadOnlyList<IedBrowserTypedWriteStep>? steps,
        out string reason)
    {
        steps = null;
        reason = "";
        var pending = new List<IedBrowserTypedWriteStep>(selected.Count);
        foreach (var row in selected)
        {
            var preview = row.Preview;
            if (!row.Eligible || !row.HasEdited ||
                preview is not { IsAvailable: true, Type: not null, OldValue: not null } ||
                row.Reference is null ||
                !IedBrowserTypedWriteInput.TryParse(preview.Type, row.NewValue,
                    out var proposed, out var error) || proposed is null)
            {
                reason = $"{row.ReferenceText}: typed value or inspection missing.";
                return false;
            }
            var live = new MmsVariableAccessAttributesResult
            {
                IsSuccess = true, Reference = row.Reference.Value,
                TypeSpecification = preview.Type
            };
            var reject = IedBrowserTypedWriteAdmission.RejectReason(
                row.Reference.Value, live, preview.GvaSignature, proposed);
            if (reject is not null)
            {
                reason = $"{row.ReferenceText}: {reject}";
                return false;
            }
            if (MmsDataCodec.Encode(preview.OldValue).AsSpan().SequenceEqual(
                    MmsDataCodec.Encode(proposed)))
            {
                reason = $"{row.ReferenceText}: unchanged value is not staged.";
                return false;
            }
            pending.Add(new(row.Reference.Value, preview.GvaSignature,
                preview.OldValue, proposed));
        }

        try { steps = IedBrowserTypedWriteBatch.Freeze(pending); }
        catch (ArgumentException ex) { reason = ex.Message; return false; }
        return true;
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_prepared || !CurrentAssociation) return;
        var selected = CheckedRows();
        if (!TryFreezePlan(selected, out var steps, out var reason) || steps is null)
        {
            StatusLabel.Text = "Write NOT SENT: " + reason;
            _prepared = false;
            WriteButton.IsEnabled = false;
            return;
        }

        var diff = string.Join(Environment.NewLine, steps.Select(s =>
            $"{s.Reference.Domain}/{s.Reference.Item} [{s.Reference.FunctionalConstraint}]" +
            $"\n  {MmsDataCodec.ToDisplayString(s.ExpectedOldValue)} → {MmsDataCodec.ToDisplayString(s.ProposedValue)}"));
        var confirmed = MessageBox.Show(this,
            $"IED: {_device.Name}\nDO: {IdentityLabel.Text}\n\n" + diff +
            $"\n\n{steps.Count} Confirmed-Write request(s), sequential, NOT ATOMIC. " +
            "A failure stops remaining DAs. No retry or rollback. " +
            "This can modify protection settings/configuration. Are you authorized to write?",
            "Confirm exact live MMS Writes", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirmed != MessageBoxResult.Yes)
        {
            StatusLabel.Text = "Operator declined; zero Writes sent.";
            return;
        }
        if (!CurrentAssociation)
        {
            StatusLabel.Text = "Association changed after review; zero Writes sent.";
            return;
        }

        SetBusy(true);
        _prepared = false;
        WriteButton.IsEnabled = false;
        try
        {
            StatusLabel.Text = $"Sending up to {steps.Count} individually confirmed DA Writes…";
            var result = await _runtime.WriteBrowserDataAttributesAsync(
                _device, steps, true, _lifetime.Token);
            for (var i = 0; i < result.Rows.Count; i++)
            {
                selected[i].Status = result.Rows[i].State?.ToString() ?? "Unattempted";
                selected[i].Status += ": " + result.Rows[i].Message;
                selected[i].Preview = null;
            }
            StatusLabel.Text =
                $"Readback verified: {result.Verified}; rejected: {result.Rejected}; " +
                $"unknown: {result.Unknown}; unverified: {result.Unverified}; " +
                $"not attempted: {result.Unattempted}. " +
                "Reinspect the IED before any new attempt; no physical operation is implied.";
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text =
                "Interrupted write; wire outcome may be UNKNOWN. Read actual IED before manual retry.";
        }
        catch (Exception ex)
        {
            StatusLabel.Text =
                "Write stopped/refused: " + ex.Message +
                ". Some in-flight outcomes may be unknown; do not retry automatically.";
        }
        finally
        {
            _prepared = false;
            SetBusy(false);
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
    private void Window_Closed(object? sender, EventArgs e) => _lifetime.Cancel();
}

public sealed class BrowserBatchWriteRow : INotifyPropertyChanged
{
    private bool _selected;
    private string _newValue = "";
    private string _status;
    private string _type = "Inspect first";
    private string _old = "—";

    public MmsObjectReference? Reference { get; }
    public string ReferenceText { get; }
    public string Fc { get; }
    public bool Eligible { get; }
    public bool HasEdited { get; private set; }
    public IedBrowserNativeWritePreview? Preview { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public BrowserBatchWriteRow(
        MmsObjectReference? reference, string referenceText, string fc,
        bool eligible, string reason)
    {
        Reference = reference;
        ReferenceText = referenceText;
        Fc = fc;
        Eligible = eligible;
        _status = reason;
    }

    public bool Selected
    {
        get => _selected;
        set { if (_selected != value) { _selected = value; Changed(); } }
    }
    public string NewValue
    {
        get => _newValue;
        set
        {
            if (_newValue == value) return;
            _newValue = value;
            HasEdited = true;
            Changed();
        }
    }
    public string Status
    {
        get => _status;
        set { _status = value; Changed(); }
    }
    public string Type
    {
        get => _type;
        set { _type = value; Changed(); }
    }
    public string Old
    {
        get => _old;
        set { _old = value; Changed(); }
    }
    private void Changed([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
