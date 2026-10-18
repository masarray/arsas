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
    // Weak keys prevent retained IED schemas/snapshots after reload. These
    // indexes contain declarations and historical evidence only, never
    // mutable current Report/Read observations.
    private sealed record SchemaIndex(
        HashSet<string> DeclaredCf, string[] UnitOwners, string[] SettingOwners);
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        LiveIedModelDiscoveryDocument, SchemaIndex> SchemaCache = new();
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<
        LiveIedCanonicalModel, Dictionary<string, string?>> SnapshotCache = new();
    private readonly LiveIedModelDiscoveryDocument? _model;
    private readonly string[] _unitOwners;
    private readonly string[] _settingOwners;
    private readonly Dictionary<string, string?> _snapshot = new(StringComparer.Ordinal);
    private readonly HashSet<string> _declaredCf = new(StringComparer.Ordinal);
    public bool HasSnapshot { get; }

    public IedBrowserCfEvidencePresentation(
        IedBrowserLiveValues live, LiveIedModelDiscoveryDocument? currentModel,
        LiveIedCanonicalModel? canonical)
    {
        _live = live ?? throw new ArgumentNullException(nameof(live));
        _model = currentModel;
        if (currentModel is not null)
        {
            var schema = SchemaCache.GetValue(currentModel, BuildSchema);
            _declaredCf = schema.DeclaredCf;
            _unitOwners = schema.UnitOwners;
            _settingOwners = schema.SettingOwners;
        }
        else
        {
            _unitOwners = [];
            _settingOwners = [];
        }
        if (canonical is not null && currentModel is not null &&
            ReferenceEquals(currentModel, canonical.Discovery))
        {
            HasSnapshot = true;
            _snapshot = SnapshotCache.GetValue(canonical, BuildSnapshot);
        }
    }

    private static SchemaIndex BuildSchema(LiveIedModelDiscoveryDocument model)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var owners = new HashSet<string>(StringComparer.Ordinal);
        var settingOwners = new HashSet<string>(StringComparer.Ordinal);
        foreach (var a in model.LogicalDevices.SelectMany(d => d.LogicalNodes)
                     .SelectMany(n => n.DataObjects).SelectMany(o => o.Attributes))
        {
            var reference = IedBrowserLiveValues.CanonicalReference(a.MmsReference);
            if (reference.Length == 0) continue;
            if (string.Equals(a.FunctionalConstraint, "CF",
                    StringComparison.OrdinalIgnoreCase))
                declared.Add(Key(reference, "CF"));
            if (string.Equals(a.FunctionalConstraint, "SG",
                    StringComparison.OrdinalIgnoreCase))
            {
                // IEC ASG analogue setMag shares the same observed CF unit
                // owner. Do not treat enum setVal or a vendor string as a
                // measurement and do not infer units from DO names.
                var settingSuffix = reference.EndsWith(".setMag.f",
                    StringComparison.Ordinal) ? ".setMag.f" :
                    reference.EndsWith(".setMag", StringComparison.Ordinal)
                        ? ".setMag" : "";
                if (settingSuffix.Length > 0)
                    settingOwners.Add(reference[..^settingSuffix.Length]);
            }
            if (!string.Equals(a.FunctionalConstraint, "MX",
                    StringComparison.OrdinalIgnoreCase)) continue;
            var suffix = reference.EndsWith(".cVal.mag.f", StringComparison.Ordinal)
                ? ".cVal.mag.f" : reference.EndsWith(".mag.f", StringComparison.Ordinal)
                    ? ".mag.f" : "";
            if (suffix.Length != 0) owners.Add(reference[..^suffix.Length]);
        }
        return new SchemaIndex(declared, owners.OrderBy(s => s, StringComparer.Ordinal)
            .ToArray(), settingOwners.OrderBy(s => s, StringComparer.Ordinal).ToArray());
    }

    private static Dictionary<string, string?> BuildSnapshot(LiveIedCanonicalModel canonical)
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var entry in canonical.InstanceValues)
        {
            if (entry.FunctionalConstraint is not ("CF" or "DC") ||
                entry.Value is null || !TryScalar(entry.Value, out var display))
                continue;
            var reference = IedBrowserLiveValues.CanonicalReference(entry.Reference);
            if (reference.Length == 0) continue;
            var key = Key(reference, entry.FunctionalConstraint);
            if (!snapshot.TryAdd(key, display))
                snapshot[key] = null;
        }
        return snapshot;
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


    /// <summary>
    /// Compose a units [CF] parent only from two exact, model-declared,
    /// observed enum leaves. Report/Read and historical snapshots are never
    /// cross-paired; a partial configuration cannot invent a unit.
    /// </summary>
    public bool TryFormatUnitsParent(string reference, string fc,
        out string unit, out string provenance)
        => TryFormatUnitsParent(reference, fc, out unit, out provenance, out _);

    /// <summary>
    /// A complete configuration pair is not necessarily current. Keep
    /// historical and stale evidence visibly distinct from an MMS Read.
    /// </summary>
    public bool TryFormatUnitsParent(string reference, string fc,
        out string unit, out string provenance, out string authority)
    {
        unit = "";
        provenance = "";
        authority = "";
        var owner = IedBrowserLiveValues.CanonicalReference(reference);
        if (!string.Equals(fc, "CF", StringComparison.OrdinalIgnoreCase) ||
            !owner.EndsWith(".units", StringComparison.Ordinal) ||
            !TryVerifyUnitPair(owner, out unit, out provenance, out var fresh))
            return false;
        authority = provenance.Contains("Discovery snapshot", StringComparison.Ordinal)
            ? "Historical discovery CF"
            : fresh ? "Verified IEC CF configuration" : "Stale IEC CF configuration";
        return true;
    }

    private bool TryVerifyUnitPair(string owner, out string unit,
        out string provenance, out bool fresh)
    {
        unit = "";
        provenance = "";
        fresh = false;
        var si = owner + ".SIUnit";
        var multiplier = owner + ".multiplier";
        if (!_declaredCf.Contains(Key(si, "CF")) ||
            !_declaredCf.Contains(Key(multiplier, "CF")) ||
            !TryGet(si, "CF", out var siRaw, out var siSource, out var siLive) ||
            !TryGet(multiplier, "CF", out var mulRaw, out var mulSource, out var mulLive))
            return false;
        if (siLive != mulLive ||
            siSource.StartsWith("Discovery snapshot", StringComparison.Ordinal) !=
            mulSource.StartsWith("Discovery snapshot", StringComparison.Ordinal))
            return false;
        var siSymbol = RenderCfValue(si, "CF", siRaw);
        var multiplierSymbol = RenderCfValue(multiplier, "CF", mulRaw);
        if (!IsProvenSymbol(siRaw, siSymbol, "SIUnit") ||
            !IsProvenSymbol(mulRaw, multiplierSymbol, "multiplier"))
            return false;
        unit = multiplierSymbol + siSymbol;
        provenance = si + " [" + siSource + "]; " +
                     multiplier + " [" + mulSource + "]";
        fresh = siLive && mulLive;
        return true;
    }

    /// <summary>
    /// IEC numeric ST (e.g. a switching accumulation counter) is not an
    /// enumeration. Permit its engineering unit only when the exact model
    /// declares a scalar integer stVal and fresh SIUnit+multiplier under
    /// that SAME DO/FC=CF. No DO-name heuristics, ancestor/sibling borrowing,
    /// historical-only snapshot, or setting-group conversion.
    /// </summary>
    public bool TryFormatVerifiedNumericStatusUnit(
        string reference, string fc, out string unit, out string provenance)
    {
        unit = "";
        provenance = "";
        if (_model is null ||
            !string.Equals(fc, "ST", StringComparison.OrdinalIgnoreCase))
            return false;
        var path = IedBrowserLiveValues.CanonicalReference(reference);
        if (!path.EndsWith(".stVal", StringComparison.Ordinal)) return false;
        var owner = path[..^".stVal".Length];
        var matches = _model.LogicalDevices.SelectMany(d => d.LogicalNodes)
            .SelectMany(n => n.DataObjects)
            .Where(o => string.Equals(
                IedBrowserLiveValues.CanonicalReference(o.Reference),
                owner, StringComparison.Ordinal))
            .Take(2).ToArray();
        if (matches.Length != 1) return false;
        var attributes = matches[0].Attributes.Where(a =>
            string.Equals(IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                path, StringComparison.Ordinal) &&
            string.Equals(a.FunctionalConstraint, "ST", StringComparison.OrdinalIgnoreCase))
            .Take(2).ToArray();
        if (attributes.Length != 1) return false;
        var declaration = attributes[0];
        // Only IEC integer primitives: DPC bitstrings, enumerations, quality
        // bitstrings, and bools must never inherit a numeric counter unit.
        var btype = (declaration.SclBType ?? "").Trim().ToUpperInvariant();
        var mms = (declaration.MmsType ?? "").Trim().ToLowerInvariant();
        var isInteger = btype is "INT8" or "INT16" or "INT32" or "INT64" or
            "INT8U" or "INT16U" or "INT32U" or "INT64U" or "INT24U" ||
            mms is "integer" or "unsigned";
        if (!isInteger ||
            btype is "ENUM" or "DBPOS" or "DPC" ||
            mms is "bit-string" or "boolean" or "enumerated")
            return false;
        return TryVerifyUnitPair(owner + ".units", out unit,
            out provenance, out var fresh) && fresh;
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
    /// FC/case/phase isolation is strict. Fresh exact CF evidence outranks
    /// older signal metadata; conflicting historical snapshots fail closed.
    /// </summary>
    public IReadOnlyDictionary<string, string?> BuildVerifiedUnits(
        IEnumerable<SignalDefinition> signals, LiveIedModelDiscoveryDocument? model)
    {
        var index = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (reference, unit) in
                 IedBrowserEngineeringValuePresentation.BuildUnitIndex(signals))
            index.Add(reference, unit);
        if (model is null || !ReferenceEquals(model, _model))
            return index;
        // O(number of modeled measurement owners), not O(all IED attributes)
        // per visible Report refresh; the weak-keyed schema is built once.
        foreach (var owner in _unitOwners)
        {
            if (!TryVerifyUnitPair(owner + ".units", out var composed,
                    out _, out var fresh))
                continue;
            var key = Key(owner, "MX");
            if (!index.TryGetValue(key, out var previous))
                index.Add(key, composed);
            else if (!string.Equals(previous, composed, StringComparison.Ordinal))
                index[key] = fresh ? composed : null;

            // A live IEC CF pair is more authoritative than generic
            // SignalDefinition.Unit metadata on individual magnitude leaves.
            // Do not apply a phase unit to a sibling, angle, quality or LN.
            if (!fresh) continue;
            index[key] = composed;
            foreach (var leaf in new[]
            {
                ".cVal.mag.f", ".instCVal.mag.f", ".mag.f", ".instMag.f"
            })
            {
                var exact = owner + leaf + "|MX";
                // Only exact paths whose owners carry this verified CF pair.
                // Avoid adding aliases when the live schema has no such leaf.
                if (index.ContainsKey(exact))
                    index[exact] = composed;
            }
        }
        // IEC ASG/SG setting magnitude: CF units belong to its exact DO,
        // never to a neighboring setting or to ordinal enum setVal values.
        foreach (var owner in _settingOwners)
        {
            if (!TryVerifyUnitPair(owner + ".units", out var composed,
                    out _, out var fresh)) continue;
            var key = Key(owner, "SG");
            if (!index.TryGetValue(key, out var previous))
                index.Add(key, composed);
            else if (!string.Equals(previous, composed, StringComparison.Ordinal))
                index[key] = fresh ? composed : null;
            if (fresh) index[key] = composed;
            if (fresh)
                foreach (var leaf in new[] { ".setMag", ".setMag.f" })
                {
                    var exact = owner + leaf + "|SG";
                    if (index.ContainsKey(exact)) index[exact] = composed;
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
