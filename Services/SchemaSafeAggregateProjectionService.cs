using System.Globalization;
using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Projects explicitly structured FAT/Engineering measurement parents only after the
/// authoritative IEC 61850 schema has named every value that is published. This service
/// deliberately never chooses a child by raw MMS position or by "first numeric" shape.
/// </summary>
public static class SchemaSafeAggregateProjectionService
{
    public sealed record Projection(
        object? Value,
        string DisplayValue,
        string Quality,
        string DeviceTimestamp,
        string ProjectionStatus);

    public sealed record ReadLeaf(
        string Reference,
        string Label,
        string DataType);

    public sealed record ReadPlan(
        string ParentReference,
        string Kind,
        IReadOnlyList<ReadLeaf> Leaves);

    private sealed record BoundLeaf(
        Iec61850BoundValueRow Row,
        IReadOnlyList<Iec61850BoundValueRow> Ancestors,
        double NumericValue);

    /// <summary>
    /// Builds the exact scalar-read plan used by the live FAT/Engineering runtime.
    /// The plan is derived only from named attributes in the authoritative SCL/live model.
    /// It never derives phase identity from MMS child position or numeric value ordering.
    /// </summary>
    public static bool TryBuildReadPlan(
        LiveIedModelDiscoveryDocument? authorityModel,
        string requestedReference,
        out ReadPlan plan,
        out string status)
    {
        plan = new ReadPlan(requestedReference ?? string.Empty, string.Empty, Array.Empty<ReadLeaf>());
        status = string.Empty;

        if (authorityModel is null)
        {
            status = "Schema-safe aggregate read plan blocked: no authoritative SCL/live IEC 61850 model is attached.";
            return false;
        }

        if (!TryFindDataObject(authorityModel, requestedReference, out var dataObject))
        {
            status = $"Schema-safe aggregate read plan blocked: DataObject schema was not found uniquely for {requestedReference}.";
            return false;
        }

        if (IsThreePhaseThd(requestedReference, out var phases))
        {
            var leaves = new List<ReadLeaf>(phases.Count);
            foreach (var phase in phases)
            {
                var prefix = NormalizeReference(requestedReference) + "." + phase.Path.ToLowerInvariant();
                var tiers = new[]
                {
                    new[] { prefix + ".cval.mag.f" },
                    new[] { prefix + ".mag.f" },
                    new[] { prefix + ".instcval.mag.f", prefix + ".instmag.f" }
                };

                if (!TryResolvePreferredAttributeReference(dataObject, tiers, out var reference, out var failure))
                {
                    status = $"Schema-safe THD read plan blocked for {requestedReference}: phase {phase.Label} has no unique named magnitude leaf. {failure}";
                    return false;
                }

                leaves.Add(new ReadLeaf(reference, phase.Label, "Float32"));
            }

            if (leaves.Select(leaf => NormalizeReference(leaf.Reference))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count() != phases.Count)
            {
                status = $"Schema-safe THD read plan blocked for {requestedReference}: phase leaves are not unique.";
                return false;
            }

            plan = new ReadPlan(requestedReference, "ThreePhaseThd", leaves);
            status = $"Schema-safe THD read plan resolved exact leaves: {string.Join(", ", leaves.Select(leaf => leaf.Reference))}.";
            return true;
        }

        if (IsDemandEnergy(requestedReference))
        {
            var prefix = NormalizeReference(requestedReference);
            var tiers = new[]
            {
                new[] { prefix + ".mag.f" },
                new[] { prefix + ".cval.mag.f" },
                new[] { prefix + ".instmag.f", prefix + ".instcval.mag.f" }
            };

            if (!TryResolvePreferredAttributeReference(dataObject, tiers, out var reference, out var failure))
            {
                status = $"Schema-safe DmdWh read plan blocked for {requestedReference}: no unique named energy magnitude leaf. {failure}";
                return false;
            }

            plan = new ReadPlan(
                requestedReference,
                "DemandEnergy",
                new[] { new ReadLeaf(reference, "Value", "Float32") });
            status = $"Schema-safe DmdWh read plan resolved exact leaf {reference}.";
            return true;
        }

        status = $"Schema-safe aggregate read planning does not own {requestedReference}.";
        return false;
    }

