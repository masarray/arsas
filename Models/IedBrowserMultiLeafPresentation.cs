using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// An exact, independently observed process field in a multi-valued FCD.
/// The name is part of its IEC schema identity; it is not an inferred unit.
/// </summary>
public sealed record IedBrowserScalarComponent(
    string Label, string Reference, string FunctionalConstraint);

/// <summary>
/// Read-only, bounded presentation for IEC ST objects exposing multiple
/// process fields (e.g. valWTr and posVal). No polling, alias guessing,
/// synthesized parent evidence, hidden channel promotion or angle pairing.
/// </summary>
public static class IedBrowserMultiLeafPresentation
{
    // These are typed IEC process-value alternatives, not arbitrarily chosen
    // first descendants. Restrict this multi-valued summary to ST attributes.
    private static readonly HashSet<string> ProcessFields =
        new(["stVal", "posVal", "valWTr", "general"], StringComparer.Ordinal);

    public static IReadOnlyList<IedBrowserScalarComponent> BuildPlan(
        IEnumerable<LiveIedDataAttributeModel> attributes,
        string objectReference, string fc)
    {
        if (!string.Equals(fc, "ST", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(objectReference))
            return [];
        var owner = IedBrowserLiveValues.CanonicalReference(objectReference);
        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var attr in attributes)
        {
            if (!string.Equals(attr.FunctionalConstraint, "ST",
                    StringComparison.OrdinalIgnoreCase)) continue;
            var reference = IedBrowserLiveValues.CanonicalReference(attr.MmsReference);
            if (!reference.StartsWith(owner + ".", StringComparison.Ordinal))
                continue;
            var label = reference[(owner.Length + 1)..];
            if (!ProcessFields.Contains(label)) continue;
            if (fields.TryGetValue(label, out var previous) &&
                !string.Equals(previous, reference, StringComparison.Ordinal))
                return []; // ambiguous type authority
            fields[label] = reference;
            if (fields.Count > 4) return [];
        }
        if (fields.Count < 2) return [];
        // Respect the model's declared field order, not the arrival order of
        // independently received Report and MMS Read frames.
        return fields.Select(kvp => new IedBrowserScalarComponent(
            kvp.Key, kvp.Value, "ST")).ToArray();
    }

    public static bool TryCompose(IedBrowserDetailRow row,
        IedBrowserLiveValues ledger, bool monitoring,
        out IedBrowserCompositeObservation summary)
    {
        ArgumentNullException.ThrowIfNull(row);
        ArgumentNullException.ThrowIfNull(ledger);
        summary = null!;
        var fields = row.ScalarComponents;
        if (fields.Count < 2 || fields.Count > 4 ||
            !string.Equals(row.FunctionalConstraint, "ST",
                StringComparison.OrdinalIgnoreCase))
            return false;
        // An actually observed whole FCD is stronger evidence than a summary.
        if (ledger.TryGet(row.ValueReference, "ST", out _, preferReport: monitoring))
            return false;

        var display = new List<string>(fields.Count);
        var evidence = new List<string>(fields.Count);
        var authorities = new HashSet<string>(StringComparer.Ordinal);
        long? association = null;
        bool stale = false;
        foreach (var field in fields)
        {
            if (field.FunctionalConstraint != "ST" ||
                field.Label.Length == 0 ||
                !ledger.TryGet(field.Reference, "ST", out var observed,
                    preferReport: monitoring))
                continue;
            if (association.HasValue && association.Value != observed.Association)
                return false; // never combine different IED connections
            if (display.Count > 0 && stale != observed.IsStale)
                return false; // never combine offline history with fresh data
            association = observed.Association;
            stale = observed.IsStale;
            display.Add(field.Label + ": " + observed.Value);
            authorities.Add(observed.Source);
            evidence.Add(field.Reference + " [ST] · " + observed.Source +
                " · observed " + observed.ObservedUtc.ToString("O") +
                " · association " + observed.Association);
        }
        if (display.Count == 0) return false;
        var partiallyObserved = display.Count != fields.Count;
        summary = new IedBrowserCompositeObservation(
            (partiallyObserved ? "Partial · " : "") + string.Join("; ", display),
            "Independent ST fields (" + string.Join("+", authorities.OrderBy(
                x => x, StringComparer.Ordinal)) + ")",
            string.Join("\n", evidence) +
                "\nIndependent process fields; not one atomic IEC sample.",
            stale);
        return true;
    }
}
