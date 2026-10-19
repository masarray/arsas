using ArIED61850Tester.Services;

namespace ArIED61850Tester.Models;

/// <summary>
/// Failure-only evidence. The typed MMS result is never replaced with a
/// synthetic display string and the canonical ledger is NOT written here.
/// This distinguishes a decoded but rejected scalar from transport failure.
/// </summary>
public static class IedBrowserRejectedScalarEvidence
{
    public static string Explain(object? raw, string rejection,
        string modelType, string functionalConstraint, string lastClientError)
    {
        if (raw is null)
            return string.IsNullOrWhiteSpace(lastClientError)
                ? rejection : lastClientError;

        if (raw is not Iec61850ReadValue read)
            return rejection + " (returned CLR type: " +
                   raw.GetType().Name + ", FC=" + functionalConstraint + ").";

        // Constrain diagnostic output; never expose opaque structures,
        // binary data or an unbounded IED-supplied string in CSV.
        static string Safe(string? value)
        {
            var compact = (value ?? string.Empty)
                .Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (compact.Length > 96) compact = compact[..96] + "…";
            return compact;
        }
        var payloadType = read.Value?.GetType().Name ?? "null";
        return rejection + " (decoded result rejected: " +
            "FC=" + Safe(functionalConstraint) +
            ", modelType=" + Safe(modelType) +
            ", projection=" + Safe(read.Projection) +
            ", CLR=" + payloadType +
            ", display='" + Safe(read.DisplayValue) + "'" +
            ", source=" + Safe(read.ReadReference) +
            "; GVA/invoke raw not captured).";
    }
}
