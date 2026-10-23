using ArIED61850Tester.Models;

namespace ArIED61850Tester;

public partial class MainWindow
{
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
        foreach (var dataSet in model.DataSets)
        {
            var matchingReports = reports
                .Where(report => SameSclReference(report.DataSetReference, dataSet.Reference))
                .ToArray();

            if (matchingReports.Length == 0)
            {
                AddLog(
                    "WARN",
                    device.Name,
                    $"Static DataSet feasibility: {dataSet.Reference} · members={dataSet.Members.Count} · NO CONFIGURED RCB. These members cannot become report-live until the IED/SCL provides a BRCB/URCB for this DataSet; ARSAS will not substitute cyclic MMS process polling.");
                continue;
            }

            var reportSummary = string.Join(", ", matchingReports.Select(report =>
                $"{report.Reference} ({(report.Buffered ? "BRCB" : "URCB")})"));
            AddLog(
                "INFO",
                device.Name,
                $"Static DataSet feasibility: {dataSet.Reference} · members={dataSet.Members.Count} · configured RCB={reportSummary}.");
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

        var unavailable = 0;
        foreach (var point in device.Points)
        {
            if (!selectedSignals.TryGetValue(NormalizeSclReference(point.IecReference), out var signal))
                continue;

            var dataSetReference = NormalizeSclReference(signal.DataSetReference);
            if (dataSetReference.Length == 0 || reportableDataSets.Contains(dataSetReference))
                continue;

            point.SourceMode = "Static DataSet: no configured RCB";
            point.Status = "Unavailable / no configured RCB";
            unavailable++;
        }

        if (unavailable > 0)
        {
            AddLog(
                "WARN",
                device.Name,
                $"Static DataSet feasibility projected to runtime: {unavailable} point(s) belong to DataSet(s) with no configured RCB and are explicitly marked unavailable instead of generic report-pending or MMS polling.");
        }
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
