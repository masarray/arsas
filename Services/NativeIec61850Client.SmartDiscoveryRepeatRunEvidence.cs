using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private const string P05fEngineCommit = "4467124775d8d9d76f3db194f9fbfd97144767a8";

    public string LastSmartDiscoveryRepeatRunEvidencePath { get; private set; } = string.Empty;

    private string TryWriteSmartDiscoveryRepeatRunEvidence(
        long associationGeneration,
        ArMms.MmsDiscoveryResult discovery,
        IReadOnlyList<SignalDefinition> signals,
        Iec61850DeviceIdentity identity)
    {
        try
        {
            // Local bookkeeping only. This refresh sends no MMS traffic and makes the
            // engine KPI signature reflect hierarchy-materialized live directory counts.
            _session.RefreshSmartDiscoveryModelKpi(discovery.IedDirectory);

            var kpi = _session.LastSmartDiscoveryKpi;
            var typeBudget = _session.LastSmartTypeProbeBudget;
            if (kpi is null)
                return string.Empty;

            var directorySignature = ComputeDirectoryModelSignature(discovery.IedDirectory);
            var projectionSignature = ComputeSignalProjectionSignature(signals);
            var deviceIdentity = string.IsNullOrWhiteSpace(identity.IedName)
                ? DetectedIedName
                : identity.IedName;

            var payload = new
            {
                SchemaVersion = 1,
                Phase = "P0-5f-run",
                CapturedAtUtc = DateTimeOffset.UtcNow,
                DeviceIdentity = deviceIdentity ?? string.Empty,
                Host = _host,
                Port = _port,
                AssociationGeneration = associationGeneration,
                EngineCommit = P05fEngineCommit,
                Model = new
                {
                    LogicalDevices = discovery.IedDirectory.LogicalDeviceCount,
                    LogicalNodes = discovery.IedDirectory.LogicalNodeCount,
                    SemanticPoints = discovery.IedDirectory.PointCount,
                    ProjectedSignals = signals.Count,
                    DataSets = discovery.ReportInventory.DataSets.Count,
                    ReportControls = discovery.ReportInventory.ReportControls.Count,
                    BufferedReportControls = discovery.ReportInventory.BufferedCount,
                    UnbufferedReportControls = discovery.ReportInventory.UnbufferedCount,
                    DirectoryModelSignature = directorySignature,
                    ProjectionSignature = projectionSignature
                },
                SmartDiscoveryKpi = new
                {
                    kpi.Generation,
                    kpi.TotalRequests,
                    kpi.SuccessfulRequests,
                    kpi.FailedRequests,
                    kpi.DuplicateRequests,
                    kpi.PeakOutstandingRequests,
                    kpi.WireAccountingComplete,
                    kpi.AccountingNotes,
                    kpi.LogicalDeviceCount,
                    kpi.LogicalNodeCount,
                    kpi.RawVariableCount,
                    kpi.FcPointCount,
                    kpi.DataSetCount,
                    kpi.DataSetDirectoryCount,
                    kpi.DataSetMemberCount,
                    kpi.ReportControlCount,
                    kpi.BufferedReportControlCount,
                    kpi.UnbufferedReportControlCount,
                    kpi.DeterministicSignature
                },
                TypeProbeBudget = typeBudget is null
                    ? null
                    : new
                    {
                        typeBudget.DirectoryPoints,
                        typeBudget.SuppliedLogicalNodeCandidates,
                        typeBudget.SuppressedNonLiveLogicalNodeCandidates,
                        typeBudget.LogicalNodeRequests,
                        typeBudget.PointsCoveredByLogicalNode,
                        typeBudget.DataObjectRequests,
                        typeBudget.PointsCoveredByDataObject,
                        typeBudget.ExactLeafRequests,
                        typeBudget.SuppressedExactRepeatRequests,
                        typeBudget.PointsCoveredByExactLeaf,
                        typeBudget.RemainingUnresolvedPoints,
                        typeBudget.TotalPlannedRequests
                    }
            };

            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "SmartDiscoveryEvidence");
            Directory.CreateDirectory(root);

            var safeIdentity = SanitizeEvidenceFileToken(
                string.IsNullOrWhiteSpace(deviceIdentity) ? "unknown-ied" : deviceIdentity);
            var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMddTHHmmssfffZ");
            var path = Path.Combine(root, $"P0-5F-{safeIdentity}-{stamp}-g{associationGeneration}.json");
            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            LastSmartDiscoveryRepeatRunEvidencePath = path;
            return path;
        }
        catch
        {
            // P0-5f evidence is observability only. A local filesystem problem must not
            // turn a successful IEC 61850 discovery into a protocol failure.
            LastSmartDiscoveryRepeatRunEvidencePath = string.Empty;
            return string.Empty;
        }
    }

    private static string ComputeDirectoryModelSignature(ArMms.MmsIedModelDirectory directory)
    {
        var canonical = directory.Points
            .OrderBy(point => point.Domain, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.DataObjectPath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(point => point.MmsItemName, StringComparer.OrdinalIgnoreCase)
            .Select(point => string.Join('|',
                NormalizeSignaturePart(point.Domain),
                NormalizeSignaturePart(point.LogicalNode),
                NormalizeSignaturePart(point.FunctionalConstraint),
                NormalizeSignaturePart(point.DataObjectPath),
                NormalizeSignaturePart(point.MmsItemName)))
            .ToArray();

        return Sha256Lines(canonical);
    }

    private static string ComputeSignalProjectionSignature(IReadOnlyList<SignalDefinition> signals)
    {
        var canonical = signals
            .OrderBy(signal => signal.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.FunctionalConstraint, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.DataType, StringComparer.OrdinalIgnoreCase)
            .ThenBy(signal => signal.Name, StringComparer.OrdinalIgnoreCase)
            .Select(signal => string.Join('|',
                NormalizeSignaturePart(signal.ObjectReference),
                NormalizeSignaturePart(signal.FunctionalConstraint),
                NormalizeSignaturePart(signal.DataType),
                NormalizeSignaturePart(signal.Name),
                NormalizeSignaturePart(signal.Category),
                NormalizeSignaturePart(signal.DataSetReference),
                NormalizeSignaturePart(signal.ReportControlReference),
                NormalizeSignaturePart(signal.QualityReference),
                NormalizeSignaturePart(signal.TimestampReference),
                NormalizeSignaturePart(signal.Source)))
            .ToArray();

        return Sha256Lines(canonical);
    }

    private static string Sha256Lines(IEnumerable<string> lines)
    {
        var text = string.Join('\n', lines);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }

    private static string NormalizeSignaturePart(string? value)
        => (value ?? string.Empty).Trim().Replace('\r', ' ').Replace('\n', ' ').ToLowerInvariant();

    private static string SanitizeEvidenceFileToken(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = value.Trim().Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray();
        return new string(chars);
    }
}
