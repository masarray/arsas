using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Lazy read-only shape projection of canonical IEC declarations. A schema
/// defines readable children, but never proves that any MMS value has been read.
/// </summary>
public static class IedBrowserStructuredDetails
{
    /// <summary>One DO row; child DAs/BDAs materialize only when expanded.</summary>
    public static IedBrowserDetailRow DataObject(
        LiveIedDataObjectModel obj, string? uniqueProcessFc, int depth = 0)
    {
        // Directly bind the DO's displayed value to its single model-proven
        // process leaf. The leaf's key is already present in the observation
        // registry; there is no second value cache or additional MMS read.
        var hasLeaf = IedBrowserSchemaIndex.TryGetUniqueProcessLeaf(
            obj, out var processLeaf, out var processFc);
        var scalarComponents = hasLeaf
            ? Array.Empty<IedBrowserScalarComponent>()
            : IedBrowserMultiLeafPresentation.BuildPlan(
                obj.Attributes, obj.Reference, "ST");
        var allowVector = !IedBrowserEngineeringComposite.IsScalarCdc(obj.InferredCdc);
        var phasePlans = allowVector
            ? IedBrowserEngineeringComposite.BuildThreePhases(
                obj.Attributes, obj.Reference)
            : Array.Empty<IedBrowserPolarPlan>();
        var polarPlan = allowVector && phasePlans.Count == 0
            ? IedBrowserEngineeringComposite.BuildPolar(obj.Attributes, obj.Reference)
            : null;
        // A compound DO such as MMXU.PhV has no unique scalar leaf, but
        // can have a single schema-verified FC for independently observed
        // phase vectors. Never infer this FC from the object's label.
        var candidateFcs = obj.Attributes
            .Where(a => a.FunctionalConstraint is "MX" or "ST")
            .Select(a => (a.FunctionalConstraint ?? "").Trim().ToUpperInvariant())
            .Where(fc => fc.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Take(2).ToArray();
        var compoundFc = candidateFcs.Length == 1 ? candidateFcs[0] : "";
        return new IedBrowserDetailRow("Data", obj.Name, "Not read", depth,
            $"IEC DO declaration · {obj.Reference} · CDC {obj.InferredCdc}")
        {
            IsDataObject = true,
            ScalarComponents = scalarComponents,
            PolarPlan = polarPlan,
            PhasePlans = phasePlans,
            ValueReference = hasLeaf ? processLeaf : obj.Reference,
            FallbackValueReference = hasLeaf ? obj.Reference : "",
            IsEngineeringValue = hasLeaf,
            EngineeringUnitReference = hasLeaf ? processLeaf : "",
            FunctionalConstraint = hasLeaf ? processFc :
                (uniqueProcessFc is { Length: > 0 } ? uniqueProcessFc : compoundFc),
            VectorComponents = hasLeaf || !allowVector
                ? Array.Empty<IedBrowserVectorComponent>() : ObservedVectors(
                    obj.Attributes.Where(a =>
                        string.Equals(a.FunctionalConstraint, compoundFc,
                            StringComparison.OrdinalIgnoreCase))
                    .Select(a => (
                        Reference: IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                        Relative: TrimObjectName(a.AttributePath, obj.Name))),
                    compoundFc),
            LoadChildren = () => AttributeRows(
                obj.Attributes, obj.Name, depth + 1, allowVector,
                mergeFcRoots: true)
        };
    }

    public static IedBrowserDetailRow Member(LiveIedDataSetMemberModel member,
        LiveIedModelDiscoveryDocument? model, int depth = 0)
    {
        var name = member.Reference;
        var slash = name.IndexOf('/');
        if (slash >= 0 && slash + 1 < name.Length) name = name[(slash + 1)..];
        // Static FCD reports can expose only a scalar semantic descendant.
        // Resolve a unique IEC process leaf once from the exact model owner.
        var identity = IedBrowserLiveValues.CanonicalReference(member.Reference);
        var objectCandidates = model?.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .Where(obj => identity == IedBrowserLiveValues.CanonicalReference(obj.Reference) ||
                identity.StartsWith(IedBrowserLiveValues.CanonicalReference(obj.Reference) + ".",
                    StringComparison.Ordinal))
            .OrderByDescending(obj => obj.Reference.Length).ToArray()
            ?? Array.Empty<LiveIedDataObjectModel>();
        // Discovery may list the same DO once per FC. Reuse the exact
        // engineering grouping; conflicting names fail closed.
        var matchingOwners = objectCandidates.Length == 0
            ? Array.Empty<LiveIedDataObjectModel>()
            : IedBrowserEngineeringObjects.GroupExact(objectCandidates
                .Where(o => o.Reference == objectCandidates[0].Reference).ToArray());
        var exactObject = matchingOwners.Count == 1 ? matchingOwners[0] : null;
        // A DataSet FCDA can identify a structured subobject (A.phsA)
        // while the discovered DO owner is A. Resolve only descendants of
        // that exact member and matching FC.
        var leaf = exactObject is null ? null : ChooseEngineeringLeaf(
            exactObject.Attributes
                .Where(a => string.Equals(a.FunctionalConstraint, member.FunctionalConstraint,
                    StringComparison.OrdinalIgnoreCase))
                .Select(a => (Reference: IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                    Prefix: identity + "."))
                .Where(a => a.Reference.StartsWith(a.Prefix, StringComparison.Ordinal))
                .Select(a => (Reference: a.Reference, Relative: a.Reference[a.Prefix.Length..]))); 
        var foundLeaf = !string.IsNullOrEmpty(leaf);
        // Only a whole FC=ST FCD containing several exact process leaves
        // receives a labelled summary. A single FCDA or a scalar leaf keeps
        // its original direct-binding semantics.
        var scalarComponents = !foundLeaf && exactObject is not null &&
            string.Equals(identity, exactObject.Reference, StringComparison.Ordinal) &&
            string.Equals(member.FunctionalConstraint, "ST", StringComparison.OrdinalIgnoreCase)
                ? IedBrowserMultiLeafPresentation.BuildPlan(
                    exactObject.Attributes, identity, "ST")
                : Array.Empty<IedBrowserScalarComponent>();
        var matchingMembers = exactObject?.Attributes.Where(a =>
            string.Equals(a.FunctionalConstraint, member.FunctionalConstraint,
                StringComparison.OrdinalIgnoreCase)).ToArray()
            ?? Array.Empty<LiveIedDataAttributeModel>();
        var memberAllowsVector = exactObject is null ||
            !IedBrowserEngineeringComposite.IsScalarCdc(exactObject.InferredCdc);
        var memberPhasePlans = memberAllowsVector
            ? IedBrowserEngineeringComposite.BuildThreePhases(
                matchingMembers, identity)
            : Array.Empty<IedBrowserPolarPlan>();
        var memberPolarPlan = memberAllowsVector && memberPhasePlans.Count == 0
            ? IedBrowserEngineeringComposite.BuildPolar(matchingMembers, identity)
            : null;
        return new IedBrowserDetailRow("Data", $"{name} [{member.FunctionalConstraint}]", "Not read",
            depth, "DataSet directory; observed live values or explicit MMS read")
        {
            ScalarComponents = scalarComponents,
            PolarPlan = memberPolarPlan,
            PhasePlans = memberPhasePlans,
            ValueReference = foundLeaf ? leaf! : member.Reference,
            FallbackValueReference = foundLeaf ? member.Reference : "",
            EngineeringUnitReference = foundLeaf ? leaf! : "",
            IsEngineeringValue = foundLeaf,
            FunctionalConstraint = member.FunctionalConstraint,
            VectorComponents = exactObject is null || !memberAllowsVector
                ? Array.Empty<IedBrowserVectorComponent>() : ObservedVectors(exactObject.Attributes
                    .Where(a => string.Equals(a.FunctionalConstraint, member.FunctionalConstraint,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(a => (
                        Reference: IedBrowserLiveValues.CanonicalReference(a.MmsReference),
                        Relative: IedBrowserLiveValues.CanonicalReference(a.MmsReference)
                            .StartsWith(identity + ".", StringComparison.Ordinal)
                            ? IedBrowserLiveValues.CanonicalReference(a.MmsReference)[(identity.Length + 1)..]
                            : "")),
                    member.FunctionalConstraint),
            LoadChildren = () => DataSetMemberChildren(member, model, depth + 1)
        };
    }

    public static IReadOnlyList<IedBrowserDetailRow> DataSetMemberChildren(
        LiveIedDataSetMemberModel member, LiveIedModelDiscoveryDocument? model, int depth)
    {
        if (model is null) return [new("Message", "No model type hierarchy is available.", Depth: depth)];

        // MMS $FC$ and canonical IEC dotted references must resolve to the
        // SAME case-sensitive object identity. A string prefix alone is never
        // enough: Pos.stVal must not expose Pos.stValue/vendor siblings.
        var memberReference = IedBrowserLiveValues.CanonicalReference(member.Reference);
        var objectModels = model.LogicalDevices.SelectMany(ld => ld.LogicalNodes)
            .SelectMany(ln => ln.DataObjects)
            .Where(obj => string.Equals(obj.Reference, memberReference, StringComparison.Ordinal) ||
                memberReference.StartsWith(obj.Reference + ".", StringComparison.Ordinal))
            .OrderByDescending(obj => obj.Reference.Length)
            .ToArray();
        if (objectModels.Length == 0)
            return [new("Message", "No exact attribute schema for this DataSet member.", Depth: depth)];

        var exactOwners = IedBrowserEngineeringObjects.GroupExact(objectModels
            .Where(o => o.Reference == objectModels[0].Reference).ToArray());
        if (exactOwners.Count != 1)
            return [new("Message",
                "Ambiguous model ownership for this exact DataSet member.", Depth: depth)];
        var parent = exactOwners[0];
        var memberCoversObject = string.Equals(
            memberReference, parent.Reference, StringComparison.Ordinal);
        var exactDescendantPrefix = memberReference + ".";
        var candidateAttributes = parent.Attributes.Where(attr =>
            string.Equals(attr.FunctionalConstraint, member.FunctionalConstraint,
                StringComparison.OrdinalIgnoreCase) &&
            (memberCoversObject ||
             string.Equals(IedBrowserLiveValues.CanonicalReference(attr.MmsReference),
                 memberReference, StringComparison.Ordinal) ||
             IedBrowserLiveValues.CanonicalReference(attr.MmsReference)
                 .StartsWith(exactDescendantPrefix, StringComparison.Ordinal))).ToArray();

        return candidateAttributes.Length == 0
            ? [new("Message", "No exact attributes for the member functional constraint.", Depth: depth)]
            : AttributeRows(candidateAttributes, parent.Name, depth,
                !IedBrowserEngineeringComposite.IsScalarCdc(parent.InferredCdc));
    }

    public static IReadOnlyList<IedBrowserDetailRow> AttributeRows(
        IReadOnlyList<LiveIedDataAttributeModel> attributes, string objectName,
        int depth = 0, bool allowVector = true,
        bool mergeFcRoots = false)
    {
        var descriptors = attributes
            .Select(attr => (attr, parts: TrimObjectName(attr.AttributePath, objectName)
                .Split('.', StringSplitOptions.RemoveEmptyEntries)))
            .Where(x => x.parts.Length > 0)
            .ToArray();
        return mergeFcRoots
            ? BuildCrossFcRoots(descriptors, depth, allowVector)
            : BuildAt(descriptors, 0, depth, allowVector);
    }


    /// <summary>
    /// One structured phase/SDO row in a DO page even when attributes are
    /// distributed across MX/CF/DC. FC namespaces remain isolated in its lazy
    /// children, never collapsed into a synthetic observed value.
    /// </summary>
    private static IReadOnlyList<IedBrowserDetailRow> BuildCrossFcRoots(
        (LiveIedDataAttributeModel attr, string[] parts)[] attrs,
        int depth, bool allowVector)
    {
        var output = new List<IedBrowserDetailRow>();
        foreach (var group in attrs.GroupBy(x => x.parts[0], StringComparer.Ordinal))
        {
            var declarations = group.ToArray();
            var fcs = declarations.Select(x => x.attr.FunctionalConstraint)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (fcs.Length == 1)
            {
                output.AddRange(BuildAt(declarations, 0, depth, allowVector));
                continue;
            }
            // Multiple FC declarations of the *same* structured name: one
            // display node, not phsA [MX], phsA [CF], phsA [DC] as siblings.
            // Value selection is limited to one proven process FC.
            var process = declarations.Where(x =>
                x.attr.FunctionalConstraint is "MX" or "ST").ToArray();
            var processFcs = process.Select(x => x.attr.FunctionalConstraint)
                .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            var fc = processFcs.Length == 1 ? processFcs[0] : "";
            var qualified = IedBrowserLiveValues.CanonicalReference(
                declarations[0].attr.ObjectReference) + "." + group.Key;
            var processOnly = process.Where(x =>
                string.Equals(x.attr.FunctionalConstraint, fc,
                    StringComparison.OrdinalIgnoreCase)).ToArray();
            var polar = allowVector && fc == "MX"
                ? IedBrowserEngineeringComposite.BuildPolar(
                    processOnly.Select(x => x.attr), qualified)
                : null;
            var phases = allowVector && fc == "MX" && polar is null
                ? IedBrowserEngineeringComposite.BuildThreePhases(
                    processOnly.Select(x => x.attr), qualified)
                : Array.Empty<IedBrowserPolarPlan>();
            var scalar = fc.Length == 0 ? null : ChooseEngineeringLeaf(
                processOnly.Where(x => x.parts.Length > 1).Select(x => (
                    Reference: IedBrowserLiveValues.CanonicalReference(x.attr.MmsReference),
                    Relative: string.Join(".", x.parts.Skip(1)))));
            var chosen = polar?.MagnitudeReference ?? scalar ?? qualified;
            var descendants = declarations.Where(x => x.parts.Length > 1).ToArray();
            output.Add(new IedBrowserDetailRow("Data", group.Key, "Not read", depth,
                "IEC structured object · exact FC namespaces: " +
                string.Join(", ", fcs))
            {
                IsDataObject = true,
                ValueReference = chosen,
                FunctionalConstraint = fc,
                EngineeringUnitReference = scalar ?? polar?.MagnitudeReference ?? "",
                IsEngineeringValue = scalar is not null || polar is not null,
                PolarPlan = polar,
                PhasePlans = phases,
                LoadChildren = descendants.Length == 0 ? null
                    : () => BuildAt(descendants, 1, depth + 1, allowVector)
            });
        }
        return output;
    }

    private static IReadOnlyList<IedBrowserDetailRow> BuildAt(
        (LiveIedDataAttributeModel attr, string[] parts)[] attrs, int pathIndex,
        int depth, bool allowVector)
    {
        var result = new List<IedBrowserDetailRow>();
        foreach (var group in attrs.Where(x => x.parts.Length > pathIndex)
                     .GroupBy(x => (Name: x.parts[pathIndex],
                         Fc: (x.attr.FunctionalConstraint ?? string.Empty).Trim().ToUpperInvariant())))
        {
            // IEC attribute paths may exist under different FCs (e.g. ST/CO).
            // Never merge them into one expandable UI branch: that would
            // cross-bind children and make the parent FC unknowable.
            var name = group.Key.Name;
            var groupArray = group.ToArray();
            var matching = groupArray.FirstOrDefault(x => x.parts.Length == pathIndex + 1);
            var hasDirect = matching.attr is not null;
            var attr = hasDirect ? matching.attr : groupArray[0].attr;
            var descendants = groupArray.Where(x => x.parts.Length > pathIndex + 1).ToArray();
            var fullPrefix = IedBrowserLiveValues.CanonicalReference(attr.ObjectReference) +
                "." + string.Join(".", groupArray[0].parts.Take(pathIndex + 1));
            var polar = allowVector
                ? IedBrowserEngineeringComposite.BuildPolar(
                    groupArray.Select(x => x.attr), fullPrefix)
                : null;
            var phases = allowVector && polar is null
                ? IedBrowserEngineeringComposite.BuildThreePhases(
                    groupArray.Select(x => x.attr), fullPrefix)
                : Array.Empty<IedBrowserPolarPlan>();
            // q/t and alternate instantaneous leaves are not blockers for a
            // model-proven normal engineering value. No first child wins.
            var scalarLeaf = name is "q" or "t" ? null :
                ChooseEngineeringLeaf(descendants.Select(x => (
                    Reference: IedBrowserLiveValues.CanonicalReference(x.attr.MmsReference),
                    Relative: string.Join(".", x.parts.Skip(pathIndex + 1)))));
            Func<IReadOnlyList<IedBrowserDetailRow>>? children = null;
            if (descendants.Length > 0)
                children = () => BuildAt(
                    descendants, pathIndex + 1, depth + 1, allowVector);
            else if (name is "q" or "t" && hasDirect)
                children = () => name == "q"
                    ? QualityChildren(depth + 1, attr.MmsReference, attr.FunctionalConstraint)
                    : TimeChildren(depth + 1, attr.MmsReference, attr.FunctionalConstraint);

            result.Add(new IedBrowserDetailRow("Data",
                $"{name} [{attr.FunctionalConstraint}]", "Not read", depth,
                $"IEC declaration · {attr.MmsReference} · {attr.TypeSource}")
            {
                // A structured intermediate row may have its own observed
                // report value even when no scalar attribute is declared at
                // that exact depth. Keep the verified parent reference/FC.
                ValueReference = hasDirect
                    ? attr.MmsReference
                    : scalarLeaf ?? (attr.ObjectReference + "." +
                        string.Join(".", groupArray[0].parts.Take(pathIndex + 1))),
                FallbackValueReference = hasDirect ? scalarLeaf ?? "" : "",
                EngineeringUnitReference = scalarLeaf ?? "",
                IsEngineeringValue = scalarLeaf is not null && descendants.Length > 0,
                PolarPlan = polar,
                PhasePlans = phases,
                VectorComponents = !allowVector || name is "q" or "t" ?
                    Array.Empty<IedBrowserVectorComponent>() : ObservedVectors(descendants.Select(x => (
                        Reference: IedBrowserLiveValues.CanonicalReference(x.attr.MmsReference),
                        Relative: string.Join(".", x.parts.Skip(pathIndex + 1)))),
                        attr.FunctionalConstraint),
                EvidenceKind = hasDirect && name == "q" ? "Quality" :
                    hasDirect && name == "t" ? "Timestamp" : "",
                FunctionalConstraint = groupArray.All(x =>
                    string.Equals(x.attr.FunctionalConstraint, attr.FunctionalConstraint,
                        StringComparison.OrdinalIgnoreCase))
                        ? attr.FunctionalConstraint : "",
                LoadChildren = children
            });
        }
        return result;
    }


    /// <summary>
    /// Vector eligibility is schema-only: a matching normal cVal/mag+ang pair
    /// with the SAME phase, FC and exact path. Never infer from a DO label,
    /// use instCVal as a substitute, or bind a neighboring phase.
    /// Multiple phases are allowed as separate independently observed
    /// components; the runtime presentation must not invent missing phases.
    /// </summary>
    private static IReadOnlyList<IedBrowserVectorComponent> ObservedVectors(
        IEnumerable<(string Reference, string Relative)> attributes, string fc)
    {
        if (string.IsNullOrWhiteSpace(fc)) return [];
        var parts = new Dictionary<string, (string Label, string Mag, string Ang)>(
            StringComparer.Ordinal);
        foreach (var (reference, relative) in attributes)
        {
            var tokens = relative.Split('.', StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length < 2 || tokens[^1] != "f") continue;
            var axis = tokens[^2];
            if (axis is not ("mag" or "ang")) continue;
            var isCVal = tokens.Length >= 3 && tokens[^3] == "cVal";
            var prefix = isCVal ? tokens[..^3] : tokens[..^2];
            // Do not treat instant/derived/vendor vectors as the steady cVal.
            if (prefix.Any(p => p is "instCVal" or "instMag")) continue;
            // Normal IEC vector syntax: mag.f / ang.f or cVal.mag.f /
            // cVal.ang.f, with at most one phase name before cVal.
            if (prefix.Length > 1) continue;
            var label = prefix.Length == 1 ? prefix[0] : "";
            // Direct mag/ang and cVal.mag/cVal.ang belong to different
            // structure owners. Never cross-pair them even when both are
            // descendants of the same presentation row.
            var key = label + "|" + (isCVal ? "cVal" : "direct");
            if (!parts.TryGetValue(key, out var pair)) pair = (label, "", "");
            if (axis == "mag")
                pair = (label, pair.Mag.Length == 0 ? reference : "", pair.Ang);
            else
                pair = (label, pair.Mag, pair.Ang.Length == 0 ? reference : "");
            parts[key] = pair;
        }
        var complete = parts.Values.Where(p => p.Mag.Length > 0 && p.Ang.Length > 0)
            .GroupBy(p => p.Label, StringComparer.Ordinal)
            .Where(group => group.Count() == 1)
            .Select(group => group.First())
            // Phase display order follows IEC measurement convention; other
            // exact schema children remain deterministically ordered.
            .OrderBy(p => p.Label switch
            {
                "phsA" => 0, "phsB" => 1, "phsC" => 2,
                "neut" => 3, "net" => 4, "res" => 5, _ => 6
            })
            .ThenBy(p => p.Label, StringComparer.Ordinal)
            .ToArray();
        return complete
            .Select(p => new IedBrowserVectorComponent(
                p.Label, p.Mag, p.Ang, fc.Trim().ToUpperInvariant()))
            .ToArray();
    }

    // Returns a scalar only when the entire candidate set proves a unique
    // IEC engineering magnitude. q/t and non-value configuration fields are
    // deliberately excluded. Magnitude + instantaneous magnitude is an
    // explicitly recognized IEC pair; other multi-leaf groups fail closed.
    private static string? ChooseEngineeringLeaf(
        IEnumerable<(string Reference, string Relative)> rows)
    {
        var candidates = rows.Where(x => !string.IsNullOrWhiteSpace(x.Reference) &&
                x.Relative is "f" or "stVal" or "posVal" or "general" or
                    "valWTr" or "mag.f" or "instMag.f" or "cVal.mag.f" or
                    "instCVal.mag.f" or "ang.f" or "cVal.ang.f")
            .DistinctBy(x => x.Reference, StringComparer.Ordinal)
            .Take(3).ToArray();
        if (candidates.Length == 1)
            return candidates[0].Reference;
        if (candidates.Length != 2) return null;
        foreach (var (normal, instant) in new[] {
            ("mag.f", "instMag.f"), ("cVal.mag.f", "instCVal.mag.f") })
        {
            if (candidates.Any(x => x.Relative == normal) &&
                candidates.Any(x => x.Relative == instant))
                return candidates.First(x => x.Relative == normal).Reference;
        }
        return null;
    }

    private static string TrimObjectName(string path, string objectName)
    {
        var prefix = objectName + ".";
        return path.StartsWith(prefix, StringComparison.Ordinal) ? path[prefix.Length..] : path;
    }

    private static IReadOnlyList<IedBrowserDetailRow> QualityChildren(
        int depth, string qualityReference, string fc)
    {
        IedBrowserDetailRow QualityField(string name, string evidence)
            => new("Data", name, "Not read", depth)
            {
                ValueReference = qualityReference,
                FunctionalConstraint = fc,
                EvidenceField = evidence,
                EvidenceKind = "Quality"
            };
        var details = new[] { "Overflow", "OutOfRange", "BadReference",
            "Oscillatory", "Failure", "OldData", "Inconsistent", "Inaccurate" };
        return
        [
            QualityField("Validity", "Validity"),
            new("Data", "Quality Details", "", depth)
            {
                LoadChildren = () => details.Select(name =>
                    new IedBrowserDetailRow("Data", name, "Not read", depth + 1)
                    {
                        ValueReference = qualityReference,
                        FunctionalConstraint = fc,
                        EvidenceField = name,
                        EvidenceKind = "Quality"
                    }).ToArray()
            },
            QualityField("Source", "Source"),
            QualityField("Test", "Test"),
            QualityField("OperatorBlocked", "OperatorBlocked")
        ];
    }

    private static IReadOnlyList<IedBrowserDetailRow> TimeChildren(
        int depth, string timestampReference, string fc)
        => new[] { "LeapSecondsKnown", "ClockFailure", "ClockNotSynchronized",
                   "TimeAccuracy" }
            .Select(name => new IedBrowserDetailRow("Data", name, "Not read", depth)
            {
                ValueReference = timestampReference,
                FunctionalConstraint = fc,
                EvidenceField = name,
                EvidenceKind = "Timestamp"
            }).ToArray();
}
