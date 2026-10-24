using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    /// <summary>
    /// Read only the exact GVA-proved LN$FC$DO parent for a failed ST/SE leaf.
    /// Never use this result to change/control an IED or synthesize an
    /// unsupported enum label. Per-parent positive and negative attempts
    /// are memoized by the single calling browser Read flight, not here.
    /// </summary>
    public async Task<IReadOnlyList<IedBrowserReadResult>> ReadVerifiedDoParentAsync(
        string parent, IReadOnlyList<IedBrowserReadTarget> verifiedChildren,
        CancellationToken cancellationToken)
    {
        if (!_session.IsMmsInitiated ||
            verifiedChildren.Count is < 1 or > 128 ||
            verifiedChildren.Any(t =>
                !string.Equals(IedBrowserVerifiedDoProjection.ParentFor(t),
                    parent, StringComparison.Ordinal)))
            return [];

        var slash = parent.IndexOf('/');
        if (slash < 1 || slash == parent.Length - 1) return [];
        var typeRoot = new MmsObjectReference(
            parent[..slash], parent[(slash + 1)..], string.Empty);

        // No unchecked model/SCL projection: BOTH wire GVA and exact MMS Read
        // must succeed, and the result's component order must match the GVA.
        var gva = await RunMmsOperationAsync(
            () => _session.GetVariableAccessAttributesAsync(typeRoot, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!gva.IsSuccess || gva.TypeSpecification is null)
        {
            LastErrorMessage = "Exact DO parent GVA unavailable for " +
                parent + ": " + gva.Message;
            return [];
        }

        var read = await RunMmsOperationAsync(
            () => _session.ReadSingleVariableAsync(typeRoot, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            LastErrorMessage = "Exact DO parent MMS Read unavailable for " +
                parent + ": " + read.Message;
            return [];
        }

        var values = IedBrowserVerifiedDoProjection.Project(
            parent, gva.TypeSpecification, read.Value, verifiedChildren);
        if (values.Count == 0)
            LastErrorMessage = "Exact DO parent GVA/Read did not prove " +
                "a model-declared scalar under " + parent + ".";
        return values;
    }
}
