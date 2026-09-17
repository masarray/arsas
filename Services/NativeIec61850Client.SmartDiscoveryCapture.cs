using System.Diagnostics;
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
        cancellationToken.ThrowIfCancellationRequested();

        // P0-5c: one owner performs the complete enrichment chain for one association
        // generation. Every concurrent caller receives the same in-flight task. Caller
        // cancellation only stops that caller waiting; it never cancels the shared MMS
        // owner and therefore cannot cause a second GVA ladder on the same association.
        var flight = GetOrCreateSmartDiscoveryAssociationFlight(
            generation => RunSmartDiscoveryAssociationFlightAsync(generation, progress));

        return await flight.WaitAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<IReadOnlyList<SignalDefinition>> RunSmartDiscoveryAssociationFlightAsync(
        long associationGeneration,
        IProgress<IedDiscoveryProgress>? progress)
    {
        // The complete directory -> GVA -> canonical model -> projection -> publish
        // sequence owns the application MMS gate. Waiter cancellation is deliberately
        // absent here: only association generation invalidation can make this owner stale.
        await _mmsIoGate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
        try
        {
            if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                return Array.Empty<SignalDefinition>();

            return await DiscoverSignalsSmartForCaptureCoreAsync(
                    associationGeneration,
                    progress)
                .ConfigureAwait(false);
        }
        finally
        {
            _mmsIoGate.Release();
        }
    }

    private async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsSmartForCaptureCoreAsync(
        long associationGeneration,
        IProgress<IedDiscoveryProgress>? progress)
    {
        if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
            return Array.Empty<SignalDefinition>();

        LastDiscoverySummary = string.Empty;
        if (!_session.IsMmsInitiated)
        {
            if (IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
            {
                LastErrorMessage = $"ARIEC61850 smart discovery requires ACSE/MMS association. Current state: {_session.State}. {_session.LastAssociationAttemptSummary}";
            }
            return Array.Empty<SignalDefinition>();
        }

        var totalWatch = Stopwatch.StartNew();
        try
        {
            // A completed discovery on this exact association generation is wire-free.
            // Concurrent callers do not reach this branch independently because they
            // already share the same association flight above. P0-5f deliberately does
            // not emit a new repeat-run evidence file from this cached branch: a repeat
            // physical run requires a new association generation and fresh wire traffic.
            if (TryGetSmartDiscoveryAuthority(out var cachedDiscovery, out var cachedModel))
            {
                progress?.Report(new IedDiscoveryProgress(
                    IedDiscoveryStage.MappingSignals,
                    "Reusing the authoritative smart discovery for this MMS association…",
                    82d, 7, 10));

                var cachedProjectionWatch = Stopwatch.StartNew();
                var cachedSnapshot = ToNativeSnapshot(cachedDiscovery.Snapshot);
                var cachedInventory = ToNativeInventory(cachedDiscovery.ReportInventory);
                var cachedSignals = BuildSmartCaptureSignalProjection(
                    cachedModel,
                    cachedSnapshot,
                    cachedInventory,
                    out var cachedProjectionStats);
                cachedProjectionWatch.Stop();

                var cachedReportWatch = Stopwatch.StartNew();
                NativeReportDiscoveryMapper.ApplyReportHints(cachedSignals, cachedInventory);
                cachedReportWatch.Stop();

                progress?.Report(new IedDiscoveryProgress(
                    IedDiscoveryStage.ResolvingIdentity,
                    "Resolving IED identity from the cached canonical live model…",
                    94d, 8, 10));

                var cachedIdentityWatch = Stopwatch.StartNew();
                var cachedIdentity = Iec61850DeviceIdentityResolver.Resolve(
                    cachedDiscovery,
                    cachedModel,
                    cachedSignals);
                cachedIdentityWatch.Stop();
                totalWatch.Stop();

                if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                    return Array.Empty<SignalDefinition>();

                var cachedLogicalNodes = cachedSignals
                    .Select(signal => signal.LogicalNode)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var cachedRawVariables = cachedSnapshot.DomainVariables.Values.Sum(values => values.Count);
                var cachedBudget = _session.LastSmartTypeProbeBudget?.Summary ?? "Smart type budget unavailable.";
                var cachedSummary =
                    $"SMART-CAPTURE PR134 P0-5c; association authority=reused; association flight=new-wire-free; control inventory=authoritative; wire discovery=skipped; " +
                    $"IEDName={(string.IsNullOrWhiteSpace(cachedIdentity.IedName) ? "unresolved" : cachedIdentity.IedName)} ({cachedIdentity.Source}); " +
                    $"{cachedDiscovery.Summary} {cachedModel.Summary} LN={cachedLogicalNodes}, SCADA candidates={cachedSignals.Count}, " +
                    $"MMS names={cachedRawVariables}, smart type probes={_smartDiscoveryTypeProbeCount}, successful type probes={_smartDiscoverySuccessfulTypeProbeCount}, " +
                    $"indexed LN hints={cachedProjectionStats.LogicalNodeHints}, indexed fallback signals={cachedProjectionStats.AddedFallbackSignals}. " +
                    $"{cachedBudget} " +
                    $"TimingMs directory=0.0, types=0.0, model=0.0, projection={cachedProjectionWatch.Elapsed.TotalMilliseconds:F1}, " +
                    $"reportHints={cachedReportWatch.Elapsed.TotalMilliseconds:F1}, identity={cachedIdentityWatch.Elapsed.TotalMilliseconds:F1}, total={totalWatch.Elapsed.TotalMilliseconds:F1}. " +
                    "Deferred: supplemental GetNameList, eager report attributes, DataSet directories, reflection fallback, adaptive sibling/equipment/reference/unit probes.";

                if (!TryPublishSmartDiscoveryPresentation(
                        associationGeneration,
                        cachedInventory,
                        cachedIdentity,
                        cachedSummary))
                {
                    return Array.Empty<SignalDefinition>();
                }

                return cachedSignals;
            }

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
                "Smart MMS discovery: association-generation single-flight bounded directory scan…",
                28d, 4, 10));

            var directoryWatch = Stopwatch.StartNew();
            var discovery = await _session
                .DiscoverSmartSingleFlightAsync(smartOptions, CancellationToken.None)
                .ConfigureAwait(false);
            directoryWatch.Stop();

            // Reconnect/dispose may invalidate the generation while the current PDU is
            // in flight. Stop at the boundary before issuing any GVA request.
            if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                return Array.Empty<SignalDefinition>();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ProbingLogicalNodes,
                "Smart MMS type discovery: coverage-aware Logical Node hierarchy probes…",
                52d, 5, 10));

            var typeWatch = Stopwatch.StartNew();
            var variableTypes = await LiveIedVariableTypeProbeExecutor
                .ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, CancellationToken.None)
                .ConfigureAwait(false);
            typeWatch.Stop();

            // A stale owner may finish an already-issued GVA batch, but it cannot build
            // or publish state into the replacement association generation.
            if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                return Array.Empty<SignalDefinition>();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.BuildingLiveModel,
                "Building canonical IEC 61850 model from smart discovery evidence…",
                68d, 6, 10));

            var modelWatch = Stopwatch.StartNew();
            var liveModel = LiveIedModelDiscoveryBuilder.Build(
                discovery,
                new LiveIedModelDiscoveryBuildOptions
                {
                    Host = _host,
                    Port = _port,
                    IncludeLowConfidenceTemplates = true
                },
                variableTypeAttributes: variableTypes);
            modelWatch.Stop();

            var snapshot = ToNativeSnapshot(discovery.Snapshot);
            var reportInventory = ToNativeInventory(discovery.ReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.MappingSignals,
                "Mapping canonical smart evidence with indexed fallbacks…",
                82d, 7, 10));

            var projectionWatch = Stopwatch.StartNew();
            var signals = BuildSmartCaptureSignalProjection(
                liveModel,
                snapshot,
                reportInventory,
                out var projectionStats);
            projectionWatch.Stop();

            // Report hints derived from structural NamedVariable/NamedVariableList evidence
            // remain available. Attribute reads and DataSet-directory reads are deferred.
            var reportWatch = Stopwatch.StartNew();
            NativeReportDiscoveryMapper.ApplyReportHints(signals, reportInventory);
            reportWatch.Stop();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ResolvingIdentity,
                "Resolving IED identity from the canonical live model…",
                94d, 8, 10));

            var identityWatch = Stopwatch.StartNew();
            var identity = Iec61850DeviceIdentityResolver.Resolve(discovery, liveModel, signals);
            identityWatch.Stop();

            if (!IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                return Array.Empty<SignalDefinition>();

            var logicalNodes = signals
                .Select(signal => signal.LogicalNode)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var rawVariables = snapshot.DomainVariables.Values.Sum(values => values.Count);
            var successfulTypeRoots = variableTypes.Count(result => result.IsSuccess);
            var typeBudget = _session.LastSmartTypeProbeBudget?.Summary ?? "Smart type budget unavailable.";

            totalWatch.Stop();
            var summary =
                $"SMART-CAPTURE PR134 P0-5c; association authority=new; association flight=single-owner; app MMS gate=exclusive; control inventory=authoritative; " +
                $"IEDName={(string.IsNullOrWhiteSpace(identity.IedName) ? "unresolved" : identity.IedName)} ({identity.Source}); " +
                $"{discovery.Summary} {liveModel.Summary} LN={logicalNodes}, SCADA candidates={signals.Count}, " +
                $"MMS names={rawVariables}, smart type probes={variableTypes.Count}, successful type probes={successfulTypeRoots}, " +
                $"indexed LN hints={projectionStats.LogicalNodeHints}, indexed fallback signals={projectionStats.AddedFallbackSignals}. " +
                $"{typeBudget} " +
                $"TimingMs directory={directoryWatch.Elapsed.TotalMilliseconds:F1}, types={typeWatch.Elapsed.TotalMilliseconds:F1}, " +
                $"model={modelWatch.Elapsed.TotalMilliseconds:F1}, projection={projectionWatch.Elapsed.TotalMilliseconds:F1}, " +
                $"reportHints={reportWatch.Elapsed.TotalMilliseconds:F1}, identity={identityWatch.Elapsed.TotalMilliseconds:F1}, total={totalWatch.Elapsed.TotalMilliseconds:F1}. " +
                "Deferred: supplemental GetNameList, eager report attributes, DataSet directories, reflection fallback, adaptive sibling/equipment/reference/unit probes.";

            // The generation check and state publication are atomic with Reset. A stale
            // owner can never write _lastDiscovery/_liveModel/identity into a new session.
            if (!TryPublishSmartDiscoveryAuthority(
                    associationGeneration,
                    discovery,
                    liveModel,
                    reportInventory,
                    identity,
                    variableTypes.Count,
                    successfulTypeRoots,
                    summary))
            {
                return Array.Empty<SignalDefinition>();
            }

            // P0-5f: emit one local, zero-traffic evidence snapshot only after a fresh
            // association owner has successfully published authority. Cached reuse above
            // deliberately never reaches this call and therefore cannot masquerade as an
            // independent physical repeat run.
            if (IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
            {
                var repeatEvidencePath = TryWriteSmartDiscoveryRepeatRunEvidence(
                    associationGeneration,
                    discovery,
                    signals,
                    identity);
                var repeatKpi = _session.LastSmartDiscoveryKpi;
                if (!string.IsNullOrWhiteSpace(repeatEvidencePath) &&
                    IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
                {
                    LastDiscoverySummary +=
                        $" P0-5f repeatEvidence={repeatEvidencePath}; " +
                        $"kpiSignature={repeatKpi?.DeterministicSignature ?? "unavailable"}.";
                }
            }

            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            totalWatch.Stop();
            if (IsCurrentSmartDiscoveryAssociationGeneration(associationGeneration))
            {
                LastErrorMessage =
                    $"ARIEC61850 smart capture discovery failed after {totalWatch.Elapsed.TotalMilliseconds:F1} ms: " +
                    $"{ex.GetType().Name}: {ex.Message}. Last discovery: {_session.LastDiscoveryAttemptSummary}. Last request: {_session.LastDiscoveryRequestHex}";
            }
            return Array.Empty<SignalDefinition>();
        }
    }
}
