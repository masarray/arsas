using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    // One caller owns the smart structural discovery for the active association. A
    // concurrent UI/runtime re-entry waits for that work and then reuses the exact
    // authoritative result instead of starting another GetNameList/GVA pass.
    private readonly SemaphoreSlim _smartDiscoveryCaptureGate = new(1, 1);
    private ArMms.MmsDiscoveryResult? _smartDiscoveryAuthority;
    private LiveIedModelDiscoveryDocument? _smartDiscoveryModelAuthority;
    private int _smartDiscoveryTypeProbeCount;
    private int _smartDiscoverySuccessfulTypeProbeCount;

    private bool TryGetSmartDiscoveryAuthority(
        out ArMms.MmsDiscoveryResult discovery,
        out LiveIedModelDiscoveryDocument model)
    {
        discovery = null!;
        model = null!;

        if (_smartDiscoveryAuthority is null ||
            _smartDiscoveryModelAuthority is null ||
            !ReferenceEquals(_lastDiscovery, _smartDiscoveryAuthority) ||
            !ReferenceEquals(_liveModel, _smartDiscoveryModelAuthority))
        {
            return false;
        }

        discovery = _smartDiscoveryAuthority;
        model = _smartDiscoveryModelAuthority;
        return true;
    }

    private void PublishSmartDiscoveryAuthority(
        ArMms.MmsDiscoveryResult discovery,
        LiveIedModelDiscoveryDocument model,
        int typeProbeCount,
        int successfulTypeProbeCount)
    {
        _smartDiscoveryAuthority = discovery;
        _smartDiscoveryModelAuthority = model;
        _smartDiscoveryTypeProbeCount = typeProbeCount;
        _smartDiscoverySuccessfulTypeProbeCount = successfulTypeProbeCount;
    }

    private readonly record struct SmartProjectionStats(
        int LogicalNodeHints,
        int AddedFallbackSignals);

    /// <summary>
    /// Projects the already-finalized canonical model and then adds only cheap indexed
    /// fallback evidence. The legacy compatibility fallback walks the discovery graph
    /// reflectively (up to 50k objects twice) and repeatedly scans the complete signal
    /// list; that work is intentionally excluded from the normal PR134 critical path.
    /// </summary>
    private static List<SignalDefinition> BuildSmartCaptureSignalProjection(
        LiveIedModelDiscoveryDocument model,
        NativeMmsDiscoverySnapshot snapshot,
        NativeReportInventory inventory,
        out SmartProjectionStats stats)
    {
        var signals = BuildSignalsFromArIecModel(model, snapshot).ToList();
        stats = AddSmartIndexedLogicalNodeFallbacks(
            signals,
            snapshot,
            inventory,
            DateTime.Now);

        // BuildSignalsFromArIecModel has already finalized the canonical list once.
        // Only newly-added fallback statuses can need a control candidate. Do not run
        // the full FinalizeDiscoveredSignals grouping/sorting pipeline a second time.
        if (stats.AddedFallbackSignals > 0)
            AddValidatedControlCandidatesFromStatus(signals);

        return signals;
    }

    private static SmartProjectionStats AddSmartIndexedLogicalNodeFallbacks(
        ICollection<SignalDefinition> signals,
        NativeMmsDiscoverySnapshot snapshot,
        NativeReportInventory inventory,
        DateTime now)
    {
        var hints = new Dictionary<string, LogicalNodeHint>(StringComparer.OrdinalIgnoreCase);
        var signalsByLogicalNode = new Dictionary<string, List<SignalDefinition>>(StringComparer.OrdinalIgnoreCase);
        var logicalNodesWithCoreSignals = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        static string LogicalNodeKey(string domain, string logicalNode)
            => string.Concat(domain, "\u001F", logicalNode);

        void IndexSignal(SignalDefinition signal)
        {
            var normalizedReference = NormalizeReference(signal.ObjectReference);
            if (!string.IsNullOrWhiteSpace(normalizedReference))
                references.Add(normalizedReference);

            var domain = ExtractDomain(signal.ObjectReference);
            var logicalNode = signal.LogicalNode?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode))
                return;

            var key = LogicalNodeKey(domain, logicalNode);
            if (!signalsByLogicalNode.TryGetValue(key, out var bucket))
            {
                bucket = new List<SignalDefinition>();
                signalsByLogicalNode[key] = bucket;
            }
            bucket.Add(signal);
            if (signal.IsScadaCoreSignal)
                logicalNodesWithCoreSignals.Add(key);
        }

        void AddHint(string domain, string logicalNode, string source)
        {
            domain = (domain ?? string.Empty).Trim().Replace('$', '.');
            logicalNode = (logicalNode ?? string.Empty).Trim().Replace('$', '.');
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode))
                return;

            var logicalNodeClass = SignalDefinition.DetectLogicalNodeClass(logicalNode).ToUpperInvariant();
            if (!IsScadaLogicalNodeClassForFallback(logicalNodeClass))
                return;

            var key = LogicalNodeKey(domain, logicalNode);
            if (!hints.ContainsKey(key))
                hints[key] = new LogicalNodeHint(domain, logicalNode, logicalNodeClass, source);
        }

        foreach (var signal in signals)
        {
            IndexSignal(signal);
            var domain = ExtractDomain(signal.ObjectReference);
            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(signal.LogicalNode))
                AddHint(domain, signal.LogicalNode, "canonical signal inventory");
        }

        foreach (var domainPair in snapshot.DomainVariables)
        {
            var domain = domainPair.Key ?? string.Empty;
            foreach (var rawName in domainPair.Value)
            {
                foreach (var hint in ExtractLogicalNodeHintsFromText(rawName, domain, "MMS NamedVariable"))
                    AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            }
        }

        foreach (var domainPair in snapshot.DomainVariableLists)
        {
            var domain = domainPair.Key ?? string.Empty;
            foreach (var rawName in domainPair.Value)
            {
                foreach (var hint in ExtractLogicalNodeHintsFromText(rawName, domain, "MMS NamedVariableList/DataSet"))
                    AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            }
        }

        foreach (var dataSet in inventory.DataSets)
        {
            AddHint(dataSet.Domain, dataSet.LogicalNode, "Report inventory DataSet");
            foreach (var hint in ExtractLogicalNodeHintsFromText(dataSet.Reference, dataSet.Domain, "Report inventory DataSet reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            foreach (var hint in ExtractLogicalNodeHintsFromText(dataSet.RawMmsName, dataSet.Domain, "Report inventory DataSet raw name"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        foreach (var reportControl in inventory.ReportControls)
        {
            AddHint(reportControl.Domain, reportControl.LogicalNode, "Report inventory RCB");
            foreach (var hint in ExtractLogicalNodeHintsFromText(reportControl.Reference, reportControl.Domain, "Report inventory RCB reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            foreach (var hint in ExtractLogicalNodeHintsFromText(reportControl.DataSetReference, reportControl.Domain, "Report inventory RCB DataSet reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        var added = 0;
        foreach (var hint in hints.Values
                     .OrderBy(item => item.Domain, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(item => item.LogicalNode, StringComparer.OrdinalIgnoreCase))
        {
            var key = LogicalNodeKey(hint.Domain, hint.LogicalNode);
            if (logicalNodesWithCoreSignals.Contains(key))
                continue;

            var existingForLogicalNode = signalsByLogicalNode.TryGetValue(key, out var bucket)
                ? bucket.ToArray()
                : Array.Empty<SignalDefinition>();

            foreach (var point in BuildArIecLogicalNodeFallbackPoints(hint.LogicalNodeClass))
            {
                if (hint.LogicalNodeClass is "MMXU" or "MMXN" &&
                    existingForLogicalNode.Length > 0 &&
                    !existingForLogicalNode.Any(signal => HasDataObjectPath(signal.ObjectReference, point.DataObject)))
                {
                    continue;
                }

                var reference = $"{hint.Domain}/{hint.LogicalNode}.{point.Path}";
                if (!references.Add(NormalizeReference(reference)))
                    continue;

                var signal = CreateArIecSignal(
                    reference,
                    point.FunctionalConstraint,
                    point.Category,
                    hint.LogicalNodeClass,
                    point.DataObject,
                    string.Empty,
                    now,
                    $"Indexed smart discovery fallback ({hint.Source})");
                signals.Add(signal);
                added++;
            }
        }

        return new SmartProjectionStats(hints.Count, added);
    }
}
