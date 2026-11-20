using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
    /// <summary>
    /// Bounded per-RCB evidence, run only after the initial report observation
    /// window. Counts already displayed members separately from wire frames
    /// and keeps RptEna/GI/write outcomes explicitly unverified when absent.
    /// No process read, subscription change or Dispatcher-wide model walk.
    /// </summary>
    private void LogStaticRcbImageTriage(Iec61850MonitorDevice device)
    {
        var groups = ArIED61850Tester.Services.StaticDataSetRcbImageTriage.Evaluate(
            device.Points, device.ReportContinuityEvidence, maxGroups: 64,
            activations: device.RcbActivationEvidence);
        var routed = groups.Count(group => group.HasActualReportFrames);
        var missing = groups.Count - routed;
        var valuePending = groups.Sum(group => group.Pending);
        AddLog(missing > 0 || valuePending > 0 ? "WARN" : "INFO",
            device.Name,
            $"Static DataSet RCB evidence: inspected={groups.Count} (max 64), " +
            $"routed={routed}, noRoutedFrames={missing}, valuePending={valuePending}. " +
            "Report ingress, RCB setup and displayed values are separate facts.");
        // Keep ordinary event logs concise even when 33+ independent RCBs
        // run in parallel. Copy Diagnostic retains per-RCB detail for all
        // groups in this bounded diagnostic snapshot.
        foreach (var group in groups.Where(group =>
                     group.Pending > 0 || !group.HasActualReportFrames).Take(8))
            AddLog("WARN", device.Name,
                "Static DataSet RCB evidence: " + group.Summary +
                " (display completeness and actual stream traffic are separate facts).");
    }

    /// <summary>
    /// Emits a protocol-feasibility inventory before monitoring starts. A configured
    /// DataSet is not automatically reportable: at least one configured BRCB/URCB must
    /// reference it. This diagnostic deliberately does not infer availability from
    /// freeBRCB/freeURCB counts or from process-value polling.
    /// </summary>
    private void LogStaticDataSetReportFeasibility(Iec61850MonitorDevice device)
    {
        var model = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (model is null)
        {
            AddLog("WARN", device.Name,
                "Static DataSet feasibility: no SCL/live design model is available; configured RCB → DataSet mapping cannot be audited.");
            return;
        }

        var reports = model.ReportControls.ToArray();
        var verifiedBindingCount = reports.Count(report =>
            !string.IsNullOrWhiteSpace(report.DataSetReference));
        var sourceLabel = device.SclWorkspace?.DesignModel is not null
            ? "open SCL RCB definitions" : "live-observed RCB instances";
        // Complete name discovery and completed bounded enrichment are not
        // equivalent to fully read RCB DatSet bindings. Separate these facts.
        AddLog("INFO", device.Name,
            $"Static DataSet RCB mapping evidence: {verifiedBindingCount}/{reports.Length} " +
            $"{sourceLabel} with nonempty DataSet reference; the remainder is " +
            "unverified, not necessarily unconfigured. RptEna and actual Report " +
            "ingress must be established independently.");

        var unobservedDataSets = new List<string>();
        foreach (var dataSet in model.DataSets)
        {
            var matchingReports = reports
                .Where(report => SameSclReference(report.DataSetReference, dataSet.Reference))
                .ToArray();
            if (matchingReports.Length == 0)
            {
                unobservedDataSets.Add(dataSet.Reference);
                continue;
            }

            // Indexed client-slot families can produce hundreds of RCB rows;
            // summarize representative exact observed names in Event Logs.
            var representative = matchingReports.Length <= 3
                ? matchingReports
                : new[] { matchingReports[0], matchingReports[1], matchingReports[^1] };
            var reportSummary = string.Join(", ", representative.Select(report =>
                $"{report.Reference} ({(report.Buffered ? "BRCB" : "URCB")})")) +
                (matchingReports.Length > 3
                    ? $" (+{matchingReports.Length - 3} indexed candidate(s))" : string.Empty);
            AddLog("INFO", device.Name,
                $"Static DataSet feasibility: {dataSet.Reference} · members={dataSet.Members.Count} · configured RCB={reportSummary}.");
        }

        if (unobservedDataSets.Count > 0)
        {
            // Never misrepresent incomplete live MMS discovery as peer absence.
            // Aggregate the warning, retaining the exact count and representative
            // references without flooding Event Logs with identical red rows.
            var classification = Services.StaticReportEvidencePolicy.ClassifyMissingRcb(
                device.SclWorkspace?.DesignModel is not null);
            var examples = string.Join(", ", unobservedDataSets.Take(6));
            AddLog("WARN", device.Name,
                $"Static DataSet feasibility: {unobservedDataSets.Count}/{model.DataSets.Count} DataSet(s) " +
                $"{classification.Label}; examples={examples}" +
                (unobservedDataSets.Count > 6
                    ? $" (+{unobservedDataSets.Count - 6} more)" : string.Empty) +
                $". {classification.Explanation}");
        }
    }

    /// <summary>
    /// Starts a causal proof timer from the operator's initial Static DataSet selection.
    /// It waits for the shared Engineering monitor to exist, labels points whose DataSet
    /// has no configured RCB, then checks for actual InformationReport traffic. A successful
    /// RptEna/GI write alone is intentionally not treated as report proof.
    /// </summary>
    private async Task ObserveInitialStaticReportEvidenceAsync(Iec61850MonitorDevice device)
    {
        try
        {
            var monitorDeadline = DateTime.UtcNow.AddSeconds(12);
            while (!device.IsMonitoring && DateTime.UtcNow < monitorDeadline)
            {
                if (!IsSharedStaticDataSetAuthority(device))
                    return;
                await Task.Delay(100, _applicationCancellation.Token);
            }

            if (!device.IsMonitoring || !IsSharedStaticDataSetAuthority(device))
                return;

            ApplyNoConfiguredRcbPointEvidence(device);
            AddLog(
                "INFO",
                device.Name,
                "Static DataSet causal gate: shared monitor started. RCB control-plane setup may use one-shot MMS; cyclic MMS process-value polling remains forbidden. Waiting for actual InformationReport traffic after GI.");
            await ObserveSharedStaticReportEvidenceAsync(device, TimeSpan.FromSeconds(3));
        }
        catch (OperationCanceledException)
        {
            // Application shutdown or lifecycle cancellation needs no warning.
        }
    }

    private void ApplyNoConfiguredRcbPointEvidence(Iec61850MonitorDevice device)
    {
        var model = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (model is null || device.Points.Count == 0)
            return;

        var reportableDataSets = model.ReportControls
            .Where(report => !string.IsNullOrWhiteSpace(report.DataSetReference))
            .Select(report => NormalizeSclReference(report.DataSetReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var selectedSignals = device.Signals
            .Where(signal => signal.IsSelected && !string.IsNullOrWhiteSpace(signal.DataSetReference))
            .GroupBy(signal => NormalizeSclReference(signal.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);

        var classification = Services.StaticReportEvidencePolicy.ClassifyMissingRcb(
            device.SclWorkspace?.DesignModel is not null);
        var withoutEvidence = 0;
        foreach (var point in device.Points)
        {
            if (!selectedSignals.TryGetValue(NormalizeSclReference(point.IecReference), out var signal))
                continue;

            var dataSetReference = NormalizeSclReference(signal.DataSetReference);
            if (dataSetReference.Length == 0 || reportableDataSets.Contains(dataSetReference))
                continue;

            point.SourceMode = classification.SourceMode;
            point.Status = classification.PointStatus;
            withoutEvidence++;
        }

        if (withoutEvidence > 0)
            AddLog("WARN", device.Name,
                $"Static DataSet feasibility projected to runtime: {withoutEvidence} point(s): " +
                $"{classification.Label}. {classification.Explanation}");
    }

    private static bool SameSclReference(string? left, string? right)
        => string.Equals(
            NormalizeSclReference(left),
            NormalizeSclReference(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeSclReference(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Replace('$', '.');
}
