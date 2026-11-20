using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// No-observation, bounded diagnostic view of an existing immutable schema.
/// A contradictory ancestor FCD/FCDA declaration cannot authorize Report FC
/// or an explicit DataSet MMS Read target. No guesses about relay wire values.
/// </summary>
public static class IedBrowserDataSetFcConflictDiagnostic
{
    public sealed record Conflict(
        string DataSet, string Reference, string FunctionalConstraint);
    public sealed record Result(int CheckedMembers, int TotalCount,
        IReadOnlyList<Conflict> Samples);

    public static Result Evaluate(
        LiveIedModelDiscoveryDocument? model, int maxSamples = 12)
    {
        if (model is null) return new Result(0, 0, Array.Empty<Conflict>());
        var index = new IedBrowserSchemaIndex(model);
        var checkedMembers = 0;
        var rejected = 0;
        var sample = new List<Conflict>();
        var limit = Math.Clamp(maxSamples, 0, 32);
        foreach (var set in model.DataSets)
            foreach (var member in set.Members)
            {
                if (string.IsNullOrWhiteSpace(set.Reference) ||
                    string.IsNullOrWhiteSpace(member.Reference) ||
                    string.IsNullOrWhiteSpace(member.FunctionalConstraint))
                    continue;
                checkedMembers++;
                if (index.TryResolveReportFc(set.Reference, member.Reference,
                        out var resolved) &&
                    string.Equals(resolved, member.FunctionalConstraint.Trim(),
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                rejected++;
                if (sample.Count < limit)
                    sample.Add(new Conflict(
                        IedBrowserLiveValues.CanonicalReference(set.Reference),
                        IedBrowserLiveValues.CanonicalReference(member.Reference),
                        member.FunctionalConstraint.Trim().ToUpperInvariant()));
            }
        return new Result(checkedMembers, rejected, sample);
    }
}
