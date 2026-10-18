using System.Text;
using AR.Iec61850.Discovery;

namespace ArIED61850Tester.Models;

/// <summary>
/// Read-only export of model-verifiable target coverage for the selected page.
/// This is a snapshot, not a substitute for on-wire MMS invoke/GVA diagnostics.
/// Unknown wire steps remain explicitly unverified; no synthetic read events.
/// </summary>
public static class IedBrowserReadEvidenceCsv
{
    public static string Build(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        IedBrowserLiveValues ledger,
        Func<IedBrowserReadTarget, IedBrowserReadFailureSample?>? absence = null,
        int maxTargets = 4096)
    {
        ArgumentNullException.ThrowIfNull(ledger);
        var result = new StringBuilder();
        result.AppendLine("CanonicalReference,FC,MmsReference,ModelType,RootCandidate,LedgerState,FailureKind,Reason,Association,ObservedUtc,Source,RawValue,Quality,DeviceTimestamp,GvaStatus,MmsInvokeStatus,UiProjection");
        if (model is null || page is null || maxTargets <= 0)
            return result.ToString();
        var targets = IedBrowserReadPlan.Build(model, page, readAll: false)
            .Take(Math.Min(maxTargets, 4096)).ToArray();
        foreach (var target in targets)
        {
            var observation = ledger.TryGet(target.Reference, target.FunctionalConstraint,
                out var current) && !current.IsStale &&
                current.Association == ledger.Association ? current : null;
            var failure = observation is null ? absence?.Invoke(target) : null;
            var state = observation is not null ? "OBSERVED" :
                failure?.Kind == IedBrowserReadAbsenceKind.Failed ? "READ_FAILED" :
                failure?.Kind == IedBrowserReadAbsenceKind.Interrupted ? "INTERRUPTED" :
                failure?.Kind == IedBrowserReadAbsenceKind.Deferred ? "DEFERRED" :
                "NOT_OBSERVED_NO_ATTEMPT_EVIDENCE";
            var cells = new[]
            {
                IedBrowserLiveValues.CanonicalReference(target.Reference),
                target.FunctionalConstraint, target.Reference, target.DataType,
                IedBrowserSgRootProjection.RootFor(target),
                state, failure?.Kind.ToString() ?? "", failure?.Reason ?? "",
                ledger.Association.ToString(System.Globalization.CultureInfo.InvariantCulture),
                observation?.ObservedUtc.ToString("O") ?? "",
                observation?.Source ?? "", observation?.Value ?? "",
                observation?.Quality ?? "", observation?.DeviceTimestamp ?? "",
                "NOT_CAPTURED", "NOT_CAPTURED", "NOT_MEASURED"
            };
            result.AppendLine(string.Join(",", cells.Select(Csv)));
        }
        return result.ToString();
    }

    private static string Csv(string? value)
        => "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ")
            .Replace("\n", " ") + "\"";
}
