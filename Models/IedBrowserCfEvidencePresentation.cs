using System.Globalization;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only engineering evidence bridge. The actual Report/Read ledger remains
/// authoritative. Initial FC-root instance values may only be presented as an
/// explicitly labelled discovery snapshot from the EXACT active model object.
/// Neither channel is mistaken for a current report or republished to SSOT.
/// </summary>
public sealed class IedBrowserCfEvidencePresentation
{
    private readonly IedBrowserLiveValues _live;
    private readonly Dictionary<string, string?> _snapshot = new(StringComparer.Ordinal);
    public bool HasSnapshot { get; }

    public IedBrowserCfEvidencePresentation(
        IedBrowserLiveValues live, LiveIedModelDiscoveryDocument? currentModel,
        LiveIedCanonicalModel? canonical)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        if (canonical is null || currentModel is null ||
            !ReferenceEquals(currentModel, canonical.Discovery))
            return;
        HasSnapshot = true;
        foreach (var entry in canonical.InstanceValues)
        {
            if (entry.FunctionalConstraint is not ("CF" or "DC") ||
                entry.Value is null ||
                !TryScalar(entry.Value, out var display))
                continue;
            var reference = IedBrowserLiveValues.CanonicalReference(entry.Reference);
            if (reference.Length == 0) continue;
            var key = Key(reference, entry.FunctionalConstraint);
            if (!_snapshot.TryAdd(key, display))
                _snapshot[key] = null; // conflicting evidence never first-wins
        }
    }

    public bool TryGet(string reference, string fc, out string value,
        out string provenance, out bool isLive)
    {
        value = "";
        provenance = "";
        isLive = false;
        if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(fc))
            return false;
        if (_live.TryGet(reference, fc, out var observed))
        {
            value = observed.Value;
            provenance = observed.Source + " · " + observed.ObservedUtc.ToString(
                "yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) + " UTC";
            isLive = !observed.IsStale;
            return true;
        }
        if (_snapshot.TryGetValue(Key(reference, fc), out var snapshot) &&
            snapshot is { Length: > 0 })
        {
            value = snapshot;
            provenance = "Discovery snapshot (historical instance evidence; not a fresh MMS Read)";
            return true;
        }
        return false;
    }

    /// <summary>Exact standard enum registry, never infer units from DO names.</summary>
    public static string RenderCfValue(string reference, string fc, string raw)
    {
        if (!string.Equals(fc, "CF", StringComparison.OrdinalIgnoreCase))
            return raw;
        var canonical = IedBrowserLiveValues.CanonicalReference(reference);
        var leaf = canonical[(canonical.LastIndexOf('.') + 1)..];
        if (leaf is not ("SIUnit" or "multiplier")) return raw;
        if (!Iec61850StandardEnumRegistry.TryResolve(
                "", "", "MV", leaf, out var definition))
            return raw;
        var input = raw.Trim();
        // An ordinal is only converted when the engine's authoritative
        // enumeration defines it. Unknown/vendor enum values remain numeric.
        if (!int.TryParse(input, NumberStyles.Integer, CultureInfo.InvariantCulture,
                out var ordinal))
            return raw;
        var match = definition.Values.FirstOrDefault(v => v.Ord == ordinal);
        return match is null ? raw : match.Symbol;
    }

    /// <summary>
    /// Extend authoritative signal units using only *observed* CF units.
    /// The model must declare both exact CF leaves next to the measurement
    /// owner. Missing multiplier is not silently interpreted as base unit.
    /// FC/case/phase isolation is strict and conflicting metadata fails closed.
    /// </summary>
    public IReadOnlyDictionary<string, string?> BuildVerifiedUnits(
        IEnumerable<SignalDefinition> signals, LiveIedModelDiscoveryDocument? model)
    {
        var index = new Dictionary<string, string?>(
            IedBrowserEngineeringValuePresentation.BuildUnitIndex(signals),
            StringComparer.Ordinal);
        if (model is null) return index;
        foreach (var obj in model.LogicalDevices.SelectMany(d => d.LogicalNodes)
                     .SelectMany(n => n.DataObjects))
        {
            var attrs = obj.Attributes
                .Select(a => (Reference: IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                              Fc: a.FunctionalConstraint))
                .Where(a => a.Reference.Length > 0).ToArray();
            var declared = attrs.Select(a => Key(a.Reference, a.Fc))
                .ToHashSet(StringComparer.Ordinal);
            foreach (var attr in attrs.Where(a =>
                string.Equals(a.Fc, "MX", StringComparison.OrdinalIgnoreCase) &&
                (a.Reference.EndsWith(".mag.f", StringComparison.Ordinal) ||
                 a.Reference.EndsWith(".cVal.mag.f", StringComparison.Ordinal))))
            {
                var suffix = attr.Reference.EndsWith(".cVal.mag.f", StringComparison.Ordinal)
                    ? ".cVal.mag.f" : ".mag.f";
                var owner = attr.Reference[..^suffix.Length];
                var si = owner + ".units.SIUnit";
                var multiplier = owner + ".units.multiplier";
                if (!declared.Contains(Key(si, "CF")) ||
                    !declared.Contains(Key(multiplier, "CF")) ||
                    !TryGet(si, "CF", out var siRaw, out _, out _) ||
                    !TryGet(multiplier, "CF", out var mulRaw, out _, out _))
                    continue;
                var siSymbol = RenderCfValue(si, "CF", siRaw);
                var mulSymbol = RenderCfValue(multiplier, "CF", mulRaw);
                if (!IsProvenSymbol(siRaw, siSymbol, "SIUnit") ||
                    !IsProvenSymbol(mulRaw, mulSymbol, "multiplier"))
                    continue;
                var composed = mulSymbol + siSymbol;
                var key = Key(owner, "MX");
                if (!index.TryGetValue(key, out var previous))
                    index.Add(key, composed);
                else if (!string.Equals(previous, composed, StringComparison.Ordinal))
                    index[key] = null;
            }
        }
        return index;
    }

    private static bool IsProvenSymbol(string raw, string symbol, string field)
    {
        if (!Iec61850StandardEnumRegistry.TryResolve("", "", "MV", field, out var definition))
            return false;
        return definition.Values.Any(v =>
            string.Equals(v.Symbol, symbol, StringComparison.Ordinal) &&
            (string.Equals(raw.Trim(), symbol, StringComparison.Ordinal) ||
             int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture,
                 out var ordinal) && ordinal == v.Ord));
    }

    private static bool TryScalar(MmsDataValue raw, out string display)
    {
        display = "";
        if (raw.Kind is not (MmsDataKind.Integer or MmsDataKind.Unsigned or
            MmsDataKind.FloatingPoint or MmsDataKind.VisibleString or
            MmsDataKind.MmsString or MmsDataKind.Boolean))
            return false;
        display = Convert.ToString(raw.Value, CultureInfo.InvariantCulture) ?? "";
        return display.Length > 0;
    }

    private static string Key(string reference, string fc)
        => IedBrowserLiveValues.CanonicalReference(reference) + "|" +
            (fc ?? "").Trim().ToUpperInvariant();
}
