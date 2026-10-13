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
        if (model is null || string.IsNullOrWhiteSpace(dataSetReference) ||
            string.IsNullOrWhiteSpace(reportedReference))
            return false;
        var set = model.DataSets.FirstOrDefault(ds =>
            IedBrowserLiveValues.CanonicalReference(ds.Reference).Equals(
                IedBrowserLiveValues.CanonicalReference(dataSetReference),
                StringComparison.OrdinalIgnoreCase));
        if (set is null) return false;
        var report = IedBrowserLiveValues.CanonicalReference(reportedReference);
        var matched = set.Members
            .Where(member => {
                var prefix = IedBrowserLiveValues.CanonicalReference(member.Reference);
                return report.Equals(prefix, StringComparison.OrdinalIgnoreCase) ||
                       report.StartsWith(prefix + ".", StringComparison.OrdinalIgnoreCase);
            })
            .Select(member => member.FunctionalConstraint)
            .Where(fc => !string.IsNullOrWhiteSpace(fc))
            .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (matched.Length != 1) return false;
        functionalConstraint = matched[0];
        return true;
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
