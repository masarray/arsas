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
            !root.EndsWith("$SG", StringComparison.Ordinal))
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
            LastErrorMessage = "SG structured read cannot verify wire type for " +
                root + ": " + type.Message;
            return [];
        }
        var read = await RunMmsOperationAsync(() =>
            _session.ReadSingleVariableAsync(reference, cancellationToken),
            cancellationToken).ConfigureAwait(false);
        if (!read.IsSuccess || read.Value is null)
        {
            LastErrorMessage = "SG structured MMS Read failed for " + root +
                ": " + read.Message;
            return [];
        }
        var result = IedBrowserSgRootProjection.Project(
            root, type.TypeSpecification, read.Value, targets);
        if (result.Count == 0)
            LastErrorMessage = "SG structure received but type/order did not prove " +
                "any exact requested leaf in " + root + ". No value published.";
        return result;
    }
}
