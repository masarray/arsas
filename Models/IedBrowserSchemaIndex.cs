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
    private readonly Dictionary<string, string> _qualityLeaves =
        new(StringComparer.Ordinal);

    private static readonly HashSet<string> ProcessLeaves =
        new(["stVal", "posVal", "valWTr", "general", "mag.f", "instMag.f", "cVal.mag.f"],
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
                if (relative == "q")
                    _qualityLeaves[Key(owner, fc)] = reference;
                if (!ProcessLeaves.Contains(relative)) continue;
                if (!primaries.TryGetValue(fc, out var paths))
                {
                    paths = new HashSet<string>(StringComparer.Ordinal);
                    primaries.Add(fc, paths);
                }
                paths.Add(reference);
            }
            // If an FC has more than one process candidate, there is no
            // unambiguous scalar DO alias. Leave the DO unobserved instead.
            foreach (var (fc, paths) in primaries)
                if (paths.Count == 1)
                    _primaryLeaves[Key(owner, fc)] = paths.First();
        }
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
    /// Only a schema-declared SINGLE scalar process leaf can represent its DO.
    /// Never reverse-map a DO snapshot into an arbitrary DA or infer q/t.
    /// </summary>
    public bool TryGetPrimaryLeaf(string? objectReference, string? fc, out string leaf)
        => _primaryLeaves.TryGetValue(
            Key(IedBrowserLiveValues.CanonicalReference(objectReference), fc ?? ""), out leaf!);

    /// <summary>
    /// Bind a real Report quality payload only to the closest declared owning
    /// DO's q leaf with the identical FC. A similarly named sibling is excluded.
    /// </summary>
    public bool TryGetQualityCompanion(string? reportReference, string? fc, out string quality)
    {
        quality = "";
        if (string.IsNullOrWhiteSpace(reportReference) || string.IsNullOrWhiteSpace(fc))
            return false;
        foreach (var owner in Ancestors(IedBrowserLiveValues.CanonicalReference(reportReference)))
        {
            if (!_dataObjects.Contains(owner)) continue;
            return _qualityLeaves.TryGetValue(Key(owner, fc), out quality!);
        }
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
