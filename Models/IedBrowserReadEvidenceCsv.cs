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
        return BuildSelection(Plan(model, page, maxTargets), ledger, absence);
    }

    /// <summary>One exact, bounded selected-page plan shared by UI preflight and export.</summary>
    public static IReadOnlyList<IedBrowserReadTarget> Plan(
        LiveIedModelDiscoveryDocument? model, IedBrowserTreeRow? page,
        int maxTargets = 4096)
        => model is null || page is null || maxTargets <= 0
            ? Array.Empty<IedBrowserReadTarget>()
            : IedBrowserReadPlan.Build(model, page, readAll: false)
                .Take(Math.Min(maxTargets, 4096)).ToArray();

    /// <summary>
    /// Fail closed on an empty selection; never label a header-only export as
    /// completed evidence. This method does not initiate MMS network traffic.
    /// </summary>
    public static string BuildSelection(
        IReadOnlyList<IedBrowserReadTarget> targets,
        IedBrowserLiveValues ledger,
        Func<IedBrowserReadTarget, IedBrowserReadFailureSample?>? absence = null)
    {
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentNullException.ThrowIfNull(ledger);
        if (targets.Count == 0)
            throw new InvalidOperationException(
                "ZERO_TARGETS: no model-verified FC attributes in selected Browser page.");
        var result = new StringBuilder();
        result.AppendLine("CanonicalReference,FC,MmsReference,ModelType,RootCandidate,LedgerState,FailureKind,Reason,Association,ObservedUtc,Source,RawValue,Quality,DeviceTimestamp,GvaStatus,MmsInvokeStatus,UiProjection,FailureReasonCode,DecodedMmsWireShape");
        foreach (var target in targets)
        {
            var projection = IedBrowserReadEvidenceProjector.Project(
                target, ledger, absence?.Invoke(target));
            var observation = projection.Observation;
            var failure = projection.Failure;
            var state = projection.State;
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
                "NOT_CAPTURED", "NOT_CAPTURED", "NOT_MEASURED",
                projection.FailureReasonCode,
                // Decoded wire-kind shape is captured from MMS result, never
                // parsed from displayed strings. No invoke/GVA proof fabricated.
                observation?.ReadWireShapeEvidence ?? ""
            };
            result.AppendLine(string.Join(",", cells.Select(Csv)));
        }
        return result.ToString();
    }

    private static string Csv(string? value)
        => "\"" + (value ?? "").Replace("\"", "\"\"").Replace("\r", " ")
            .Replace("\n", " ") + "\"";
}
