using System.Globalization;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed record ResolvedIecSignalRead(object Value, string EffectiveReference)
{
    public bool UsedAlternateReference(string requestedReference)
        => !string.Equals(Normalize(requestedReference), Normalize(EffectiveReference), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => (value ?? string.Empty).Trim().Replace('$', '.');
}

public static class IecSignalReadResolver
{
    public static async Task<ResolvedIecSignalRead?> ReadAsync(
        IIec61850Client client,
        SignalDefinition signal,
        CancellationToken cancellationToken)
    {
        // Object-level THD and demand-energy members are structured values. They must
        // never enter the generic parent-read path, because a raw MMS structure can only
        // identify its children positionally. Build an exact named-leaf plan from the
        // per-IED SCL/live schema and read those scalar leaves directly instead.
        if (client is NativeIec61850Client native && IsSchemaSafeAggregate(signal.ObjectReference))
            return await ReadSchemaSafeAggregateAsync(native, signal, cancellationToken).ConfigureAwait(false);

        var references = BuildReadCandidates(signal.ObjectReference).ToList();
        Exception? firstFailure = null;

        foreach (var reference in references)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var value = await client.ReadValueAsync(
                    reference,
                    signal.FunctionalConstraint,
                    signal.DataType,
                    cancellationToken).ConfigureAwait(false);
                if (value != null)
                    return new ResolvedIecSignalRead(value, reference);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                firstFailure ??= ex;
            }
        }

        if (firstFailure != null)
            throw firstFailure;
        return null;
    }

    private sealed record AggregateLeafRead(
        SchemaSafeAggregateProjectionService.ReadLeaf Leaf,
        double NumericValue,
        string Quality,
        string DeviceTimestamp);

    private static async Task<ResolvedIecSignalRead?> ReadSchemaSafeAggregateAsync(
        NativeIec61850Client client,
        SignalDefinition signal,
        CancellationToken cancellationToken)
    {
        // Failure to prove the schema is an intentional hard stop for this parent value.
        // Do not fall through to NativeIec61850Client.ReadValueAsync(parent), where raw
        // structure ordering could silently assign the wrong phase/value.
        if (!client.TryBuildSchemaSafeAggregateReadPlan(signal.ObjectReference, out var plan, out _))
            return null;

        var reads = new List<AggregateLeafRead>(plan.Leaves.Count);
        foreach (var leaf in plan.Leaves)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var value = await client.ReadValueAsync(
                leaf.Reference,
                signal.FunctionalConstraint,
                leaf.DataType,
                cancellationToken).ConfigureAwait(false);
            if (value is null)
                return null;

            var raw = Iec61850ReadValue.Unwrap(value);
            if (!TryConvertNumeric(raw, out var numeric))
                return null;

            var rich = value as Iec61850ReadValue;
            reads.Add(new AggregateLeafRead(
                leaf,
                numeric,
                rich?.Quality ?? string.Empty,
                rich?.DeviceTimestamp ?? string.Empty));
        }

        if (plan.Kind.Equals("ThreePhaseThd", StringComparison.OrdinalIgnoreCase))
        {
            if (reads.Count != 3)
                return null;

            var display = string.Join(", ", reads.Select(read =>
                $"{read.Leaf.Label}={read.NumericValue.ToString("0.######", CultureInfo.InvariantCulture)}"));
            var rich = new Iec61850ReadValue
            {
                Value = display,
                DisplayValue = display,
                Quality = UnanimousUseful(reads.Select(read => read.Quality)),
                DeviceTimestamp = UnanimousUseful(reads.Select(read => read.DeviceTimestamp)),
                SourceReference = signal.ObjectReference,
                ReadReference = string.Join(" | ", reads.Select(read => read.Leaf.Reference)),
                Projection = "schema-safe-three-phase-exact-leaf-reads"
            };
            return new ResolvedIecSignalRead(rich, signal.ObjectReference);
        }

        if (plan.Kind.Equals("DemandEnergy", StringComparison.OrdinalIgnoreCase))
        {
            var read = reads.Count == 1 ? reads[0] : null;
            if (read is null)
                return null;

            var display = read.NumericValue.ToString("0.######", CultureInfo.InvariantCulture);
            var rich = new Iec61850ReadValue
            {
                Value = read.NumericValue,
                DisplayValue = display,
                Quality = read.Quality,
                DeviceTimestamp = read.DeviceTimestamp,
                SourceReference = signal.ObjectReference,
                ReadReference = read.Leaf.Reference,
                Projection = "schema-safe-demand-energy-exact-leaf-read"
            };

            // The runtime keeps the FAT identity on the parent point, but uses this exact
            // effective leaf to derive low-rate q/t companion references. Returning the
            // parent here would incorrectly derive XPRE_MMTR1.q instead of DmdWhMV.q.
            return new ResolvedIecSignalRead(rich, read.Leaf.Reference);
        }

        return null;
    }

    private static bool IsSchemaSafeAggregate(string reference)
        => SignalDefinition.IsThreePhaseMeasurementAggregate(reference) ||
           SignalDefinition.IsDemandEnergyAggregate(reference);

    private static bool TryConvertNumeric(object? value, out double numeric)
    {
        numeric = 0d;
        if (value is null)
            return false;

        try
        {
            switch (value)
            {
                case double d:
                    numeric = d;
                    return double.IsFinite(d);
                case float f:
                    numeric = f;
                    return float.IsFinite(f);
                case decimal m:
                    numeric = (double)m;
                    return double.IsFinite(numeric);
                case byte or sbyte or short or ushort or int or uint or long or ulong:
                    numeric = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return double.IsFinite(numeric);
            }
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException)
        {
            return false;
        }

        return double.TryParse(
                   Convert.ToString(value, CultureInfo.InvariantCulture),
                   NumberStyles.Float | NumberStyles.AllowThousands,
                   CultureInfo.InvariantCulture,
                   out numeric) &&
               double.IsFinite(numeric);
    }

    private static string UnanimousUseful(IEnumerable<string> values)
    {
        var useful = values
            .Where(value => !string.IsNullOrWhiteSpace(value) && value != "-")
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return useful.Length == 1 ? useful[0] : string.Empty;
    }

    public static bool ApplyEffectiveReference(SignalDefinition signal, string effectiveReference)
    {
        if (string.IsNullOrWhiteSpace(effectiveReference) || ReferencesEqual(signal.ObjectReference, effectiveReference))
            return false;

        signal.ObjectReference = effectiveReference;
        signal.QualityReference = BuildCompanionReference(effectiveReference, "q");
        signal.TimestampReference = BuildCompanionReference(effectiveReference, "t");
        signal.Source = string.IsNullOrWhiteSpace(signal.Source)
            ? "MMS readable sibling"
            : $"{signal.Source} / MMS readable sibling";
        return true;
    }

    public static string GetPreferredSelectionReference(string reference)
    {
        var normalized = Normalize(reference);
        if (IsOperationalCurrentOrVoltageReference(normalized) && normalized.Contains("/PPRE_MMXU", StringComparison.OrdinalIgnoreCase))
            normalized = ReplaceToken(normalized, "/PPRE_MMXU", "/RPRE_MMXU");
        if (IsOperationalValueReference(normalized) && normalized.Contains(".cVal.mag.f", StringComparison.OrdinalIgnoreCase))
            return ReplaceToken(normalized, ".cVal.mag.f", ".instCVal.mag.f");
        return normalized;
    }

    private static IEnumerable<string> BuildReadCandidates(string requestedReference)
    {
        var normalized = Normalize(requestedReference);
        if (string.IsNullOrWhiteSpace(normalized)) yield break;

        var candidates = new List<string>();
        AddMeasurementPair(candidates, normalized);
        var preferred = GetPreferredSelectionReference(normalized);
        if (!ReferencesEqual(preferred, normalized))
            AddMeasurementPair(candidates, preferred);

        foreach (var candidate in candidates)
            yield return candidate;
    }

    private static void AddMeasurementPair(ICollection<string> candidates, string reference)
    {
        AddUnique(candidates, reference);
        if (reference.Contains(".instCVal.mag.f", StringComparison.OrdinalIgnoreCase))
            AddUnique(candidates, ReplaceToken(reference, ".instCVal.mag.f", ".cVal.mag.f"));
        else if (reference.Contains(".cVal.mag.f", StringComparison.OrdinalIgnoreCase))
            AddUnique(candidates, ReplaceToken(reference, ".cVal.mag.f", ".instCVal.mag.f"));
        else if (reference.Contains(".instMag.f", StringComparison.OrdinalIgnoreCase))
            AddUnique(candidates, ReplaceToken(reference, ".instMag.f", ".mag.f"));
        else if (reference.Contains(".mag.f", StringComparison.OrdinalIgnoreCase))
            AddUnique(candidates, ReplaceToken(reference, ".mag.f", ".instMag.f"));
    }

    private static void AddUnique(ICollection<string> candidates, string reference)
    {
        if (!candidates.Any(candidate => ReferencesEqual(candidate, reference)))
            candidates.Add(reference);
    }

    private static string BuildCompanionReference(string reference, string companion)
    {
        var parent = Normalize(reference);
        foreach (var suffix in new[] { ".instCVal.mag.f", ".cVal.mag.f", ".instMag.f", ".mag.f" })
        {
            if (!parent.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) continue;
            parent = parent[..^suffix.Length];
            return $"{parent}.{companion}";
        }
        return string.Empty;
    }

    private static bool IsOperationalValueReference(string reference)
        => reference.Contains("operationalvalues", StringComparison.OrdinalIgnoreCase) ||
           reference.Contains("operational_values", StringComparison.OrdinalIgnoreCase);

    private static bool IsOperationalCurrentOrVoltageReference(string reference)
        => IsOperationalValueReference(reference) &&
           (reference.Contains(".A.", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains(".PhV.", StringComparison.OrdinalIgnoreCase) ||
            reference.Contains(".PPV.", StringComparison.OrdinalIgnoreCase));

    private static string ReplaceToken(string source, string oldValue, string newValue)
    {
        var index = source.IndexOf(oldValue, StringComparison.OrdinalIgnoreCase);
        return index < 0 ? source : string.Concat(source.AsSpan(0, index), newValue, source.AsSpan(index + oldValue.Length));
    }

    private static bool ReferencesEqual(string left, string right)
        => string.Equals(Normalize(left), Normalize(right), StringComparison.OrdinalIgnoreCase);

    private static string Normalize(string value) => (value ?? string.Empty).Trim().Replace('$', '.');
}
