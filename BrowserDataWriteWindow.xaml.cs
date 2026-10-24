using System.Windows;
using System.Windows.Controls;
using AR.Iec61850.Mms;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

/// <summary>
/// Explicit, one-DA data Write workflow. This is not IEC Control and never
/// calls Select/Operate or reconfigures a Report/GOOSE control block.
/// </summary>
public partial class BrowserDataWriteWindow : Window
{
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly MmsObjectReference _reference;
    private readonly long _association;
    private readonly LiveIedModelDiscoveryDocument? _model;
    private readonly CancellationTokenSource _lifetime = new();
    private bool CurrentAssociation => _device.IsConnected && !_device.IsDemo &&
        !_device.IsBusy && !_device.LiveValues.IsOffline &&
        _device.LiveValues.Association == _association &&
        ReferenceEquals(_device.LiveDiscoveryModel, _model) &&
        !_lifetime.IsCancellationRequested;
    private IedBrowserNativeWritePreview? _preview;
    private bool _busy;
    public bool WriteAttempted { get; private set; }
    public long WriteAssociation { get; private set; }
    public string WriteStatus { get; private set; } = "";
    public (string Reference, string Fc) AttemptedLeaf =>
        (_reference.Domain + "/" + _reference.Item, _reference.FunctionalConstraint);

    public BrowserDataWriteWindow(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        LiveIedDataAttributeModel attribute)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _association = device.LiveValues.Association;
        _model = device.LiveDiscoveryModel;
        ArgumentNullException.ThrowIfNull(attribute);
        _reference = MmsObjectReference.Parse(
            attribute.MmsReference, attribute.FunctionalConstraint);
        ReferenceLabel.Text = $"{_device.Name}  •  {_reference.Domain}/{_reference.Item}";
        AttributeLabel.Text = _reference.Item.Split((char)36).LastOrDefault() ?? "Value";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _busy = true;
        try
        {
            if (!CurrentAssociation)
                throw new InvalidOperationException("IED connection changed; reopen Write.");
            _preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                _device, _reference, _lifetime.Token);
            if (!CurrentAssociation) return;
            if (_preview is not { IsAvailable: true, Type: not null, OldValue: not null } p)
            {
                StatusLabel.Text = _preview?.Message ?? "Live GVA and value not available.";
                DiffLabel.Text = "No Write is permitted. Select another live DA.";
                return;
            }

