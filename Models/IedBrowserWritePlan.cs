using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
        {
            if (!int.TryParse(raw.Trim(), NumberStyles.Integer, invariant, out var ctl) ||
                ctl is < 0 or > 4)
                throw new FormatException("ctlModel requires the exact enumerated integer 0–4.");
            return MmsDataValue.Integer(ctl);
        }
        return t switch
        {
            "BOOLEAN" or "BOOL" => raw.Trim().ToLowerInvariant() switch
            {
                "true" or "1" => MmsDataValue.Boolean(true),
                "false" or "0" => MmsDataValue.Boolean(false),
                _ => throw new FormatException("Boolean must be true/false or 1/0.")
            },
            "ENUM" or "ENUMERATED" or "INT8" or "INT16" or "INT32" or "INT64" or
                "INTEGER" => MmsDataValue.Integer(
                    long.Parse(raw.Trim(), NumberStyles.Integer, invariant)),
            "INT8U" or "INT16U" or "INT24U" or "INT32U" or "INT64U" or
                "UNSIGNED" => MmsDataValue.Unsigned(
                    ulong.Parse(raw.Trim(), NumberStyles.Integer, invariant)),
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
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RequestedValue))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}
