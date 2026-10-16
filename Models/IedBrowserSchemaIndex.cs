using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Immutable, source-model-specific IEC identity index. Built O(schema + DataSet
/// members) once, then each Report FC / owner / member lookup visits only the
/// explicit ancestor path. IEC object spelling is case-sensitive; FC is not.
/// This class contains declarations ONLY: it never supplies live evidence.
/// </summary>
public sealed class IedBrowserSchemaIndex
{
    private readonly Dictionary<string, Dictionary<string, HashSet<string>>> _members =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _primaryLeaves =
        new(StringComparer.Ordinal);
    private readonly HashSet<string> _dataObjects = new(StringComparer.Ordinal);
    // Schema-only DPC stVal/DO identities, computed once on model replacement.
    // Never classify arbitrary numeric enums or a name-only Pos as Dbpos.
    private readonly HashSet<string> _declaredDoublePointValues = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _qualityLeaves =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _timestampLeaves =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> ProcessLeaves =
        new(["stVal", "posVal", "valWTr", "general", "mag.f", "instMag.f", "cVal.mag.f", "instCVal.mag.f"],
            StringComparer.Ordinal);

    public LiveIedModelDiscoveryDocument SourceModel { get; }

    public IedBrowserSchemaIndex(LiveIedModelDiscoveryDocument model)
    {
        SourceModel = model ?? throw new ArgumentNullException(nameof(model));
        foreach (var set in model.DataSets)
        {
            var setKey = IedBrowserLiveValues.CanonicalReference(set.Reference);
            if (!_members.TryGetValue(setKey, out var contents))
            {
                contents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
                _members.Add(setKey, contents);
            }
            foreach (var member in set.Members)
            {
                var reference = IedBrowserLiveValues.CanonicalReference(member.Reference);
                var fc = NormalizeFc(member.FunctionalConstraint);
                if (reference.Length == 0 || fc.Length == 0) continue;
                if (!contents.TryGetValue(reference, out var constraints))
                {
                    constraints = new HashSet<string>(StringComparer.Ordinal);
                    contents.Add(reference, constraints);
                }
                constraints.Add(fc);
            }
        }

        foreach (var obj in model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
                     .SelectMany(ln => ln.DataObjects))
        {
            var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
            if (owner.Length == 0) continue;
            _dataObjects.Add(owner);
            var primaries = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var attr in obj.Attributes)
            {
                var reference = IedBrowserLiveValues.CanonicalReference(attr.MmsReference);
                var fc = NormalizeFc(attr.FunctionalConstraint);
                if (reference.Length == 0 || fc.Length == 0 ||
                    !reference.StartsWith(owner + ".", StringComparison.Ordinal))
                    continue;
                var relative = reference[(owner.Length + 1)..];
                // IEC DPC declarations and exact Dbpos BTypes are the only
                // authority for double-point display. Both the precise
                // process leaf and its own DO row may use this presentation.
                if (fc == "ST" && relative == "stVal" &&
                    (string.Equals(obj.InferredCdc, "DPC", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(attr.SclBType, "Dbpos", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(attr.SclBType, "DPC", StringComparison.OrdinalIgnoreCase)))
                {
                    _declaredDoublePointValues.Add(Key(reference, fc));
                    _declaredDoublePointValues.Add(Key(owner, fc));
                }
                // A structured DO such as PPV contains CDC subobjects
                // phsAB/phsBC/phsCA, each with its own q/t. Index their
                // exact declared parent path instead of dropping the nested
                // raw evidence or borrowing another phase's flags.
                if (relative == "q" || relative.EndsWith(".q", StringComparison.Ordinal))
                    _qualityLeaves[Key(reference[..^2], fc)] = reference;
                if (relative == "t" || relative.EndsWith(".t", StringComparison.Ordinal))
                    _timestampLeaves[Key(reference[..^2], fc)] = reference;
                if (!ProcessLeaves.Contains(relative)) continue;
                if (!primaries.TryGetValue(fc, out var paths))
                {
                    paths = new HashSet<string>(StringComparer.Ordinal);
                    primaries.Add(fc, paths);
                }
                paths.Add(reference);
            }
            // Only IEC-defined steady/instantaneous magnitude alternatives
            // permit deterministic scalar display priority. Unrelated process
            // leaves remain ambiguous and are never merged.
            foreach (var (fc, paths) in primaries)
                if (TrySelectDisplayLeaf(owner, paths, out var primary))
                    _primaryLeaves[Key(owner, fc)] = primary;
        }
    }

    /// <summary>
    /// Resolve the one model-proven display process DA. IEC magnitude and
    /// instantaneous magnitude are one explicitly sanctioned alternative pair:
    /// the steady `mag/cVal` is the preferred displayed value. Other pairs
    /// (such as stVal/posVal) remain ambiguous. No observed value is invented.
    /// </summary>
    public static bool TryGetUniqueProcessLeaf(
        LiveIedDataObjectModel obj, out string leaf, out string fc)
    {
        leaf = "";
        fc = "";
        var owner = IedBrowserLiveValues.CanonicalReference(obj.Reference);
        if (owner.Length == 0) return false;
        var candidates = obj.Attributes
            .Where(attr => NormalizeFc(attr.FunctionalConstraint) is "ST" or "MX")
            .Select(attr => (
                Reference: IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                Fc: NormalizeFc(attr.FunctionalConstraint)))
            .Where(attr => attr.Reference.StartsWith(owner + ".", StringComparison.Ordinal)
                && ProcessLeaves.Contains(attr.Reference[(owner.Length + 1)..]))
            .Distinct()
            .Take(3)
            .ToArray();
        if (candidates.Length == 0 || candidates.Select(x => x.Fc).Distinct().Count() != 1)
            return false;
        if (!TrySelectDisplayLeaf(owner, candidates.Select(x => x.Reference),
            out leaf))
            return false;
        fc = candidates[0].Fc;
        return true;
    }

    private static bool TrySelectDisplayLeaf(
        string owner, IEnumerable<string> candidates, out string leaf)
    {
        leaf = "";
        var paths = candidates.Distinct(StringComparer.Ordinal).Take(3).ToArray();
        if (paths.Length == 1)
        {
            leaf = paths[0];
            return true;
        }
        if (paths.Length != 2)
            return false;

        // IEC 61850 MX: magnitude (mag/cVal) and instantaneous magnitude
        // (instMag/instCVal) are alternatives of the SAME measurement DO.
        // Prefer the normal engineering measurement; retain the instantaneous
        // attribute independently in the drill-down. Never select an arbitrary
        // member of a multi-phase/vector/compound object.
        var normal = owner + ".mag.f";
        var instant = owner + ".instMag.f";
        if (paths.Contains(normal, StringComparer.Ordinal) &&
            paths.Contains(instant, StringComparer.Ordinal))
        {
            leaf = normal;
            return true;
        }
        normal = owner + ".cVal.mag.f";
        instant = owner + ".instCVal.mag.f";
        if (paths.Contains(normal, StringComparer.Ordinal) &&
            paths.Contains(instant, StringComparer.Ordinal))
        {
            leaf = normal;
            return true;
        }
        return false;
    }

    private static string NormalizeFc(string? fc)
        => (fc ?? "").Trim().ToUpperInvariant();

    private static string Key(string reference, string fc)
        => reference + "|" + NormalizeFc(fc);

    /// <summary>
    /// DataSet declarations authorize an observed member FC if and only if all
    /// covering ancestor membership entries agree on the same FC. Dataset
    /// declaration of a DO also covers its DA/BDA descendants with that FC.
    /// </summary>
    public bool TryResolveReportFc(string? dataSet, string? reportReference, out string fc)
    {
        fc = "";
        if (string.IsNullOrWhiteSpace(dataSet) || string.IsNullOrWhiteSpace(reportReference))
            return false;
        var setRef = IedBrowserLiveValues.CanonicalReference(dataSet);
        if (!_members.TryGetValue(setRef, out var members)) return false;
        var reference = IedBrowserLiveValues.CanonicalReference(reportReference);
        foreach (var ancestor in Ancestors(reference))
        {
            if (!members.TryGetValue(ancestor, out var constraints)) continue;
            foreach (var declaredFc in constraints)
            {
                if (fc.Length > 0 && fc != declaredFc)
                {
                    fc = "";
                    return false;
                }
                fc = declaredFc;
            }
        }
        return fc.Length != 0;
    }

    public bool ContainsDataSetMember(string? dataSet, string? reference, string? fc)
    {
        if (string.IsNullOrWhiteSpace(dataSet) || string.IsNullOrWhiteSpace(reference) ||
            string.IsNullOrWhiteSpace(fc) ||
            !_members.TryGetValue(IedBrowserLiveValues.CanonicalReference(dataSet), out var members))
            return false;
        var targetFc = NormalizeFc(fc);
        return Ancestors(IedBrowserLiveValues.CanonicalReference(reference)).Any(
            ancestor => members.TryGetValue(ancestor, out var constraints) &&
                        constraints.Contains(targetFc));
    }

    /// <summary>
    /// True only for the exact model-proven DPC process stVal (or its
    /// declaring DO) and FC=ST. A report's text is never typing authority.
    /// Use solely for operator presentation; the canonical ledger remains raw.
    /// </summary>
    public bool IsDeclaredDoublePointValue(string? reference, string? fc)
    {
        if (NormalizeFc(fc) != "ST") return false;
        var identity = IedBrowserLiveValues.CanonicalReference(reference);
        return identity.Length > 0 &&
            _declaredDoublePointValues.Contains(Key(identity, "ST"));
    }

    /// <summary>
    /// Only a schema-declared SINGLE scalar process leaf can represent its DO.
    /// Never reverse-map a DO snapshot into an arbitrary DA or infer q/t.
    /// </summary>
    public bool TryGetPrimaryLeaf(string? objectReference, string? fc, out string leaf)
        => _primaryLeaves.TryGetValue(
            Key(IedBrowserLiveValues.CanonicalReference(objectReference), fc ?? ""), out leaf!);

    /// <summary>
    /// Raw structured evidence may represent a DO only when the canonical
    /// reference itself is a declared DO; a nested DA cannot borrow q/t from
    /// its parent merely because it contains a similar MMS BIT STRING.
    /// </summary>
    public bool IsDeclaredDataObject(string? reference)
        => _dataObjects.Contains(IedBrowserLiveValues.CanonicalReference(reference));

    /// <summary>
    /// A structured Report carrier may be an exact nested CDC container,
    /// rather than a top-level DO. It may supply raw q/t ONLY when that
    /// same container and FC actually declare a matching companion leaf.
    /// </summary>
    public bool IsDeclaredCompanionContainer(string? reference, string? fc)
    {
        var owner = IedBrowserLiveValues.CanonicalReference(reference);
        if (owner.Length == 0 || string.IsNullOrWhiteSpace(fc)) return false;
        var key = Key(owner, fc);
        return _qualityLeaves.ContainsKey(key) || _timestampLeaves.ContainsKey(key);
    }

    /// <summary>
    /// Bind a real Report quality payload only to the closest declared owning
    /// DO's q leaf with the identical FC. A similarly named sibling is excluded.
    /// </summary>
    public bool TryGetQualityCompanion(string? reportReference, string? fc, out string quality)
    {
        quality = "";
        if (string.IsNullOrWhiteSpace(reportReference) || string.IsNullOrWhiteSpace(fc))
            return false;
        // Closest exact schema-declared CDC q wins. An FCD nested phase
        // may own its own q even when only its outer PPV is an IEC DO.
        var reference = IedBrowserLiveValues.CanonicalReference(reportReference);
        if (reference.EndsWith(".q", StringComparison.Ordinal) &&
            _qualityLeaves.TryGetValue(Key(reference[..^2], fc), out var own) &&
            string.Equals(own, reference, StringComparison.Ordinal))
        {
            quality = own;
            return true;
        }
        foreach (var owner in Ancestors(reference))
            if (_qualityLeaves.TryGetValue(Key(owner, fc), out quality!))
                return true;
        return false;
    }

    /// <summary>
    /// Time evidence is mapped only to a model-declared t under the same
    /// exact DO and FC as its raw reported FCD; never a nearby sibling.
    /// </summary>
    public bool TryGetTimestampCompanion(string? reportReference, string? fc, out string timestamp)
    {
        timestamp = "";
        if (string.IsNullOrWhiteSpace(reportReference) || string.IsNullOrWhiteSpace(fc))
            return false;
        var reference = IedBrowserLiveValues.CanonicalReference(reportReference);
        if (reference.EndsWith(".t", StringComparison.Ordinal) &&
            _timestampLeaves.TryGetValue(Key(reference[..^2], fc), out var own) &&
            string.Equals(own, reference, StringComparison.Ordinal))
        {
            timestamp = own;
            return true;
        }
        foreach (var owner in Ancestors(reference))
            if (_timestampLeaves.TryGetValue(Key(owner, fc), out timestamp!))
                return true;
        return false;
    }

    /// <summary>
    /// Derive q only for a reported value other than the q leaf itself. A
    /// directly reported q contains its own payload; companion normalization
    /// must never erase that stronger raw evidence.
    /// </summary>
    public bool TryGetDerivedQualityCompanion(string? reportReference, string? fc,
        out string quality)
    {
        if (TryGetQualityCompanion(reportReference, fc, out quality) &&
            !string.Equals(quality, IedBrowserLiveValues.CanonicalReference(reportReference),
                StringComparison.Ordinal))
            return true;
        quality = "";
        return false;
    }

    // Finite, allocation-bounded (by path depth) ancestor traversal: exact
    // reference first, then dot-delimited IEC parents, never substring prefix.
    private static IEnumerable<string> Ancestors(string reference)
    {
        for (var current = reference; current.Length > 0;)
        {
            yield return current;
            var dot = current.LastIndexOf('.');
            if (dot <= current.IndexOf('/')) yield break;
            current = current[..dot];
        }
    }
}
