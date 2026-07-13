using System.Text.RegularExpressions;

namespace ArIED61850Tester.Models;

public class SignalDefinition : ObservableObject
{
    private bool _isSelected;
    private bool _isReportCapable;
    private string _value = "-";
    private string _quality = "Unknown";
    private string _deviceTimestamp = "-";
    private string _probeStatus = "Not probed";
    private string _reportCoverage = "Polling fallback";
    private string _displayReference = string.Empty;
    private DateTime _timestamp = DateTime.MinValue;
    private string _controlCdc = string.Empty;
    private string _controlModelReference = string.Empty;
    private string _controlStatusReference = string.Empty;
    private string _controlModelText = "Auto-detect";
    private Iec61850ControlModelKind _controlModelKind = Iec61850ControlModelKind.Unknown;
    private bool _controlModelResolved;
    private string _controlValueType = string.Empty;
    private string _controlCurrentValue = "-";
    private string? _deferredControlCurrentValue;
    private string _controlSetPointText = string.Empty;
    private string _controlLastResult = string.Empty;
    private bool _controlIsBusy;
    private bool _controlInterlockCheck = true;
    private bool _controlSynchroCheck;
    private bool _controlTestMode;
    private bool _controlConfirmationPending;
    private string _controlPendingValue = string.Empty;
    private string _controlPendingAction = string.Empty;

