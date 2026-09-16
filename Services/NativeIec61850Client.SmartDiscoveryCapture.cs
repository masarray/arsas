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
        // Protect one physical MMS association from accidental concurrent discovery
        // (double-click, overlapping runtime requests, or future background consumers).
        // The MMS operation gate additionally prevents report/read workflows from
        // entering a legacy discovery path before the smart authority is published.
        await _smartDiscoveryCaptureGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _mmsIoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await DiscoverSignalsSmartForCaptureCoreAsync(cancellationToken, progress).ConfigureAwait(false);
            }
            finally
            {
                _mmsIoGate.Release();
            }
        }
        finally
        {
            _smartDiscoveryCaptureGate.Release();
        }
    }

    private async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsSmartForCaptureCoreAsync(
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

        var totalWatch = Stopwatch.StartNew();
        try
        {
            // A second discovery request on the same association must be wire-free. The
            // authority marker is reference-bound to _lastDiscovery/_liveModel and also
            // explicitly bound to the current host/port association lifecycle.
            if (TryGetSmartDiscoveryAuthority(out var cachedDiscovery, out var cachedModel))
            {
                progress?.Report(new IedDiscoveryProgress(
                    IedDiscoveryStage.MappingSignals,
                    "Reusing the authoritative smart discovery for this MMS association…",
                    82d, 7, 10));

                var cachedProjectionWatch = Stopwatch.StartNew();
                var cachedSnapshot = ToNativeSnapshot(cachedDiscovery.Snapshot);
                LastReportInventory = ToNativeInventory(cachedDiscovery.ReportInventory);
                var cachedSignals = BuildSmartCaptureSignalProjection(
                    cachedModel,
                    cachedSnapshot,
                    LastReportInventory,
                    out var cachedProjectionStats);
                cachedProjectionWatch.Stop();

                var cachedReportWatch = Stopwatch.StartNew();
                NativeReportDiscoveryMapper.ApplyReportHints(cachedSignals, LastReportInventory);
                cachedReportWatch.Stop();

                progress?.Report(new IedDiscoveryProgress(
                    IedDiscoveryStage.ResolvingIdentity,
                    "Resolving IED identity from the cached canonical live model…",
                    94d, 8, 10));

                var cachedIdentityWatch = Stopwatch.StartNew();
                DetectedIdentity = Iec61850DeviceIdentityResolver.Resolve(cachedDiscovery, cachedModel, cachedSignals);
                cachedIdentityWatch.Stop();
                totalWatch.Stop();

                var cachedLogicalNodes = cachedSignals
                    .Select(signal => signal.LogicalNode)
                    .Where(value => !string.IsNullOrWhiteSpace(value))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .Count();
                var cachedRawVariables = cachedSnapshot.DomainVariables.Values.Sum(values => values.Count);

                LastDiscoverySummary =
                    $"SMART-CAPTURE PR134 R3; association authority=reused; engine single-flight=reused; wire discovery=skipped; " +
                    $"IEDName={(string.IsNullOrWhiteSpace(DetectedIedName) ? "unresolved" : DetectedIedName)} ({DetectedIdentity.Source}); " +
                    $"{cachedDiscovery.Summary} {cachedModel.Summary} LN={cachedLogicalNodes}, SCADA candidates={cachedSignals.Count}, " +
                    $"MMS names={cachedRawVariables}, smart type probes={_smartDiscoveryTypeProbeCount}, successful type probes={_smartDiscoverySuccessfulTypeProbeCount}, " +
                    $"indexed LN hints={cachedProjectionStats.LogicalNodeHints}, indexed fallback signals={cachedProjectionStats.AddedFallbackSignals}. " +
                    $"TimingMs directory=0.0, types=0.0, model=0.0, projection={cachedProjectionWatch.Elapsed.TotalMilliseconds:F1}, " +
                    $"reportHints={cachedReportWatch.Elapsed.TotalMilliseconds:F1}, identity={cachedIdentityWatch.Elapsed.TotalMilliseconds:F1}, total={totalWatch.Elapsed.TotalMilliseconds:F1}. " +
                    "Deferred: supplemental GetNameList, eager report attributes, DataSet directories, reflection fallback, adaptive sibling/equipment/reference/unit probes.";
                LastErrorMessage = LastDiscoverySummary;
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
                "Smart MMS discovery: association single-flight bounded directory scan…",
                28d, 4, 10));

            var directoryWatch = Stopwatch.StartNew();
            // Once this caller owns the application MMS gate, keep that gate until the
            // shared directory flight itself completes. A UI/waiter cancellation must
            // not release the gate while the engine continues the association-scoped
            // discovery in the background.
            var discovery = await _session
                .DiscoverSmartSingleFlightAsync(smartOptions, CancellationToken.None)
                .ConfigureAwait(false);
            directoryWatch.Stop();
            _lastDiscovery = discovery;

            cancellationToken.ThrowIfCancellationRequested();
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ProbingLogicalNodes,
                "Smart MMS type discovery: Logical Node hierarchy probes…",
                52d, 5, 10));

            var typeWatch = Stopwatch.StartNew();
            var variableTypes = await LiveIedVariableTypeProbeExecutor
                .ProbeSmartAsync(_session, discovery.IedDirectory, smartOptions, cancellationToken)
                .ConfigureAwait(false);
            typeWatch.Stop();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.BuildingLiveModel,
                "Building canonical IEC 61850 model from smart discovery evidence…",
                68d, 6, 10));

            var modelWatch = Stopwatch.StartNew();
            _liveModel = LiveIedModelDiscoveryBuilder.Build(
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
            LastReportInventory = ToNativeInventory(discovery.ReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.MappingSignals,
                "Mapping canonical smart evidence with indexed fallbacks…",
                82d, 7, 10));

            var projectionWatch = Stopwatch.StartNew();
            var signals = BuildSmartCaptureSignalProjection(
                _liveModel,
                snapshot,
                LastReportInventory,
                out var projectionStats);
            projectionWatch.Stop();

            // Report hints derived from structural NamedVariable/NamedVariableList evidence
            // remain available. Attribute reads and DataSet-directory reads are deferred.
            var reportWatch = Stopwatch.StartNew();
            NativeReportDiscoveryMapper.ApplyReportHints(signals, LastReportInventory);
            reportWatch.Stop();

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ResolvingIdentity,
                "Resolving IED identity from the canonical live model…",
                94d, 8, 10));

            var identityWatch = Stopwatch.StartNew();
            DetectedIdentity = Iec61850DeviceIdentityResolver.Resolve(discovery, _liveModel, signals);
            identityWatch.Stop();

            var logicalNodes = signals
                .Select(signal => signal.LogicalNode)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var rawVariables = snapshot.DomainVariables.Values.Sum(values => values.Count);
            var successfulTypeRoots = variableTypes.Count(result => result.IsSuccess);

            // Publish only after the complete projection succeeds. If mapping fails, a
            // retry is allowed to repeat wire discovery rather than reusing partial state.
            PublishSmartDiscoveryAuthority(
                discovery,
                _liveModel,
                variableTypes.Count,
                successfulTypeRoots);

            totalWatch.Stop();
            LastDiscoverySummary =
                $"SMART-CAPTURE PR134 R3; association authority=new; engine single-flight=new; app MMS gate=exclusive; " +
                $"IEDName={(string.IsNullOrWhiteSpace(DetectedIedName) ? "unresolved" : DetectedIedName)} ({DetectedIdentity.Source}); " +
                $"{discovery.Summary} {_liveModel.Summary} LN={logicalNodes}, SCADA candidates={signals.Count}, " +
                $"MMS names={rawVariables}, smart type probes={variableTypes.Count}, successful type probes={successfulTypeRoots}, " +
                $"indexed LN hints={projectionStats.LogicalNodeHints}, indexed fallback signals={projectionStats.AddedFallbackSignals}. " +
                $"TimingMs directory={directoryWatch.Elapsed.TotalMilliseconds:F1}, types={typeWatch.Elapsed.TotalMilliseconds:F1}, " +
                $"model={modelWatch.Elapsed.TotalMilliseconds:F1}, projection={projectionWatch.Elapsed.TotalMilliseconds:F1}, " +
                $"reportHints={reportWatch.Elapsed.TotalMilliseconds:F1}, identity={identityWatch.Elapsed.TotalMilliseconds:F1}, total={totalWatch.Elapsed.TotalMilliseconds:F1}. " +
                "Deferred: supplemental GetNameList, eager report attributes, DataSet directories, reflection fallback, adaptive sibling/equipment/reference/unit probes.";
            LastErrorMessage = LastDiscoverySummary;
            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            totalWatch.Stop();
            LastErrorMessage =
                $"ARIEC61850 smart capture discovery failed after {totalWatch.Elapsed.TotalMilliseconds:F1} ms: " +
                $"{ex.GetType().Name}: {ex.Message}. Last discovery: {_session.LastDiscoveryAttemptSummary}. Last request: {_session.LastDiscoveryRequestHex}";
            return Array.Empty<SignalDefinition>();
        }
    }
}
