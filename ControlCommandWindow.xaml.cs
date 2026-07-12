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

    public ObservableCollection<string> ValueOptions { get; } = new();
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
                    : "The IED reports status-only or an unknown ctlModel. ArIED will not guess an operating sequence.";
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
                    OriginCategory = "Maintenance"
                },
                _cancellation.Token);

            CommandStage = result.Stage;
            CommandStatus = BuildCommandResultText(result);
            if (!string.IsNullOrWhiteSpace(result.FeedbackValue) && result.FeedbackValue != "-")
                CurrentValue = result.FeedbackValue;
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
                ValueOptions.Add("Raise");
                ValueOptions.Add("Lower");
                break;
            case "BSC":
                if (TryExtractNumber(currentValue, out var step))
                {
                    ValueOptions.Add(Math.Round(step - 1d).ToString(System.Globalization.CultureInfo.InvariantCulture));
                    ValueOptions.Add(Math.Round(step + 1d).ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
                else
                {
                    ValueOptions.Add("0");
                }
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
        var details = new List<string> { result.Message };
        if (result.CommandTerminationReceived)
            details.Add(result.PositiveTermination ? "Positive CommandTermination received." : "Negative CommandTermination received.");
        if (!string.IsNullOrWhiteSpace(result.ControlError))
            details.Add($"ControlError: {result.ControlError}.");
        if (!string.IsNullOrWhiteSpace(result.AddCause))
            details.Add($"AddCause: {result.AddCause}.");
        if (!string.IsNullOrWhiteSpace(result.LastApplErrorText))
            details.Add(result.LastApplErrorText);
        if (!string.IsNullOrWhiteSpace(result.ElapsedText) && result.ElapsedText != "-")
            details.Add($"Control service: {result.ElapsedText}.");
        if (!string.IsNullOrWhiteSpace(result.FeedbackElapsedText) && result.FeedbackElapsedText != "-")
            details.Add($"Process feedback: {result.FeedbackElapsedText}.");
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
