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
    }

    private static void AddUnique(ICollection<string> candidates, string reference)
    {
        if (!candidates.Any(candidate => ReferencesEqual(candidate, reference)))
            candidates.Add(reference);
    }

    private static string BuildCompanionReference(string reference, string companion)
    {
        var parent = Normalize(reference);
        foreach (var suffix in new[] { ".instCVal.mag.f", ".cVal.mag.f", ".mag.f" })
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
