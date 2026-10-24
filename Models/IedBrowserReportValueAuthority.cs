using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Browser observes every schema-proven report member, including members not
/// selected as Explorer SCADA points. This is a read-only projection of real
/// report ingress; it does not change the acquisition plan or synthesize a GI.
/// </summary>
public static class IedBrowserReportValueAuthority
{
    public static bool TryResolveFunctionalConstraint(
        LiveIedModelDiscoveryDocument? model, string? dataSetReference,
        string? reportedReference, out string functionalConstraint)
    {
        functionalConstraint = "";
        return model is not null &&
            new IedBrowserSchemaIndex(model).TryResolveReportFc(
                dataSetReference, reportedReference, out functionalConstraint);
    }

    public static bool SafeScalar(string? value)
    {
        var v = value?.Trim() ?? "";
        // A report may contain a parent MMS struct. Never interpret its ToString()
        // or a wire reference echo as an IEC process value.
        return v.Length is > 0 and <= 512 &&
               v is not ("-" or "Not read" or "Unknown") &&
               !v.StartsWith("{", StringComparison.Ordinal) &&
               !v.StartsWith("[", StringComparison.Ordinal) &&
               !v.Contains("System.Collections", StringComparison.OrdinalIgnoreCase);
    }
}
