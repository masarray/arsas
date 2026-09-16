using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    // This path is intentionally isolated to the PR #134 Wireshark comparison build.
    // It keeps live MMS evidence authoritative while removing ARSAS's historical
    // supplemental GetNameList/read/probe passes from the discovery critical path.
    private static bool SmartDiscoveryCaptureModeEnabled => true;

    private async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsSmartForCaptureAsync(
        CancellationToken cancellationToken,
        IProgress<IedDiscoveryProgress>? progress)
    {
        LastDiscoverySummary = string.Empty;
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.IsMmsInitiated)
        {
            LastErrorMessage = $"ARIEC61850 smart discovery requires ACSE/MMS association. Current state: {_session.State}. {_session.LastAssociationAttemptSummary}";
            return Array.Empty<SignalDefinition>();
        }

        try
        {
            var smartOptions = new ArMms.MmsSmartDiscoveryOptions
            {
                MaxConcurrentChains = 8,
                UnknownPeerMaxConcurrentChains = 4,
                MaxDomains = 256,
                MaxVariableNamesPerDomain = 20000,
                MaxVariableListNamesPerDomain = 4096,
                MaxNameListPages = 64,
                ProbeReportAttributes = false,
                ReadDataSetDirectories = false
            };

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.DiscoveringDirectory,
                "Smart MMS discovery: bounded parallel directory scan…",
                28d, 4, 10));

            var discovery = await _session
                .DiscoverSmartAsync(smartOptions, cancellationToken)
                .ConfigureAwait(false);
            _lastDiscovery = discovery;

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ProbingLogicalNodes,
                "Smart MMS type discovery: Logical Node hierarchy probes…",
                52d, 5, 10));

            var variableTypes = await LiveIedVariableTypeProbeExecutor
                .ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, cancellationToken)
                .ConfigureAwait(false);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.BuildingLiveModel,
                "Building canonical IEC 61850 model from smart discovery evidence…",
                68d, 6, 10));

            _liveModel = LiveIedModelDiscoveryBuilder.Build(
                discovery,
                new LiveIedModelDiscoveryBuildOptions
                {
                    Host = _host,
                    Port = _port,
                    IncludeLowConfidenceTemplates = true
                },
                variableTypeAttributes: variableTypes);

            var snapshot = ToNativeSnapshot(discovery.Snapshot);
            LastReportInventory = ToNativeInventory(discovery.ReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.MappingSignals,
                "Mapping smart structural model to the ARSAS signal workspace…",
                82d, 7, 10));

            var signals = BuildSignalsFromArIecModel(_liveModel, snapshot).ToList();
            AddGenericLogicalNodeFallbacksFromDiscoveryArtifacts(
                signals,
                discovery,
                snapshot,
                LastReportInventory,
                DateTime.Now);
            signals = FinalizeDiscoveredSignals(signals).ToList();

            // Report hints derived from structural NamedVariable/NamedVariableList evidence
            // remain available. Attribute reads and DataSet-directory reads are deferred.
            NativeReportDiscoveryMapper.ApplyReportHints(signals, LastReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ResolvingIdentity,
                "Resolving IED identity from the canonical live model…",
                94d, 8, 10));

            DetectedIdentity = Iec61850DeviceIdentityResolver.Resolve(discovery, _liveModel, signals);

            var logicalNodes = signals
                .Select(signal => signal.LogicalNode)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var rawVariables = snapshot.DomainVariables.Values.Sum(values => values.Count);
            var successfulTypeRoots = variableTypes.Count(result => result.IsSuccess);

            LastDiscoverySummary =
                $"SMART-CAPTURE PR134; IEDName={(string.IsNullOrWhiteSpace(DetectedIedName) ? "unresolved" : DetectedIedName)} ({DetectedIdentity.Source}); " +
                $"{discovery.Summary} {_liveModel.Summary} LN={logicalNodes}, SCADA candidates={signals.Count}, " +
                $"MMS names={rawVariables}, smart type probes={variableTypes.Count}, successful type probes={successfulTypeRoots}. " +
                "Deferred in this capture build: supplemental GetNameList, eager report attributes, DataSet directories, adaptive sibling probes, primary-equipment proof probes, per-signal operational-reference probes, and engineering-unit reads.";
            LastErrorMessage = LastDiscoverySummary;
            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastErrorMessage = $"ARIEC61850 smart capture discovery failed: {ex.GetType().Name}: {ex.Message}. Last discovery: {_session.LastDiscoveryAttemptSummary}. Last request: {_session.LastDiscoveryRequestHex}";
            return Array.Empty<SignalDefinition>();
        }
    }
}