    public static bool TryProject(
        LiveIedModelDiscoveryDocument? authorityModel,
        ArMms.MmsDataValue? value,
        string requestedReference,
        string readReference,
        string dataType,
        out Projection projection,
        out string status)
    {
        projection = new Projection(null, "-", string.Empty, string.Empty, string.Empty);
        status = string.Empty;

        if (authorityModel is null)
        {
            status = "Schema-safe aggregate projection blocked: no authoritative SCL/live IEC 61850 model is attached.";
            return false;
        }

        if (value is null || value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
        {
            status = "Schema-safe aggregate projection requires a structured MMS value.";
            return false;
        }

        if (!TryFindDataObject(authorityModel, requestedReference, out var dataObject))
        {
            status = $"Schema-safe aggregate projection blocked: DataObject schema was not found for {requestedReference}.";
            return false;
        }

        var rootSchema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(dataObject).ToRootNode();
        var readSchema = TryFindSchemaNode(rootSchema, readReference, out var exactReadSchema)
            ? exactReadSchema
            : ReferencesEqual(readReference, dataObject.Reference)
                ? rootSchema
                : null;
        if (readSchema is null)
        {
            status = $"Schema-safe aggregate projection blocked: read reference {readReference} is outside schema {dataObject.Reference}.";
            return false;
        }

        var binding = Iec61850ValueBindingEngine.Bind(readSchema, value);
        if (binding.HasMismatch)
        {
            status = $"Schema-safe aggregate projection blocked by schema/value mismatch: {FormatDiagnostics(binding.Diagnostics)}";
            return false;
        }

        if (IsThreePhaseThd(requestedReference, out var phases))
            return TryProjectThreePhase(binding.Root, requestedReference, readReference, phases, out projection, out status);

        if (IsDemandEnergy(requestedReference))
            return TryProjectDemandEnergy(binding.Root, requestedReference, readReference, dataType, out projection, out status);

        status = $"Schema-safe aggregate projection does not own {requestedReference}.";
        return false;
    }

    private static bool TryProjectThreePhase(
        Iec61850BoundValueRow root,
        string requestedReference,
        string readReference,
        IReadOnlyList<(string Path, string Label)> phases,
        out Projection projection,
        out string status)
    {
        projection = new Projection(null, "-", string.Empty, string.Empty, string.Empty);
        var values = new List<string>(phases.Count);
        var leaves = new List<BoundLeaf>(phases.Count);

        foreach (var phase in phases)
        {
            var prefix = NormalizeReference(requestedReference) + "." + phase.Path.ToLowerInvariant();
            var candidateTiers = new[]
            {
                new[] { prefix + ".cval.mag.f" },
                new[] { prefix + ".mag.f" },
                new[] { prefix + ".instcval.mag.f", prefix + ".instmag.f" }
            };

            if (!TryResolvePreferredNumericLeaf(root, candidateTiers, out var leaf, out var failure))
            {
                status = $"Schema-safe THD projection blocked for {requestedReference}: phase {phase.Label} has no unique named magnitude leaf. {failure}";
                return false;
            }

            leaves.Add(leaf);
            values.Add($"{phase.Label}={leaf.NumericValue.ToString("0.######", CultureInfo.InvariantCulture)}");
        }

        // Every phase is resolved independently by exact IEC reference. A repeated row
        // would mean the schema did not actually prove three distinct phase identities.
        if (leaves.Select(leaf => NormalizeReference(leaf.Row.Reference)).Distinct(StringComparer.OrdinalIgnoreCase).Count() != phases.Count)
        {
            status = $"Schema-safe THD projection blocked for {requestedReference}: phase references are not unique.";
            return false;
        }

        var display = string.Join(", ", values);
        projection = new Projection(
            display,
            display,
            FirstUsefulAcrossLeaves(leaves, root, row => row.Quality),
            FirstUsefulAcrossLeaves(leaves, root, row => row.Timestamp),
            "schema-safe-three-phase-aggregate");
        status = $"Schema-safe THD aggregate projected from exact named leaves: {string.Join(", ", leaves.Select(leaf => leaf.Row.Reference))}.";
        return true;
    }

    private static bool TryProjectDemandEnergy(
        Iec61850BoundValueRow root,
        string requestedReference,
        string readReference,
        string dataType,
        out Projection projection,
        out string status)
    {
        projection = new Projection(null, "-", string.Empty, string.Empty, string.Empty);
        var prefix = NormalizeReference(requestedReference);

        // DmdWhMV has existed in both canonical mag.f and vendor instantaneous instMag.f
        // forms. Canonical engineering value wins when present. Instantaneous forms are
        // accepted only when exactly one named fallback exists. No arbitrary numeric child
        // is ever used.
        var candidateTiers = new[]
        {
            new[] { prefix + ".mag.f" },
            new[] { prefix + ".cval.mag.f" },
            new[] { prefix + ".instmag.f", prefix + ".instcval.mag.f" }
        };

        if (!TryResolvePreferredNumericLeaf(root, candidateTiers, out var leaf, out var failure))
        {
            status = $"Schema-safe DmdWh projection blocked for {requestedReference}: no unique named energy magnitude leaf. {failure}";
            return false;
        }

        var numeric = leaf.NumericValue;
        var display = numeric.ToString("0.######", CultureInfo.InvariantCulture);
        projection = new Projection(
            numeric,
            display,
            FirstUseful(leaf.Row.Quality, leaf.Ancestors.Reverse().Select(row => row.Quality), root.Quality),
            FirstUseful(leaf.Row.Timestamp, leaf.Ancestors.Reverse().Select(row => row.Timestamp), root.Timestamp),
            "schema-safe-demand-energy-aggregate");
        status = $"Schema-safe DmdWh aggregate projected from exact named leaf {leaf.Row.Reference}.";
        return true;
    }

    private static bool TryResolvePreferredAttributeReference(
        LiveIedDataObjectModel dataObject,
        IReadOnlyList<string[]> candidateTiers,
        out string reference,
        out string failure)
    {
        reference = string.Empty;
        failure = string.Empty;

        foreach (var tier in candidateTiers)
        {
            var expected = tier.ToHashSet(StringComparer.OrdinalIgnoreCase);
            var matches = dataObject.Attributes
                .Select(attribute => new
                {
                    Attribute = attribute,
                    EffectiveReference = EffectiveAttributeReference(dataObject, attribute)
                })
                .Where(item => expected.Contains(NormalizeReference(item.EffectiveReference)))
                .Where(item => string.IsNullOrWhiteSpace(item.Attribute.FunctionalConstraint) ||
                               item.Attribute.FunctionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase))
                .GroupBy(item => NormalizeReference(item.EffectiveReference), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First().EffectiveReference)
                .ToArray();

            if (matches.Length == 0)
                continue;
            if (matches.Length != 1)
            {
                failure = $"Approved named fallback tier is ambiguous: {string.Join(", ", matches)}.";
                return false;
            }

            reference = matches[0];
            return true;
        }

        failure = "The authoritative schema contains none of the approved exact magnitude references.";
        return false;
    }

    private static string EffectiveAttributeReference(
        LiveIedDataObjectModel dataObject,
        LiveIedDataAttributeModel attribute)
    {
        if (!string.IsNullOrWhiteSpace(attribute.ObjectReference))
            return attribute.ObjectReference.Trim().Replace('$', '.');

        var path = (attribute.AttributePath ?? string.Empty).Trim().Replace('$', '.').Trim('.');
        return string.IsNullOrWhiteSpace(path)
            ? string.Empty
            : dataObject.Reference.Trim().Replace('$', '.').TrimEnd('.') + "." + path;
    }

    private static bool TryResolvePreferredNumericLeaf(
        Iec61850BoundValueRow root,
        IReadOnlyList<string[]> candidateTiers,
        out BoundLeaf leaf,
        out string failure)
    {
        leaf = new BoundLeaf(new Iec61850BoundValueRow(), Array.Empty<Iec61850BoundValueRow>(), 0d);
        failure = string.Empty;

        foreach (var tier in candidateTiers)
        {
            var matches = new List<BoundLeaf>();
            foreach (var reference in tier)
            {
                if (!TryFindBoundRow(root, reference, out var row, out var ancestors))
                    continue;
                if (!TryParseNumeric(row.Value, out var numeric))
                {
                    failure = $"Named leaf {row.Reference} was bound but its value '{row.Value}' is not numeric.";
                    return false;
                }
                matches.Add(new BoundLeaf(row, ancestors, numeric));
            }

            if (matches.Count == 0)
                continue;
            if (matches.Count != 1)
            {
                failure = $"Named fallback tier is ambiguous: {string.Join(", ", matches.Select(match => match.Row.Reference))}.";
                return false;
            }

            leaf = matches[0];
            return true;
        }

        failure = "The authoritative schema contains none of the approved exact magnitude references.";
        return false;
    }

    private static bool TryFindDataObject(
        LiveIedModelDiscoveryDocument model,
        string reference,
        out LiveIedDataObjectModel dataObject)
    {
        dataObject = new LiveIedDataObjectModel();
        if (!TryGetDataObjectReference(reference, out var dataObjectReference))
            return false;

        var matches = model.LogicalDevices
            .SelectMany(device => device.LogicalNodes)
            .SelectMany(node => node.DataObjects)
            .Where(candidate => ReferencesEqual(candidate.Reference, dataObjectReference))
            .ToArray();
        if (matches.Length != 1)
            return false;

        dataObject = matches[0];
        return true;
    }

    private static bool TryGetDataObjectReference(string reference, out string dataObjectReference)
    {
        dataObjectReference = string.Empty;
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash <= 0 || slash >= text.Length - 1)
            return false;

        var domain = text[..slash];
        var segments = text[(slash + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return false;

        dataObjectReference = $"{domain}/{segments[0]}.{segments[1]}";
        return true;
    }

    private static bool TryFindSchemaNode(
        Iec61850ValueSchemaNode node,
        string reference,
        out Iec61850ValueSchemaNode result)
    {
        if (ReferencesEqual(node.Reference, reference))
        {
            result = node;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryFindSchemaNode(child, reference, out result))
                return true;
        }

        result = new Iec61850ValueSchemaNode();
        return false;
    }

    private static bool TryFindBoundRow(
        Iec61850BoundValueRow root,
        string reference,
        out Iec61850BoundValueRow row,
        out IReadOnlyList<Iec61850BoundValueRow> ancestors)
    {
        var path = new List<Iec61850BoundValueRow>();
        return TryFindBoundRow(root, reference, path, out row, out ancestors);
    }

    private static bool TryFindBoundRow(
        Iec61850BoundValueRow current,
        string reference,
        List<Iec61850BoundValueRow> path,
        out Iec61850BoundValueRow row,
        out IReadOnlyList<Iec61850BoundValueRow> ancestors)
    {
        path.Add(current);
        if (ReferencesEqual(current.Reference, reference))
        {
            row = current;
            ancestors = path.Take(path.Count - 1).ToArray();
            path.RemoveAt(path.Count - 1);
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryFindBoundRow(child, reference, path, out row, out ancestors))
            {
                path.RemoveAt(path.Count - 1);
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        row = new Iec61850BoundValueRow();
        ancestors = Array.Empty<Iec61850BoundValueRow>();
        return false;
    }

    private static bool IsThreePhaseThd(
        string reference,
        out IReadOnlyList<(string Path, string Label)> phases)
    {
        var normalized = NormalizeReference(reference).TrimEnd('.');
        if (normalized.EndsWith(".thda", StringComparison.OrdinalIgnoreCase))
        {
            phases = new[]
            {
                ("phsA", "A"),
                ("phsB", "B"),
                ("phsC", "C")
            };
            return true;
        }

        if (normalized.EndsWith(".thdppv", StringComparison.OrdinalIgnoreCase))
        {
            phases = new[]
            {
                ("phsAB", "AB"),
                ("phsBC", "BC"),
                ("phsCA", "CA")
            };
            return true;
        }

        phases = Array.Empty<(string, string)>();
        return false;
    }

    private static bool IsDemandEnergy(string reference)
        => NormalizeReference(reference).TrimEnd('.').EndsWith(".dmdwhmv", StringComparison.OrdinalIgnoreCase);

    private static bool TryParseNumeric(string? value, out double numeric)
        => double.TryParse(
            (value ?? string.Empty).Trim(),
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out numeric);

    private static string FirstUsefulAcrossLeaves(
        IReadOnlyList<BoundLeaf> leaves,
        Iec61850BoundValueRow root,
        Func<Iec61850BoundValueRow, string> selector)
    {
        // Parent composite quality/timestamp is only advertised when the selected phase
        // leaves agree. Mixed evidence must remain blank rather than implying one phase's
        // metadata applies to the whole aggregate.
        var useful = leaves
            .Select(leaf => FirstUseful(selector(leaf.Row), leaf.Ancestors.Reverse().Select(selector), selector(root)))
            .Where(IsUseful)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return useful.Length == 1 ? useful[0] : string.Empty;
    }

    private static string FirstUseful(string primary, IEnumerable<string> inherited, string fallback)
    {
        if (IsUseful(primary))
            return primary;
        foreach (var candidate in inherited)
        {
            if (IsUseful(candidate))
                return candidate;
        }
        return IsUseful(fallback) ? fallback : string.Empty;
    }

    private static bool IsUseful(string? value)
        => !string.IsNullOrWhiteSpace(value) && value != "-";

    private static string FormatDiagnostics(IReadOnlyList<string> diagnostics)
        => diagnostics.Count == 0
            ? "no binding diagnostics"
            : string.Join("; ", diagnostics.Take(4));

    private static bool ReferencesEqual(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty)
            .Trim()
            .Replace('$', '.')
            .Replace("..", ".")
            .ToLowerInvariant();
}
