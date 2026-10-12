using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ArIED61850Tester;

public partial class ControlCommandWindow : Window, INotifyPropertyChanged
{
    private readonly Iec61850MonitorRuntime _runtime;
    private readonly Iec61850MonitorDevice _device;
    private readonly SignalDefinition _signal;
    private readonly CancellationTokenSource _cancellation = new();
    private string _controlModelText = "Reading ctlModel…";
    private bool _modelReadable;
    private bool _confirmConfigurationAuthorization;
    private string _configurationConfirmationText = string.Empty;
    private string _configurationResultText = "Read-only inspection first. No configuration is written automatically.";
    private ControlModelChoice? _selectedModelChoice;
    private string _controlCdcText = "CONTROL";
    private string _currentValue = "-";
    private string _engineStatusText = "Checking ARIEC61850 Smart Control capability…";
    private string _sequenceText = "Detecting the required IEC 61850 control sequence…";
    private string _controlEvidenceText = string.Empty;
    private string _selectedValue = string.Empty;
    private bool _interlockCheck = true;
    private bool _synchroCheck;
    private bool _testMode;
    private bool _confirmLiveCommand;
    private bool _isBusy;
    private bool _isReady;
    private string _commandStage = "Ready to inspect";
    private string _commandStatus = "ArIED will read the live control model and current status before enabling Send Command.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public sealed record ControlModelChoice(Iec61850ControlModelKind Model, string Label);
    public ObservableCollection<string> ValueOptions { get; } = new();
    public ObservableCollection<ControlModelChoice> AvailableControlModels { get; } = new()
    {
        new(Iec61850ControlModelKind.DirectNormal, "1 · Direct · Normal"),
        new(Iec61850ControlModelKind.SboNormal, "2 · SBO · Normal"),
        new(Iec61850ControlModelKind.DirectEnhanced, "3 · Direct · Enhanced"),
        new(Iec61850ControlModelKind.SboEnhanced, "4 · SBO · Enhanced")
    };
    public ControlModelChoice? SelectedModelChoice
    {
        get => _selectedModelChoice;
        set { if (Set(ref _selectedModelChoice, value)) Raise(nameof(CanConfigureModel)); }
    }
    public bool ConfirmConfigurationAuthorization
    {
        get => _confirmConfigurationAuthorization;
        set { if (Set(ref _confirmConfigurationAuthorization, value)) Raise(nameof(CanConfigureModel)); }
    }
    public string ConfigurationConfirmationText
    {
        get => _configurationConfirmationText;
        set { if (Set(ref _configurationConfirmationText, value)) Raise(nameof(CanConfigureModel)); }
    }
    public string ConfigurationResultText
    {
        get => _configurationResultText;
        private set => Set(ref _configurationResultText, value);
    }
    public string RequiredConfigurationConfirmation => _signal.ObjectReference;
    public bool CanConfigureModel => !IsBusy && _modelReadable && SelectedModelChoice != null &&
        SelectedModelChoice.Model != _signal.ControlModelKind &&
        ConfirmConfigurationAuthorization &&
        ConfigurationConfirmationText.Trim().Equals(_signal.ObjectReference, StringComparison.Ordinal);
    public string DeviceSummary => $"{_device.Name} • {_device.EndpointText}";
    public string SignalName => _signal.Name;
    public string Telegram => Iec61850MonitorPoint.StripIedNamePrefix(_signal.ObjectReference, _device.Name);
    public string ControlModelText { get => _controlModelText; private set => Set(ref _controlModelText, value); }
    public string ControlCdcText { get => _controlCdcText; private set => Set(ref _controlCdcText, value); }
    public string CurrentValue { get => _currentValue; private set => Set(ref _currentValue, value); }
    public string EngineStatusText { get => _engineStatusText; private set => Set(ref _engineStatusText, value); }
    public string SequenceText { get => _sequenceText; private set => Set(ref _sequenceText, value); }
    public string ControlEvidenceText { get => _controlEvidenceText; private set => Set(ref _controlEvidenceText, value); }
    public string SelectedValue { get => _selectedValue; set { if (Set(ref _selectedValue, value)) Raise(nameof(CanSend)); } }
    public bool InterlockCheck { get => _interlockCheck; set => Set(ref _interlockCheck, value); }
    public bool SynchroCheck { get => _synchroCheck; set => Set(ref _synchroCheck, value); }
    public bool TestMode
    {
        get => _testMode;
        set
        {
            if (!Set(ref _testMode, value)) return;
            UpdateLiveWarning();
            Raise(nameof(CanSend));
        }
    }
    public bool ConfirmLiveCommand
    {
        get => _confirmLiveCommand;
        set
        {
            if (Set(ref _confirmLiveCommand, value))
                Raise(nameof(CanSend));
        }
    }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            Raise(nameof(CanSend));
            Raise(nameof(SendButtonText));
            Raise(nameof(CanConfigureModel));
        }
    }
    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (!Set(ref _isReady, value)) return;
            UpdateLiveWarning();
            Raise(nameof(CanSend));
        }
    }
    public string CommandStage { get => _commandStage; private set => Set(ref _commandStage, value); }
    public string CommandStatus { get => _commandStatus; private set => Set(ref _commandStatus, value); }
    public string SendButtonText => IsBusy ? "Sending…" : TestMode ? "Send Test" : "Send Command";
    public bool CanSend => IsReady && !IsBusy && !string.IsNullOrWhiteSpace(SelectedValue) && (TestMode || ConfirmLiveCommand);

    public ControlCommandWindow(
        Iec61850MonitorRuntime runtime,
        Iec61850MonitorDevice device,
        SignalDefinition signal)
    {
        _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));
        _device = device ?? throw new ArgumentNullException(nameof(device));
        _signal = signal ?? throw new ArgumentNullException(nameof(signal));

        InitializeComponent();
        DataContext = this;
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        await InspectAsync();
    }

    private async Task InspectAsync()
    {
        _modelReadable = false;
        ConfirmConfigurationAuthorization = false;
        ConfigurationConfirmationText = string.Empty;
        IsBusy = true;
        CommandStage = "Inspecting control";
        CommandStatus = "Reading ctlModel and current status from the IED…";
        SetResultTone("Neutral");
        try
        {
            var capabilities = await _runtime.InspectControlAsync(
                _device.DeviceId,
                _signal,
                _cancellation.Token);

            ControlModelText = capabilities.ControlModelText;
            _modelReadable = _signal.ControlModelKind != Iec61850ControlModelKind.Unknown &&
                capabilities.EngineControlServiceAvailable;
            Raise(nameof(CanConfigureModel));
            ControlCdcText = string.IsNullOrWhiteSpace(capabilities.ControlCdc) ? "CONTROL" : capabilities.ControlCdc;
            CurrentValue = capabilities.CurrentValue;
            EngineStatusText = capabilities.EngineControlServiceStatus;
            SequenceText = capabilities.SequenceText;
            ControlEvidenceText = $"ctlVal: {capabilities.CtlValSignature}\nSBO timeout: {capabilities.SboTimeoutText}\nOperate timeout: {capabilities.OperTimeoutText}\nCommandTermination: {(capabilities.SupportsCommandTermination ? "supported" : "not required / unavailable")}\n{capabilities.DiscoveryEvidence}";
            PopulateValueOptions(capabilities.ControlCdc, capabilities.CurrentValue);
            IsReady = capabilities.SupportsOperate;
            if (!capabilities.EngineControlServiceAvailable)
            {
                CommandStage = "Smart Control unavailable";
                CommandStatus = "The connected ARIEC61850 build does not provide a command-ready native control service for this object.";
                SetResultTone("Warning");
            }
            else
            {
                CommandStage = capabilities.SupportsOperate ? "Ready" : "Control unavailable";
                CommandStatus = capabilities.SupportsOperate
                    ? $"{capabilities.ControlModelText}. {capabilities.SequenceText}. Select a command value and required check flags."
                    : capabilities.EngineControlServiceStatus;
                SetResultTone(capabilities.SupportsOperate ? "Ready" : "Error");
            }
        }
        catch (OperationCanceledException)
        {
            CommandStage = "Cancelled";
            CommandStatus = "Control inspection was cancelled.";
        }
        catch (Exception ex)
        {
            IsReady = false;
            CommandStage = "Inspection failed";
            CommandStatus = $"{ex.GetType().Name}: {ex.Message}";
            SetResultTone("Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async void RefreshControlModel_Click(object sender, RoutedEventArgs e)
        => await InspectAsync();

    private async void ConfigureControlModel_Click(object sender, RoutedEventArgs e)
    {
        if (!CanConfigureModel || SelectedModelChoice == null)
            return;
        // Snapshots are never refreshed silently after the operator confirms
        // a target. Engine still re-reads and compares exact live ctlModel before
        // its single CF Write.
        var expected = _signal.ControlModelKind;
        var requested = SelectedModelChoice.Model;
        var warning = $"Commissioning operation (NOT a test):\\n\\n" +
            $"IED: {_device.Name} ({_device.EndpointText})\\n" +
            $"Object: {_signal.ObjectReference}\\n" +
            $"CF attribute: {_signal.ObjectReference}.ctlModel\\n" +
            $"Expected live: {(int)expected} ({expected})\\n" +
            $"Requested: {(int)requested} ({requested})\\n\\n" +
            "This can change the control security mode of the IED. " +
            "No Operate is sent, but the change may affect other clients. Continue?";
        if (MessageBox.Show(this, warning, "Confirm ctlModel commissioning",
                MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No)
            != MessageBoxResult.Yes)
            return;
        IsBusy = true;
        IsReady = false;
        ConfigurationResultText = "Verifying live ctlModel before a single CF Write…";
        try
        {
            var result = await _runtime.ConfigureControlModelAsync(
                _device.DeviceId, _signal, expected, requested, _cancellation.Token);
            ConfigurationResultText = result.Detail;
            if (result.ReadbackVerified)
            {
                await InspectAsync(); // fresh typed descriptor, not stale readiness
                ConfigurationResultText = result.Detail +
                    " Fresh control inspection completed; verify the operating procedure.";
            }
            else
            {
                CommandStage = "Commissioning not verified";
                CommandStatus = result.Detail;
                SetResultTone("Warning");
            }
        }
        catch (OperationCanceledException)
        {
            ConfigurationResultText = "Cancelled. Read back ctlModel before any future operation.";
            CommandStage = "Commissioning uncertain";
        }
        catch (Exception ex)
        {
            ConfigurationResultText = $"{ex.GetType().Name}: {ex.Message} Readback and reinspection required.";
            CommandStage = "Commissioning failed or uncertain";
            SetResultTone("Error");
        }
        finally
        {
            ConfirmConfigurationAuthorization = false;
            ConfigurationConfirmationText = string.Empty;
            IsBusy = false;
        }
    }

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSend)
            return;

        IsBusy = true;
        CommandStage = TestMode ? "Sending test command" : "Sending live command";
        CommandStatus = "Executing the IEC 61850 control sequence selected from ctlModel…";
        SetResultTone("Neutral");
        try
        {
            var result = await _runtime.ExecuteControlAsync(
                _device.DeviceId,
                new Iec61850ControlCommandRequest
                {
                    Signal = _signal,
                    ValueText = SelectedValue,
                    InterlockCheck = InterlockCheck,
                    SynchroCheck = SynchroCheck,
                    TestMode = TestMode,
                    FeedbackTimeoutMs = _signal.IsPositionControl ? 12000 : 8000,
                    CommandTerminationTimeoutMs = 10000,
                    OriginCategory = "StationControl"
                },
                _cancellation.Token);

            CommandStage = result.IsSuccess && !TestMode ? "Command accepted" : result.Stage;
            CommandStatus = BuildCommandResultText(result);
            if (result.IsSuccess && !TestMode)
            {
                CommandStatus += " Command accepted by the IEC 61850 control service. Waiting for independent IED process feedback; monitored stVal is not changed from the command path.";
            }
            SetResultTone(result.IsSuccess ? "Success" : "Error");
        }
        catch (OperationCanceledException)
        {
            CommandStage = "Cancelled";
            CommandStatus = "The command operation was cancelled.";
            SetResultTone("Neutral");
        }
        catch (Exception ex)
        {
            CommandStage = "Command failed";
            CommandStatus = $"{ex.GetType().Name}: {ex.Message}";
            SetResultTone("Error");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void PopulateValueOptions(string cdc, string currentValue)
    {
        ValueOptions.Clear();
        switch ((cdc ?? string.Empty).Trim().ToUpperInvariant())
        {
            case "DPC":
                ValueOptions.Add("Open [01]");
                ValueOptions.Add("Closed [10]");
                break;
            case "SPC":
                var commandText = $"{_signal.Name} {_signal.ObjectReference}";
                if (commandText.Contains("TapOpR", StringComparison.OrdinalIgnoreCase) ||
                    commandText.Contains("Raise", StringComparison.OrdinalIgnoreCase))
                {
                    ValueOptions.Add("Raise");
                }
                else if (commandText.Contains("TapOpL", StringComparison.OrdinalIgnoreCase) ||
                         commandText.Contains("Lower", StringComparison.OrdinalIgnoreCase))
                {
                    ValueOptions.Add("Lower");
                }
                else
                {
                    ValueOptions.Add("On");
                    ValueOptions.Add("Off");
                }
                break;
            case "INC":
            case "ISC":
            case "INC/ISC":
                if (long.TryParse(currentValue, System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var numericValue))
                    ValueOptions.Add(numericValue.ToString(System.Globalization.CultureInfo.InvariantCulture));
                break;
            case "BSC":
                ValueOptions.Add("Raise");
                ValueOptions.Add("Lower");
                break;
            case "APC":
                ValueOptions.Add(TryExtractNumber(currentValue, out var analogue)
                    ? analogue.ToString("G", System.Globalization.CultureInfo.InvariantCulture)
                    : "0");
                break;
            default:
                if (!string.IsNullOrWhiteSpace(currentValue) && currentValue != "-")
                    ValueOptions.Add(currentValue);
                break;
        }

        SelectedValue = ValueOptions.FirstOrDefault() ?? string.Empty;
    }


    private static string BuildCommandResultText(Iec61850ControlCommandResult result)
    {
        var details = new List<string>();
        if (!result.IsSuccess)
        {
            var explanation = Iec61850ControlFailureReason.Explain(result);
            details.Add($"{explanation.Summary} [{explanation.Confidence}].");
            details.Add($"Check: {explanation.Checks}.");
        }
        var rejectedStep = result.WireSteps.FirstOrDefault(step => !step.RequestAccepted);
        if (rejectedStep != null)
        {
            var rejectedStage = rejectedStep.Action.Equals("SelectWithValue", StringComparison.OrdinalIgnoreCase) ? "SBOw" : rejectedStep.Action;
            details.Add($"IED REJECTED {rejectedStage}: {result.Message}");
            var operateSent = result.WireSteps.Any(step => step.Action.Equals("Operate", StringComparison.OrdinalIgnoreCase));
            if (rejectedStage.Equals("SBOw", StringComparison.OrdinalIgnoreCase) && !operateSent)
            {
                details.Add("Operate was NOT sent because SBOw selection failed.");
                details.Add("CommandTermination is not expected because Operate never started.");
            }
        }
        else
        {
            details.Add(result.Message);
        }

        if (result.CompletionState.Equals("NotSent", StringComparison.OrdinalIgnoreCase))
            details.Add("No MMS control request was sent to the IED.");
        else if (!string.IsNullOrWhiteSpace(result.ResponseHex))
            details.Add("MMS request/response wire evidence was captured.");
        else if (!string.IsNullOrWhiteSpace(result.RequestHex))
            details.Add("MMS request encoding was captured, but no MMS response was captured.");
        if (result.WireSteps.Count > 0)
            details.Add($"Wire sequence: {string.Join(" → ", result.WireSteps.Select(step => step.Action))}.");
        if (result.CommandTerminationReceived)
            details.Add(result.PositiveTermination ? "Positive CommandTermination received." : "Negative CommandTermination received.");
        if (!string.IsNullOrWhiteSpace(result.ControlError))
            details.Add($"ControlError: {result.ControlError}.");
        if (!string.IsNullOrWhiteSpace(result.AddCause))
        {
            details.Add($"AddCause: {result.AddCause}.");
            details.Add(Iec61850ControlFailureReason.Explain(result).Summary);
        }
        if (!string.IsNullOrWhiteSpace(result.LastApplErrorText))
            details.Add(result.LastApplErrorText);
        if (!string.IsNullOrWhiteSpace(result.ElapsedText) && result.ElapsedText != "-")
            details.Add($"Control service: {result.ElapsedText}.");
        if (!string.IsNullOrWhiteSpace(result.FeedbackElapsedText) && result.FeedbackElapsedText != "-")
            details.Add($"Control-side feedback verification: {result.FeedbackElapsedText}. This does not overwrite monitored stVal.");
        if (!string.IsNullOrWhiteSpace(result.TotalElapsedText) && result.TotalElapsedText != "-")
            details.Add($"Total: {result.TotalElapsedText}.");
        return string.Join(" ", details.Where(text => !string.IsNullOrWhiteSpace(text)));
    }

    private static bool TryExtractNumber(string? text, out double value)
    {
        value = 0d;
        var match = System.Text.RegularExpressions.Regex.Match(text ?? string.Empty, @"[-+]?\d+(?:[\.,]\d+)?");
        return match.Success && double.TryParse(
            match.Value.Replace(',', '.'),
            System.Globalization.NumberStyles.Float,
            System.Globalization.CultureInfo.InvariantCulture,
            out value);
    }

    private void UpdateLiveWarning()
    {
        if (LiveCommandWarning == null)
            return;
        LiveCommandWarning.Visibility = IsReady && !TestMode ? Visibility.Visible : Visibility.Collapsed;
        if (TestMode)
            ConfirmLiveCommand = false;
    }

    private void SetResultTone(string tone)
    {
        if (ResultPanel == null)
            return;

        (ResultPanel.Background, ResultPanel.BorderBrush) = tone switch
        {
            "Success" => (BrushFrom("#ECFDF3"), BrushFrom("#86EFAC")),
            "Error" => (BrushFrom("#FEF3F2"), BrushFrom("#FDA29B")),
            "Warning" => (BrushFrom("#FFF8E7"), BrushFrom("#F4C76A")),
            "Ready" => (BrushFrom("#EFF8FF"), BrushFrom("#B2DDFF")),
            _ => (BrushFrom("#F8FAFC"), BrushFrom("#D8E0EA"))
        };
    }

    private static SolidColorBrush BrushFrom(string hex)
    {
        var converted = ColorConverter.ConvertFromString(hex);
        var color = converted is Color parsed ? parsed : Colors.Transparent;
        return new SolidColorBrush(color);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        _cancellation.Cancel();
        _cancellation.Dispose();
    }

    private bool Set<T>(ref T field, T value, [CallerMemberName] string propertyName = "")
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(propertyName);
        return true;
    }

    private void Raise([CallerMemberName] string propertyName = "")
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}
