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
    private readonly CancellationTokenSource _lifetime = new();
    private IedBrowserNativeWritePreview? _preview;
    private bool _busy;

    public BrowserDataWriteWindow(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        LiveIedDataAttributeModel attribute)
    {
        InitializeComponent();
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        ArgumentNullException.ThrowIfNull(attribute);
        _reference = MmsObjectReference.Parse(
            attribute.MmsReference, attribute.FunctionalConstraint);
        ReferenceLabel.Text = $"{_device.Name}  •  {_reference.Domain}/{_reference.Item}";
        AttributeLabel.Text = _reference.Item.Split('
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _busy = true;
        try
        {
            _preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                _device, _reference, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
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
            // Type-driven input: BOOLEAN always uses a picker; scalar text starts
            // with the VERIFIED current value, never an unexplained empty box.
            var boolean = p.Type.MmsType == "boolean";
            BoolPicker.Visibility = boolean ? Visibility.Visible : Visibility.Collapsed;
            NewValueBox.Visibility = boolean ? Visibility.Collapsed : Visibility.Visible;
            if (boolean)
            {
                BoolPicker.SelectedIndex = p.CurrentText.Equals("true",
                    StringComparison.OrdinalIgnoreCase) ? 0 : 1;
                BoolPicker.IsEnabled = supported;
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

    private void NewValueBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshInput();

    private void BoolPicker_SelectionChanged(object sender, SelectionChangedEventArgs e)
        => RefreshInput();

    private string SelectedInput() =>
        BoolPicker.Visibility == Visibility.Visible
            ? (BoolPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? ""
            : NewValueBox.Text;

    private void RefreshInput()
    {
        if (WriteButton is null || DiffLabel is null || NewValueBox is null ||
            BoolPicker is null) return;
        WriteButton.IsEnabled = false;
        if (_busy || _preview is not { IsAvailable: true, Type: not null, OldValue: not null } p ||
            !(BoolPicker.Visibility == Visibility.Visible
                ? BoolPicker.IsEnabled : NewValueBox.IsEnabled))
            return;
        if (!IedBrowserTypedWriteInput.TryParse(p.Type, SelectedInput(),
                out var value, out var error) || value is null)
        {
            DiffLabel.Text = error;
            return;
        }
        var gva = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true, Reference = _reference, TypeSpecification = p.Type
        };
        var rejection = IedBrowserTypedWriteAdmission.RejectReason(
            _reference, gva, p.GvaSignature, value);
        if (rejection is not null)
        {
            DiffLabel.Text = rejection;
            return;
        }
        var changed = !MmsDataCodec.Encode(p.OldValue).AsSpan().SequenceEqual(
            MmsDataCodec.Encode(value));
        DiffLabel.Text = changed
            ? $"{p.CurrentText} → {MmsDataCodec.ToDisplayString(value)}"
            : "No change";
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

        var confirm =
            "Confirm ONE IEC 61850 MMS data Write on the connected IED?\n\n" +
            $"IED: {_device.Name}\n" +
            $"DA: {_reference.Domain}/{_reference.Item} [{_reference.FunctionalConstraint}]\n" +
            $"Before: {p.CurrentText}\n" +
            $"After: {MmsDataCodec.ToDisplayString(proposed)}\n\n" +
            "No automatic retries, rollback, or control Operate. " +
            "The server may reject Write; readback must independently match.";
        if (MessageBox.Show(this, confirm, "Confirm live MMS Write",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _busy = true;
        WriteButton.IsEnabled = false;
        NewValueBox.IsEnabled = false;
        BoolPicker.IsEnabled = false;
        StatusLabel.Text = "Writing…";
        try
        {
            var step = new IedBrowserTypedWriteStep(
                _reference, p.GvaSignature, p.OldValue, proposed);
            var outcome = await _runtime.WriteBrowserDataAttributesAsync(
                _device, new[] { step }, true, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            var result = outcome.Rows.Single();
            StatusLabel.Text = $"{result.State}: {result.Message}";
            DiffLabel.Text =
                "Write completed or resolved as uncertain. Close and inspect the actual IED value " +
                "before another attempt; physical process change is NOT implied.";
            _preview = null; // A new attempt must acquire a NEW live GVA/read + confirmation.
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Write may have been dispatched: outcome UNKNOWN. Read IED manually.";
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Write refused or outcome unresolved: " + ex.Message;
        }
        finally
        {
            _busy = false;
            WriteButton.IsEnabled = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e) => _lifetime.Cancel();
}
).LastOrDefault() ?? "Value";
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        _busy = true;
        try
        {
            _preview = await _runtime.PreviewBrowserDataAttributeWriteAsync(
                _device, _reference, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            if (_preview is not { IsAvailable: true, Type: not null, OldValue: not null } p)
            {
                StatusLabel.Text = _preview?.Message ?? "Live GVA and value not available.";
                DiffLabel.Text = "No Write is permitted. Select another live DA.";
                return;
            }

            CurrentLabel.Text = p.CurrentText;
            TypeLabel.Text = p.Type.SclBType.Length > 0
                ? $"{p.Type.SclBType} ({p.Type.MmsType}, {p.Type.Size?.ToString() ?? "?"})"
                : $"{p.Type.MmsType} ({p.Type.Size?.ToString() ?? "?"})";
            var supported = p.Type.MmsType switch
            {
                "boolean" or "integer" or "unsigned" or "visible-string" or "mms-string" => true,
                "floating-point" when p.Type.Size == 32 => true,
                _ => false
            };
            NewValueBox.IsEnabled = supported;
            StatusLabel.Text = supported
                ? "Live GVA/read verified. Enter a new value and inspect the diff."
                : "This MMS wire type has no qualified typed text editor.";
            DiffLabel.Text = $"Current: {p.CurrentText}\nNew: —\nFC={_reference.FunctionalConstraint} · GVA locked to this association.";
            if (supported) NewValueBox.Focus();
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

    private void NewValueBox_TextChanged(object sender, TextChangedEventArgs e)
        => RefreshInput();

    private void RefreshInput()
    {
        if (WriteButton is null || DiffLabel is null || NewValueBox is null) return;
        WriteButton.IsEnabled = false;
        if (_busy || _preview is not { IsAvailable: true, Type: not null, OldValue: not null } p ||
            !NewValueBox.IsEnabled)
            return;
        if (!IedBrowserTypedWriteInput.TryParse(p.Type, NewValueBox.Text,
                out var value, out var error) || value is null)
        {
            DiffLabel.Text = error;
            return;
        }
        var gva = new MmsVariableAccessAttributesResult
        {
            IsSuccess = true, Reference = _reference, TypeSpecification = p.Type
        };
        var rejection = IedBrowserTypedWriteAdmission.RejectReason(
            _reference, gva, p.GvaSignature, value);
        if (rejection is not null)
        {
            DiffLabel.Text = rejection;
            return;
        }
        var oldBytes = MmsDataCodec.Encode(p.OldValue);
        var newBytes = MmsDataCodec.Encode(value);
        var changed = !oldBytes.AsSpan().SequenceEqual(newBytes);
        DiffLabel.Text = $"Current: {p.CurrentText}\nNew: {MmsDataCodec.ToDisplayString(value)}\n" +
                         $"FC={_reference.FunctionalConstraint} · " +
                         (changed ? "One exact DA will be written." : "No change; Write disabled.");
        WriteButton.IsEnabled = changed;
    }

    private async void Write_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _preview is not { IsAvailable: true, Type: not null, OldValue: not null } p ||
            !WriteButton.IsEnabled ||
            !IedBrowserTypedWriteInput.TryParse(p.Type, NewValueBox.Text,
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

        var confirm =
            "Confirm ONE IEC 61850 MMS data Write on the connected IED?\n\n" +
            $"IED: {_device.Name}\n" +
            $"DA: {_reference.Domain}/{_reference.Item} [{_reference.FunctionalConstraint}]\n" +
            $"Before: {p.CurrentText}\n" +
            $"After: {MmsDataCodec.ToDisplayString(proposed)}\n\n" +
            "No automatic retries, rollback, or control Operate. " +
            "The server may reject Write; readback must independently match.";
        if (MessageBox.Show(this, confirm, "Confirm live MMS Write",
                MessageBoxButton.YesNo, MessageBoxImage.Warning,
                MessageBoxResult.No) != MessageBoxResult.Yes)
            return;

        _busy = true;
        WriteButton.IsEnabled = false;
        NewValueBox.IsEnabled = false;
        StatusLabel.Text = "One exact MMS Write requested…";
        try
        {
            var step = new IedBrowserTypedWriteStep(
                _reference, p.GvaSignature, p.OldValue, proposed);
            var outcome = await _runtime.WriteBrowserDataAttributesAsync(
                _device, new[] { step }, true, _lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            var result = outcome.Rows.Single();
            StatusLabel.Text = $"{result.State}: {result.Message}";
            DiffLabel.Text =
                "Write completed or resolved as uncertain. Close and inspect the actual IED value " +
                "before another attempt; physical process change is NOT implied.";
            _preview = null; // A new attempt must acquire a NEW live GVA/read + confirmation.
        }
        catch (OperationCanceledException)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Write may have been dispatched: outcome UNKNOWN. Read IED manually.";
        }
        catch (Exception ex)
        {
            if (!_lifetime.IsCancellationRequested)
                StatusLabel.Text = "Write refused or outcome unresolved: " + ex.Message;
        }
        finally
        {
            _busy = false;
            WriteButton.IsEnabled = false;
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private void Window_Closed(object? sender, EventArgs e) => _lifetime.Cancel();
}
