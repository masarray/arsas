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
    private string _modelShortText = "Inspecting…";
    private string _sequenceHint = "Inspecting control…";
    private string _originatorId = "ARSAS";
    private string _selectedOriginCategory = "StationControl";
    private string _controlNumberText = "Auto";
    private string _terminationText = "—";
    private string _wireTimelineText = string.Empty;
    private Iec61850ControlModelKind _liveControlModel = Iec61850ControlModelKind.Unknown;
    private bool _interlockCheck = true;
    private bool _synchroCheck;
    private bool _testMode;
    private bool _confirmLiveCommand;
    private bool _isBusy;
    private bool _commandFlightActive;
    private bool _commandAttempted;
    private bool _isReady;
    private string _commandStage = "Ready to inspect";
    private string _commandStatus = "ArIED will read the live control model and current status before enabling Send Command.";

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<string> ValueOptions { get; } = new();
    public IReadOnlyList<string> OriginCategories { get; } = new[]
    {
        "StationControl", "BayControl", "RemoteControl", "AutomaticBay",
        "AutomaticStation", "AutomaticRemote", "Maintenance", "Process"
    };
    public string DeviceSummary => $"{_device.Name} • {_device.EndpointText}";
    public string SignalName => _signal.Name;
    public string Telegram => Iec61850MonitorPoint.StripIedNamePrefix(_signal.ObjectReference, _device.Name);
    public string ControlModelText { get => _controlModelText; private set => Set(ref _controlModelText, value); }
    public string ControlCdcText { get => _controlCdcText; private set => Set(ref _controlCdcText, value); }
    public string CurrentValue { get => _currentValue; private set => Set(ref _currentValue, value); }
    public string EngineStatusText { get => _engineStatusText; private set => Set(ref _engineStatusText, value); }
    public string SequenceText { get => _sequenceText; private set => Set(ref _sequenceText, value); }
    public string ControlEvidenceText
    {
        get => _controlEvidenceText;
        private set
        {
            if (Set(ref _controlEvidenceText, value)) Raise(nameof(CanCopyEvidence));
        }
    }
    public string SelectedValue { get => _selectedValue; set { if (Set(ref _selectedValue, value)) Raise(nameof(CanSend)); } }
    public string ModelShortText { get => _modelShortText; private set => Set(ref _modelShortText, value); }
    public string SequenceHint { get => _sequenceHint; private set => Set(ref _sequenceHint, value); }
    public string ControlNumberText { get => _controlNumberText; private set => Set(ref _controlNumberText, value); }
    public string TerminationText { get => _terminationText; private set => Set(ref _terminationText, value); }
    public string WireTimelineText { get => _wireTimelineText; private set => Set(ref _wireTimelineText, value); }
    public string SelectedOriginCategory
    {
        get => _selectedOriginCategory;
        set
        {
            if (Set(ref _selectedOriginCategory, value)) Raise(nameof(CanSend));
        }
    }
    public string OriginatorId
    {
        get => _originatorId;
        set
        {
            if (Set(ref _originatorId, value))
            {
                Raise(nameof(OriginValidationText));
                Raise(nameof(CanSend));
            }
        }
    }
    public string OriginValidationText => IsValidOriginator(OriginatorId)
        ? string.Empty : "Originator ID requires 1–64 printable ASCII characters.";
    public static bool IsValidOriginator(string? id) =>
        !string.IsNullOrWhiteSpace(id) && id.Length <= 64 &&
        id.All(c => c is >= ' ' and <= '~');
    public bool AllowCustomValue
        => _controlCdcText is "APC" or "INC" or "ISC" or "INC/ISC";

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
            Raise(nameof(SendButtonText));
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
            Raise(nameof(CanClose));
            Raise(nameof(CanReinspect));
            Raise(nameof(CanEdit));
            Raise(nameof(CanCopyEvidence));
            Raise(nameof(SendButtonText));
        }
    }
    public bool CanClose => !_commandFlightActive;
    public bool CanCopyEvidence => !IsBusy && !_commandFlightActive &&
        !string.IsNullOrWhiteSpace(ControlEvidenceText);
    public bool CanReinspect => !_commandFlightActive && !IsBusy;
    public bool CanEdit => IsReady && !IsBusy && !_commandAttempted;
    public bool IsReady
    {
        get => _isReady;
        private set
        {
            if (!Set(ref _isReady, value)) return;
            UpdateLiveWarning();
            Raise(nameof(CanSend));
            Raise(nameof(CanEdit));
        }
    }
    public string CommandStage { get => _commandStage; private set => Set(ref _commandStage, value); }
    public string CommandStatus { get => _commandStatus; private set => Set(ref _commandStatus, value); }
    public string SendButtonText => IsBusy ? "Working…" : TestMode ? "Send test" :
        _liveControlModel is Iec61850ControlModelKind.SboNormal or
            Iec61850ControlModelKind.SboEnhanced ? "Select → Operate" : "Operate";
    public bool CanSend => IsReady && !IsBusy && !_commandAttempted &&
        !string.IsNullOrWhiteSpace(SelectedValue) &&
        IsValidOriginator(OriginatorId) && OriginCategories.Contains(SelectedOriginCategory) &&
        ConfirmLiveCommand;

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
        // Honor the actual DPI-aware working area. All details scroll while
        // the command confirmation footer stays on-screen at 1366x768/150%.
        var work = SystemParameters.WorkArea;
        Height = Math.Max(MinHeight, Math.Min(500, work.Height - 24));
        Width = Math.Max(MinWidth, Math.Min(565, work.Width - 24));
        await InspectAsync();
    }

    private async Task InspectAsync()
    {
        if (_commandFlightActive || IsBusy) return;
        _commandAttempted = false;
        ConfirmLiveCommand = false;
        IsReady = false;
        SelectedValue = string.Empty;
        ControlNumberText = "Auto";
        TerminationText = "—";
        WireTimelineText = string.Empty;
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
            _liveControlModel = capabilities.ControlModel;
            ModelShortText = capabilities.ControlModel switch
            {
                Iec61850ControlModelKind.DirectNormal => "Direct · normal",
                Iec61850ControlModelKind.DirectEnhanced => "Direct · enhanced",
                Iec61850ControlModelKind.SboNormal => "SBO · normal",
                Iec61850ControlModelKind.SboEnhanced => "SBO · enhanced",
                Iec61850ControlModelKind.StatusOnly => "Status only",
                _ => "Unverified"
            };
            SequenceHint = !capabilities.SupportsOperate ? "Not operable" :
                capabilities.UsesSelectBeforeOperate
                ? "Native: Select → Operate" : "Native: Operate";
            Raise(nameof(SendButtonText));
            ControlCdcText = string.IsNullOrWhiteSpace(capabilities.ControlCdc) ? "CONTROL" : capabilities.ControlCdc;
            Raise(nameof(AllowCustomValue));
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
                    ? "Choose a value and verify the checks before sending."
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

    private async void SendCommand_Click(object sender, RoutedEventArgs e)
    {
        if (!CanSend)
            return;

        // Keep explicit staged safety confirmation without cluttering the main popup.
        // SBO selection is part of the native one-shot sequence, NOT an already-held lease.
        var confirm = MessageBox.Show(this,
            $"IED: {_device.Name}\nObject: {Telegram}\nValue: {SelectedValue}\n" +
            $"Sequence: {ModelShortText}\nOrigin: {SelectedOriginCategory}/{OriginatorId.Trim()}\n\n" +
            (TestMode ? "Send an IEC 61850 command with Test=true?" :
                        "Send a LIVE IEC 61850 command to this IED?"),
            "Confirm control", MessageBoxButton.YesNo,
            MessageBoxImage.Warning, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        // One explicit wire attempt consumes the preview. Subsequent commands
        // need a fresh typed ctlModel / state inspection on this association.
        _commandAttempted = true;
        Raise(nameof(CanSend));
        _commandFlightActive = true;
        Raise(nameof(CanClose));
        Raise(nameof(CanReinspect));
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
                    Originator = OriginatorId.Trim(),
                    OriginCategory = SelectedOriginCategory
                },
                _cancellation.Token);

            ControlNumberText = result.ControlNumber;
            WireTimelineText = Iec61850ControlOutcomePresentation.WireTimeline(result);
            TerminationText = result.CommandTerminationReceived
                ? result.PositiveTermination ? "Positive" : "Negative" : "Not reported";
            var outcome = Iec61850ControlOutcomePresentation.From(result, TestMode);
            CommandStage = outcome.Stage;
            CommandStatus = outcome.Summary;
            // IEC 61850 service acceptance is not process-image authority.
            // Wire steps, termination, LastApplError and timeout remain inspectable.
            ControlEvidenceText += "\n\nRESULT\n" + BuildCommandResultText(result);
            SetResultTone(outcome.Tone);
        }
        catch (OperationCanceledException)
        {
            CommandStage = "Outcome uncertain";
            CommandStatus = "Request may have reached the IED. Verify before another attempt.";
            SetResultTone("Neutral");
        }
        catch (InvalidOperationException ex) when (
            ex.Message.Contains("command NOT SENT", StringComparison.OrdinalIgnoreCase))
        {
            // The facade rejected locally before an MMS control service.
            // Never label this as an attempted command's wire failure.
            CommandStage = "Command not sent";
            CommandStatus = ex.Message;
            SetResultTone("Warning");
        }
        catch (Exception ex)
        {
            // An unexpected failure after dispatch cannot prove the command was NOT sent.
            CommandStage = "Outcome unresolved";
            CommandStatus = $"{ex.GetType().Name}: {ex.Message}. Verify IED state before retry.";
            SetResultTone("Warning");
        }
        finally
        {
            _commandFlightActive = false;
            Raise(nameof(CanClose));
            Raise(nameof(CanReinspect));
            IsBusy = false;
            Raise(nameof(CanCopyEvidence));
            Raise(nameof(CanSend));
            Raise(nameof(CanEdit));
        }
    }

    private async void Reinspect_Click(object sender, RoutedEventArgs e)
    {
        // Only a read-only live reinspection can re-arm the command action.
        // Never retry a timed out or ambiguous native command automatically.
        if (!CanReinspect) return;
        await InspectAsync();
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

        // Never pre-select Open, Close, Raise, or a numeric setpoint.
        // The operator must deliberately choose an action after inspection.
        SelectedValue = string.Empty;
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
        // Test=true is an MMS control flag, not an offline simulator.
        // Some servers may still change equipment: authorization is mandatory.
        LiveCommandWarning.Visibility = IsReady ? Visibility.Visible : Visibility.Collapsed;
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

    private void CopyEvidence_Click(object sender, RoutedEventArgs e)
    {
        if (!CanCopyEvidence) return;
        // Pure local clipboard export: NO read, Select, Operate, Cancel, Write,
        // reconnect or other MMS/network action is permitted in this handler.
        var snapshot = new Iec61850ControlFatEvidence(
            DateTimeOffset.UtcNow,
            DeviceSummary, Telegram, ControlCdcText, ControlModelText,
            CurrentValue, SelectedValue, SelectedOriginCategory, OriginatorId,
            InterlockCheck, SynchroCheck, TestMode, CommandStage, CommandStatus,
            ControlNumberText, TerminationText, WireTimelineText, ControlEvidenceText);
        try
        {
            Clipboard.SetText(snapshot.ToClipboardText());
            CopyEvidenceButton.Content = "Copied";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            CopyEvidenceButton.Content = "Copy failed";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e)
        => Close();

    private void Window_Closing(object? sender, CancelEventArgs e)
    {
        // Never abandon a dispatched native control flight by simply closing
        // its UI. Late responses need the owning session to resolve/quarantine.
        if (_commandFlightActive)
        {
            e.Cancel = true;
            CommandStatus = "IED operation still resolving. Close after the result is known.";
            return;
        }
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