    private static readonly HashSet<string> ControlServiceLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctlModel", "ctlVal", "ctlNum", "stSeld", "SBO", "SBOw", "Oper", "Cancel",
        "origin", "T", "Test", "Check", "operTm", "sboClass", "sboTimeout", "operTimeout"
    };

    private static readonly string[] KnownLogicalNodeClasses =
    {
        "CSWI", "XCBR", "XSWI",
        "ATCC", "AVCO", "AVC", "YPTR",
        "MMXU", "MMXN", "MSQI",
        "PTOC", "PTRC", "PDIF", "PDIS", "PIOC", "PTOV", "PTUV", "PTEF", "PDEF", "PSCH", "RREC", "RBRF",
        "GGIO", "GAPC", "LLN0", "LPHD", "CILO", "CPOW"
    };

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (Set(ref _isSelected, value))
                Raise(nameof(ReportPlan));
        }
    }

    public string Name { get; set; } = "";
    public string ObjectReference { get; set; } = "";
    public string DisplayReference
    {
        get => string.IsNullOrWhiteSpace(_displayReference) ? ObjectReference : _displayReference;
        set => Set(ref _displayReference, value?.Trim() ?? string.Empty);
    }
    public string FunctionalConstraint { get; set; } = "";
    public string DataType { get; set; } = "";
    public string Category { get; set; } = "";
    public string Unit { get; set; } = "";
    public string Confidence { get; set; } = "Medium";
    public string DataSetReference { get; set; } = "";
    public string ReportControlReference { get; set; } = "";
    public string ReportCoverageReason { get; set; } = "Readable by MMS; report coverage not confirmed.";

    // Compatibility alias used by the UI search filter.
    // The canonical field is ReportCoverageReason; keeping this read-only alias prevents
    // compile breaks when older/newer UI code searches by report-plan reason text.
    public string ReportPlanReason => ReportCoverageReason;

    public string QualityReference { get; set; } = "";
    public string TimestampReference { get; set; } = "";
    public string Source { get; set; } = "Online";
    public bool IsControlSignal { get; set; }
    public bool IsValidControlObject => IsControlSignal && IsControlObjectReference(ObjectReference);
    public string ControlCdc
    {
        get => _controlCdc;
        set
        {
            if (!Set(ref _controlCdc, value?.Trim() ?? string.Empty)) return;
            RaiseControlActionProperties();
        }
    }
    public string ControlModelReference { get => _controlModelReference; set => Set(ref _controlModelReference, value?.Trim() ?? string.Empty); }
    public string ControlStatusReference { get => _controlStatusReference; set => Set(ref _controlStatusReference, value?.Trim() ?? string.Empty); }
    public string ControlModelText
    {
        get => _controlModelText;
        set
        {
            var normalized = string.IsNullOrWhiteSpace(value) ? "Auto-detect" : value.Trim();
            var changed = Set(ref _controlModelText, normalized);

            if (normalized.Contains("auto-detect", StringComparison.OrdinalIgnoreCase))
                ApplyControlModel(Iec61850ControlModelKind.Unknown, resolved: false, updateDisplay: false);
            else
                UpdateControlModelFromEvidence(normalized, updateDisplay: false);

            if (changed)
                Raise(nameof(SignalPropertiesSummary));
        }
    }
    public Iec61850ControlModelKind ControlModelKind => _controlModelKind;
    public bool ControlModelResolved => _controlModelResolved;
    public bool ControlSupportsOperate => _controlModelResolved && _controlModelKind is
        Iec61850ControlModelKind.DirectNormal or
        Iec61850ControlModelKind.SboNormal or
        Iec61850ControlModelKind.DirectEnhanced or
        Iec61850ControlModelKind.SboEnhanced;
    public bool IsReadOnlyControl => _controlModelResolved && !ControlSupportsOperate;
    public string ControlValueType { get => _controlValueType; set => Set(ref _controlValueType, value?.Trim() ?? string.Empty); }

    /// <summary>
    /// Current process feedback shown in the fast Command Panel. While a command is in
    /// progress, report/poll updates are coalesced and the final command observation is
    /// published atomically. This prevents a stale pre-operate sample from making an SBO
    /// command look as though it changed and immediately reverted.
    /// </summary>
    public string ControlCurrentValue
    {
        get => _controlCurrentValue;
        set
        {
            var normalized = NormalizeControlDisplayValue(value);
            if (ControlIsBusy)
            {
                _deferredControlCurrentValue = normalized;
                return;
            }

            ApplyControlCurrentValue(normalized);
        }
    }

    public string ControlCurrentTone
    {
        get
        {
            var text = (ControlCurrentValue ?? string.Empty).Trim().ToLowerInvariant();
            if (text is "closed" or "true" or "on" or "active") return "Energized";
            if (text is "open" or "false" or "off" or "inactive") return "Deenergized";
            if (text.Contains("intermediate") || text.Contains("bad")) return "Abnormal";
            return "Neutral";
        }
    }

    public string ControlSetPointText { get => _controlSetPointText; set => Set(ref _controlSetPointText, value?.Trim() ?? string.Empty); }
    public string ControlLastResult
    {
        get => _controlLastResult;
        set
        {
            var normalized = value?.Trim() ?? string.Empty;
            UpdateControlModelFromEvidence(normalized, updateDisplay: true);
            normalized = NormalizeControlResultText(normalized);
            Set(ref _controlLastResult, normalized);
        }
    }
    public bool ControlIsBusy
    {
        get => _controlIsBusy;
        set
        {
            if (!Set(ref _controlIsBusy, value))
                return;

            Raise(nameof(ControlCanConfirm));

            if (value)
            {
                _deferredControlCurrentValue = null;
                return;
            }

            if (_deferredControlCurrentValue != null)
            {
                var deferred = _deferredControlCurrentValue;
                _deferredControlCurrentValue = null;
                ApplyControlCurrentValue(deferred);
            }
        }
    }

    public bool ControlInterlockCheck
    {
        get => _controlInterlockCheck;
        set => Set(ref _controlInterlockCheck, value);
    }

    public bool ControlSynchroCheck
    {
        get => _controlSynchroCheck;
        set => Set(ref _controlSynchroCheck, value);
    }

    public bool ControlTestMode
    {
        get => _controlTestMode;
        set => Set(ref _controlTestMode, value);
    }

    public bool ControlConfirmationPending => _controlConfirmationPending;
    public string ControlPendingValue => _controlPendingValue;
    public string ControlPendingAction => _controlPendingAction;
    public string ControlPendingConfirmationLabel =>
        string.IsNullOrWhiteSpace(_controlPendingAction) ? "Confirm command" : $"Confirm {_controlPendingAction}";
    public bool ControlCanConfirm => _controlConfirmationPending && ControlSupportsOperate && !ControlIsBusy;

    public void StageControlConfirmation(string requestedValue, string actionLabel)
    {
        var normalizedValue = requestedValue?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(normalizedValue))
            return;

        Set(ref _controlPendingValue, normalizedValue, nameof(ControlPendingValue));
        if (Set(ref _controlPendingAction, actionLabel?.Trim() ?? string.Empty, nameof(ControlPendingAction)))
            Raise(nameof(ControlPendingConfirmationLabel));
        if (Set(ref _controlConfirmationPending, true, nameof(ControlConfirmationPending)))
            Raise(nameof(ControlCanConfirm));
    }

    public void ClearControlConfirmation()
    {
        Set(ref _controlPendingValue, string.Empty, nameof(ControlPendingValue));
        if (Set(ref _controlPendingAction, string.Empty, nameof(ControlPendingAction)))
            Raise(nameof(ControlPendingConfirmationLabel));
        if (Set(ref _controlConfirmationPending, false, nameof(ControlConfirmationPending)))
            Raise(nameof(ControlCanConfirm));
    }

    private bool CanExposeControlActions => !_controlModelResolved || ControlSupportsOperate;

    private bool IsPositionSemanticControl
    {
        get
        {
            var reference = (ObjectReference ?? string.Empty).Replace('$', '.').TrimEnd('.');
            var leaf = reference[(reference.LastIndexOf('.') + 1)..];
            return leaf.Equals("Pos", StringComparison.OrdinalIgnoreCase) ||
                   ((LogicalNodeClass.Equals("CSWI", StringComparison.OrdinalIgnoreCase) ||
                     LogicalNodeClass.Equals("XCBR", StringComparison.OrdinalIgnoreCase) ||
                     LogicalNodeClass.Equals("XSWI", StringComparison.OrdinalIgnoreCase)) &&
                    reference.Contains(".Pos", StringComparison.OrdinalIgnoreCase));
        }
    }

    public bool IsPositionControl => CanExposeControlActions && IsPositionSemanticControl;
    public bool IsRaiseOnlyControl => CanExposeControlActions && (ContainsControlToken("TapOpR") || ContainsControlToken("Raise"));
    public bool IsLowerOnlyControl => CanExposeControlActions && (ContainsControlToken("TapOpL") || ContainsControlToken("Lower"));
    public bool IsRaiseLowerControl
    {
        get
        {
            if (!CanExposeControlActions || IsPositionControl || IsRaiseOnlyControl || IsLowerOnlyControl) return false;
            var cdc = (ControlCdc ?? string.Empty).Trim().ToUpperInvariant();
            return cdc is "INC" or "ISC" or "INC/ISC";
        }
    }
    public bool IsBooleanControl => CanExposeControlActions && !IsPositionControl && !IsRaiseOnlyControl && !IsLowerOnlyControl && !IsRaiseLowerControl &&
                                    (ControlCdc ?? string.Empty).Trim().Equals("SPC", StringComparison.OrdinalIgnoreCase);
    public bool IsSetPointControl
    {
        get
        {
            if (!CanExposeControlActions || IsPositionControl || IsRaiseOnlyControl || IsLowerOnlyControl || IsRaiseLowerControl || IsBooleanControl) return false;
            var cdc = (ControlCdc ?? string.Empty).Trim().ToUpperInvariant();
            return cdc is "APC" or "BAC" or "BSC";
        }
    }
    public bool IsGenericControl => !CanExposeControlActions ||
                                    (!IsPositionControl && !IsRaiseOnlyControl && !IsLowerOnlyControl &&
                                     !IsRaiseLowerControl && !IsBooleanControl && !IsSetPointControl);

    public string ControlActionLabel
    {
        get
        {
            if (IsReadOnlyControl) return "Read only";
            if (IsPositionControl) return "Open / Close";
            if (IsRaiseOnlyControl) return "Raise";
            if (IsLowerOnlyControl) return "Lower";
            var cdc = (ControlCdc ?? string.Empty).Trim().ToUpperInvariant();
            if (cdc == "DPC") return "Open / Close";
            if (cdc is "INC" or "ISC") return "Raise / Lower";
            if (cdc == "BSC") return "Tap / step position";
            if (cdc == "SPC")
            {
                var commandText = $"{Name} {ObjectReference}";
                if (commandText.Contains("TapOpR", StringComparison.OrdinalIgnoreCase) ||
                    commandText.Contains("Raise", StringComparison.OrdinalIgnoreCase))
                    return "Raise tap";
                if (commandText.Contains("TapOpL", StringComparison.OrdinalIgnoreCase) ||
                    commandText.Contains("Lower", StringComparison.OrdinalIgnoreCase))
                    return "Lower tap";
                return "On / Off";
            }
            if (cdc is "APC" or "BAC") return "Set value";
            return "Send command";
        }
    }

    private void ApplyControlCurrentValue(string normalized)
    {
        if (Set(ref _controlCurrentValue, normalized, nameof(ControlCurrentValue)))
            Raise(nameof(ControlCurrentTone));
    }

    private void UpdateControlModelFromEvidence(string? evidence, bool updateDisplay)
    {
        if (!TryParseControlModel(evidence, out var model))
            return;

        ApplyControlModel(model, resolved: true, updateDisplay);
    }

    private void ApplyControlModel(Iec61850ControlModelKind model, bool resolved, bool updateDisplay)
    {
        var modelChanged = _controlModelKind != model;
        var resolvedChanged = _controlModelResolved != resolved;
        _controlModelKind = model;
        _controlModelResolved = resolved;

        if (updateDisplay && resolved)
        {
            var friendly = FriendlyControlModel(model);
            if (!string.Equals(_controlModelText, friendly, StringComparison.Ordinal))
            {
                _controlModelText = friendly;
                Raise(nameof(ControlModelText));
            }
        }

        if (!modelChanged && !resolvedChanged)
            return;

        Raise(nameof(ControlModelKind));
        Raise(nameof(ControlModelResolved));
        Raise(nameof(ControlSupportsOperate));
        Raise(nameof(ControlCanConfirm));
        Raise(nameof(IsReadOnlyControl));
        RaiseControlActionProperties();
    }

    private void RaiseControlActionProperties()
    {
        Raise(nameof(ControlActionLabel));
        Raise(nameof(IsPositionControl));
        Raise(nameof(IsRaiseOnlyControl));
        Raise(nameof(IsLowerOnlyControl));
        Raise(nameof(IsRaiseLowerControl));
        Raise(nameof(IsBooleanControl));
        Raise(nameof(IsSetPointControl));
        Raise(nameof(IsGenericControl));
        Raise(nameof(SignalPropertiesSummary));
    }

    private string NormalizeControlResultText(string text)
    {
        if (IsReadOnlyControl && ControlModelKind == Iec61850ControlModelKind.StatusOnly)
            return "Status only — read-only object; commands disabled by the IED ctlModel.";
        if (IsReadOnlyControl && ControlModelKind == Iec61850ControlModelKind.Unknown)
            return "Unknown ctlModel — commands disabled until the live control model is resolved.";

        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var sequence = ControlModelKind switch
        {
            Iec61850ControlModelKind.SboEnhanced => "SBOw → Operate",
            Iec61850ControlModelKind.SboNormal => "SBO Select → Operate",
            _ => string.Empty
        };
        if (string.IsNullOrWhiteSpace(sequence) || text.Contains("SBO", StringComparison.OrdinalIgnoreCase))
            return text;

        return text.StartsWith("Sending", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Feedback", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Command", StringComparison.OrdinalIgnoreCase) ||
               text.StartsWith("Control", StringComparison.OrdinalIgnoreCase)
            ? $"{sequence} • {text}"
            : text;
    }

    private static bool TryParseControlModel(string? text, out Iec61850ControlModelKind model)
    {
        model = Iec61850ControlModelKind.Unknown;
        if (string.IsNullOrWhiteSpace(text) || text.Contains("auto-detect", StringComparison.OrdinalIgnoreCase))
            return false;

        var normalized = text.Trim().ToLowerInvariant();
        var numeric = Regex.Match(normalized, @"ctlmodel\s*[:=]\s*([0-4])", RegexOptions.IgnoreCase);
        if (numeric.Success)
        {
            model = numeric.Groups[1].Value switch
            {
                "0" => Iec61850ControlModelKind.StatusOnly,
                "1" => Iec61850ControlModelKind.DirectNormal,
                "2" => Iec61850ControlModelKind.SboNormal,
                "3" => Iec61850ControlModelKind.DirectEnhanced,
                "4" => Iec61850ControlModelKind.SboEnhanced,
                _ => Iec61850ControlModelKind.Unknown
            };
            return true;
        }

        if (normalized.Contains("statusonly") || normalized.Contains("status only"))
            model = Iec61850ControlModelKind.StatusOnly;
        else if (normalized.Contains("selectbeforeoperateenhanced") ||
                 (normalized.Contains("sbo") && normalized.Contains("enhanced")))
            model = Iec61850ControlModelKind.SboEnhanced;
        else if (normalized.Contains("selectbeforeoperatenormal") ||
                 (normalized.Contains("sbo") && normalized.Contains("normal")))
            model = Iec61850ControlModelKind.SboNormal;
        else if (normalized.Contains("directenhanced") ||
                 (normalized.Contains("direct") && normalized.Contains("enhanced")))
            model = Iec61850ControlModelKind.DirectEnhanced;
        else if (normalized.Contains("directnormal") ||
                 (normalized.Contains("direct") && normalized.Contains("normal")))
            model = Iec61850ControlModelKind.DirectNormal;
        else if (normalized == "unknown" ||
                 (normalized.Contains("ctlmodel") && normalized.Contains("unknown")))
            model = Iec61850ControlModelKind.Unknown;
        else
            return false;

        return true;
    }

    private static string FriendlyControlModel(Iec61850ControlModelKind model)
        => model switch
        {
            Iec61850ControlModelKind.DirectNormal => "Direct Operate (DO) • Normal security",
            Iec61850ControlModelKind.SboNormal => "Select Before Operate (SBO) • Normal security",
            Iec61850ControlModelKind.DirectEnhanced => "Direct Operate (DO) • Enhanced security",
            Iec61850ControlModelKind.SboEnhanced => "Select Before Operate (SBO) • Enhanced security",
            Iec61850ControlModelKind.StatusOnly => "Status only",
            _ => "Unknown"
        };

    private bool ContainsControlToken(string token)
        => $"{Name} {ObjectReference}".Contains(token, StringComparison.OrdinalIgnoreCase);

    private string NormalizeControlDisplayValue(string? value)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        if (IsPositionSemanticControl && ArIED61850Tester.Services.Iec61850ValueFormatter.TryNormalizeDbpos(text, out var code))
        {
            return code switch
            {
                0 => "Intermediate",
                1 => "Open",
                2 => "Closed",
                3 => "Bad state",
                _ => text
            };
        }

        if (bool.TryParse(text, out var boolean))
            return boolean ? "True" : "False";
        if (text.Equals("ON", StringComparison.OrdinalIgnoreCase)) return IsPositionSemanticControl ? "Closed" : "True";
        if (text.Equals("OFF", StringComparison.OrdinalIgnoreCase)) return IsPositionSemanticControl ? "Open" : "False";
        return text;
    }

    public bool IsReportCapable
    {
        get => _isReportCapable;
        set
        {
            if (Set(ref _isReportCapable, value))
                Raise(nameof(ReportPlan));
        }
    }

    public string LogicalNode => ExtractLogicalNode(ObjectReference);
    public string LogicalNodeClass => DetectLogicalNodeClass(LogicalNode);

    // q/t, Health, Beh, Mod, RCB attributes, nameplate, and other engineering leaves are
    // companion/diagnostic attributes. They must not become user-selected SCADA points.
    public bool IsRawAttribute => IsRawEngineeringAttribute(ObjectReference, DataType);

    public bool IsValueSignal => !IsControlSignal && IsRuntimeValueSignal(ObjectReference, FunctionalConstraint, DataType, Category);
    public bool CanPublishAsSignal => !IsControlSignal && IsValueSignal && !IsRawAttribute;
    public bool IsKnownReadFailure => IsKnownReadFailureState(Value, Quality, ProbeStatus);
    public bool CanPublishToRuntime => CanPublishAsSignal && !IsKnownReadFailure;
    public bool IsScadaCoreSignal => IsCoreScadaSignal(ObjectReference, LogicalNodeClass, DataType, Category);
    public int SortPriority => IsControlSignal
        ? 45
        : CalculateSortPriority(LogicalNodeClass, ObjectReference, Category, Confidence, IsScadaCoreSignal);

    public string ReportCoverage
    {
        get => _reportCoverage;
        set
        {
            if (Set(ref _reportCoverage, string.IsNullOrWhiteSpace(value) ? "Polling fallback" : value))
                Raise(nameof(ReportPlan));
        }
    }

    public string ReportPlan => IsControlSignal
        ? "IEC 61850 control object"
        : !CanPublishAsSignal
            ? "Hidden attribute"
            : !string.IsNullOrWhiteSpace(ReportCoverage)
                ? ReportCoverage
                : IsReportCapable
                    ? "Report candidate + polling fallback"
                    : "MMS polling";

    public string SignalPropertiesSummary => BuildSignalPropertiesSummary();

    public string Value { get => _value; set => Set(ref _value, value); }
    public string Quality { get => _quality; set => Set(ref _quality, value); }
    public string DeviceTimestamp { get => _deviceTimestamp; set => Set(ref _deviceTimestamp, string.IsNullOrWhiteSpace(value) ? "-" : value); }
    public string ProbeStatus { get => _probeStatus; set => Set(ref _probeStatus, string.IsNullOrWhiteSpace(value) ? "Not probed" : value); }
    public DateTime Timestamp { get => _timestamp; set => Set(ref _timestamp, value); }

    private string BuildSignalPropertiesSummary()
    {
        var q = string.IsNullOrWhiteSpace(QualityReference) ? "auto sidecar / not confirmed" : QualityReference;
        var t = string.IsNullOrWhiteSpace(TimestampReference) ? "auto sidecar / not confirmed" : TimestampReference;
        var ds = string.IsNullOrWhiteSpace(DataSetReference)
            ? IsReportCapable ? "temporary dynamic DataSet candidate" : "not covered by confirmed DataSet"
            : DataSetReference;
        var rcb = string.IsNullOrWhiteSpace(ReportControlReference)
            ? IsReportCapable ? "dynamic report candidate / polling last fallback" : "none / polling fallback"
            : ReportControlReference;
        var reason = string.IsNullOrWhiteSpace(ReportCoverageReason) ? ReportPlan : ReportCoverageReason;

        var control = IsControlSignal
            ? $"Control CDC: {(string.IsNullOrWhiteSpace(ControlCdc) ? "unknown" : ControlCdc)}\n" +
              $"ctlModel attribute: {(string.IsNullOrWhiteSpace(ControlModelReference) ? "not confirmed" : ControlModelReference)}\n" +
              $"Status feedback: {(string.IsNullOrWhiteSpace(ControlStatusReference) ? "auto-resolve" : ControlStatusReference)}\n" +
              $"Control model: {ControlModelText}\n"
            : string.Empty;

        return $"IEC Signal: {ObjectReference}\n" +
               $"Functional Constraint: {FunctionalConstraint}\n" +
               $"Data Type: {DataType}\n" +
               $"Category: {Category}\n" +
               control +
               $"Logical Node: {LogicalNode} ({LogicalNodeClass})\n" +
               $"Quality Attribute: {q}\n" +
               $"Timestamp Attribute: {t}\n" +
               $"DataSet: {ds}\n" +
               $"Report Control Block: {rcb}\n" +
               $"Runtime Source: {ReportPlan}\n" +
               $"Reason: {reason}";
    }

    private static string ExtractLogicalNode(string reference)
    {
        if (string.IsNullOrWhiteSpace(reference)) return "";
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash == reference.Length - 1) return "";
        var afterSlash = reference[(slash + 1)..];
        var dot = afterSlash.IndexOf('.');
        return dot > 0 ? afterSlash[..dot] : afterSlash;
    }

    public static bool IsControlObjectReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('/'))
            return false;
        var leaf = normalized[(normalized.LastIndexOf('.') + 1)..];
        return !ControlServiceLeafNames.Contains(leaf);
    }

    public static string DetectLogicalNodeClass(string logicalNodeName)
    {
        if (string.IsNullOrWhiteSpace(logicalNodeName)) return "";

        // IEC 61850 logical node names commonly allow vendor/project prefix/suffix.
        // Example from live IEDs: BI6GGIO1, OCRSR12PROT/PTRC1, CTRLCSWI1.
        // We therefore detect the standard LN class inside the full LN name, not only at the start.
        foreach (var cls in KnownLogicalNodeClasses)
        {
            if (logicalNodeName.Contains(cls, StringComparison.OrdinalIgnoreCase))
                return cls;
        }

        return logicalNodeName;
    }

    private static int CalculateSortPriority(string lnClass, string reference, string category, string confidence, bool isCoreScadaSignal)
    {
        if (!isCoreScadaSignal) return 800 + AttributeNoisePenalty(reference);

        // SCADA/substation tester workflow order: switchgear position first, protection second, measurements last.
        // In HMI operation the user usually needs CB/DS position visibility before analysing protection and analog trends.
        return lnClass.ToUpperInvariant() switch
        {
            "CSWI" => 10,
            "XCBR" => 12,
            "XSWI" => 14,
            "PTOC" => 100,
            "PTRC" => 102,
            "PDIF" => 104,
            "PDIS" => 106,
            "PIOC" => 108,
            "PTOV" => 110,
            "PTUV" => 112,
            "PTEF" => 114,
            "PDEF" => 116,
            "ATCC" => 180,
            "AVC" or "AVCO" => 185,
            "MMXU" => 220,
            "MMXN" => 225,
            "GGIO" => 260,
            "YPTR" => 270,
            _ when string.Equals(category, "Position", StringComparison.OrdinalIgnoreCase) => 20,
            _ when string.Equals(category, "Protection", StringComparison.OrdinalIgnoreCase) => 120,
            _ when string.Equals(category, "Measurement", StringComparison.OrdinalIgnoreCase) => 240,
            _ => 300
        };
    }

    private static int AttributeNoisePenalty(string reference)
    {
        var lower = NormalizeRef(reference);
        if (lower.EndsWith(".q")) return 40;
        if (lower.EndsWith(".t") || lower.EndsWith(".tm")) return 50;
        if (lower.Contains(".ctlval") || lower.Contains(".origin") || lower.Contains(".ctlmodel")) return 60;
        if (lower.Contains(".mod.") || lower.EndsWith(".mod.stval") || lower.Contains(".beh.") || lower.Contains(".health") || lower.Contains(".eehealth")) return 90;
        return 0;
    }

    public static bool IsCoreScadaSignal(string reference, string logicalNodeClass, string dataType, string category)
    {
        var r = NormalizeRef(reference);
        var cls = logicalNodeClass.ToUpperInvariant();

        if (IsRawEngineeringAttribute(reference, dataType))
            return false;
        if (!IsRuntimeValueLeaf(r, dataType))
            return false;
        if (IsExcludedStatisticLogicalNode(reference))
            return false;

        // Primary equipment status that operators expect in HMI/SCADA.
        if ((cls is "CSWI" or "XCBR" or "XSWI") && r.EndsWith(".pos.stval"))
            return true;

        // Normal measurement groups use cVal. Siemens OperationalValues groups expose
        // the directly readable instantaneous leaf as instCVal, while cVal can reject
        // direct MMS reads even though the parent DO is visible in an engineering tool.
        if (cls is "MMXU" or "MMXN")
            return IsDefaultScadaMeasurementMagnitude(r);

        // Protection HMI points: operate/trip/start general flags only.
        if (cls == "PTOC" && (r.EndsWith(".op.general") || r.EndsWith(".str.general"))) return true;
        if (cls == "PTRC" && r.EndsWith(".tr.general")) return true;
        if (cls == "RBRF" && (r.EndsWith(".opex.general") || r.EndsWith(".op.general"))) return true;
        if ((cls is "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF") && r.EndsWith(".op.general")) return true;

        if (cls is "ATCC" or "AVC" or "AVCO")
            return IsAvrOperationalSignal(r, dataType, category);

        if (cls == "GGIO")
            return IsGgioOperationalSignal(r, dataType, category);

        if (cls == "YPTR" && r.Contains(".tappos."))
            return dataType is "Int32" or "Integer" or "UInt32" or "Enum";

        return false;
    }

    private static bool IsGgioOperationalSignal(string normalizedReference, string dataType, string category)
    {
        var r = NormalizeRef(normalizedReference);

        // Automation IEDs often expose DI points as GGIO.Ind15.stVal and analogs as GGIO.AnIn1.mag.f.
        // Keep those as real SCADA points, but never promote GGIO.Beh/Health/q/t as selectable signals.
        if (Regex.IsMatch(r, @"\.ind\d+\.stval$", RegexOptions.IgnoreCase))
            return dataType is "Boolean" or "Enum" or "Int32" or "Integer";

        if (Regex.IsMatch(r, @"\.anin\d+\.(?:mag\.)?f$", RegexOptions.IgnoreCase) ||
            Regex.IsMatch(r, @"\.anin\d+\.mag\.f$", RegexOptions.IgnoreCase))
            return dataType is "Float32" or "Float" or "Double";

        if (dataType is "Boolean" && r.EndsWith(".stval") && string.Equals(category, "Status", StringComparison.OrdinalIgnoreCase))
            return true;

        return false;
    }

    private static bool IsAvrOperationalSignal(string normalizedReference, string dataType, string category)
    {
        var r = NormalizeRef(normalizedReference);
        if (IsRawEngineeringAttribute(r, dataType) ||
            r.EndsWith(".ctlmodel") ||
            r.EndsWith(".persistent") ||
            r.EndsWith(".d") ||
            r.Contains(".oper.") ||
            r.EndsWith(".oper"))
        {
            return false;
        }

        if (string.Equals(category, "Measurement", StringComparison.OrdinalIgnoreCase) && dataType == "Float32")
            return IsKnownAvrMeasurement(r);

        if (dataType is "Boolean" or "Enum" or "Int32" or "Integer" or "UInt32")
        {
            return r.Contains(".loc.") ||
                   r.Contains(".tapchg.valwtr.posval") ||
                   r.EndsWith(".tapchg.stval") ||
                   r.Contains(".parop.") ||
                   r.Contains(".ltcblk") ||
                   r.Contains(".mastersel.") ||
                   r.Contains(".followsel.") ||
                   r.Contains(".circasel.") ||
                   r.Contains(".circapfsel.") ||
                   r.Contains(".funcmon.") ||
                   r.Contains(".auto.") ||
                   r.Contains(".ldc.") ||
                   r.Contains(".errpar.") ||
                   r.Contains(".opcntrs.");
        }

        return false;
    }

    private static bool IsKnownAvrMeasurement(string normalizedReference)
    {
        var r = NormalizeRef(normalizedReference);
        return Regex.IsMatch(
            r,
            @"\.(?:ctlv|loda|circa|phang|ctldv)\.(?:mag\.)?f$",
            RegexOptions.IgnoreCase);
    }

    private static bool IsRuntimeValueSignal(string reference, string functionalConstraint, string dataType, string category)
    {
        var fc = (functionalConstraint ?? string.Empty).Trim().ToUpperInvariant();
        if (!string.IsNullOrWhiteSpace(fc) && fc is not ("ST" or "MX"))
            return false;

        var r = NormalizeRef(reference);
        if (IsRawEngineeringAttribute(r, dataType))
            return false;

        return IsRuntimeValueLeaf(r, dataType) ||
               (string.Equals(category, "Position", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".stval")) ||
               (string.Equals(category, "Protection", StringComparison.OrdinalIgnoreCase) && r.EndsWith(".general"));
    }

    private static bool IsRuntimeValueLeaf(string normalizedReference, string dataType)
    {
        var r = NormalizeRef(normalizedReference);
        if (string.Equals(dataType, "Quality", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataType, "Timestamp", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(dataType, "Directory", StringComparison.OrdinalIgnoreCase))
            return false;

        return r.EndsWith(".stval") ||
               r.EndsWith(".general") ||
               r.EndsWith(".posval") ||
               r.EndsWith(".actval") ||
               r.EndsWith(".setval") ||
               r.EndsWith(".mag.f") ||
               r.EndsWith(".ang.f") ||
               r.EndsWith(".f") ||
               r.EndsWith(".i");
    }

    public static bool IsKnownReadFailureState(string value, string quality, string probeStatus)
    {
        var status = (probeStatus ?? string.Empty).Trim();
        if (status.Equals("Readable", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Not probed", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("Reading...", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (status.Equals("Not readable", StringComparison.OrdinalIgnoreCase) ||
            status.EndsWith("Exception", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("TimeoutException", StringComparison.OrdinalIgnoreCase) ||
            status.Equals("OperationCanceledException", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var v = (value ?? string.Empty).Trim();
        var q = (quality ?? string.Empty).Trim();
        return q.Equals("Bad", StringComparison.OrdinalIgnoreCase) &&
               (string.IsNullOrWhiteSpace(v) || v == "-" || v.Equals("Read failed", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsRawEngineeringAttribute(string reference, string dataType)
    {
        if (string.Equals(dataType, "Quality", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(dataType, "Timestamp", StringComparison.OrdinalIgnoreCase)) return true;

        var r = NormalizeRef(reference);
        return IsStatisticsOrHarmonicNoise(r) ||
               r.EndsWith(".q") ||
               r.EndsWith(".t") ||
               r.EndsWith(".tm") ||
               r.Contains(".rp.") ||
               r.Contains(".br.") ||
               r.Contains(".ctlmodel") ||
               r.Contains(".ctlval") ||
               r.Contains(".origin") ||
               r.Contains(".db") ||
               r.EndsWith(".d") ||
               r.Contains(".du") ||
               r.Contains(".configrev") ||
               r.Contains(".numpts") ||
               r.Contains(".olddata") ||
               r.Contains(".mod.") ||
               r.EndsWith(".mod.stval") ||
               r.Contains(".beh.") ||
               r.EndsWith(".beh.stval") ||
               r.Contains(".health.") ||
               r.EndsWith(".health.stval") ||
               r.Contains(".eehealth.") ||
               r.EndsWith(".eehealth.stval") ||
               r.Contains(".namplt.") ||
               r.Contains(".vendor") ||
               r.Contains(".swrev") ||
               r.Contains(".configrev");
    }


    public static bool IsDefaultScadaMeasurementMagnitude(string normalizedReference)
    {
        var r = NormalizeRef(normalizedReference);
        if (IsStatisticsOrHarmonicNoise(r)) return false;
        if (!r.EndsWith(".mag.f")) return false;

        var operationalValues = r.Contains("operationalvalues") || r.Contains("operational_values");
        if (operationalValues)
        {
            if (!r.Contains(".instcval.mag.f")) return false;
        }
        else
        {
            if (!r.Contains(".cval.mag.f") || r.Contains(".instcval.")) return false;
        }

        return r.Contains(".a.phsa.") ||
               r.Contains(".a.phsb.") ||
               r.Contains(".a.phsc.") ||
               r.Contains(".a.neut.") ||
               r.Contains(".a.net.") ||
               r.Contains(".phv.phsa.") ||
               r.Contains(".phv.phsb.") ||
               r.Contains(".phv.phsc.") ||
               r.Contains(".ppv.phsab.") ||
               r.Contains(".ppv.phsbc.") ||
               r.Contains(".ppv.phsca.");
    }

    public static bool IsInstantCurrentOrVoltageMagnitude(string normalizedReference)
    {
        // Kept for compatibility with earlier code. Advanced raw browse can still find
        // instCVal, but default HMI recommendations use cVal only.
        return IsDefaultScadaMeasurementMagnitude(normalizedReference);
    }

    public static bool IsStatisticsOrHarmonicNoise(string normalizedReference)
    {
        var r = NormalizeRef(normalizedReference);
        return IsExcludedStatisticLogicalNode(r) ||
               Regex.IsMatch(r, @"(^|[./$])(?:har|harm|mean|min|max|avg|average|dmd|demand)\d*(?:mmxu|mmxn)", RegexOptions.IgnoreCase) ||
               r.Contains(".mean") || r.Contains("mean.") ||
               r.Contains(".min") || r.Contains("min.") ||
               r.Contains(".max") || r.Contains("max.") ||
               r.Contains(".avg") || r.Contains("avg.") ||
               r.Contains(".average") ||
               r.Contains(".dmd") || r.Contains("demand") ||
               r.Contains(".har") || r.Contains("harm") ||
               r.Contains(".thd") || r.Contains(".tdd") ||
               r.Contains(".hz") ||
               r.Contains(".w.") || r.Contains("totw") ||
               r.Contains(".var") || r.Contains("totvar") ||
               r.Contains(".va") || r.Contains("totva") ||
               r.Contains(".pf") ||
               r.Contains(".ang.") || r.EndsWith(".ang.f");
    }

    public static bool IsExcludedStatisticLogicalNode(string reference)
    {
        var text = (reference ?? string.Empty).Replace('$', '.').Replace('\\', '/');
        // Vendor IEDs often insert digits between the statistics prefix and MMXU, e.g. Har2MMXU.
        // These LNs are useful for power-quality/statistics pages, but are bad default HMI tags.
        return Regex.IsMatch(text, @"(^|[./])(?:HAR|HARM|MIN|MAX|MEAN|AVG|AVERAGE|DMD|DMMD)\d*(?:MMXU|MMXN)", RegexOptions.IgnoreCase);
    }

    private static string NormalizeRef(string reference)
    {
        return (reference ?? string.Empty)
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();
    }
}
