using AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// One operator-selected, exact-FC scalar edit. A batch never implies a
/// structure/DO overwrite, an alternate FC or a new association.
/// </summary>
public sealed record IedBrowserTypedWriteStep(
    MmsObjectReference Reference,
    string ExpectedGvaSignature,
    MmsDataValue ExpectedOldValue,
    MmsDataValue ProposedValue);

public sealed record IedBrowserTypedWriteRowResult(
    IedBrowserTypedWriteStep Step,
    IedBrowserNativeWriteState? State,
    string Message);

public sealed record IedBrowserTypedWriteBatchResult(
    IReadOnlyList<IedBrowserTypedWriteRowResult> Rows)
{
    public int Verified => Rows.Count(x => x.State == IedBrowserNativeWriteState.AcceptedVerified);
    public int Rejected => Rows.Count(x => x.State == IedBrowserNativeWriteState.Rejected);
    public int Unknown => Rows.Count(x => x.State == IedBrowserNativeWriteState.Unknown);
    public int Unattempted => Rows.Count(x => x.State is null);
    public int Unverified => Rows.Count(x => x.State is
        IedBrowserNativeWriteState.AcceptedReadbackMismatch or
        IedBrowserNativeWriteState.AcceptedReadbackUnavailable);
}

/// <summary>
/// Sequential, non-atomic multi-DA transaction runner. All submitted targets
/// must belong to ONE exact IEC DO and have distinct exact FC+leaf identities.
/// Stops at first failure/uncertainty. It never retries or compensates.
/// </summary>
public static class IedBrowserTypedWriteBatch
{
    public const int MaxSelectedAttributes = 64;

    public static IReadOnlyList<IedBrowserTypedWriteStep> Freeze(
        IEnumerable<IedBrowserTypedWriteStep> selected)
    {
        ArgumentNullException.ThrowIfNull(selected);
        var steps = selected.ToArray();
        if (steps.Length is 0 or > MaxSelectedAttributes)
            throw new ArgumentException("Select 1–64 exact data attributes.", nameof(selected));

        string? ownerDo = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var step in steps)
        {
            if (step is null || step.ExpectedOldValue is null ||
                step.ProposedValue is null || string.IsNullOrWhiteSpace(step.ExpectedGvaSignature))
                throw new ArgumentException("Each selected edit needs typed old/new values and live GVA signature.");

            var reference = step.Reference;
            var parts = reference.Item.Split('$');
            if (string.IsNullOrWhiteSpace(reference.Domain) || parts.Length < 4 ||
                parts.Any(string.IsNullOrWhiteSpace) ||
                !string.Equals(parts[1], reference.FunctionalConstraint, StringComparison.OrdinalIgnoreCase) ||
                reference.FunctionalConstraint.ToUpperInvariant() is not ("CF" or "SP" or "DC"))
                throw new ArgumentException("Exact IEC DA identity or FC is not eligible for the generic plan.");

            if (parts.Skip(2).Any(x => x is "Oper" or "SBO" or "SBOw" or "Cancel"))
                throw new ArgumentException("IEC control service cannot be a generic Write target.");

            var thisDo = reference.Domain + "/" + parts[0] + "$" + parts[2];
            if (ownerDo is null) ownerDo = thisDo;
            else if (!string.Equals(ownerDo, thisDo, StringComparison.Ordinal))
                throw new ArgumentException("All selected DAs must belong to the same exact DO.");

            if (!seen.Add(reference.Domain + "/" + reference.Item))
                throw new ArgumentException("Duplicate MMS references are not allowed in a batch.");
        }
        return Array.AsReadOnly(steps);
    }

    public static async Task<IedBrowserTypedWriteBatchResult> ExecuteAsync(
        IReadOnlyList<IedBrowserTypedWriteStep> frozen,
        Func<IedBrowserTypedWriteStep, CancellationToken, Task<IedBrowserNativeWriteResult>> oneShot,
        Func<bool> associationLeaseIsCurrent,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(oneShot);
        ArgumentNullException.ThrowIfNull(associationLeaseIsCurrent);
        // Revalidate caller-provided collection; don't trust a mutable list.
        var steps = Freeze(frozen);
        var results = new List<IedBrowserTypedWriteRowResult>(steps.Count);
        var stopped = false;

        foreach (var step in steps)
        {
            if (stopped || cancellationToken.IsCancellationRequested || !associationLeaseIsCurrent())
            {
                results.Add(new(step, null, "Not attempted (prior failure, cancellation or stale association)."));
                stopped = true;
                continue;
            }

            IedBrowserNativeWriteResult result;
            try
            {
                result = await oneShot(step, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is OperationCanceledException or
                                       IOException or InvalidOperationException or
                                       ObjectDisposedException)
            {
                // The callback could have dispatched before it faulted.
                result = new(IedBrowserNativeWriteState.Unknown,
                    $"Outcome unknown after attempted dispatch ({ex.GetType().Name}).");
            }

            results.Add(new(step, result.State, result.Message));
            // AcceptedVerified is the only state allowing another request.
            stopped = result.State != IedBrowserNativeWriteState.AcceptedVerified;
        }
        return new IedBrowserTypedWriteBatchResult(results.AsReadOnly());
    }
}
