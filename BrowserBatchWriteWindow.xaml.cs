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
        !_device.IsDemo && !_device.IsBusy &&
        !_device.LiveValues.IsOffline &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model) &&
        _device.LiveValues.Association == _association;

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(BrowserBatchWriteRow.Selected) or
            nameof(BrowserBatchWriteRow.NewValue))
        {
            _prepared = false;
            if (WriteButton is not null) WriteButton.IsEnabled = CanWriteSelection;
        }
    }

    private bool CanWriteSelection => !_busy && CurrentAssociation &&
        Rows.Any(row => row.Selected && row.Eligible && row.CanEdit && row.HasEdited);

    private void SetBusy(bool busy)
    {
        _busy = busy;
        RowsGrid.IsEnabled = !busy;
        InspectButton.IsEnabled = !busy;
        WriteButton.IsEnabled = CanWriteSelection;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        var work = SystemParameters.WorkArea;
        Height = Math.Max(MinHeight, Math.Min(490, work.Height - 26));
        Width = Math.Max(MinWidth, Math.Min(660, work.Width - 26));
        // A DO editor opens populated, just like a single-DA editor. Bound
        // the initial read to prevent a large DO from blocking the operator;
        // the Read button can inspect an additional explicit selection.
        await PrimeLiveEditorsAsync();
    }

    private async Task PrimeLiveEditorsAsync()
    {
        if (!CurrentAssociation) return;
        var visible = Rows.Where(r => r.Eligible).Take(24).ToArray();
        SetBusy(true);
        try
        {
            foreach (var row in visible)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation) return;
                var preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                    _device, row.Reference!.Value, _lifetime.Token);
                if (!CurrentAssociation) return;
                row.Preview = preview.IsAvailable ? preview : null;
                row.Type = preview.Type?.MmsType ?? "unverified";
                row.Old = preview.CurrentText;
                row.ApplyLiveEditor(preview.Type?.MmsType == "boolean", preview.CurrentText);
                row.Status = preview.IsAvailable ? "Live Read" : preview.Message;
                row.NotifyEditorChanged();
            }
            StatusLabel.Text = $"Read {visible.Length} DA(s). Edit and select one or more values, then Write.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            StatusLabel.Text = "Read unavailable: " + ex.Message;
        }
        finally { SetBusy(false); }
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
            selected.Any(r => !r.Eligible) || !CurrentAssociation)
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
                row.NotifyEditorChanged();
                var preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                    _device, row.Reference!.Value, _lifetime.Token);
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed during inspection.");
                row.Preview = preview.IsAvailable ? preview : null;
                row.Type = preview.Type?.MmsType ?? "unverified";
                row.Old = preview.CurrentText;
                row.ApplyLiveEditor(preview.Type?.MmsType == "boolean", preview.CurrentText);
                row.Status = preview.Message;
                if (row.Preview is null)
                {
                    StatusLabel.Text = "One DA has no valid live typed Read/GVA proof. No Write sent.";
                    return;
                }
            }

            if (selected.All(r => !r.HasEdited))
            {
                StatusLabel.Text = $"Read {selected.Length} live DA(s). Edit one or more values before Write.";
                return;
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
        if (_busy || !CurrentAssociation) return;
        var selected = CheckedRows();
        if (selected.Length is 0 or > IedBrowserTypedWriteBatch.MaxSelectedAttributes ||
            selected.Any(r => !r.Eligible || !r.HasEdited || !r.CanEdit))
        {
            StatusLabel.Text = "Select and edit 1–64 live-readable DA values.";
            return;
        }

        // Clicking Write is sufficient operator intent. Perform fresh GVA +
        // current-value inspection before dispatch, freezing all target values.
        // No extra modal confirmation, no Write on open/Read/edit.
        SetBusy(true);
        _prepared = false;
        IReadOnlyList<IedBrowserTypedWriteStep>? steps = null;
        try
        {
            foreach (var row in selected)
            {
                _lifetime.Token.ThrowIfCancellationRequested();
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed; no Write sent.");
                var preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                    _device, row.Reference!.Value, _lifetime.Token);
                if (!CurrentAssociation)
                    throw new InvalidOperationException("IED association changed; no Write sent.");
                row.Preview = preview.IsAvailable ? preview : null;
                row.Type = preview.Type?.MmsType ?? "unverified";
                row.Old = preview.CurrentText;
                row.ApplyLiveEditor(preview.Type?.MmsType == "boolean", preview.CurrentText);
                row.NotifyEditorChanged();
            }
            if (!TryFreezePlan(selected, out steps, out var reason) || steps is null)
            {
                StatusLabel.Text = "Write ditolak: " + reason;
                return;
            }
            if (!CurrentAssociation)
                throw new InvalidOperationException("IED association changed; no Write sent.");

            StatusLabel.Text = $"Writing {steps.Count} changed DA(s) and verifying readback…";
            // Native typed admission checks every GVA/old-value/association;
            // sequential, stop-on-failure, no automatic retry or rollback.
            var result = await _runtime.WriteBrowserDataAttributesAsync(
                _device, steps, true, _lifetime.Token);
            for (var i = 0; i < selected.Length; i++)
            {
                var row = selected[i];
                if (i < result.Rows.Count)
                {
                    row.Status = result.Rows[i].State + ": " + result.Rows[i].Message;
                    row.Preview = null;
                    row.NotifyEditorChanged();
                }
            }

            // Refresh every selected DA regardless of success/rejection.
            // Verify the newly displayed value against the staged exact MMS
            // bytes; an independent mismatch can never earn a green toast.
            var readbackMismatch = false;
            for (var i = 0; i < selected.Length; i++)
            {
                var row = selected[i];
                if (!CurrentAssociation)
                {
                    row.Status += " · connection changed";
                    readbackMismatch = true;
                    break;
                }
                try
                {
                    var actual = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                        _device, row.Reference!.Value, _lifetime.Token);
                    if (!CurrentAssociation)
                    {
                        readbackMismatch = true;
                        break;
                    }
                    if (actual is { IsAvailable: true, OldValue: not null })
                    {
                        var matches = MmsDataCodec.Encode(actual.OldValue).AsSpan()
                            .SequenceEqual(MmsDataCodec.Encode(steps[i].ProposedValue));
                        row.Old = actual.CurrentText;
                        row.Preview = actual;
                        row.ResetToReadback(actual.CurrentText);
                        if (!matches)
                        {
                            row.Status += " · Readback differs: " + actual.CurrentText;
                            readbackMismatch = true;
                        }
                        row.NotifyEditorChanged();
                    }
                    else
                    {
                        row.Status += " · Readback unavailable";
                        readbackMismatch = true;
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    row.Status += " · Readback unavailable: " + ex.Message;
                    readbackMismatch = true;
                }
            }
            var anyFailure = readbackMismatch ||
                result.Rejected > 0 || result.Unknown > 0 ||
                result.Unverified > 0 || result.Unattempted > 0;
            var shortReason = result.Rows.LastOrDefault()?.Message ?? "IED refused the requested value.";
            if (shortReason.Length > 110) shortReason = shortReason[..107] + "…";
            StatusLabel.Text = anyFailure
                ? $"Write ditolak · {result.Verified} verified, {result.Rejected} rejected, " +
                  $"{result.Unknown} unknown · {shortReason}"
                : $"Write verified · {result.Verified} DA(s) · values refreshed from IED.";
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                anyFailure ? System.Windows.Media.Color.FromRgb(255, 242, 228)
                           : System.Windows.Media.Color.FromRgb(232, 248, 237));
        }
        catch (OperationCanceledException)
        {
            StatusLabel.Text = "Write uncertain; read the actual IED state before manual retry.";
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 242, 228));
        }
        catch (Exception ex)
        {
            StatusLabel.Text = "Write ditolak / unresolved: " + ex.Message;
            ResultBanner.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(255, 242, 228));
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
    private string _newValue = "Inspect first";
    private string _status;
    private string _type = "Inspect first";
    private string _old = "—";

    public MmsObjectReference? Reference { get; }
    public string ReferenceText { get; }
    public string DisplayName { get; }
    public string Fc { get; }
    public bool Eligible { get; }
    public bool CanEdit => Eligible &&
        Preview is { IsAvailable: true, Type: not null } p &&
        (p.Type.MmsType switch
        {
            "boolean" => p.CurrentText.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                         p.CurrentText.Equals("false", StringComparison.OrdinalIgnoreCase),
            "integer" or "unsigned" or "visible-string" or "mms-string" => true,
            "floating-point" when p.Type.Size == 32 => true,
            _ => false // Unknown enum/struct/FLOAT64 is not a qualified editable leaf.
        });
    public bool HasEdited { get; private set; }
    public IedBrowserNativeWritePreview? Preview { get; set; }
    public event PropertyChangedEventHandler? PropertyChanged;

    public BrowserBatchWriteRow(
        MmsObjectReference? reference, string referenceText, string fc,
        bool eligible, string reason)
    {
        Reference = reference;
        ReferenceText = referenceText;
        DisplayName = referenceText.Split((char)36).LastOrDefault() ?? referenceText;
        Fc = fc;
        Eligible = eligible;
        _status = reason;
    }

    public void NotifyEditorChanged() => Changed(nameof(CanEdit));
    public IReadOnlyList<string> BooleanChoices { get; } = new[] { "true", "false" };
    public Visibility BooleanEditorVisibility => _booleanEditor ? Visibility.Visible : Visibility.Collapsed;
    public Visibility TextEditorVisibility => _booleanEditor ? Visibility.Collapsed : Visibility.Visible;
    private bool _booleanEditor;

    public void ApplyLiveEditor(bool boolean, string actualValue)
    {
        if (_booleanEditor != boolean)
        {
            _booleanEditor = boolean;
            Changed(nameof(BooleanEditorVisibility));
            Changed(nameof(TextEditorVisibility));
        }
        if (!HasEdited && !string.IsNullOrWhiteSpace(actualValue))
        {
            _newValue = actualValue;
            Changed(nameof(NewValue));
        }
        Changed(nameof(CanEdit));
    }

    public void ResetToReadback(string actual)
    {
        _newValue = actual;
        HasEdited = false;
        Selected = false;
        Changed(nameof(NewValue));
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
