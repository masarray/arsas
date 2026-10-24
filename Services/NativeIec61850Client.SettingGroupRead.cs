using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// Captured MMS-compatible wire shape: one read-only MMS Read of a verified
    /// LN$SG variable (not repeated speculative LN$SG$DO$setVal reads).
    /// Wire GVA defines the actual ordered child names and types. A mismatched
    /// structure fails closed instead of assigning a neighbor's setting.
    /// </summary>
    public async Task<IReadOnlyList<IedBrowserReadResult>> ReadVerifiedSgRootAsync(
        string root, IReadOnlyList<IedBrowserReadTarget> targets,
        CancellationToken cancellationToken)
    {
        if (!_session.IsMmsInitiated || targets.Count == 0 ||
            targets.Any(t => !string.Equals(
                IedBrowserSgRootProjection.RootFor(t), root, StringComparison.Ordinal)))
            return [];
        var slash = root.IndexOf('/');
        if (slash <= 0 || slash >= root.Length - 1 ||
            !(root.EndsWith("$SG0", StringComparison.Ordinal) ||
              root.EndsWith("$SP0", StringComparison.Ordinal) ||
              root.EndsWith("$SE0", StringComparison.Ordinal) ||
              root.EndsWith("$CF0", StringComparison.Ordinal) ||
              root.EndsWith("$DC0", StringComparison.Ordinal) ||
              root.EndsWith("$EX0", StringComparison.Ordinal)))
            return [];
        var reference = new MmsObjectReference(
            root[..slash], root[(slash + 1)..], string.Empty);
        // Two fixed bounded requests, both on the existing read-only client:
        // type specification and exactly one structured MMS FC root.
        var type = await RunMmsOperationAsync(() =>
            _session.GetVariableAccessAttributesAsync(reference, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!type.IsSuccess || type.TypeSpecification is null)
        {
            LastErrorMessage = "Typed FC root read cannot verify wire type for " +
                root + ": " + type.Message;
            return [];
        }
        var read = await RunMmsOperationAsync(() =>
            _session.ReadSingleVariableAsync(reference, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            LastErrorMessage = "Typed FC root MMS Read failed for " + root +
                ": " + read.Message;
            return [];
        }
        var result = IedBrowserSgRootProjection.Project(
            root, type.TypeSpecification, read.Value, targets);
        if (result.Count == 0)
            LastErrorMessage = "Typed FC root received but type/order did not prove " +
                "any exact requested leaf in " + root + ". No value published.";
        return result;
    }

    /// <summary>
    /// One read-only grouped FC-root MMS Read matching the ordered multi-root
    /// wire shape. A successful typed GVA of EVERY admitted root must precede
    /// its inclusion in the batch. Unproved roots remain on the existing
    /// individual/leaf fallback path, never a positional or text-based guess.
    /// </summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<IedBrowserReadResult>>>
        ReadVerifiedSgRootsAsync(
            IReadOnlyList<(string Root, IReadOnlyList<IedBrowserReadTarget> Targets)> groups,
            CancellationToken cancellationToken)
    {
        var output = new Dictionary<string, IReadOnlyList<IedBrowserReadResult>>(
            StringComparer.Ordinal);
        if (!_session.IsMmsInitiated || groups.Count is < 2 or > 5 ||
            groups.Select(g => g.Root).Distinct(StringComparer.Ordinal).Count() != groups.Count)
            return output;

        var admitted = new List<(string Root, MmsObjectReference Reference,
            MmsTypeSpecificationNode Type, IReadOnlyList<IedBrowserReadTarget> Targets)>();
        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!IedBrowserSgRootProjection.CanReadRoot(group.Targets.Count) ||
                group.Targets.Any(t =>
                    !string.Equals(IedBrowserSgRootProjection.RootFor(t),
                        group.Root, StringComparison.Ordinal)))
                continue;
            var slash = group.Root.IndexOf('/');
            if (slash < 1 || slash >= group.Root.Length - 1) continue;
            var reference = new MmsObjectReference(
                group.Root[..slash], group.Root[(slash + 1)..], string.Empty);
            var type = await RunMmsOperationAsync(() =>
                _session.GetVariableAccessAttributesAsync(reference, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            if (!type.IsSuccess || type.TypeSpecification is null)
            {
                LastErrorMessage = "Typed FC root GVA unavailable for " +
                    group.Root + ": " + type.Message;
                continue;
            }
            admitted.Add((group.Root, reference,
                type.TypeSpecification, group.Targets));
        }
        if (admitted.Count < 2) return output;
        // Reuse the pinned engine's existing bounded, invoke-fenced MMS batch.
        // This avoids widening the engine trial or bypassing R7/R10 guards.
        var engineTargets = admitted.Select(entry => new InitialFcReadTarget
        {
            Domain = entry.Reference.Domain,
            LogicalNode = entry.Reference.Item.Split('$')[0],
            FunctionalConstraint = entry.Targets[0].FunctionalConstraint,
            MmsItemName = entry.Reference.Item,
            Source = "ExactLiveMmsGvaVerifiedBrowserRoot"
        }).ToArray();
        var plan = InitialFcReadPlanner.Build(engineTargets, engineTargets.Length);
        if (!plan.IsValid || plan.Batches.Count != 1)
        {
            LastErrorMessage = "Grouped FC root read plan invalid: " + plan.Summary;
            return output;
        }
        var executed = await RunMmsOperationAsync(() =>
            _session.ExecuteInitialFcReadPlanAsync(
                plan, TimeSpan.FromSeconds(5), cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (executed.Batches.Count != 1)
        {
            LastErrorMessage = "Grouped FC root MMS execution unavailable: " +
                executed.Message;
            return output;
        }
        var wire = executed.Batches[0].Read;
        if (wire.ExtraAccessResultCount != 0 || wire.Results.Count != admitted.Count)
        {
            LastErrorMessage = "Grouped FC root MMS response cardinality mismatch; " +
                "no positional scalar projection authorized.";
            return output;
        }
        for (var i = 0; i < admitted.Count; i++)
        {
            var entry = admitted[i];
            var access = wire.Results[i];
            if (!access.IsSuccess || access.Value is null)
            {
                LastErrorMessage = "Grouped FC root Read unavailable for " +
                    entry.Root + ": " + access.Message;
                continue;
            }
            var values = IedBrowserSgRootProjection.Project(
                entry.Root, entry.Type, access.Value, entry.Targets);
            if (values.Count == 0)
            {
                LastErrorMessage = "Grouped FC root typed GVA/Read did not match " +
                    "any exact model-declared scalar under " + entry.Root;
                continue;
            }
            output[entry.Root] = values;
        }
        return output;
    }
}
