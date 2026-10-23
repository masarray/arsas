using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// A finite, per-click MMS configuration Write plan. Only the exact LIVE MMS
/// attribute directory can authorize a target. A DataSet or SCL-only row never
/// becomes a Write target. No CO service, ST/MX/q/t or RCB fields are admitted.
/// This is a configuration editor, NOT an IEC control-command substitute.
/// </summary>
public static class IedBrowserWritePlan
{
    private static readonly HashSet<string> ConfigurationFc =
        new(["CF", "DC", "SP", "SG", "SE"], StringComparer.OrdinalIgnoreCase);

    public const int MaxTargets = 128;

    public static IReadOnlyList<IedBrowserWriteRow> Build(
        LiveIedModelDiscoveryDocument? liveModel,
        IedBrowserTreeRow? page, string? focusedDoReference = null)
    {
        if (liveModel is null || page is null || page.Kind is not ("LN" or "DO" or "DA") ||
            page.Kind == "LN" && string.IsNullOrWhiteSpace(focusedDoReference))
            return [];

        var candidates = IedBrowserReadPlan.Build(
            liveModel, page, false, focusedDoReference);
        if (candidates.Count > MaxTargets * 20)
            return []; // Refuse huge LN-wide selections; focus the exact DO.
        var admitted = candidates
            .Where(target => ConfigurationFc.Contains(target.FunctionalConstraint))
            // ctlModel has a dedicated native Control-model configuration service.
            // Never send it through the Browser primitive MMS Write path.
            .Where(target => !target.Reference.EndsWith(".ctlModel", StringComparison.OrdinalIgnoreCase))
            .Where(target => !target.Reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase) &&
                             !target.Reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase))
            .Where(target => IsSupported(target.DataType))
            .DistinctBy(target => target.Reference + "|" + target.FunctionalConstraint,
                StringComparer.OrdinalIgnoreCase)
            .Take(MaxTargets + 1)
            .ToArray();
        if (admitted.Length > MaxTargets)
            return []; // No silent partial bulk writes.
        return admitted.Select(target => new IedBrowserWriteRow(
            target.Reference, target.FunctionalConstraint, target.DataType)).ToArray();
    }

    public static bool IsSupported(string type)
    {
        var t = (type ?? "").Trim().ToUpperInvariant();
        return t is "BOOLEAN" or "BOOL" or "ENUM" or "ENUMERATED" or
            "INT8" or "INT16" or "INT32" or "INT64" or "INTEGER" or
            "INT8U" or "INT16U" or "INT24U" or "INT32U" or "INT64U" or
            "UNSIGNED" or "FLOAT32" or "FLOAT" or
            "VISSTRING32" or "VISSTRING64" or "VISSTRING65" or
            "VISSTRING129" or "VISSTRING255" or "VISIBLESTRING" or
            "STRING" or "STRING255" or "MMSSTRING";
    }

    public static MmsDataValue EncodeValue(string type, string raw, string reference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        raw ??= "";
        var t = (type ?? "").Trim().ToUpperInvariant();
        var invariant = CultureInfo.InvariantCulture;
        if (reference.EndsWith(".ctlModel", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException(
                "ctlModel requires the dedicated verified native ControlModel configuration service.");
        return t switch
        {
            "BOOLEAN" or "BOOL" => raw.Trim().ToLowerInvariant() switch
            {
                "true" or "1" => MmsDataValue.Boolean(true),
                "false" or "0" => MmsDataValue.Boolean(false),
                _ => throw new FormatException("Boolean must be true/false or 1/0.")
            },
            // MMS integer representation does not prove an SCL EnumType mapping.
            // Until symbol/ordinal evidence is available, never guess an enum.
            "ENUM" or "ENUMERATED" => throw new NotSupportedException(
                "Enumerated Write requires exact verified SCL EnumType metadata."),
            "INT8" or "INT16" or "INT32" or "INT64" or
                "INTEGER" => EncodeSignedInteger(raw, t, invariant),
            "INT8U" or "INT16U" or "INT24U" or "INT32U" or "INT64U" or
                "UNSIGNED" => EncodeUnsignedInteger(raw, t, invariant),
            // Engine MmsDataCodec currently emits IEEE-754 binary32 only.
            // Do NOT silently narrow a double/IEEE-754 binary64 MMS DA.
            "FLOAT32" or "FLOAT" => EncodeFloat(raw, invariant),
            "VISSTRING32" or "VISSTRING64" or "VISSTRING65" or
                "VISSTRING129" or "VISSTRING255" or "VISIBLESTRING" =>
                    EncodeString(raw, t, mms: false),
            "STRING" or "STRING255" or "MMSSTRING" =>
                EncodeString(raw, t, mms: true),
            _ => throw new NotSupportedException(
                "No verified safe primitive MMS encoder for this DA type: " + type)
        };
    }

    private static MmsDataValue EncodeSignedInteger(string raw, string declaredType,
        CultureInfo culture)
    {
        var value = long.Parse(raw.Trim(), NumberStyles.Integer, culture);
        var (min, max) = declaredType switch
        {
            "INT8" => ((long)sbyte.MinValue, sbyte.MaxValue),
            "INT16" => ((long)short.MinValue, short.MaxValue),
            "INT32" => ((long)int.MinValue, int.MaxValue),
            _ => (long.MinValue, long.MaxValue)
        };
        if (value < min || value > max)
            throw new OverflowException("Integer is outside the declared " + declaredType + " range.");
        return MmsDataValue.Integer(value);
    }

    private static MmsDataValue EncodeUnsignedInteger(string raw, string declaredType,
        CultureInfo culture)
    {
        var value = ulong.Parse(raw.Trim(), NumberStyles.Integer, culture);
        var max = declaredType switch
        {
            "INT8U" => (ulong)byte.MaxValue,
            "INT16U" => ushort.MaxValue,
            "INT24U" => 0xFFFFFFUL,
            "INT32U" => uint.MaxValue,
            _ => ulong.MaxValue
        };
        if (value > max)
            throw new OverflowException("Unsigned integer exceeds the declared " +
                                        declaredType + " range.");
        return MmsDataValue.Unsigned(value);
    }

    private static MmsDataValue EncodeFloat(string raw, CultureInfo culture)
    {
        var f = float.Parse(raw.Trim(), NumberStyles.Float, culture);
        if (!float.IsFinite(f))
            throw new FormatException("Float must be a finite numeric value.");
        return MmsDataValue.FloatingPoint(f);
    }

    private static MmsDataValue EncodeString(string raw, string type, bool mms)
    {
        var limit = type switch
        {
            "VISSTRING32" => 32,
            "VISSTRING64" => 64,
            "VISSTRING65" => 65,
            "VISSTRING129" => 129,
            _ => 255
        };
        if (raw.Length > limit)
            throw new FormatException("String exceeds the declared length " + limit + ".");
        if (raw.Any(c => c > 0x7F))
            throw new FormatException("This MMS string encoder supports ASCII only.");
        return mms ? MmsDataValue.MmsString(raw) : MmsDataValue.VisibleString(raw);
    }
}

public sealed class IedBrowserWriteRow : INotifyPropertyChanged
{
    private bool _selected;
    private string _requestedValue = "";
    private string _current = "Not read";
    private string _result = "Pending";
    private bool _hasVerifiedRead;
    public MmsDataValue? LiveBeforeValue { get; private set; }
    public bool HasEdited { get; private set; }
    public bool HasVerifiedRead
    {
        get => _hasVerifiedRead;
        private set
        {
            if (_hasVerifiedRead == value) return;
            _hasVerifiedRead = value;
            Notify(nameof(HasVerifiedRead));
            Notify(nameof(TextEditorVisibility));
            Notify(nameof(BooleanEditorVisibility));
            Notify(nameof(CtlModelEditorVisibility));
            Notify(nameof(PlaceholderVisibility));
            Notify(nameof(PlaceholderText));
        }
    }
    public string ShortName =>
        Reference.Split('.').LastOrDefault() ?? Reference;
    public bool IsBoolean => DataType.Equals("BOOLEAN", StringComparison.OrdinalIgnoreCase) ||
                             DataType.Equals("BOOL", StringComparison.OrdinalIgnoreCase);
    public bool IsUnverifiedEnum => !IsControlModel &&
        (DataType.Equals("ENUM", StringComparison.OrdinalIgnoreCase) ||
         DataType.Equals("ENUMERATED", StringComparison.OrdinalIgnoreCase));
    public string PlaceholderText => IsUnverifiedEnum
        ? "Enum mapping unverified — Write disabled"
        : "Read to edit";
    public IReadOnlyList<string> BooleanOptions { get; } = new[] { "false", "true" };
    public Visibility TextEditorVisibility =>
        HasVerifiedRead && !IsBoolean && !IsControlModel && !IsUnverifiedEnum
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BooleanEditorVisibility =>
        HasVerifiedRead && IsBoolean && !IsControlModel
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CtlModelEditorVisibility =>
        HasVerifiedRead && IsControlModel
            ? Visibility.Visible : Visibility.Collapsed;
    public Visibility PlaceholderVisibility =>
        HasVerifiedRead && !IsUnverifiedEnum ? Visibility.Collapsed : Visibility.Visible;

    public void ApplyLiveRead(MmsDataValue value)
    {
        LiveBeforeValue = value;
        Current = MmsDataCodec.ToDisplayString(value);
        _requestedValue = value.Value switch
        {
            bool b => b ? "true" : "false",
            string text => text,
            long or ulong or int or uint or float or double =>
                Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? "",
            _ => ""
        };
        HasEdited = false;
        Notify(nameof(RequestedValue));
        HasVerifiedRead = true;
    }

    public void ExpireLiveRead()
    {
        HasVerifiedRead = false;
        HasEdited = false;
        LiveBeforeValue = null;
    }

    private void Notify(string field) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(field));

    public IedBrowserWriteRow(string reference, string fc, string type)
    {
        Reference = reference;
        FunctionalConstraint = fc;
        DataType = type;
    }
    public string Reference { get; }
    public string FunctionalConstraint { get; }
    public string DataType { get; }
    public bool IsControlModel => Reference.EndsWith(".ctlModel", StringComparison.OrdinalIgnoreCase);
    public string Current
    {
        get => _current;
        set { if (_current == value) return; _current = value; PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(Current))); }
    }
    public string Result
    {
        get => _result;
        set { if (_result == value) return; _result = value; PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(Result))); }
    }
    public bool Selected
    {
        get => _selected;
        set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this,
            new PropertyChangedEventArgs(nameof(Selected))); }
    }
    public string RequestedValue
    {
        get => _requestedValue;
        set { if (_requestedValue == value) return; _requestedValue = value;
            if (HasVerifiedRead) HasEdited = true;
            Notify(nameof(RequestedValue)); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
