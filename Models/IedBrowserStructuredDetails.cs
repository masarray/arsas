using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Lazy read-only shape projection of canonical IEC declarations. A schema
/// defines readable children, but never proves that any MMS value has been read.
/// </summary>
public static class IedBrowserStructuredDetails
{
    public static IedBrowserDetailRow Member(LiveIedDataSetMemberModel member,
        LiveIedModelDiscoveryDocument? model, int depth = 0)
    {
        var name = member.Reference;
        var slash = name.IndexOf('/');
        if (slash >= 0 && slash + 1 < name.Length) name = name[(slash + 1)..];
        return new IedBrowserDetailRow("Data", $"{name} [{member.FunctionalConstraint}]", "Not read",
            depth, "DataSet directory; observed live values or explicit MMS read")
        {
            ValueReference = member.Reference,
            FunctionalConstraint = member.FunctionalConstraint,
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

        var parent = objectModels[0];
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
            : AttributeRows(candidateAttributes, parent.Name, depth);
    }

    public static IReadOnlyList<IedBrowserDetailRow> AttributeRows(
        IReadOnlyList<LiveIedDataAttributeModel> attributes, string objectName, int depth = 0)
    {
        var descriptors = attributes
            .Select(attr => (attr, parts: TrimObjectName(attr.AttributePath, objectName)
                .Split('.', StringSplitOptions.RemoveEmptyEntries)))
            .Where(x => x.parts.Length > 0)
            .ToArray();
        return BuildAt(descriptors, 0, depth);
    }

    private static IReadOnlyList<IedBrowserDetailRow> BuildAt(
        (LiveIedDataAttributeModel attr, string[] parts)[] attrs, int pathIndex, int depth)
    {
        var result = new List<IedBrowserDetailRow>();
        foreach (var group in attrs.Where(x => x.parts.Length > pathIndex)
                     .GroupBy(x => x.parts[pathIndex], StringComparer.Ordinal))
        {
            var name = group.Key;
            var groupArray = group.ToArray();
            var matching = groupArray.FirstOrDefault(x => x.parts.Length == pathIndex + 1);
            var hasDirect = matching.attr is not null;
            var attr = hasDirect ? matching.attr : groupArray[0].attr;
            var descendants = groupArray.Where(x => x.parts.Length > pathIndex + 1).ToArray();
            Func<IReadOnlyList<IedBrowserDetailRow>>? children = null;
            if (descendants.Length > 0)
                children = () => BuildAt(descendants, pathIndex + 1, depth + 1);
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
                    : attr.ObjectReference + "." +
                        string.Join(".", groupArray[0].parts.Take(pathIndex + 1)),
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