            CurrentLabel.Text = $"Current: {p.CurrentText}";
            TypeLabel.Text = p.Type.SclBType.Length > 0
                ? $"{p.Type.SclBType} ({p.Type.MmsType}, {p.Type.Size?.ToString() ?? "?"})"
                : $"{p.Type.MmsType} ({p.Type.Size?.ToString() ?? "?"})";
            var supported = p.Type.MmsType switch
            {
                "boolean" or "integer" or "unsigned" or "visible-string" or "mms-string" => true,
                "floating-point" when p.Type.Size == 32 => true,
                _ => false
            };
            var boolean = p.Type.MmsType == "boolean";
            BoolPicker.Visibility = boolean ? Visibility.Visible : Visibility.Collapsed;
            NewValueBox.Visibility = boolean ? Visibility.Collapsed : Visibility.Visible;
            if (boolean)
            {
                var current = p.CurrentText.Trim();
                BoolPicker.SelectedIndex = current.Equals("true", StringComparison.OrdinalIgnoreCase) ? 0 :
                    current.Equals("false", StringComparison.OrdinalIgnoreCase) ? 1 : -1;
                BoolPicker.IsEnabled = supported && BoolPicker.SelectedIndex >= 0;
                if (BoolPicker.SelectedIndex < 0)
                {
                    StatusLabel.Text = "Unrecognized live Boolean; Write disabled.";
                    DiffLabel.Text = "Read a verified true/false value before Write.";
                    return;
                }
            }
            else
            {
                NewValueBox.Text = p.CurrentText;
                NewValueBox.IsEnabled = supported;
            }
            StatusLabel.Text = supported ? "Choose a value, then Write" : "Unsupported live MMS type";
            DiffLabel.Text = supported ? "No change" : "No qualified editor for this MMS type.";
            if (supported && !boolean) NewValueBox.SelectAll();
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Preview cancelled; no Write sent.";
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Preview refused: " + ex.Message;
        }
        finally
        {
            _busy = false;
            RefreshInput();
        }
    }

    private void NewValueBox_TextChanged(object sender, TextChangedEventArgs e) => RefreshInput();
    private void BoolPicker_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshInput();
    private string SelectedInput() => BoolPicker.Visibility == Visibility.Visible
        ? (BoolPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ""
        : NewValueBox.Text;

    private void RefreshInput()
    {
        if (WriteButton is null || DiffLabel is null || NewValueBox is null || BoolPicker is null) return;
        WriteButton.IsEnabled = false;
        if (_busy || !CurrentAssociation ||
            _preview is not { IsAvailable: true, Type: not null, OldValue: not null } p ||
            !(BoolPicker.Visibility == Visibility.Visible ? BoolPicker.IsEnabled : NewValueBox.IsEnabled))
            return;
        if (!IedBrowserTypedWriteInput.TryParse(p.Type, SelectedInput(), out var value, out var error) || value is null)
        {
            DiffLabel.Text = error;
            return;
        }
        var gva = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true, Reference = _reference, TypeSpecification = p.Type
        };
        var rejection = IedBrowserTypedWriteAdmission.RejectReason(_reference, gva, p.GvaSignature, value);
        if (rejection is not null) { DiffLabel.Text = rejection; return; }
        var changed = !MmsDataCodec.Encode(p.OldValue).AsSpan().SequenceEqual(MmsDataCodec.Encode(value));
        DiffLabel.Text = changed ? p.CurrentText + " → " + MmsDataCodec.ToDisplayString(value) : "No change";
        WriteButton.IsEnabled = changed;
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _preview is not { IsAvailable: true, Type: not null, OldValue: not null } p ||
            !WriteButton.IsEnabled ||
            !IedBrowserTypedWriteInput.TryParse(p.Type, SelectedInput(),
                out var proposed, out _) || proposed is null)
            return;
        var gva = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true, Reference = _reference, TypeSpecification = p.Type
        };
        if (IedBrowserTypedWriteAdmission.RejectReason(
                _reference, gva, p.GvaSignature, proposed) is not null)
        {
            RefreshInput();
            return;
        }

        // Write is the explicit operator action. Do not display a second
        // confirmation or silently retry; native admission verifies typed
        // evidence and the original association at the wire boundary.
        if (!CurrentAssociation)
        {
            StatusLabel.Text = "Connection changed. Reopen Write.";
            return;
        }

        _busy = true;
        WriteButton.IsEnabled = false;
        NewValueBox.IsEnabled = false;
        BoolPicker.IsEnabled = false;
        StatusLabel.Text = "Writing…";
        bool verified = false;
        string feedback = "IED did not verify Write.";
        WriteAttempted = true;
        WriteAssociation = _association;
        try
        {
            var step = new IedBrowserTypedWriteStep(
                _reference, p.GvaSignature, p.OldValue, proposed);
            var outcome = await _runtime.WriteBrowserDataAttributesAsync(
                _device, new[] { step }, true, _lifetime.Token);
            verified = outcome.Verified == 1;
            feedback = verified ? "Write verified" :
                "Write ditolak: " + (outcome.Rows.FirstOrDefault()?.Message ?? "IED refused");
        }
        catch (OperationCanceledException)
        {
            feedback = "Write outcome unknown: connection interrupted.";
        }
        catch (Exception ex)
        {
            feedback = "Write ditolak / unknown: " + ex.Message;
        }
        finally
        {
            WriteStatus = feedback;
            _busy = false;
            // No duplicate read of the value inside this popup. Browser will
            // issue its own exact live MMS Read and publish only into SSOT.
            if (IsLoaded) Close();
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e) => _lifetime.Cancel();
}
