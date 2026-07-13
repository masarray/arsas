using System;
using System.Collections.Generic;
using System.Globalization;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;
using ArControl = AR.Iec61850.Control;

namespace ArIED61850Tester.Services;

/// <summary>
/// Native IEC 61850 MMS client backed by the ARIEC61850 engine.
/// </summary>
public sealed class NativeIec61850Client : IIec61850Client, IIec61850ControlClient
{
    private readonly ArMms.MmsClientSession _session = new();
    private readonly SemaphoreSlim _mmsIoGate = new(1, 1);
    private ArMms.MmsDiscoveryResult? _lastDiscovery;
    private LiveIedModelDiscoveryDocument? _liveModel;
    private readonly Dictionary<string, ArMms.MmsPersistentReportMonitorSession> _reportMonitorSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, IReadOnlyList<string>> _reportMonitorCoverage = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ArControl.Iec61850ControlObjectSession> _controlSessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _controlSessionGate = new(1, 1);
    private readonly SemaphoreSlim _controlCommandGate = new(1, 1);
    private string _host = string.Empty;
    private int _port = 102;
    private int _engineCompatibilityWarningIssued;

    public bool IsConnected => _session.IsMmsInitiated;
    public bool IsTransportReady => _session.IsTransportConnected;
    public bool IsMmsReady => _session.IsMmsInitiated;
    public bool IsMmsInitiateFailed => _session.State == ArMms.MmsAssociationState.MmsInitiateFailed;
    public string NativeState => _session.State.ToString();
    public string ConnectionMode => "ARIEC61850 native MMS";
    public string LastErrorMessage { get; private set; } = string.Empty;
    public string LastConnectionFailureKind { get; private set; } = string.Empty;
    public string LastConnectionTechnicalSummary { get; private set; } = string.Empty;
    public string LastAssociationResponseHex => _session.LastAssociationResponseHex;
    public string LastAssociationAttemptSummary => _session.LastAssociationAttemptSummary;
    public string LastReadRequestHex => _session.LastReadRequestHex;
    public string LastReadResponseHex => _session.LastReadResponseHex;
    public string LastReadAttemptSummary => _session.LastReadAttemptSummary;
    public string LastDiscoveryRequestHex => _session.LastDiscoveryRequestHex;
    public string LastDiscoveryResponseHex => _session.LastDiscoveryResponseHex;
    public string LastDiscoverySummary { get; private set; } = string.Empty;
    public NativeReportInventory LastReportInventory { get; private set; } = new();
    public Iec61850DeviceIdentity DetectedIdentity { get; private set; } = new();
    public string DetectedIedName => DetectedIdentity.IedName;

    public async Task ConnectAsync(string ipAddress, int port, CancellationToken cancellationToken)
    {
        await DisposeControlSessionsAsync().ConfigureAwait(false);
        LastErrorMessage = string.Empty;
        LastConnectionFailureKind = string.Empty;
        LastConnectionTechnicalSummary = string.Empty;
        _lastDiscovery = null;
        _liveModel = null;
        _reportMonitorSessions.Clear();
        _reportMonitorCoverage.Clear();
        Interlocked.Exchange(ref _engineCompatibilityWarningIssued, 0);
        DetectedIdentity = new Iec61850DeviceIdentity();
        _host = ipAddress?.Trim() ?? string.Empty;
        _port = port <= 0 ? 102 : port;

        try
        {
            await RunMmsOperationAsync(
                () => _session.ConnectAsync(
                    _host,
                    _port,
                    TimeSpan.FromSeconds(8),
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            LastErrorMessage = string.IsNullOrWhiteSpace(_session.LastAssociationAttemptSummary)
                ? _session.LastHandshakeMessage
                : _session.LastAssociationAttemptSummary;
            LastConnectionFailureKind = string.Empty;
            LastConnectionTechnicalSummary = string.Empty;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            var failure = Iec61850ConnectionFailureClassifier.Classify(
                _host,
                _port,
                ex,
                _session.LastAssociationAttemptSummary);
            LastConnectionFailureKind = failure.Kind;
            LastConnectionTechnicalSummary = failure.TechnicalSummary;
            LastErrorMessage = failure.FriendlyMessage;
            await _session.DisposeAsync().ConfigureAwait(false);
        }
    }

    public Iec61850DeviceDiagnosticSnapshot CaptureDiagnosticSnapshot(
        string phase,
        Exception? exception = null)
        => new()
        {
            CapturedAt = DateTimeOffset.Now,
            Phase = phase ?? string.Empty,
            FailureKind = LastConnectionFailureKind,
            FriendlyMessage = LastErrorMessage,
            ExceptionType = exception?.GetType().FullName ?? string.Empty,
            ExceptionMessage = exception?.Message ?? string.Empty,
            ConnectionMode = ConnectionMode,
            NativeState = NativeState,
            TransportReady = IsTransportReady,
            MmsReady = IsMmsReady,
            AssociationAttemptSummary = string.IsNullOrWhiteSpace(LastConnectionTechnicalSummary)
                ? LastAssociationAttemptSummary
                : $"{LastConnectionTechnicalSummary} | Raw={LastAssociationAttemptSummary}",
            AssociationResponseHex = LastAssociationResponseHex,
            DiscoverySummary = LastDiscoverySummary,
            DiscoveryRequestHex = LastDiscoveryRequestHex,
            DiscoveryResponseHex = LastDiscoveryResponseHex
        };

    public async Task<IReadOnlyList<SignalDefinition>> DiscoverSignalsAsync(CancellationToken cancellationToken, IProgress<IedDiscoveryProgress>? progress = null)
    {
        LastDiscoverySummary = string.Empty;
        cancellationToken.ThrowIfCancellationRequested();
        progress?.Report(new IedDiscoveryProgress(
            IedDiscoveryStage.DiscoveringDirectory,
            "Reading MMS directory, DataSets, and Report Control Blocks…",
            24d, 4, 15));

        if (!_session.IsMmsInitiated)
        {
            LastErrorMessage = $"ARIEC61850 online discovery requires ACSE/MMS association. Current state: {_session.State}. {_session.LastAssociationAttemptSummary}";
            return Array.Empty<SignalDefinition>();
        }

        try
        {
            var discovery = await RunMmsOperationAsync(
                () => _session.DiscoverAsync(
                    probeReportAttributes: true,
                    maxReportAttributeProbes: 64,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            _lastDiscovery = discovery;
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.BuildingLiveModel,
                "Building the live IEC 61850 data model…",
                42d, 5, 15));
            _liveModel = LiveIedModelDiscoveryBuilder.Build(discovery, new LiveIedModelDiscoveryBuildOptions
            {
                Host = _host,
                Port = _port,
                IncludeLowConfidenceTemplates = true
            });

            var primarySnapshot = ToNativeSnapshot(discovery.Snapshot);
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.BrowsingSupplementalNames,
                "Browsing supplemental MMS names and type information…",
                50d, 6, 15));
            var supplementalSnapshot = await TryBuildSupplementalGetNameListSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var snapshot = MergeDiscoverySnapshots(primarySnapshot, supplementalSnapshot);
            LastReportInventory = ToNativeInventory(discovery.ReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.MappingSignals,
                "Mapping readable ST/MX values, quality, and IED timestamps…",
                58d, 7, 15));
            var signals = BuildSignalsFromArIecModel(_liveModel, snapshot).ToList();
            AddGenericLogicalNodeFallbacksFromDiscoveryArtifacts(
                signals,
                discovery,
                snapshot,
                LastReportInventory,
                DateTime.Now);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ProbingLogicalNodes,
                $"Probing vendor-specific Logical Node siblings across {signals.Count:N0} candidates…",
                67d, 8, 15));
            var adaptiveSiblingProbeCount = await AddAdaptiveLogicalNodeSiblingProbeSignalsAsync(
                signals,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ProbingPrimaryEquipment,
                "Checking primary-equipment and protection domains…",
                74d, 9, 15));
            var primaryEquipmentProbeCount = await AddPrimaryEquipmentDomainProbeSignalsAsync(
                signals,
                snapshot,
                cancellationToken).ConfigureAwait(false);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ResolvingOperationalReferences,
                "Resolving effective operational value references…",
                81d, 10, 15));
            var operationalValueReplacementCount = await ResolveOperationalValueReferencesAsync(
                signals,
                cancellationToken).ConfigureAwait(false);

            signals = FinalizeDiscoveredSignals(signals).ToList();
            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.EnrichingEngineeringUnits,
                $"Reading engineering units for {signals.Count:N0} signal candidates…",
                88d, 11, 15));
            var engineeringUnitCount = await EnrichEngineeringUnitsAsync(signals, cancellationToken).ConfigureAwait(false);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.AnalyzingReporting,
                $"Analyzing {LastReportInventory.ReportControls.Count:N0} discovered Report Control Blocks…",
                93d, 12, 15));
            NativeReportDiscoveryMapper.ApplyReportHints(signals, LastReportInventory);

            progress?.Report(new IedDiscoveryProgress(
                IedDiscoveryStage.ResolvingIdentity,
                "Resolving the real IEDName and Logical Device boundaries…",
                96d, 13, 15));
            DetectedIdentity = Iec61850DeviceIdentityResolver.Resolve(discovery, _liveModel, signals);

            var discoveredLogicalNodes = signals
                .Select(s => s.LogicalNode)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            var primaryVariableCount = primarySnapshot.DomainVariables.Values.Sum(v => v.Count);
            var supplementalVariableCount = supplementalSnapshot.DomainVariables.Values.Sum(v => v.Count);
            LastDiscoverySummary = $"IEDName={(string.IsNullOrWhiteSpace(DetectedIedName) ? "unresolved" : DetectedIedName)} ({DetectedIdentity.Source}); {discovery.Summary} {_liveModel.Summary} LN={discoveredLogicalNodes}, SCADA candidates={signals.Count}, MMS names={primaryVariableCount}, supplemental names={supplementalVariableCount}, adaptive sibling probes={adaptiveSiblingProbeCount}, primary-equipment probes={primaryEquipmentProbeCount}, operational references corrected={operationalValueReplacementCount}, engineering units resolved={engineeringUnitCount}. Engine=ARIEC61850 live model + full GetNameList directory + VAA type-tree + adaptive LN sibling proof-probe + IEC unit metadata.";
            LastErrorMessage = LastDiscoverySummary;
            return signals;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastErrorMessage = $"ARIEC61850 online discovery failed: {ex.GetType().Name}: {ex.Message}. Last discovery: {_session.LastDiscoveryAttemptSummary}. Last request: {_session.LastDiscoveryRequestHex}";
            return Array.Empty<SignalDefinition>();
        }
    }

    public async Task ProbeReportControlAsync(NativeReportControlCandidate rcb, CancellationToken cancellationToken)
    {
        if (rcb == null) throw new ArgumentNullException(nameof(rcb));
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.IsMmsInitiated)
        {
            rcb.Status = $"Probe blocked: ACSE/MMS not associated ({_session.State})";
            LastErrorMessage = rcb.Status;
            return;
        }

        rcb.Status = "ARIEC61850 read-only attribute probe running";
        await TryReadReportAttributeAsync(rcb, "DatSet", value =>
        {
            var text = NormalizeReportAttributeText(value);
            if (!string.IsNullOrWhiteSpace(text))
                rcb.DataSetReference = NormalizeReportedDataSetReference(rcb.Domain, text);
        }, cancellationToken).ConfigureAwait(false);

        await TryReadReportAttributeAsync(rcb, "RptID", value => rcb.ReportId = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "ConfRev", value => rcb.ConfRev = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "IntgPd", value => rcb.IntegrityPeriodMs = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "RptEna", value => rcb.EnabledState = NormalizeReportAttributeText(value), cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "BufTm", _ => { }, cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "TrgOps", _ => { }, cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, "OptFlds", _ => { }, cancellationToken).ConfigureAwait(false);
        await TryReadReportAttributeAsync(rcb, rcb.Buffered ? "ResvTms" : "Resv", _ => { }, cancellationToken).ConfigureAwait(false);

        rcb.Status = string.IsNullOrWhiteSpace(rcb.DataSetReference)
            ? "Probed: DataSet not returned"
            : "Probed read-only";
        LastErrorMessage = rcb.Status;
    }

    public async Task<NativeReportMonitorStartResult> StartReportMonitorAsync(ReportControlPlan plan, CancellationToken cancellationToken)
    {
        if (plan == null) throw new ArgumentNullException(nameof(plan));
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.IsMmsInitiated)
        {
            LastErrorMessage = $"ARIEC61850 report monitor requires ACSE/MMS association. Current state: {_session.State}.";
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage
            };
        }

        if (_reportMonitorSessions.ContainsKey(plan.PlanId))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = true,
                PlanId = plan.PlanId,
                Message = $"Report monitor already active for {plan.DisplayReference}.",
                ReportControlReference = plan.ReportControlReference,
                DataSetReference = plan.DataSetReference,
                AcquisitionLabel = BuildAcquisitionLabel(
                    plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase),
                    plan.Buffered,
                    plan.ReportControlReference),
                CoveredReferences = _reportMonitorCoverage.TryGetValue(plan.PlanId, out var existingCoverage)
                    ? existingCoverage
                    : Array.Empty<string>()
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery == null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage
            };
        }

        var inventory = BuildEngineReportInventory(discovery.ReportInventory, plan);
        var directory = discovery.IedDirectory;
        var dataSetDirectories = await ReadPlannedDataSetDirectoriesAsync(plan, directory, cancellationToken).ConfigureAwait(false);

        var forceDynamicPlan = ShouldForceDynamicReportPlan(plan);
        var subscription = forceDynamicPlan
            ? ArMms.MmsReportSubscriptionPlanner.BuildDynamicPlan(
                inventory,
                directory,
                plan.Bindings.Select(b => b.IecReference),
                preferredLogicalDevice: ResolvePreferredLogicalDevice(plan),
                preferredRcbReference: plan.ReportControlReference,
                dataSetName: BuildDynamicDataSetName(plan),
                strictRcb: false,
                allowUrCbFallback: true,
                allowPollingFallback: true)
            : ArMms.MmsReportSubscriptionPlanner.BuildStaticPlan(
                inventory,
                dataSetDirectories,
                preferredRcbReference: plan.ReportControlReference,
                preferredDataSetReference: plan.DataSetReference,
                strictRcb: !string.IsNullOrWhiteSpace(plan.ReportControlReference),
                allowUrCbFallback: true,
                allowPollingFallback: true);

        var dynamicDataSetPlanned = forceDynamicPlan && subscription.IsReady;
        if (!forceDynamicPlan && !subscription.IsReady && IsDynamicReportWriteAllowed(plan))
        {
            var dynamicPlan = ArMms.MmsReportSubscriptionPlanner.BuildDynamicPlan(
                inventory,
                directory,
                plan.Bindings.Select(b => b.IecReference),
                preferredLogicalDevice: ResolvePreferredLogicalDevice(plan),
                preferredRcbReference: plan.ReportControlReference,
                dataSetName: BuildDynamicDataSetName(plan),
                strictRcb: false,
                allowUrCbFallback: true,
                allowPollingFallback: true);

            if (dynamicPlan.IsReady)
            {
                subscription = dynamicPlan;
                dynamicDataSetPlanned = true;
            }
        }

        if (!subscription.IsReady)
        {
            var blockers = subscription.Blockers.Count == 0 ? "no detailed blocker returned" : string.Join("; ", subscription.Blockers.Take(4));
            LastErrorMessage = $"ARIEC61850 report subscription plan blocked for {plan.DisplayReference}: {blockers}";
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage,
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                Warnings = subscription.Warnings.Concat(subscription.Blockers).ToArray()
            };
        }

        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);

        var start = await RunMmsOperationAsync(
            () => _session.StartPersistentReportMonitorAsync(
                subscription,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: dynamicDataSetPlanned,
                directory,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (!start.IsSuccess || start.Session == null)
        {
            LastErrorMessage = $"ARIEC61850 persistent report monitor failed for {plan.DisplayReference}: {start.Message}";
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage,
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = dynamicDataSetPlanned,
                CoveredReferences = coveredReferences,
                Warnings = start.Warnings.Concat(subscription.Warnings).ToArray()
            };
        }

        var activeRcbReference = start.Session.ReportControl.Reference;
        var activeDataSetReference = start.Session.Plan.DataSetReference;
        if (!string.IsNullOrWhiteSpace(activeRcbReference))
        {
            plan.ReportControlReference = activeRcbReference;
            plan.Buffered = IsBufferedRcbReference(activeRcbReference, plan.Buffered);
        }
        if (!string.IsNullOrWhiteSpace(activeDataSetReference))
            plan.DataSetReference = activeDataSetReference;

        var acquisitionLabel = BuildAcquisitionLabel(dynamicDataSetPlanned, plan.Buffered, plan.ReportControlReference);

        _reportMonitorSessions[plan.PlanId] = start.Session;
        _reportMonitorCoverage[plan.PlanId] = coveredReferences;
        LastErrorMessage = $"ARIEC61850 persistent report monitor active: {subscription.Summary}. {start.Message}";
        return new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = plan.PlanId,
            Message = LastErrorMessage,
            SubscriptionSummary = subscription.Summary,
            MemberCount = subscription.Members.Count,
            WriteStepCount = start.WriteSteps.Count,
            UsedDynamicDataSet = dynamicDataSetPlanned,
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            AcquisitionLabel = acquisitionLabel,
            CoveredReferences = coveredReferences,
            Warnings = start.Warnings.Concat(subscription.Warnings).ToArray()
        };
    }

    public async Task<NativeReportMonitorSliceResult> ReceiveReportMonitorSliceAsync(string planId, TimeSpan duration, CancellationToken cancellationToken)
    {
        if (!_reportMonitorSessions.TryGetValue(planId, out var session))
        {
            return new NativeReportMonitorSliceResult
            {
                PlanId = planId,
                Message = $"Report monitor session not found for plan {planId}."
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        var slice = await RunMmsOperationAsync(
            () => _session.ReceivePersistentReportMonitorSliceAsync(
                session,
                duration,
                discovery?.IedDirectory,
                pollReferences: null,
                pollInterval: null,
                triggerGeneralInterrogation: false,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var updates = new List<NativeReportValueUpdate>();
        var frames = new List<NativeReportFrameMetadata>();
        var warnings = new List<string>();
        foreach (var report in slice.Reports)
        {
            var header = report.Header;
            frames.Add(new NativeReportFrameMetadata
            {
                ReportControlReference = session.ReportControl.Reference,
                ReportId = header.ReportId,
                DataSetReference = header.DataSetReference,
                SequenceNumber = header.SequenceNumber,
                SubSequenceNumber = header.SubSequenceNumber,
                MoreSegmentsFollow = header.MoreSegmentsFollow,
                BufferOverflow = header.BufferOverflow,
                ConfRev = header.ConfRev,
                EntryIdHex = header.EntryIdHex,
                ReportTimestamp = header.TimeOfEntry,
                ReceivedAt = report.ReceivedAt
            });

            var projection = ArMms.MmsReportValueProjector.Project(report);
            warnings.AddRange(projection.Warnings);
            updates.AddRange(projection.Updates.Select(update => new NativeReportValueUpdate
            {
                Reference = update.Reference,
                FunctionalConstraint = update.FunctionalConstraint,
                Value = update.Value,
                Quality = update.Quality,
                Timestamp = update.Timestamp,
                Reason = update.Reason,
                Source = update.Source,
                ProjectionStatus = update.ProjectionStatus,
                HasValue = ReadOptionalBooleanProperty(
                    update,
                    "HasValue",
                    !string.IsNullOrWhiteSpace(update.Value) && update.Value != "-"),
                HasQuality = ReadOptionalBooleanProperty(
                    update,
                    "HasQuality",
                    !string.IsNullOrWhiteSpace(update.Quality) && update.Quality != "-"),
                HasTimestamp = ReadOptionalBooleanProperty(
                    update,
                    "HasTimestamp",
                    !string.IsNullOrWhiteSpace(update.Timestamp) && update.Timestamp != "-"),
                ReportControlReference = session.ReportControl.Reference,
                ReportId = header.ReportId,
                DataSetReference = header.DataSetReference,
                SequenceNumber = header.SequenceNumber,
                ConfRev = header.ConfRev,
                UpdatedAt = update.UpdatedAt
            }));
        }

        if (!HasArIedReportCompatibilityApi() &&
            Interlocked.Exchange(ref _engineCompatibilityWarningIssued, 1) == 0)
        {
            warnings.Add(
                "ARIEC61850 compatibility mode is active because the referenced engine does not expose " +
                "HasValue/HasQuality/HasTimestamp and UnroutedPersistentReportCount. Build with the bundled " +
                "patched ARIEC61850 engine for partial q/t reports and safe multi-RCB routing diagnostics.");
        }

        return new NativeReportMonitorSliceResult
        {
            PlanId = planId,
            ReportCount = slice.Reports.Count,
            PollReadCount = slice.PollReads.Count,
            UnroutedReportCount = ReadOptionalInt32Property(_session, "UnroutedPersistentReportCount"),
            Message = slice.Message,
            ReportFrames = frames,
            Updates = updates,
            Warnings = warnings.Distinct(StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    public async Task<IReadOnlyList<NativeReportMonitorStopResult>> StopReportMonitorsAsync()
    {
        var results = new List<NativeReportMonitorStopResult>();
        foreach (var item in _reportMonitorSessions.ToArray())
        {
            try
            {
                var stop = await RunMmsOperationAsync(
                    () => _session.StopPersistentReportMonitorAsync(item.Value, CancellationToken.None),
                    CancellationToken.None).ConfigureAwait(false);
                results.Add(new NativeReportMonitorStopResult
                {
                    IsSuccess = stop.IsSuccess,
                    PlanId = item.Key,
                    Message = stop.Message
                });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                results.Add(new NativeReportMonitorStopResult
                {
                    IsSuccess = false,
                    PlanId = item.Key,
                    Message = $"Report monitor cleanup failed: {ex.GetType().Name}: {ex.Message}"
                });
            }
            finally
            {
                _reportMonitorSessions.Remove(item.Key);
                _reportMonitorCoverage.Remove(item.Key);
            }
        }

        return results;
    }

    private async Task<T> RunMmsOperationAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        await _mmsIoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await operation().ConfigureAwait(false);
        }
        finally
        {
            _mmsIoGate.Release();
        }
    }

    private async Task RunMmsOperationAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        await _mmsIoGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
        }
        finally
        {
            _mmsIoGate.Release();
        }
    }

    private async Task TryReadReportAttributeAsync(NativeReportControlCandidate rcb, string attribute, Action<object?> apply, CancellationToken cancellationToken)
    {
        try
        {
            var value = await ReadValueAsync($"{rcb.Reference}.{attribute}", rcb.FunctionalConstraint, GuessReportAttributeType(attribute), cancellationToken).ConfigureAwait(false);
            if (value != null) apply(value);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            rcb.Status = $"Attribute probe partial: {attribute} {ex.GetType().Name}";
        }
    }

    private static string GuessReportAttributeType(string attribute)
    {
        return attribute.ToLowerInvariant() switch
        {
            "rptid" or "datset" or "entryid" => "String",
            "rptena" or "resv" or "gi" or "purgebuf" => "Boolean",
            "confrev" or "intgpd" or "buftm" or "sqnum" or "resvtms" => "UInt32",
            "trgops" or "optflds" => "BitString",
            _ => "String"
        };
    }

    private static string NormalizeReportAttributeText(object? value)
    {
        var text = value?.ToString()?.Trim() ?? string.Empty;
        return string.IsNullOrWhiteSpace(text) ? string.Empty : text;
    }

    private static string NormalizeReportedDataSetReference(string domain, string value)
    {
        var text = value.Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        if (text.Contains('/')) return text;

        return text.Contains('.') ? $"{domain}/{text}" : $"{domain}/LLN0.{text}";
    }

    private async Task<ArMms.MmsDiscoveryResult?> EnsureDiscoveryForReportingAsync(CancellationToken cancellationToken)
    {
        if (_lastDiscovery != null)
            return _lastDiscovery;

        try
        {
            var discovery = await RunMmsOperationAsync(
                () => _session.DiscoverAsync(
                    probeReportAttributes: true,
                    maxReportAttributeProbes: 96,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);

            _lastDiscovery = discovery;
            _liveModel = LiveIedModelDiscoveryBuilder.Build(discovery, new LiveIedModelDiscoveryBuildOptions
            {
                Host = _host,
                Port = _port,
                IncludeLowConfidenceTemplates = true
            });
            LastReportInventory = ToNativeInventory(discovery.ReportInventory);
            DetectedIdentity = Iec61850DeviceIdentityResolver.Resolve(discovery, _liveModel, Array.Empty<SignalDefinition>());
            LastDiscoverySummary = $"IEDName={(string.IsNullOrWhiteSpace(DetectedIedName) ? "unresolved" : DetectedIedName)} ({DetectedIdentity.Source}); {discovery.Summary} {_liveModel.Summary} Engine=ARIEC61850 live-model/schema/reporting.";
            return discovery;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastErrorMessage = $"ARIEC61850 reporting discovery failed: {ex.GetType().Name}: {ex.Message}. Last discovery: {_session.LastDiscoveryAttemptSummary}";
            return null;
        }
    }

    private async Task<IReadOnlyList<ArMms.MmsDataSetDirectoryResult>> ReadPlannedDataSetDirectoriesAsync(
        ReportControlPlan plan,
        ArMms.MmsIedModelDirectory directory,
        CancellationToken cancellationToken)
    {
        var dataSets = new[]
            {
                plan.DataSetReference
            }
            .Concat(_lastDiscovery?.ReportInventory.ReportControls
                .Where(rcb => ReferencesEqual(rcb.Reference, plan.ReportControlReference))
                .Select(rcb => rcb.DataSetReference) ?? Array.Empty<string>())
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (dataSets.Length == 0)
            return Array.Empty<ArMms.MmsDataSetDirectoryResult>();

        return await RunMmsOperationAsync(
            () => _session.GetDataSetDirectoriesAsync(dataSets, directory, cancellationToken),
            cancellationToken).ConfigureAwait(false);
    }

    private static ArMms.MmsReportInventory BuildEngineReportInventory(ArMms.MmsReportInventory source, ReportControlPlan plan)
    {
        var inventory = new ArMms.MmsReportInventory();
        foreach (var dataSet in source.DataSets)
        {
            inventory.DataSets.Add(new ArMms.MmsDataSetCandidate
            {
                Domain = dataSet.Domain,
                LogicalNode = dataSet.LogicalNode,
                Name = dataSet.Name,
                Reference = dataSet.Reference,
                RawMmsName = dataSet.RawMmsName
            });
        }

        var forceDynamicPlan = ShouldForceDynamicReportPlan(plan);
        foreach (var rcb in source.ReportControls)
        {
            var clone = CloneReportControl(rcb);
            // A temporary dynamic DataSet must be paired with explicit dchg/qchg/dupd
            // trigger options. Previously the dynamic planner inherited whatever TrgOps
            // happened to be stored in the free URCB, so GI/integrity worked while CB
            // position changes were never reported.
            if (forceDynamicPlan)
                ApplyDynamicPlanRequirements(clone, plan);
            inventory.ReportControls.Add(clone);
        }

        if (!string.IsNullOrWhiteSpace(plan.DataSetReference) &&
            !inventory.DataSets.Any(ds => ReferencesEqual(ds.Reference, plan.DataSetReference)))
        {
            var parsedDataSet = ParseDataSetReference(plan.DataSetReference);
            inventory.DataSets.Add(new ArMms.MmsDataSetCandidate
            {
                Domain = parsedDataSet.Domain,
                LogicalNode = parsedDataSet.LogicalNode,
                Name = parsedDataSet.Name,
                Reference = plan.DataSetReference,
                RawMmsName = string.IsNullOrWhiteSpace(parsedDataSet.LogicalNode)
                    ? parsedDataSet.Name
                    : $"{parsedDataSet.LogicalNode}${parsedDataSet.Name}"
            });
        }

        if (!string.IsNullOrWhiteSpace(plan.ReportControlReference))
        {
            var existing = inventory.ReportControls.FirstOrDefault(rcb => ReferencesEqual(rcb.Reference, plan.ReportControlReference));
            if (existing == null)
                inventory.ReportControls.Add(CreateReportControlFromPlan(plan));
            else
                ApplyPlanHints(existing, plan);
        }

        return inventory;
    }

    private static ArMms.MmsReportControlCandidate CloneReportControl(ArMms.MmsReportControlCandidate source)
        => new()
        {
            Domain = source.Domain,
            LogicalNode = source.LogicalNode,
            FunctionalConstraint = source.FunctionalConstraint,
            Name = source.Name,
            Reference = source.Reference,
            Buffered = source.Buffered,
            DataSetReference = source.DataSetReference,
            ReportId = source.ReportId,
            ConfRev = source.ConfRev,
            IntegrityPeriodMs = source.IntegrityPeriodMs,
            EnabledState = source.EnabledState,
            ReservationState = source.ReservationState,
            ReservationTimeSeconds = source.ReservationTimeSeconds,
            BufferTimeMs = source.BufferTimeMs,
            TriggerOptions = source.TriggerOptions,
            OptionalFields = source.OptionalFields,
            Status = source.Status,
            Attributes = source.Attributes.ToList()
        };

    private static ArMms.MmsReportControlCandidate CreateReportControlFromPlan(ReportControlPlan plan)
    {
        var parsed = ParseReportControlReference(plan.ReportControlReference, plan.Buffered);
        var rcb = new ArMms.MmsReportControlCandidate
        {
            Domain = parsed.Domain,
            LogicalNode = parsed.LogicalNode,
            FunctionalConstraint = parsed.FunctionalConstraint,
            Name = parsed.Name,
            Reference = plan.ReportControlReference,
            Buffered = plan.Buffered,
            DataSetReference = plan.DataSetReference,
            ReportId = plan.ReportId,
            IntegrityPeriodMs = plan.IntegrityPeriodMs > 0 ? plan.IntegrityPeriodMs.ToString(CultureInfo.InvariantCulture) : string.Empty,
            TriggerOptions = plan.TriggerOptions,
            OptionalFields = plan.OptionalFields,
            Status = "ArIED 61850 report plan"
        };
        rcb.Attributes.AddRange(parsed.Buffered
            ? ["RptID", "RptEna", "DatSet", "ConfRev", "OptFlds", "BufTm", "SqNum", "TrgOps", "IntgPd", "GI", "PurgeBuf", "EntryID", "TimeOfEntry", "ResvTms"]
            : ["RptID", "RptEna", "Resv", "DatSet", "ConfRev", "OptFlds", "BufTm", "SqNum", "TrgOps", "IntgPd", "GI"]);
        return rcb;
    }

    private static void ApplyPlanHints(ArMms.MmsReportControlCandidate target, ReportControlPlan plan)
    {
        if (string.IsNullOrWhiteSpace(target.DataSetReference) && !string.IsNullOrWhiteSpace(plan.DataSetReference))
            target.DataSetReference = plan.DataSetReference;
        if (string.IsNullOrWhiteSpace(target.ReportId) && !string.IsNullOrWhiteSpace(plan.ReportId))
            target.ReportId = plan.ReportId;
        if (string.IsNullOrWhiteSpace(target.IntegrityPeriodMs) && plan.IntegrityPeriodMs > 0)
            target.IntegrityPeriodMs = plan.IntegrityPeriodMs.ToString(CultureInfo.InvariantCulture);

        if (plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase))
        {
            ApplyDynamicPlanRequirements(target, plan);
            return;
        }

        if (string.IsNullOrWhiteSpace(target.TriggerOptions) && !string.IsNullOrWhiteSpace(plan.TriggerOptions))
            target.TriggerOptions = plan.TriggerOptions;
        if (string.IsNullOrWhiteSpace(target.OptionalFields) && !string.IsNullOrWhiteSpace(plan.OptionalFields))
            target.OptionalFields = plan.OptionalFields;
    }

    private static void ApplyDynamicPlanRequirements(ArMms.MmsReportControlCandidate target, ReportControlPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.TriggerOptions))
            target.TriggerOptions = plan.TriggerOptions;
        if (!string.IsNullOrWhiteSpace(plan.OptionalFields))
            target.OptionalFields = plan.OptionalFields;
    }

    private static (string Domain, string LogicalNode, string Name) ParseDataSetReference(string reference)
    {
        var text = reference.Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        var domain = slash > 0 ? text[..slash] : string.Empty;
        var item = slash > 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
        var dot = item.LastIndexOf('.');
        if (dot <= 0 || dot >= item.Length - 1)
            return (domain, string.Empty, item);
        return (domain, item[..dot], item[(dot + 1)..]);
    }

    private static (string Domain, string LogicalNode, string FunctionalConstraint, string Name, bool Buffered) ParseReportControlReference(string reference, bool buffered)
    {
        var text = reference.Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        var domain = slash > 0 ? text[..slash] : string.Empty;
        var item = slash > 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
        var segments = item.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var logicalNode = segments.Length > 0 ? segments[0] : string.Empty;
        var functionalConstraint = segments.FirstOrDefault(s => s.Equals("BR", StringComparison.OrdinalIgnoreCase) || s.Equals("RP", StringComparison.OrdinalIgnoreCase))
            ?? (buffered ? "BR" : "RP");
        var name = segments.Length > 0 ? segments[^1] : (buffered ? "BRCB" : "URCB");
        return (domain, logicalNode, functionalConstraint, name, buffered || functionalConstraint.Equals("BR", StringComparison.OrdinalIgnoreCase));
    }

    private static bool ShouldForceDynamicReportPlan(ReportControlPlan plan)
    {
        return plan.AllowDynamicDataSetWrites &&
               plan.Status.Contains("Dynamic", StringComparison.OrdinalIgnoreCase) &&
               string.IsNullOrWhiteSpace(plan.ReportControlReference) &&
               string.IsNullOrWhiteSpace(plan.DataSetReference);
    }

    private static bool IsDynamicReportWriteAllowed(ReportControlPlan plan)
    {
        // Engineering writes must be an explicit typed option. Text labels are not a
        // security boundary and must never silently enable DataSet/RCB modification.
        return plan.AllowDynamicDataSetWrites;
    }

    private static IReadOnlyList<string> ExtractSubscriptionMemberReferences(System.Collections.IEnumerable members)
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in members)
        {
            if (member == null)
                continue;

            var reference = member as string ?? ReadStringProperty(
                member,
                "Reference",
                "ObjectReference",
                "IecReference",
                "MmsReference",
                "FcdaReference",
                "SourceReference",
                "VariableReference",
                "FullPath",
                "Path");

            if (string.IsNullOrWhiteSpace(reference) || !reference.Contains('/'))
            {
                var domain = ReadStringProperty(member, "Domain", "DomainName", "LogicalDevice", "LdName");
                var item = ReadStringProperty(member, "Item", "ItemName", "Variable", "VariableName", "MmsItem", "Name");
                if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(item))
                    reference = $"{domain}/{item}";
            }

            if (string.IsNullOrWhiteSpace(reference))
            {
                var text = member.ToString()?.Trim() ?? string.Empty;
                if (text.Contains('/'))
                    reference = text;
            }

            if (!string.IsNullOrWhiteSpace(reference))
                references.Add(reference.Trim());
        }

        return references.OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static string ResolvePreferredLogicalDevice(ReportControlPlan plan)
    {
        if (!string.IsNullOrWhiteSpace(plan.ReportControlReference))
        {
            var slash = plan.ReportControlReference.IndexOf('/');
            if (slash > 0)
                return plan.ReportControlReference[..slash];
        }

        var reference = plan.Bindings.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.IecReference))?.IecReference ?? string.Empty;
        var refSlash = reference.IndexOf('/');
        return refSlash > 0 ? reference[..refSlash] : string.Empty;
    }

    private static bool IsBufferedRcbReference(string reference, bool fallback)
    {
        if (string.IsNullOrWhiteSpace(reference)) return fallback;
        var normalized = reference.Replace('$', '.');
        return normalized.Contains(".BR.", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("/BR/", StringComparison.OrdinalIgnoreCase) ||
               normalized.Contains("BRCB", StringComparison.OrdinalIgnoreCase) ||
               (!normalized.Contains("URCB", StringComparison.OrdinalIgnoreCase) && fallback);
    }

    private static string BuildAcquisitionLabel(bool dynamicDataSet, bool buffered, string reportControlReference)
    {
        var fallbackName = buffered ? "BRCB" : "URCB";
        var name = ExtractReportControlName(reportControlReference, fallbackName);
        return $"{(dynamicDataSet ? "Dynamic" : "Static")}: {name}";
    }

    private static string ExtractReportControlName(string reference, string fallbackName)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text)) return fallbackName;
        var slash = text.LastIndexOf('/');
        var item = slash >= 0 && slash < text.Length - 1 ? text[(slash + 1)..] : text;
        var segments = item.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? fallbackName : segments[^1];
    }

    private static string BuildDynamicDataSetName(ReportControlPlan plan)
        => "ARIED_" + (string.IsNullOrWhiteSpace(plan.PlanId) ? Guid.NewGuid().ToString("N")[..8] : plan.PlanId[..Math.Min(8, plan.PlanId.Length)]).ToUpperInvariant();

    public Task<object?> ReadValueAsync(string objectReference, CancellationToken cancellationToken)
    {
        return ReadValueAsync(objectReference, string.Empty, string.Empty, cancellationToken);
    }

    public async Task<object?> ReadValueAsync(string objectReference, string functionalConstraint, string dataType, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_session.IsMmsInitiated)
        {
            LastErrorMessage = !_session.IsTransportConnected
                ? "ARIEC61850 transport is not connected. Start the session again after TCP/COTP/ACSE association succeeds."
                : _session.State == ArMms.MmsAssociationState.MmsInitiateFailed
                    ? $"ARIEC61850 TCP/COTP connected, but ACSE/MMS Initiate was rejected or not understood by the IED. Last response: {_session.LastAssociationResponseHex}"
                    : $"ARIEC61850 transport is ready, but ACSE/MMS Initiate is not complete yet. State: {_session.State}.";
            return null;
        }

        var attempts = new List<string>();
        foreach (var candidate in BuildReadReferenceCandidates(objectReference, functionalConstraint))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (candidate.UseSmartDirectory && _lastDiscovery?.IedDirectory != null)
            {
                try
                {
                    var smart = await RunMmsOperationAsync(
                        () => _session.ReadSmartAsync(_lastDiscovery.IedDirectory, candidate.Reference, cancellationToken),
                        cancellationToken).ConfigureAwait(false);
                    attempts.Add($"{candidate.Label}/smart: {smart.ReadResult.Message}");
                    if (smart.ReadResult.IsSuccess)
                    {
                        var projected = ProjectReadValue(smart.ReadResult.Value, dataType, candidate.Reference, objectReference);
                        if (projected != null)
                        {
                            LastErrorMessage = $"ARIEC61850 read OK via {candidate.Label}/smart. {smart.ResolveResult.Message}";
                            return projected;
                        }

                        attempts.Add($"{candidate.Label}/smart projection blocked: {LastErrorMessage}");
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    attempts.Add($"{candidate.Label}/smart exception: {ex.GetType().Name}: {ex.Message}");
                }
            }

            try
            {
                var normalized = ArMms.MmsObjectReference.Parse(candidate.Reference, candidate.FunctionalConstraint);
                var result = await RunMmsOperationAsync(
                    () => _session.ReadSingleVariableAsync(normalized, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
                attempts.Add($"{candidate.Label}/direct {normalized}: {(result.IsSuccess ? "OK" : result.Message)}");
                if (result.IsSuccess)
                {
                    var projected = ProjectReadValue(result.Value, dataType, candidate.Reference, objectReference);
                    if (projected != null)
                    {
                        LastErrorMessage = $"ARIEC61850 read OK via {candidate.Label}/direct: {normalized}. {result.Message}";
                        return projected;
                    }

                    attempts.Add($"{candidate.Label}/direct projection blocked: {LastErrorMessage}");
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                attempts.Add($"{candidate.Label}/direct exception: {ex.GetType().Name}: {ex.Message}");
            }
        }

        LastErrorMessage = attempts.Count == 0
            ? $"ARIEC61850 read failed for {objectReference}: no read candidates."
            : $"ARIEC61850 read failed for {objectReference}: {string.Join(" | ", attempts.Take(8))}";
        return null;
    }

    public async Task<Iec61850ControlCapabilities> InspectControlAsync(
        SignalDefinition signal,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(signal);
        if (!signal.IsControlSignal || !signal.IsValidControlObject)
            throw new InvalidOperationException($"{signal.Name} is not a valid IEC 61850 control Data Object root.");
        if (!_session.IsMmsInitiated)
            throw new InvalidOperationException("The IEC 61850 MMS association is not active.");

        var control = await GetOrOpenControlSessionAsync(signal, cancellationToken).ConfigureAwait(false);
        var descriptor = control.Descriptor;
        var effectiveCdc = ResolveControlSemanticCdc(descriptor, signal);
        var status = await RunMmsOperationAsync(
            () => control.ReadStatusAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var normalizedStatus = NormalizeControlFeedback(effectiveCdc, status.DisplayValue, status.State);
        var currentValue = status.IsSuccess ? normalizedStatus.Value : "-";
        var currentState = normalizedStatus.State.ToString();
        var statusReference = descriptor.StatusReference;

        // Keep the application-level feedback mapping for cross-object controls such as
        // TapOpR/TapOpL -> TapChg.valWTr.posVal. The native descriptor remains the source
        // of truth for the command sequence, while the preferred feedback reference makes
        // commissioning feedback more useful for pulse-style controls.
        if (!string.IsNullOrWhiteSpace(signal.ControlStatusReference) &&
            !string.Equals(signal.ControlStatusReference, descriptor.StatusReference, StringComparison.OrdinalIgnoreCase))
        {
            var preferred = await ReadValueAsync(
                signal.ControlStatusReference,
                "ST",
                string.IsNullOrWhiteSpace(signal.ControlValueType) ? "Unknown" : signal.ControlValueType,
                cancellationToken).ConfigureAwait(false);
            if (preferred != null)
            {
                var normalizedPreferred = NormalizeControlFeedback(
                    effectiveCdc,
                    preferred.ToString() ?? currentValue,
                    ArControl.Iec61850ControlStatusState.Unknown);
                currentValue = normalizedPreferred.Value;
                currentState = normalizedPreferred.State == ArControl.Iec61850ControlStatusState.Unknown
                    ? "Application feedback"
                    : normalizedPreferred.State.ToString();
                statusReference = signal.ControlStatusReference;
            }
        }

        var model = MapControlModel(descriptor.ControlModel);
        var modelText = FriendlyControlModel(descriptor.ControlModel);
        var valueType = MapControlValueType(descriptor);

        signal.ControlCdc = effectiveCdc;
        signal.ControlValueType = valueType;
        signal.ControlCurrentValue = currentValue;
        signal.ControlStatusReference = statusReference;
        signal.ControlModelReference = $"{descriptor.ObjectReference}.ctlModel";
        signal.ControlModelText = modelText;

        return new Iec61850ControlCapabilities
        {
            ObjectReference = descriptor.ObjectReference,
            StatusReference = statusReference,
            ControlModelReference = $"{descriptor.ObjectReference}.ctlModel",
            ControlModel = model,
            ControlModelText = modelText,
            ControlCdc = effectiveCdc,
            ControlValueType = valueType,
            CtlValSignature = descriptor.CtlValSpecification.Signature,
            CurrentValue = currentValue,
            CurrentState = currentState,
            EngineControlServiceAvailable = true,
            IsOperationallyReady = descriptor.IsOperationallyReady,
            SupportsCommandTermination = descriptor.SupportsCommandTermination,
            SupportsTimeActivatedOperate = descriptor.SupportsTimeActivatedOperate,
            SboTimeoutText = FormatControlTimeout(descriptor.SboTimeout),
            OperTimeoutText = FormatControlTimeout(descriptor.OperTimeout),
            SequenceText = FriendlyControlSequence(descriptor.ControlModel),
            DiscoveryEvidence = descriptor.DiscoveryEvidence,
            EngineControlServiceStatus = descriptor.IsOperationallyReady
                ? "ARIEC61850 Smart Control is ready. Exact live ctlVal typing and the required Direct/SBO sequence were validated."
                : "ARIEC61850 found the control object, but the live descriptor is incomplete and command execution remains blocked."
        };
    }

    public async Task<Iec61850ControlCommandResult> ExecuteControlAsync(
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken)
    {
        await _controlCommandGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await ExecuteControlCoreAsync(request, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _controlCommandGate.Release();
        }
    }

    private async Task<Iec61850ControlCommandResult> ExecuteControlCoreAsync(
        Iec61850ControlCommandRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (!_session.IsMmsInitiated)
            throw new InvalidOperationException("The IEC 61850 MMS association is not active.");
        if (!request.Signal.IsControlSignal || !request.Signal.IsValidControlObject)
            throw new InvalidOperationException("The selected row is not a valid IEC 61850 control Data Object root.");

        var totalStopwatch = Stopwatch.StartNew();
        var control = await GetOrOpenControlSessionAsync(request.Signal, cancellationToken).ConfigureAwait(false);
        var descriptor = control.Descriptor;
        var effectiveCdc = ResolveControlSemanticCdc(descriptor, request.Signal);
        var capabilities = BuildControlCapabilities(descriptor, request.Signal, "-", effectiveCdc);
        if (!descriptor.IsOperationallyReady)
        {
            return ControlFailure(
                "Validation",
                "ARIEC61850 could not build an operationally safe control descriptor from the live IED model.",
                capabilities,
                request.ValueText);
        }

        if (!TryBuildNativeControlValue(
                descriptor,
                request.Signal,
                request.ValueText,
                out var controlValue,
                out var expectedValue,
                out var expectedState,
                out var parseError))
        {
            return ControlFailure("Validation", parseError, capabilities, request.ValueText);
        }

        var preferredFeedbackReference = !string.IsNullOrWhiteSpace(request.Signal.ControlStatusReference)
            ? request.Signal.ControlStatusReference
            : descriptor.StatusReference;
        var initialFeedback = await ReadControlFeedbackAsync(
            control,
            preferredFeedbackReference,
            request.Signal.ControlValueType,
            effectiveCdc,
            cancellationToken).ConfigureAwait(false);

        capabilities = BuildControlCapabilities(descriptor, request.Signal, initialFeedback.Value, effectiveCdc);
        request.Signal.ControlCdc = capabilities.ControlCdc;
        request.Signal.ControlValueType = capabilities.ControlValueType;
        request.Signal.ControlStatusReference = capabilities.StatusReference;
        request.Signal.ControlModelReference = capabilities.ControlModelReference;
        request.Signal.ControlModelText = capabilities.ControlModelText;
        request.Signal.ControlCurrentValue = initialFeedback.Value;

        if (!request.TestMode && initialFeedback.IsSuccess &&
            ControlFeedbackMatches(effectiveCdc, expectedValue, initialFeedback.Value, initialFeedback.Value))
        {
            totalStopwatch.Stop();
            return ControlAlreadyAtRequestedState(
                capabilities,
                expectedValue,
                initialFeedback.Value,
                totalStopwatch.Elapsed);
        }

        var nativeRequest = new ArControl.Iec61850ControlRequest
        {
            ControlValue = controlValue!,
            Origin = ArControl.Iec61850Origin.FromText(
                string.IsNullOrWhiteSpace(request.Originator) ? "ArIED61850" : request.Originator.Trim(),
                ParseOriginCategory(request.OriginCategory)),
            Test = request.TestMode,
            InterlockCheck = request.InterlockCheck,
            SynchroCheck = request.SynchroCheck,
            AutoSelect = true,
            CommandTerminationTimeout = TimeSpan.FromMilliseconds(
                Math.Clamp(request.CommandTerminationTimeoutMs, 1000, 120000))
        };

        ArControl.Iec61850ControlActionResult action;
        try
        {
            action = await RunMmsOperationAsync(
                () => control.OperateAsync(nativeRequest, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ControlFailure(
                "Control exception",
                $"{ex.GetType().Name}: {ex.Message}",
                capabilities,
                expectedValue);
        }

        if (!action.IsSuccess)
        {
            var failureMessage = BuildNativeControlFailureMessage(action);
            return MapNativeControlResult(
                action,
                capabilities,
                expectedValue,
                initialFeedback.Value,
                feedbackConfirmed: false,
                stage: FriendlyCompletionStage(action),
                message: failureMessage,
                isSuccess: false);
        }

        if (request.TestMode)
        {
            return MapNativeControlResult(
                action,
                capabilities,
                expectedValue,
                initialFeedback.Value,
                feedbackConfirmed: false,
                stage: action.CommandTerminationReceived ? "Test command terminated" : "Test command accepted",
                message: action.CommandTerminationReceived
                    ? "The test command completed with positive CommandTermination. Process movement is not required while Test=true."
                    : "The IED accepted the test command. Process movement is not required while Test=true.",
                isSuccess: true);
        }

        if (string.IsNullOrWhiteSpace(preferredFeedbackReference))
        {
            return MapNativeControlResult(
                action,
                capabilities,
                expectedValue,
                "-",
                feedbackConfirmed: false,
                stage: action.CommandTerminationReceived ? "Command terminated" : "Command accepted",
                message: action.CommandTerminationReceived
                    ? "Positive CommandTermination was received. The live model did not expose a process feedback reference."
                    : "The MMS control service accepted the command. The live model did not expose a process feedback reference.",
                isSuccess: true);
        }

        var feedbackStopwatch = Stopwatch.StartNew();
        var feedback = await WaitForNativeControlFeedbackAsync(
            control,
            preferredFeedbackReference,
            request.Signal.ControlValueType,
            effectiveCdc,
            expectedValue,
            expectedState,
            initialFeedback.Value,
            Math.Clamp(request.FeedbackTimeoutMs, 1000, 30000),
            cancellationToken).ConfigureAwait(false);
        feedbackStopwatch.Stop();
        totalStopwatch.Stop();

        var success = action.IsSuccess && feedback.Confirmed;
        request.Signal.ControlCurrentValue = feedback.Value;
        return MapNativeControlResult(
            action,
            capabilities,
            expectedValue,
            feedback.Value,
            feedback.Confirmed,
            stage: feedback.Confirmed ? "Feedback confirmed" : "Feedback timeout",
            message: feedback.Confirmed
                ? $"{FriendlyControlSequence(descriptor.ControlModel)} completed and process feedback reached {feedback.Value}."
                : $"The IEC 61850 control sequence completed, but the expected process feedback was not observed before timeout. Last value: {feedback.Value}.",
            isSuccess: success,
            feedbackElapsed: feedbackStopwatch.Elapsed,
            totalElapsed: totalStopwatch.Elapsed);
    }

    private Iec61850ControlCapabilities BuildControlCapabilities(
        ArControl.Iec61850ControlObjectDescriptor descriptor,
        SignalDefinition signal,
        string currentValue,
        string? effectiveCdc = null)
    {
        var statusReference = !string.IsNullOrWhiteSpace(signal.ControlStatusReference)
            ? signal.ControlStatusReference
            : descriptor.StatusReference;
        return new Iec61850ControlCapabilities
        {
            ObjectReference = descriptor.ObjectReference,
            StatusReference = statusReference,
            ControlModelReference = $"{descriptor.ObjectReference}.ctlModel",
            ControlModel = MapControlModel(descriptor.ControlModel),
            ControlModelText = FriendlyControlModel(descriptor.ControlModel),
            ControlCdc = string.IsNullOrWhiteSpace(effectiveCdc) ? ResolveControlSemanticCdc(descriptor, signal) : effectiveCdc,
            ControlValueType = MapControlValueType(descriptor),
            CtlValSignature = descriptor.CtlValSpecification.Signature,
            CurrentValue = currentValue,
            CurrentState = "Unknown",
            EngineControlServiceAvailable = true,
            IsOperationallyReady = descriptor.IsOperationallyReady,
            SupportsCommandTermination = descriptor.SupportsCommandTermination,
            SupportsTimeActivatedOperate = descriptor.SupportsTimeActivatedOperate,
            SboTimeoutText = FormatControlTimeout(descriptor.SboTimeout),
            OperTimeoutText = FormatControlTimeout(descriptor.OperTimeout),
            SequenceText = FriendlyControlSequence(descriptor.ControlModel),
            DiscoveryEvidence = descriptor.DiscoveryEvidence,
            EngineControlServiceStatus = descriptor.IsOperationallyReady
                ? "ARIEC61850 Smart Control is ready."
                : "The native Smart Control descriptor is incomplete."
        };
    }

    private async Task<(bool IsSuccess, string Value, ArControl.Iec61850ControlStatusState State)> ReadControlFeedbackAsync(
        ArControl.Iec61850ControlObjectSession control,
        string reference,
        string dataType,
        string feedbackCdc,
        CancellationToken cancellationToken)
    {
        if (!string.IsNullOrWhiteSpace(reference) &&
            !string.Equals(reference, control.Descriptor.StatusReference, StringComparison.OrdinalIgnoreCase))
        {
            var feedbackDataType = feedbackCdc.Equals("DPC", StringComparison.OrdinalIgnoreCase)
                ? "Dbpos"
                : string.IsNullOrWhiteSpace(dataType) ? "Unknown" : dataType;
            var raw = await ReadValueAsync(
                reference,
                "ST",
                feedbackDataType,
                cancellationToken).ConfigureAwait(false);
            if (raw != null)
            {
                var normalized = NormalizeControlFeedback(
                    feedbackCdc,
                    raw.ToString() ?? "-",
                    ArControl.Iec61850ControlStatusState.Unknown);
                return (true, normalized.Value, normalized.State);
            }
        }

        var status = await RunMmsOperationAsync(
            () => control.ReadStatusAsync(cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var normalizedStatus = NormalizeControlFeedback(feedbackCdc, status.DisplayValue, status.State);
        return (status.IsSuccess, normalizedStatus.Value, normalizedStatus.State);
    }

    private async Task<(bool Confirmed, string Value)> WaitForNativeControlFeedbackAsync(
        ArControl.Iec61850ControlObjectSession control,
        string reference,
        string dataType,
        string cdc,
        string expectedValue,
        ArControl.Iec61850ControlStatusState? expectedState,
        string initialValue,
        int timeoutMs,
        CancellationToken cancellationToken)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        var last = string.IsNullOrWhiteSpace(initialValue) ? "-" : initialValue;
        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var status = await ReadControlFeedbackAsync(control, reference, dataType, cdc, cancellationToken).ConfigureAwait(false);
            if (status.IsSuccess)
            {
                last = status.Value;
                if (expectedState.HasValue && status.State == expectedState.Value)
                    return (true, last);
                if (ControlFeedbackMatches(cdc, expectedValue, initialValue, last))
                    return (true, last);
            }

            await Task.Delay(150, cancellationToken).ConfigureAwait(false);
        }

        return (false, last);
    }

    private static bool TryBuildNativeControlValue(
        ArControl.Iec61850ControlObjectDescriptor descriptor,
        SignalDefinition signal,
        string text,
        out ArControl.Iec61850ControlValue? value,
        out string expected,
        out ArControl.Iec61850ControlStatusState? expectedState,
        out string error)
    {
        value = null;
        expected = (text ?? string.Empty).Trim();
        expectedState = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(expected))
        {
            error = "Choose a command value.";
            return false;
        }

        var cdc = (descriptor.Cdc ?? string.Empty).Trim().ToUpperInvariant();
        var mmsType = (descriptor.CtlValSpecification.MmsType ?? string.Empty).Trim().ToLowerInvariant();
        var normalized = expected.ToLowerInvariant();
        var signalText = $"{signal.Name} {signal.ObjectReference}";
        var ctlChildNames = descriptor.CtlValSpecification.Children
            .Select(child => new string((child.Name ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray()))
            .ToArray();
        var isBscStructure = mmsType == "structure" &&
                             (ctlChildNames.Contains("posval") || ctlChildNames.Contains("transind"));
        var isApcStructure = mmsType == "structure" &&
                             (ctlChildNames.Contains("f") || ctlChildNames.Contains("i"));

        // Some vendor IEDs expose CSWI/XCBR/XSWI.Pos with a Boolean ctlVal while
        // the process feedback remains the standard double-point Pos.stVal. Preserve
        // Open/Closed semantics for the user and only adapt the wire value here.
        if (signal.IsPositionControl && mmsType == "boolean")
        {
            if (normalized.Contains("open") || normalized is "off" or "false" or "0" or "01")
            {
                value = ArControl.Iec61850ControlValue.Boolean(false);
                expected = "Open [01]";
                expectedState = ArControl.Iec61850ControlStatusState.Open;
                return true;
            }
            if (normalized.Contains("close") || normalized.Contains("closed") || normalized is "on" or "true" or "1" or "10")
            {
                value = ArControl.Iec61850ControlValue.Boolean(true);
                expected = "Closed [10]";
                expectedState = ArControl.Iec61850ControlStatusState.Closed;
                return true;
            }
            error = "Position command value must be Open or Close.";
            return false;
        }

        if (cdc == "DPC" || (mmsType == "bit-string" && descriptor.CtlValSpecification.Size == 2))
        {
            if (normalized.Contains("open") || normalized is "off" or "1" or "01")
            {
                value = ArControl.Iec61850ControlValue.Open();
                expected = "Open [01]";
                expectedState = ArControl.Iec61850ControlStatusState.Open;
                return true;
            }
            if (normalized.Contains("close") || normalized.Contains("closed") || normalized is "on" or "2" or "10")
            {
                value = ArControl.Iec61850ControlValue.Close();
                expected = "Closed [10]";
                expectedState = ArControl.Iec61850ControlStatusState.Closed;
                return true;
            }
            error = "DPC command value must be Open [01] or Closed [10].";
            return false;
        }

        if (cdc == "SPC" || mmsType == "boolean")
        {
            var isPulseRaise = signalText.Contains("TapOpR", StringComparison.OrdinalIgnoreCase) ||
                               signalText.Contains("Raise", StringComparison.OrdinalIgnoreCase);
            var isPulseLower = signalText.Contains("TapOpL", StringComparison.OrdinalIgnoreCase) ||
                               signalText.Contains("Lower", StringComparison.OrdinalIgnoreCase);
            if (normalized is "raise" or "up" || isPulseRaise)
            {
                value = ArControl.Iec61850ControlValue.On();
                expected = "Raise";
                return true;
            }
            if (normalized is "lower" or "down" || isPulseLower)
            {
                value = ArControl.Iec61850ControlValue.On();
                expected = "Lower";
                return true;
            }
            if (TryParseBooleanControl(normalized, out var boolean))
            {
                value = ArControl.Iec61850ControlValue.Boolean(boolean);
                expected = boolean ? "On" : "Off";
                expectedState = boolean
                    ? ArControl.Iec61850ControlStatusState.On
                    : ArControl.Iec61850ControlStatusState.Off;
                return true;
            }
            error = "SPC command value must be On/Off. Tap pulse objects use Raise or Lower.";
            return false;
        }

        if (cdc.Contains("INC", StringComparison.OrdinalIgnoreCase) ||
            cdc.Contains("ISC", StringComparison.OrdinalIgnoreCase) ||
            mmsType is "integer" or "unsigned" or "bcd")
        {
            if (normalized is "raise" or "up")
            {
                value = ArControl.Iec61850ControlValue.Raise();
                expected = "Raise";
                return true;
            }
            if (normalized is "lower" or "down")
            {
                value = ArControl.Iec61850ControlValue.Lower();
                expected = "Lower";
                return true;
            }
            if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
            {
                if (mmsType == "unsigned")
                {
                    if (integer < 0)
                    {
                        error = "The live ctlVal type is unsigned and cannot accept a negative value.";
                        return false;
                    }
                    value = ArControl.Iec61850ControlValue.Unsigned((ulong)integer);
                }
                else
                {
                    value = ArControl.Iec61850ControlValue.Integer(integer);
                }
                expected = integer.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            error = "Integer control requires Raise, Lower, or a whole-number target.";
            return false;
        }

        if (cdc == "BSC" || isBscStructure)
        {
            if (long.TryParse(normalized, NumberStyles.Integer, CultureInfo.InvariantCulture, out var position))
            {
                value = ArControl.Iec61850ControlValue.StepPosition(position);
                expected = position.ToString(CultureInfo.InvariantCulture);
                return true;
            }
            error = "BSC command requires a numeric target position.";
            return false;
        }

        if (cdc == "APC" || mmsType == "floating-point" || isApcStructure)
        {
            var numeric = normalized.Replace(',', '.');
            if (double.TryParse(numeric, NumberStyles.Float, CultureInfo.InvariantCulture, out var analogue))
            {
                value = ArControl.Iec61850ControlValue.Analogue(analogue);
                expected = analogue.ToString("G", CultureInfo.InvariantCulture);
                return true;
            }
            error = "APC command requires a numeric setpoint.";
            return false;
        }

        error = $"ArIED has no safe typed command mapping for CDC '{descriptor.Cdc}' / ctlVal '{descriptor.CtlValSpecification.Signature}'.";
        return false;
    }

    private static bool TryParseBooleanControl(string value, out bool result)
    {
        if (value is "on" or "true" or "1" or "set" or "start")
        {
            result = true;
            return true;
        }
        if (value is "off" or "false" or "0" or "reset" or "stop")
        {
            result = false;
            return true;
        }
        return bool.TryParse(value, out result);
    }

    private static bool ControlFeedbackMatches(string cdc, string expected, string initial, string actual)
    {
        var normalizedCdc = (cdc ?? string.Empty).Trim().ToUpperInvariant();
        if (normalizedCdc == "DPC")
        {
            return Iec61850ValueFormatter.TryNormalizeDbpos(expected, out var expectedCode) &&
                   Iec61850ValueFormatter.TryNormalizeDbpos(actual, out var actualCode) &&
                   expectedCode == actualCode;
        }

        var expectedText = expected.Trim().ToLowerInvariant();
        if (expectedText is "raise" or "up" or "lower" or "down")
        {
            if (TryExtractNumber(initial, out var before) && TryExtractNumber(actual, out var after))
                return expectedText is "raise" or "up" ? after > before : after < before;
            return !string.Equals(initial?.Trim(), actual?.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        if (normalizedCdc == "SPC")
        {
            return TryParseBooleanControl(expectedText, out var expectedBool) &&
                   TryParseBooleanControl(actual.Trim().ToLowerInvariant(), out var actualBool) &&
                   expectedBool == actualBool;
        }

        if (TryExtractNumber(expected, out var expectedNumber) && TryExtractNumber(actual, out var actualNumber))
        {
            return Math.Abs(expectedNumber - actualNumber) <= Math.Max(0.0001d, Math.Abs(expectedNumber) * 0.001d);
        }

        return string.Equals(expected.Trim(), actual.Trim(), StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryExtractNumber(string? text, out double value)
    {
        value = 0;
        var match = Regex.Match(text ?? string.Empty, @"[-+]?\d+(?:[\.,]\d+)?");
        if (!match.Success)
            return false;
        var normalized = match.Value.Replace(',', '.');
        return double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static Iec61850ControlModelKind MapControlModel(ArControl.Iec61850ControlModel model)
        => model switch
        {
            ArControl.Iec61850ControlModel.StatusOnly => Iec61850ControlModelKind.StatusOnly,
            ArControl.Iec61850ControlModel.DirectNormal => Iec61850ControlModelKind.DirectNormal,
            ArControl.Iec61850ControlModel.SelectBeforeOperateNormal => Iec61850ControlModelKind.SboNormal,
            ArControl.Iec61850ControlModel.DirectEnhanced => Iec61850ControlModelKind.DirectEnhanced,
            ArControl.Iec61850ControlModel.SelectBeforeOperateEnhanced => Iec61850ControlModelKind.SboEnhanced,
            _ => Iec61850ControlModelKind.Unknown
        };

    private static string FriendlyControlModel(ArControl.Iec61850ControlModel model)
        => model switch
        {
            ArControl.Iec61850ControlModel.DirectNormal => "Direct Operate (DO) • Normal security",
            ArControl.Iec61850ControlModel.SelectBeforeOperateNormal => "Select Before Operate (SBO) • Normal security",
            ArControl.Iec61850ControlModel.DirectEnhanced => "Direct Operate (DO) • Enhanced security",
            ArControl.Iec61850ControlModel.SelectBeforeOperateEnhanced => "Select Before Operate (SBO) • Enhanced security",
            ArControl.Iec61850ControlModel.StatusOnly => "Status only",
            _ => "Unknown"
        };

    private static string FriendlyControlSequence(ArControl.Iec61850ControlModel model)
        => model switch
        {
            ArControl.Iec61850ControlModel.DirectNormal => "Operate → MMS acceptance → process feedback",
            ArControl.Iec61850ControlModel.SelectBeforeOperateNormal => "SBO Select → Operate → MMS acceptance → process feedback",
            ArControl.Iec61850ControlModel.DirectEnhanced => "Operate → CommandTermination → process feedback",
            ArControl.Iec61850ControlModel.SelectBeforeOperateEnhanced => "SBOw → Operate → CommandTermination → process feedback",
            _ => "No safe command sequence available"
        };

    private static string MapControlValueType(ArControl.Iec61850ControlObjectDescriptor descriptor)
    {
        var cdc = (descriptor.Cdc ?? string.Empty).Trim().ToUpperInvariant();
        return cdc switch
        {
            "DPC" => "Dbpos",
            "SPC" => "Boolean",
            "BSC" => "ValWithTrans",
            "APC" => descriptor.CtlValSpecification.MmsType,
            _ when cdc.Contains("INC", StringComparison.OrdinalIgnoreCase) || cdc.Contains("ISC", StringComparison.OrdinalIgnoreCase)
                => descriptor.CtlValSpecification.MmsType,
            _ => descriptor.CtlValSpecification.Signature
        };
    }

    private static ArControl.Iec61850OriginCategory ParseOriginCategory(string? text)
        => Enum.TryParse<ArControl.Iec61850OriginCategory>(text, true, out var category)
            ? category
            : ArControl.Iec61850OriginCategory.Maintenance;

    private static string FormatControlTimeout(TimeSpan? timeout)
        => timeout.HasValue ? $"{timeout.Value.TotalSeconds:0.###} s" : "-";

    private static string FriendlyCompletionStage(ArControl.Iec61850ControlActionResult result)
        => result.CompletionState switch
        {
            ArControl.Iec61850ControlCompletionState.PositiveTermination => "Positive CommandTermination",
            ArControl.Iec61850ControlCompletionState.NegativeTermination => "Negative CommandTermination",
            ArControl.Iec61850ControlCompletionState.Accepted => "Control accepted",
            ArControl.Iec61850ControlCompletionState.Rejected => "Control rejected",
            ArControl.Iec61850ControlCompletionState.TimedOut => "Control timeout",
            ArControl.Iec61850ControlCompletionState.AssociationLost => "Association lost",
            ArControl.Iec61850ControlCompletionState.Cancelled => "Control cancelled",
            ArControl.Iec61850ControlCompletionState.Unsupported => "Control unsupported",
            _ => result.CompletionState.ToString()
        };

    private static string BuildNativeControlFailureMessage(ArControl.Iec61850ControlActionResult result)
    {
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(result.ClientError))
            parts.Add(result.ClientError);
        if (!string.IsNullOrWhiteSpace(result.ControlError))
            parts.Add($"ControlError={result.ControlError}");
        if (!string.IsNullOrWhiteSpace(result.AddCause))
            parts.Add($"AddCause={result.AddCause}");
        if (!string.IsNullOrWhiteSpace(result.LastApplErrorText))
            parts.Add(result.LastApplErrorText);
        if (parts.Count == 0)
            parts.Add($"IEC 61850 control ended with {result.CompletionState}.");
        return string.Join(" • ", parts);
    }

    private static Iec61850ControlCommandResult MapNativeControlResult(
        ArControl.Iec61850ControlActionResult result,
        Iec61850ControlCapabilities capabilities,
        string requestedValue,
        string feedbackValue,
        bool feedbackConfirmed,
        string stage,
        string message,
        bool isSuccess,
        TimeSpan? feedbackElapsed = null,
        TimeSpan? totalElapsed = null)
        => new()
        {
            IsSuccess = isSuccess,
            ServiceAccepted = result.RequestAccepted,
            FeedbackConfirmed = feedbackConfirmed,
            CommandTerminationReceived = result.CommandTerminationReceived,
            PositiveTermination = result.PositiveTermination,
            CompletionState = result.CompletionState.ToString(),
            Stage = stage,
            Message = message,
            ControlModelText = capabilities.ControlModelText,
            SequenceText = capabilities.SequenceText,
            RequestedValue = requestedValue,
            FeedbackValue = string.IsNullOrWhiteSpace(feedbackValue) ? "-" : feedbackValue,
            ControlError = result.ControlError,
            AddCause = result.AddCause,
            LastApplErrorText = result.LastApplErrorText,
            ClientError = result.ClientError,
            ControlNumber = result.ControlNumber?.ToString(CultureInfo.InvariantCulture) ?? "-",
            SequenceTimestamp = result.SequenceTimestamp?.ToString("O", CultureInfo.InvariantCulture) ?? "-",
            ElapsedText = $"{result.Elapsed.TotalMilliseconds:0.###} ms",
            FeedbackElapsedText = feedbackElapsed.HasValue ? $"{feedbackElapsed.Value.TotalMilliseconds:0.###} ms" : "-",
            TotalElapsedText = totalElapsed.HasValue ? $"{totalElapsed.Value.TotalMilliseconds:0.###} ms" : $"{result.Elapsed.TotalMilliseconds:0.###} ms",
            RequestHex = result.RequestHex,
            ResponseHex = result.ResponseHex
        };

    private static string InferControlCdc(
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        IReadOnlyList<LiveIedDataAttributeModel> attributes,
        bool hasControlOperation)
    {
        var objectName = (dataObject.Name ?? string.Empty).Trim().ToUpperInvariant();
        var lnClass = string.IsNullOrWhiteSpace(logicalNode.LnClass)
            ? SignalDefinition.DetectLogicalNodeClass(logicalNode.Name).ToUpperInvariant()
            : logicalNode.LnClass.Trim().ToUpperInvariant();

        if (objectName == "POS" && lnClass is "CSWI" or "XCBR" or "XSWI")
            return "DPC";
        if (objectName.Contains("TAPOPR") || objectName.Contains("TAPOPL") ||
            objectName.Contains("RAISE") || objectName.Contains("LOWER"))
            return "SPC";
        if (objectName.Contains("TAPCHG") || objectName.Contains("STEP"))
            return "INC";

        var ctlVal = attributes.FirstOrDefault(attribute =>
            (attribute.AttributePath ?? string.Empty).EndsWith("ctlVal", StringComparison.OrdinalIgnoreCase) ||
            (attribute.ObjectReference ?? string.Empty).EndsWith(".ctlVal", StringComparison.OrdinalIgnoreCase));
        var typeText = $"{ctlVal?.SclBType} {ctlVal?.MmsType} {ctlVal?.MmsTypeSignature}".ToUpperInvariant();
        if (typeText.Contains("BOOLEAN")) return "SPC";
        if (typeText.Contains("FLOAT")) return "APC";
        if (typeText.Contains("BIT") && objectName.Contains("POS")) return "DPC";
        if ((typeText.Contains("INTEGER") || typeText.Contains("INT")) && objectName.Contains("POS")) return "DPC";
        if (typeText.Contains("INTEGER") || typeText.Contains("INT")) return "INC";

        // A CO operation tree is still kept as a generic control object. The command
        // dialog will read ctlModel and refuses to operate when the CDC/value type
        // cannot be established safely.
        return hasControlOperation ? string.Empty : string.Empty;
    }

    private static string BuildControlStatusReference(string reference, string cdc)
    {
        var target = (reference ?? string.Empty).Trim().TrimEnd('.');
        if (string.IsNullOrWhiteSpace(target))
            return string.Empty;

        var normalized = (cdc ?? string.Empty).Trim().ToUpperInvariant();
        if (normalized == "BSC")
            return $"{target}.valWTr.posVal";

        if (normalized == "SPC" &&
            (target.EndsWith(".TapOpR", StringComparison.OrdinalIgnoreCase) ||
             target.EndsWith(".TapOpL", StringComparison.OrdinalIgnoreCase)))
        {
            var objectSeparator = target.LastIndexOf('.');
            return objectSeparator > 0
                ? $"{target[..objectSeparator]}.TapChg.valWTr.posVal"
                : string.Empty;
        }

        return normalized is "DPC" or "SPC" or "INC" or "ISC"
            ? $"{target}.stVal"
            : string.Empty;
    }

    private static Iec61850ControlCommandResult ControlAlreadyAtRequestedState(
        Iec61850ControlCapabilities capabilities,
        string requestedValue,
        string currentValue,
        TimeSpan elapsed)
        => new()
        {
            IsSuccess = true,
            ServiceAccepted = false,
            FeedbackConfirmed = true,
            CommandTerminationReceived = false,
            PositiveTermination = false,
            CompletionState = "NotSent",
            Stage = "Already at requested state",
            Message = $"Live MMS preflight confirmed {currentValue}; no SBOw/Operate command was sent.",
            ControlModelText = capabilities.ControlModelText,
            SequenceText = "Live preflight only • no command sent",
            RequestedValue = requestedValue,
            FeedbackValue = currentValue,
            ElapsedText = "0 ms",
            FeedbackElapsedText = "0 ms",
            TotalElapsedText = $"{elapsed.TotalMilliseconds:0.###} ms"
        };

    private static Iec61850ControlCommandResult ControlFailure(
        string stage,
        string message,
        Iec61850ControlCapabilities capabilities,
        string requestedValue)
        => new()
        {
            IsSuccess = false,
            ServiceAccepted = false,
            FeedbackConfirmed = false,
            CompletionState = "Rejected",
            Stage = stage,
            Message = message,
            ControlModelText = capabilities.ControlModelText,
            SequenceText = capabilities.SequenceText,
            RequestedValue = requestedValue,
            FeedbackValue = capabilities.CurrentValue
        };

    public async ValueTask DisposeAsync()
    {
        await DisposeControlSessionsAsync().ConfigureAwait(false);
        await StopReportMonitorsAsync().ConfigureAwait(false);
        await _mmsIoGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _session.DisposeAsync().ConfigureAwait(false);
        }
        finally
        {
            _mmsIoGate.Release();
        }
        _mmsIoGate.Dispose();
        _controlSessionGate.Dispose();
        _controlCommandGate.Dispose();
    }

    private async Task<ArControl.Iec61850ControlObjectSession> GetOrOpenControlSessionAsync(
        SignalDefinition signal,
        CancellationToken cancellationToken)
    {
        var key = NormalizeControlObjectKey(signal.ObjectReference);
        await _controlSessionGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_controlSessions.TryGetValue(key, out var existing))
                return existing;

            var service = new ArControl.Iec61850ControlService();
            var opened = await RunMmsOperationAsync(
                () => service.OpenAsync(_session, signal.ObjectReference, cancellationToken),
                cancellationToken).ConfigureAwait(false);
            _controlSessions[key] = opened;
            return opened;
        }
        finally
        {
            _controlSessionGate.Release();
        }
    }

    private async Task DisposeControlSessionsAsync()
    {
        List<ArControl.Iec61850ControlObjectSession> sessions;
        await _controlSessionGate.WaitAsync().ConfigureAwait(false);
        try
        {
            sessions = _controlSessions.Values.Distinct().ToList();
            _controlSessions.Clear();
        }
        finally
        {
            _controlSessionGate.Release();
        }

        foreach (var session in sessions)
        {
            try { await session.DisposeAsync().ConfigureAwait(false); }
            catch { /* best-effort cleanup during reconnect/shutdown */ }
        }
    }

    private static string NormalizeControlObjectKey(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.').ToLowerInvariant();

    private static string ResolveControlSemanticCdc(
        ArControl.Iec61850ControlObjectDescriptor descriptor,
        SignalDefinition signal)
        => signal.IsPositionControl ? "DPC" : descriptor.Cdc;

    private static (string Value, ArControl.Iec61850ControlStatusState State) NormalizeControlFeedback(
        string cdc,
        string? value,
        ArControl.Iec61850ControlStatusState state)
    {
        var text = string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
        if (cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) &&
            Iec61850ValueFormatter.TryNormalizeDbpos(text, out var code))
        {
            return code switch
            {
                0 => ("Intermediate", ArControl.Iec61850ControlStatusState.Intermediate),
                1 => ("Open", ArControl.Iec61850ControlStatusState.Open),
                2 => ("Closed", ArControl.Iec61850ControlStatusState.Closed),
                3 => ("Bad state", ArControl.Iec61850ControlStatusState.Bad),
                _ => (text, state)
            };
        }

        if (cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) && TryParseBooleanControl(text.ToLowerInvariant(), out var boolean))
            return (boolean ? "True" : "False", boolean ? ArControl.Iec61850ControlStatusState.On : ArControl.Iec61850ControlStatusState.Off);

        return (text, state);
    }

    private readonly record struct LogicalNodeSiblingKey(string Domain, string Prefix, string LogicalNodeClass, int InstanceWidth);
    private readonly record struct LogicalNodeNameParts(string Prefix, string LogicalNodeClass, int Instance, int InstanceWidth);

    private async Task<int> AddAdaptiveLogicalNodeSiblingProbeSignalsAsync(
        ICollection<SignalDefinition> signals,
        CancellationToken cancellationToken)
    {
        if (!_session.IsMmsInitiated || signals.Count == 0)
            return 0;

        var observed = new Dictionary<LogicalNodeSiblingKey, SortedSet<int>>();
        foreach (var signal in signals)
        {
            var domain = ExtractDomain(signal.ObjectReference);
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(signal.LogicalNode))
                continue;
            if (!TryParseLogicalNodeName(signal.LogicalNode, out var parts))
                continue;
            if (!ShouldAdaptiveProbeLogicalNodeClass(parts.LogicalNodeClass))
                continue;

            var key = new LogicalNodeSiblingKey(domain, parts.Prefix, parts.LogicalNodeClass, parts.InstanceWidth);
            if (!observed.TryGetValue(key, out var set))
            {
                set = new SortedSet<int>();
                observed[key] = set;
            }
            set.Add(parts.Instance);
        }

        var addedSignals = 0;
        foreach (var group in observed.OrderBy(g => g.Key.Domain, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(g => g.Key.Prefix, StringComparer.OrdinalIgnoreCase)
                                      .ThenBy(g => g.Key.LogicalNodeClass, StringComparer.OrdinalIgnoreCase))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (group.Value.Count == 0) continue;

            var maxObserved = group.Value.Max;
            var consecutiveMisses = 0;
            var instance = Math.Max(1, group.Value.Min);
            var hardSafetyLimit = Math.Max(maxObserved + 64, 256);

            while (instance <= hardSafetyLimit && consecutiveMisses < 10)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (group.Value.Contains(instance))
                {
                    instance++;
                    consecutiveMisses = 0;
                    continue;
                }

                var logicalNode = BuildLogicalNodeName(group.Key.Prefix, group.Key.LogicalNodeClass, instance, group.Key.InstanceWidth);
                var exists = signals.Any(s =>
                    ExtractDomain(s.ObjectReference).Equals(group.Key.Domain, StringComparison.OrdinalIgnoreCase) &&
                    s.LogicalNode.Equals(logicalNode, StringComparison.OrdinalIgnoreCase));
                if (exists)
                {
                    group.Value.Add(instance);
                    instance++;
                    consecutiveMisses = 0;
                    continue;
                }

                var found = await ProbeLogicalNodeInstanceExistsAsync(
                    group.Key.Domain,
                    logicalNode,
                    group.Key.LogicalNodeClass,
                    cancellationToken).ConfigureAwait(false);

                if (!found)
                {
                    consecutiveMisses++;
                    instance++;
                    continue;
                }

                group.Value.Add(instance);
                consecutiveMisses = 0;
                var now = DateTime.Now;
                foreach (var point in BuildArIecLogicalNodeFallbackPoints(group.Key.LogicalNodeClass))
                {
                    var reference = $"{group.Key.Domain}/{logicalNode}.{point.Path}";
                    if (signals.Any(s => ReferencesEqual(s.ObjectReference, reference)))
                        continue;

                    signals.Add(CreateArIecSignal(
                        reference,
                        point.FunctionalConstraint,
                        point.Category,
                        group.Key.LogicalNodeClass,
                        point.DataObject,
                        string.Empty,
                        now,
                        "Adaptive IEC 61850 LN sibling proof-probe"));
                    addedSignals++;
                }

                instance++;
            }
        }

        return addedSignals;
    }

    private async Task<int> AddPrimaryEquipmentDomainProbeSignalsAsync(
        ICollection<SignalDefinition> signals,
        NativeMmsDiscoverySnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (!_session.IsMmsInitiated)
            return 0;

        var domains = snapshot.DomainVariables.Keys
            .Concat(snapshot.DomainVariableLists.Keys)
            .Concat(signals.Select(s => ExtractDomain(s.ObjectReference)))
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var added = 0;

        foreach (var domain in domains)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var match = System.Text.RegularExpressions.Regex.Match(
                domain,
                @"CB(?<instance>\d+)$",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (!match.Success)
                continue;

            var instance = match.Groups["instance"].Value.TrimStart('0');
            if (string.IsNullOrWhiteSpace(instance)) instance = "1";
            var logicalNode = $"XCBR{instance}";
            var reference = $"{domain}/{logicalNode}.Pos.stVal";
            if (!signals.Any(s => ReferencesEqual(s.ObjectReference, reference)))
            {
                try
                {
                    var value = await ReadValueAsync(reference, "ST", "Dbpos", cancellationToken).ConfigureAwait(false);
                    if (value != null)
                    {
                        var signal = CreateArIecSignal(
                            reference,
                            "ST",
                            "DoublePointStatus",
                            "XCBR",
                            "Pos",
                            "DPC",
                            DateTime.Now,
                            "Primary-equipment logical-device proof-probe");
                        ApplyDiscoveryReadValue(signal, value);
                        signals.Add(signal);
                        added++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // A CB-like logical-device name is only a hint. Keep it out of the list unless Pos.stVal is proven readable.
                }
            }

            var breakerFailureReference = $"{domain}/RBRF1.OpEx.general";
            if (!signals.Any(s => ReferencesEqual(s.ObjectReference, breakerFailureReference)))
            {
                try
                {
                    var value = await ReadValueAsync(breakerFailureReference, "ST", "Boolean", cancellationToken).ConfigureAwait(false);
                    if (value != null)
                    {
                        var signal = CreateArIecSignal(
                            breakerFailureReference,
                            "ST",
                            "Protection",
                            "RBRF",
                            "OpEx",
                            "ACT",
                            DateTime.Now,
                            "Breaker-failure proof-probe");
                        ApplyDiscoveryReadValue(signal, value);
                        signals.Add(signal);
                        added++;
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // RBRF is optional. It is exposed only when the standard OpEx leaf is proven readable.
                }
            }
        }

        return added;
    }

    private async Task<int> ResolveOperationalValueReferencesAsync(
        IEnumerable<SignalDefinition> signals,
        CancellationToken cancellationToken)
    {
        var candidates = signals
            .Where(signal => signal.IsScadaCoreSignal &&
                             signal.ObjectReference.Contains("OperationalValues/", StringComparison.OrdinalIgnoreCase) &&
                             signal.ObjectReference.Contains("/PPRE_MMXU", StringComparison.OrdinalIgnoreCase) &&
                             signal.ObjectReference.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var corrected = 0;

        foreach (var signal in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var requestedReference = signal.ObjectReference;
                var resolved = await IecSignalReadResolver.ReadAsync(this, signal, cancellationToken).ConfigureAwait(false);
                if (resolved == null)
                    continue;

                if (IecSignalReadResolver.ApplyEffectiveReference(signal, resolved.EffectiveReference))
                    corrected++;
                ApplyDiscoveryReadValue(signal, resolved.Value);
                signal.ReportCoverageReason = $"Online proof-read resolved the readable OperationalValues leaf from {requestedReference}.";
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Leave the original discovery evidence available in Advanced raw; it will not be auto-selected after a failed proof-read.
                signal.IsSelected = false;
                signal.ProbeStatus = "Not readable";
            }
        }

        return corrected;
    }

    private async Task<int> EnrichEngineeringUnitsAsync(IReadOnlyCollection<SignalDefinition> signals, CancellationToken cancellationToken)
    {
        if (!_session.IsMmsInitiated)
            return 0;

        var groups = signals
            .Where(s => s.DataType.Equals("Float32", StringComparison.OrdinalIgnoreCase))
            .Select(s => new { Signal = s, Owner = GetEngineeringUnitOwner(s.ObjectReference) })
            .Where(x => !string.IsNullOrWhiteSpace(x.Owner))
            .GroupBy(x => x.Owner, StringComparer.OrdinalIgnoreCase)
            .Take(160)
            .ToList();
        var resolved = 0;

        foreach (var group in groups)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var siUnitValue = await ReadValueAsync($"{group.Key}.units.SIUnit", "CF", "Enum", cancellationToken).ConfigureAwait(false);
                var multiplierValue = await ReadValueAsync($"{group.Key}.units.multiplier", "CF", "Enum", cancellationToken).ConfigureAwait(false);

                var fallbackBaseUnit = group.Select(x => x.Signal.Unit).FirstOrDefault(u => !string.IsNullOrWhiteSpace(u)) ?? string.Empty;
                if (!TryResolveSiUnit(siUnitValue, fallbackBaseUnit, out var baseUnit))
                    continue;

                var prefix = TryResolveUnitMultiplier(multiplierValue, out var resolvedPrefix)
                    ? resolvedPrefix
                    : string.Empty;
                var unit = prefix + baseUnit;
                if (string.IsNullOrWhiteSpace(unit))
                    continue;

                foreach (var item in group)
                {
                    var previousUnit = item.Signal.Unit;
                    item.Signal.Unit = unit;
                    item.Signal.Value = ReplaceFormattedUnit(item.Signal.Value, previousUnit, unit);
                }
                resolved++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Unit metadata is optional on vendor models. Keep the conservative inferred unit if CF read is rejected.
            }
        }

        return resolved;
    }

    private static string ReplaceFormattedUnit(string value, string previousUnit, string resolvedUnit)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text is "-" or "Pending read" or "Read failed")
            return value ?? string.Empty;

        if (!string.IsNullOrWhiteSpace(previousUnit))
        {
            var suffix = $" {previousUnit}";
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return $"{text[..^suffix.Length]} {resolvedUnit}";
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? $"{text} {resolvedUnit}"
            : value ?? string.Empty;
    }

    private static string GetEngineeringUnitOwner(string reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        foreach (var suffix in new[] { ".instCVal.mag.f", ".cVal.mag.f", ".mag.f" })
        {
            if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return text[..^suffix.Length];
        }
        return string.Empty;
    }

    private static void ApplyDiscoveryReadValue(SignalDefinition signal, object value)
    {
        if (value is Iec61850ReadValue rich)
        {
            signal.Value = Iec61850ValueFormatter.Format(rich.Value ?? rich.ToString(), signal.DataType, signal.Unit);
            signal.Quality = rich.HasQuality ? rich.Quality : "Good";
            signal.DeviceTimestamp = rich.HasDeviceTimestamp ? rich.DeviceTimestamp : "-";
        }
        else
        {
            signal.Value = Iec61850ValueFormatter.Format(value, signal.DataType, signal.Unit);
            signal.Quality = "Good";
        }
        signal.ProbeStatus = "Readable";
        signal.Timestamp = DateTime.Now;
    }

    private static bool TryResolveSiUnit(object? value, string fallback, out string unit)
    {
        unit = string.Empty;
        var raw = Iec61850ReadValue.Unwrap(value);
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var ordinal))
        {
            unit = ordinal switch
            {
                5 => "A",
                9 => "deg",
                23 => "°C",
                29 => "V",
                30 => "Ω",
                33 => "Hz",
                38 => "W",
                55 => "VA",
                57 => "var",
                _ => string.Empty
            };
        }
        else
        {
            var normalized = text.Replace(" ", string.Empty).ToLowerInvariant();
            unit = normalized switch
            {
                "a" or "amp" or "ampere" => "A",
                "v" or "volt" => "V",
                "hz" or "hertz" => "Hz",
                "w" or "watt" => "W",
                "va" => "VA",
                "var" => "var",
                "deg" or "degree" => "deg",
                "°c" or "celsius" => "°C",
                "ohm" or "ω" => "Ω",
                _ => string.Empty
            };
        }

        if (string.IsNullOrWhiteSpace(unit))
            unit = fallback;
        return !string.IsNullOrWhiteSpace(unit);
    }

    private static bool TryResolveUnitMultiplier(object? value, out string prefix)
    {
        prefix = string.Empty;
        if (value == null)
            return false;

        var raw = Iec61850ReadValue.Unwrap(value);
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture)?.Trim() ?? string.Empty;
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var exponent))
        {
            prefix = exponent switch
            {
                -12 => "p",
                -9 => "n",
                -6 => "µ",
                -3 => "m",
                -2 => "c",
                -1 => "d",
                0 => string.Empty,
                1 => "da",
                2 => "h",
                3 => "k",
                6 => "M",
                9 => "G",
                12 => "T",
                _ => string.Empty
            };
            return exponent is -12 or -9 or -6 or -3 or -2 or -1 or 0 or 1 or 2 or 3 or 6 or 9 or 12;
        }

        var normalized = text.Replace(" ", string.Empty).ToLowerInvariant();
        prefix = normalized switch
        {
            "" or "none" or "null" => string.Empty,
            "p" or "pico" => "p",
            "n" or "nano" => "n",
            "u" or "µ" or "micro" => "µ",
            "m" or "milli" => "m",
            "c" or "centi" => "c",
            "d" or "deci" => "d",
            "da" or "deca" => "da",
            "h" or "hecto" => "h",
            "k" or "kilo" => "k",
            "mega" => "M",
            "g" or "giga" => "G",
            "t" or "tera" => "T",
            _ => string.Empty
        };
        return normalized is "" or "none" or "null" or "p" or "pico" or "n" or "nano" or "u" or "µ" or "micro" or "m" or "milli" or "c" or "centi" or "d" or "deci" or "da" or "deca" or "h" or "hecto" or "k" or "kilo" or "mega" or "g" or "giga" or "t" or "tera";
    }

    private static bool ShouldAdaptiveProbeLogicalNodeClass(string logicalNodeClass)
    {
        var cls = (logicalNodeClass ?? string.Empty).Trim().ToUpperInvariant();
        return cls is "MMXU" or "MMXN" or "MSQI" or "GGIO" or
               "CSWI" or "XCBR" or "XSWI" or
               "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF" or "RBRF";
    }

    private async Task<bool> ProbeLogicalNodeInstanceExistsAsync(
        string domain,
        string logicalNode,
        string logicalNodeClass,
        CancellationToken cancellationToken)
    {
        foreach (var point in BuildAdaptiveProbePoints(logicalNodeClass))
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                var reference = $"{domain}/{logicalNode}.{point.Path}";
                var value = await ReadValueAsync(reference, point.FunctionalConstraint, point.DataType, cancellationToken).ConfigureAwait(false);
                if (value != null)
                    return true;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Missing objects and vendor-specific rejects are normal while probing sibling LNs.
            }
        }

        return false;
    }

    private readonly record struct AdaptiveProbePoint(string Path, string FunctionalConstraint, string DataType);

    private static IEnumerable<AdaptiveProbePoint> BuildAdaptiveProbePoints(string logicalNodeClass)
    {
        var cls = (logicalNodeClass ?? string.Empty).Trim().ToUpperInvariant();
        if (cls is "MMXU" or "MMXN" or "MSQI")
        {
            yield return new("PhV.phsA.cVal.mag.f", "MX", "Float32");
            yield return new("PhV.phsA.instCVal.mag.f", "MX", "Float32");
            yield return new("A.phsA.cVal.mag.f", "MX", "Float32");
            yield return new("A.phsA.instCVal.mag.f", "MX", "Float32");
            yield return new("PPV.phsAB.cVal.mag.f", "MX", "Float32");
            yield return new("PPV.phsAB.instCVal.mag.f", "MX", "Float32");
            yield break;
        }

        if (cls == "GGIO")
        {
            yield return new("Ind1.stVal", "ST", "Boolean");
            yield return new("AnIn1.mag.f", "MX", "Float32");
            yield break;
        }

        if (cls is "CSWI" or "XCBR" or "XSWI")
        {
            yield return new("Pos.stVal", "ST", "Enum");
            yield break;
        }

        if (cls == "PTRC")
        {
            yield return new("Tr.general", "ST", "Boolean");
            yield break;
        }

        if (cls == "RBRF")
        {
            yield return new("OpEx.general", "ST", "Boolean");
            yield break;
        }

        if (cls.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            yield return new("Op.general", "ST", "Boolean");
            yield return new("Str.general", "ST", "Boolean");
        }
    }

    private static bool TryParseLogicalNodeName(string logicalNode, out LogicalNodeNameParts parts)
    {
        parts = default;
        var text = (logicalNode ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var classes = new[]
        {
            "MMXU", "MMXN", "MSQI", "GGIO", "CSWI", "XCBR", "XSWI",
            "PTOC", "PTRC", "PDIF", "PDIS", "PIOC", "PTOV", "PTUV", "PTEF", "PDEF", "RBRF"
        };

        foreach (var cls in classes.OrderByDescending(c => c.Length))
        {
            var index = text.LastIndexOf(cls, StringComparison.OrdinalIgnoreCase);
            if (index < 0)
                continue;

            var prefix = text[..index];
            var suffix = text[(index + cls.Length)..];
            if (string.IsNullOrWhiteSpace(suffix) || !suffix.All(char.IsDigit))
                continue;
            if (!int.TryParse(suffix, out var instance) || instance <= 0)
                continue;

            parts = new LogicalNodeNameParts(prefix, cls, instance, suffix.Length);
            return true;
        }

        return false;
    }

    private static string BuildLogicalNodeName(string prefix, string logicalNodeClass, int instance, int width)
    {
        var instanceText = instance.ToString(CultureInfo.InvariantCulture);
        if (width > 1)
            instanceText = instanceText.PadLeft(width, '0');
        return $"{prefix}{logicalNodeClass}{instanceText}";
    }


    private async Task<NativeMmsDiscoverySnapshot> TryBuildSupplementalGetNameListSnapshotAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_host))
            return new NativeMmsDiscoverySnapshot();

        // Some vendor IEDs expose the full logical-node directory through plain MMS
        // GetNameList even when the higher-level ARIEC61850 model builder only returns
        // the first readable branch.  Use a short, independent read-only association as
        // a supplemental directory browse.  If the IED allows only one client, this fails
        // silently and the primary ARIEC61850 discovery remains authoritative.
        await using var supplemental = new ArIED61850Tester.Protocol.Iec61850.NativeIec61850Session();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(12));

        try
        {
            await supplemental.ConnectAsync(_host, _port <= 0 ? 102 : _port, timeout.Token).ConfigureAwait(false);
            if (!supplemental.IsMmsInitiated)
                return new NativeMmsDiscoverySnapshot();

            var domainVariables = await supplemental.DiscoverDomainVariableNamesAsync(timeout.Token).ConfigureAwait(false);
            var typeTreeVariables = await supplemental.DiscoverDomainVariableTypeTreeNamesAsync(domainVariables, timeout.Token).ConfigureAwait(false);
            var domainVariableLists = await supplemental.DiscoverDomainVariableListNamesAsync(timeout.Token).ConfigureAwait(false);
            return new NativeMmsDiscoverySnapshot
            {
                DomainVariables = MergeDomainNameMaps(domainVariables, typeTreeVariables),
                DomainVariableLists = domainVariableLists
            };
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Discovery must never fail only because the supplemental browse was rejected.
            return new NativeMmsDiscoverySnapshot();
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new NativeMmsDiscoverySnapshot();
        }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> MergeDomainNameMaps(
        IReadOnlyDictionary<string, IReadOnlyList<string>> first,
        IReadOnlyDictionary<string, IReadOnlyList<string>> second)
    {
        var merged = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        void Add(IReadOnlyDictionary<string, IReadOnlyList<string>> source)
        {
            foreach (var pair in source)
            {
                var domain = (pair.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(domain)) continue;
                if (!merged.TryGetValue(domain, out var set))
                {
                    set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    merged[domain] = set;
                }
                foreach (var name in pair.Value)
                {
                    var text = (name ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(text)) set.Add(text);
                }
            }
        }

        Add(first);
        Add(second);
        return merged.ToDictionary(k => k.Key, v => (IReadOnlyList<string>)v.Value.ToList(), StringComparer.OrdinalIgnoreCase);
    }

    private static NativeMmsDiscoverySnapshot MergeDiscoverySnapshots(params NativeMmsDiscoverySnapshot?[] snapshots)
    {
        var variables = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, SortedSet<string>>(StringComparer.OrdinalIgnoreCase);

        static void MergeInto(
            Dictionary<string, SortedSet<string>> target,
            IReadOnlyDictionary<string, IReadOnlyList<string>> source)
        {
            foreach (var pair in source)
            {
                var domain = (pair.Key ?? string.Empty).Trim();
                if (string.IsNullOrWhiteSpace(domain)) continue;
                if (!target.TryGetValue(domain, out var set))
                {
                    set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
                    target[domain] = set;
                }

                foreach (var item in pair.Value)
                {
                    var text = (item ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(text)) set.Add(text);
                }
            }
        }

        foreach (var snapshot in snapshots)
        {
            if (snapshot == null) continue;
            MergeInto(variables, snapshot.DomainVariables);
            MergeInto(lists, snapshot.DomainVariableLists);
        }

        return new NativeMmsDiscoverySnapshot
        {
            DomainVariables = variables.ToDictionary(k => k.Key, v => (IReadOnlyList<string>)v.Value.ToList(), StringComparer.OrdinalIgnoreCase),
            DomainVariableLists = lists.ToDictionary(k => k.Key, v => (IReadOnlyList<string>)v.Value.ToList(), StringComparer.OrdinalIgnoreCase)
        };
    }

    private static IReadOnlyList<SignalDefinition> BuildSignalsFromArIecModel(
        LiveIedModelDiscoveryDocument model,
        NativeMmsDiscoverySnapshot fallbackSnapshot)
    {
        var now = DateTime.Now;
        var signals = new List<SignalDefinition>();

        foreach (var logicalDevice in model.LogicalDevices)
        {
            var logicalDeviceDomainHint = ResolveLogicalDeviceDomainHint(logicalDevice);

            foreach (var logicalNode in logicalDevice.LogicalNodes)
            {
                foreach (var dataObject in logicalNode.DataObjects)
                {
                    AddArIecSmartTargets(signals, logicalNode, dataObject, now);
                    AddArIecControlTarget(signals, logicalNode, dataObject, now);
                    AddArIecAvrSemanticTargets(signals, logicalNode, dataObject, now);
                    var dataObjectDomain = ExtractDomain(dataObject.Reference);
                    if (!string.IsNullOrWhiteSpace(dataObjectDomain) &&
                        (string.IsNullOrWhiteSpace(logicalDeviceDomainHint) ||
                         dataObjectDomain.EndsWith(logicalDeviceDomainHint, StringComparison.OrdinalIgnoreCase) ||
                         logicalDeviceDomainHint.Equals("LD0", StringComparison.OrdinalIgnoreCase) ||
                         logicalDeviceDomainHint.Equals("LD01", StringComparison.OrdinalIgnoreCase)))
                    {
                        logicalDeviceDomainHint = dataObjectDomain;
                    }
                }

                // Some IEDs expose the LN shell and DataSet/RCB correctly, but their online
                // data-object tree is shallow. Keep every logical-node instance separate; the
                // later probe/runtime decides readable vs polling fallback.
                AddArIecLogicalNodeFallbackTargets(signals, logicalDevice, logicalNode, logicalDeviceDomainHint, now);
            }
        }

        foreach (var fallback in NativeMmsDiscoveryMapper.BuildSignals(fallbackSnapshot))
        {
            if (!signals.Any(s => ReferencesEqual(s.ObjectReference, fallback.ObjectReference)))
                signals.Add(fallback);
        }

        return FinalizeDiscoveredSignals(signals);
    }


    private static IReadOnlyList<SignalDefinition> FinalizeDiscoveredSignals(IEnumerable<SignalDefinition> signals)
    {
        var expanded = signals.ToList();
        AddValidatedControlCandidatesFromStatus(expanded);

        return expanded
            .Where(s => s.DataType != "Directory")
            .Where(s => !s.IsControlSignal || IsValidControlObjectReference(s.ObjectReference))
            .Where(s => s.IsControlSignal || IsTesterReadableSignal(s))
            .GroupBy(s => NormalizeReference(s.ObjectReference), StringComparer.OrdinalIgnoreCase)
            .Select(g => g
                .OrderByDescending(x => x.Source.StartsWith("ARIEC61850", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.IsControlSignal)
                .ThenByDescending(x => x.IsScadaCoreSignal)
                .ThenByDescending(x => ConfidenceScore(x.Confidence))
                .First())
            .OrderBy(s => s.SortPriority)
            .ThenByDescending(s => ConfidenceScore(s.Confidence))
            .ThenBy(s => s.LogicalNode, StringComparer.OrdinalIgnoreCase)
            .ThenBy(s => s.ObjectReference, StringComparer.OrdinalIgnoreCase)
            .Take(12000)
            .ToList();
    }

    private static void AddValidatedControlCandidatesFromStatus(ICollection<SignalDefinition> signals)
    {
        var existing = signals
            .Where(signal => signal.IsControlSignal)
            .Select(signal => NormalizeReference(signal.ObjectReference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var status in signals.Where(signal => !signal.IsControlSignal).ToArray())
        {
            var reference = (status.ObjectReference ?? string.Empty).Trim().Replace('$', '.');
            if (string.IsNullOrWhiteSpace(reference))
                continue;

            string controlReference;
            string cdc;
            if (reference.EndsWith(".Pos.stVal", StringComparison.OrdinalIgnoreCase) &&
                status.LogicalNodeClass is "CSWI" or "XCBR" or "XSWI")
            {
                controlReference = reference[..^6];
                cdc = "DPC";
            }
            else if ((reference.EndsWith(".TapOpR.stVal", StringComparison.OrdinalIgnoreCase) ||
                      reference.EndsWith(".TapOpL.stVal", StringComparison.OrdinalIgnoreCase)) &&
                     status.LogicalNodeClass is "ATCC" or "AVC" or "AVCO")
            {
                controlReference = reference[..^6];
                cdc = "SPC";
            }
            else
            {
                continue;
            }

            if (!existing.Add(NormalizeReference(controlReference)))
                continue;

            var objectName = controlReference[(controlReference.LastIndexOf('.') + 1)..];
            signals.Add(new SignalDefinition
            {
                Name = $"{status.LogicalNode} {objectName} Control",
                ObjectReference = controlReference,
                FunctionalConstraint = "CO",
                DataType = $"{cdc} Control",
                Category = "Control",
                Confidence = "Medium",
                IsSelected = false,
                IsControlSignal = true,
                ControlCdc = cdc,
                ControlModelReference = $"{controlReference}.ctlModel",
                ControlStatusReference = cdc == "SPC" &&
                                         (controlReference.EndsWith(".TapOpR", StringComparison.OrdinalIgnoreCase) ||
                                          controlReference.EndsWith(".TapOpL", StringComparison.OrdinalIgnoreCase))
                    ? $"{controlReference[..controlReference.LastIndexOf('.')]}.TapChg.valWTr.posVal"
                    : reference,
                ControlModelText = "Validate ctlModel on command",
                ControlValueType = InferControlValueType(cdc),
                IsReportCapable = false,
                ReportCoverage = "Command object",
                ReportCoverageReason = "Control candidate inferred from a standard controllable status object. ctlModel is read live and must validate before ArIED enables command execution.",
                Source = "ARIEC61850 status-to-control validation candidate",
                Value = "Control object",
                Quality = "-",
                DeviceTimestamp = "-",
                ProbeStatus = "Control candidate — ctlModel validation required",
                Timestamp = DateTime.Now
            });
        }
    }

    private readonly record struct LogicalNodeHint(string Domain, string LogicalNode, string LogicalNodeClass, string Source);

    private static void AddGenericLogicalNodeFallbacksFromDiscoveryArtifacts(
        ICollection<SignalDefinition> signals,
        object discovery,
        NativeMmsDiscoverySnapshot snapshot,
        NativeReportInventory inventory,
        DateTime now)
    {
        var hints = new Dictionary<string, LogicalNodeHint>(StringComparer.OrdinalIgnoreCase);

        void AddHint(string domain, string logicalNode, string source)
        {
            domain = (domain ?? string.Empty).Trim().Replace('$', '.');
            logicalNode = (logicalNode ?? string.Empty).Trim().Replace('$', '.');
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(logicalNode))
                return;

            var cls = SignalDefinition.DetectLogicalNodeClass(logicalNode).ToUpperInvariant();
            if (!IsScadaLogicalNodeClassForFallback(cls))
                return;

            var key = $"{domain}/{logicalNode}";
            if (!hints.ContainsKey(key))
                hints[key] = new LogicalNodeHint(domain, logicalNode, cls, source);
        }

        foreach (var signal in signals)
        {
            var domain = ExtractDomain(signal.ObjectReference);
            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(signal.LogicalNode))
                AddHint(domain, signal.LogicalNode, "existing signal inventory");
        }

        foreach (var domainPair in snapshot.DomainVariables)
        {
            var domain = domainPair.Key ?? string.Empty;
            foreach (var raw in domainPair.Value)
                foreach (var hint in ExtractLogicalNodeHintsFromText(raw, domain, "MMS NamedVariable"))
                    AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        foreach (var domainPair in snapshot.DomainVariableLists)
        {
            var domain = domainPair.Key ?? string.Empty;
            foreach (var raw in domainPair.Value)
                foreach (var hint in ExtractLogicalNodeHintsFromText(raw, domain, "MMS NamedVariableList/DataSet"))
                    AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        foreach (var ds in inventory.DataSets)
        {
            AddHint(ds.Domain, ds.LogicalNode, "Report inventory DataSet");
            foreach (var hint in ExtractLogicalNodeHintsFromText(ds.Reference, ds.Domain, "Report inventory DataSet reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            foreach (var hint in ExtractLogicalNodeHintsFromText(ds.RawMmsName, ds.Domain, "Report inventory DataSet raw name"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        foreach (var rcb in inventory.ReportControls)
        {
            AddHint(rcb.Domain, rcb.LogicalNode, "Report inventory RCB");
            foreach (var hint in ExtractLogicalNodeHintsFromText(rcb.Reference, rcb.Domain, "Report inventory RCB reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
            foreach (var hint in ExtractLogicalNodeHintsFromText(rcb.DataSetReference, rcb.Domain, "Report inventory RCB DataSet reference"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        var knownDomains = signals
            .Select(s => ExtractDomain(s.ObjectReference))
            .Concat(snapshot.DomainVariables.Keys)
            .Concat(snapshot.DomainVariableLists.Keys)
            .Select(d => (d ?? string.Empty).Trim())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var reflectionDefaultDomain = knownDomains.Count == 1 ? knownDomains[0] : string.Empty;

        foreach (var text in EnumerateDiscoveryStrings(discovery, maxDepth: 7, maxItems: 50000))
        {
            foreach (var hint in ExtractLogicalNodeHintsFromText(text, reflectionDefaultDomain, "ARIEC61850 discovery object"))
                AddHint(hint.Domain, hint.LogicalNode, hint.Source);
        }

        foreach (var hint in EnumerateLogicalNodeHintsFromObjectShapes(discovery, reflectionDefaultDomain, maxDepth: 8, maxItems: 50000))
            AddHint(hint.Domain, hint.LogicalNode, hint.Source);

        foreach (var hint in hints.Values.OrderBy(h => h.Domain, StringComparer.OrdinalIgnoreCase).ThenBy(h => h.LogicalNode, StringComparer.OrdinalIgnoreCase))
        {
            var logicalNodeSignals = signals
                .Where(s => ExtractDomain(s.ObjectReference).Equals(hint.Domain, StringComparison.OrdinalIgnoreCase) &&
                            s.LogicalNode.Equals(hint.LogicalNode, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var hasCoreSignalForLn = signals.Any(s =>
                ExtractDomain(s.ObjectReference).Equals(hint.Domain, StringComparison.OrdinalIgnoreCase) &&
                s.LogicalNode.Equals(hint.LogicalNode, StringComparison.OrdinalIgnoreCase) &&
                s.IsScadaCoreSignal);

            if (hasCoreSignalForLn)
                continue;

            foreach (var point in BuildArIecLogicalNodeFallbackPoints(hint.LogicalNodeClass))
            {
                if (hint.LogicalNodeClass is "MMXU" or "MMXN" &&
                    logicalNodeSignals.Count > 0 &&
                    !logicalNodeSignals.Any(signal => HasDataObjectPath(signal.ObjectReference, point.DataObject)))
                    continue;

                var reference = $"{hint.Domain}/{hint.LogicalNode}.{point.Path}";
                if (signals.Any(s => ReferencesEqual(s.ObjectReference, reference)))
                    continue;

                signals.Add(CreateArIecSignal(
                    reference,
                    point.FunctionalConstraint,
                    point.Category,
                    hint.LogicalNodeClass,
                    point.DataObject,
                    string.Empty,
                    now,
                    $"Generic full-LN discovery fallback ({hint.Source})"));
            }
        }
    }

    private static bool IsScadaLogicalNodeClassForFallback(string logicalNodeClass)
    {
        var cls = (logicalNodeClass ?? string.Empty).ToUpperInvariant();
        return cls is "GGIO" or "MMXU" or "MMXN" or "CSWI" or "XCBR" or "XSWI" or
               "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF" or "RBRF" or
               "ATCC" or "AVC" or "AVCO" or "YPTR";
    }

    private static bool HasDataObjectPath(string reference, string dataObject)
    {
        var text = (reference ?? string.Empty).Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash < 0) return false;
        var firstDot = text.IndexOf('.', slash + 1);
        if (firstDot < 0 || firstDot == text.Length - 1) return false;
        var secondDot = text.IndexOf('.', firstDot + 1);
        var discoveredDataObject = secondDot < 0 ? text[(firstDot + 1)..] : text[(firstDot + 1)..secondDot];
        return discoveredDataObject.Equals(dataObject, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<LogicalNodeHint> ExtractLogicalNodeHintsFromText(string text, string defaultDomain, string source)
    {
        if (string.IsNullOrWhiteSpace(text))
            yield break;

        var normalized = text.Trim().Replace('$', '.').Replace('\\', '/');
        var pattern = @"(?:(?<domain>[A-Za-z0-9_.-]+)/)?(?<ln>[A-Za-z0-9_]*(?:LLN0|LPHD\d*|GGIO\d+|MMXU\d+|MMXN\d+|CSWI\d+|XCBR\d+|XSWI\d+|PTOC\d+|PTRC\d+|PDIF\d+|PDIS\d+|PIOC\d+|PTOV\d+|PTUV\d+|PTEF\d+|PDEF\d+|RBRF\d+|ATCC\d+|AVCO\d+|AVC\d+|YPTR\d+))(?:[.$/]|$)";
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(normalized, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase))
        {
            var ln = match.Groups["ln"].Value;
            var domain = match.Groups["domain"].Success ? match.Groups["domain"].Value : defaultDomain;
            if (string.IsNullOrWhiteSpace(domain) || string.IsNullOrWhiteSpace(ln))
                continue;

            yield return new LogicalNodeHint(domain, ln, SignalDefinition.DetectLogicalNodeClass(ln), source);
        }
    }


    private static IEnumerable<LogicalNodeHint> EnumerateLogicalNodeHintsFromObjectShapes(object? root, string defaultDomain, int maxDepth, int maxItems)
    {
        if (root == null || maxDepth <= 0 || maxItems <= 0)
            yield break;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, int Depth)>();
        queue.Enqueue((root, 0));
        var visitedItems = 0;
        var emitted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        while (queue.Count > 0 && visitedItems < maxItems)
        {
            var (value, depth) = queue.Dequeue();
            if (value == null) continue;
            if (value is string) continue;

            var type = value.GetType();
            if (type.IsValueType || type.IsEnum) continue;
            if (!visited.Add(value)) continue;
            visitedItems++;

            var reference = ReadStringProperty(value, "Reference", "ObjectReference", "IecReference", "MmsReference", "Path", "FullPath", "FullName");
            var domain = ExtractDomain(reference);
            if (string.IsNullOrWhiteSpace(domain))
            {
                domain = ReadStringProperty(value, "Domain", "DomainName", "MmsDomain", "MmsDomainName", "LogicalDevice", "LogicalDeviceName", "LdName", "LdInst", "LdInstance");
                if (domain.Contains('/')) domain = ExtractDomain(domain);
            }
            if (string.IsNullOrWhiteSpace(domain))
                domain = defaultDomain;

            var lnClass = ReadStringProperty(value, "LnClass", "LogicalNodeClass", "Class", "LogicalNodeType", "LogicalNodeClassName");
            var inst = ReadStringProperty(value, "Inst", "Instance", "InstanceNumber", "LnInst", "LogicalNodeInstance", "InstanceId");
            var lnName = ReadStringProperty(value, "LogicalNode", "LogicalNodeName", "LnName", "Name", "InstanceName", "NodeName");

            if (!string.IsNullOrWhiteSpace(reference) && reference.Contains('/'))
            {
                var fromReference = ExtractLogicalNode(reference);
                if (!string.IsNullOrWhiteSpace(fromReference))
                    lnName = fromReference;
            }

            if (!string.IsNullOrWhiteSpace(lnClass))
                lnName = BuildLogicalNodeName(lnName, lnClass, inst, value);

            if (string.IsNullOrWhiteSpace(lnClass) && LooksLikeLogicalNodeName(lnName))
                lnClass = SignalDefinition.DetectLogicalNodeClass(lnName);

            if (!string.IsNullOrWhiteSpace(domain) && !string.IsNullOrWhiteSpace(lnName) && !string.IsNullOrWhiteSpace(lnClass))
            {
                var cls = SignalDefinition.DetectLogicalNodeClass(lnName);
                if (string.IsNullOrWhiteSpace(cls)) cls = lnClass;
                if (IsScadaLogicalNodeClassForFallback(cls))
                {
                    var key = $"{domain}/{lnName}";
                    if (emitted.Add(key))
                        yield return new LogicalNodeHint(domain.Trim().Replace('$', '.'), lnName.Trim().Replace('$', '.'), cls, "ARIEC61850 object-shape LN inventory");
                }
            }

            if (depth >= maxDepth)
                continue;

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    if (entry.Key != null) queue.Enqueue((entry.Key, depth + 1));
                    if (entry.Value != null) queue.Enqueue((entry.Value, depth + 1));
                }
                continue;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (item != null) queue.Enqueue((item, depth + 1));
                continue;
            }

            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                object? propertyValue;
                try
                {
                    propertyValue = prop.GetValue(value);
                }
                catch
                {
                    continue;
                }

                if (propertyValue != null)
                    queue.Enqueue((propertyValue, depth + 1));
            }
        }
    }

    private static bool LooksLikeLogicalNodeName(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return false;
        return System.Text.RegularExpressions.Regex.IsMatch(
            text.Trim(),
            @"^[A-Za-z0-9_]*(LLN0|LPHD\d*|GGIO\d+|MMXU\d+|MMXN\d+|CSWI\d+|XCBR\d+|XSWI\d+|PTOC\d+|PTRC\d+|PDIF\d+|PDIS\d+|PIOC\d+|PTOV\d+|PTUV\d+|PTEF\d+|PDEF\d+|ATCC\d+|AVCO\d+|AVC\d+|YPTR\d+)$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
    }

    private static IEnumerable<string> EnumerateDiscoveryStrings(object? root, int maxDepth, int maxItems)
    {
        if (root == null || maxDepth <= 0 || maxItems <= 0)
            yield break;

        var visited = new HashSet<object>(ReferenceEqualityComparer.Instance);
        var queue = new Queue<(object Value, int Depth)>();
        queue.Enqueue((root, 0));
        var emitted = 0;

        while (queue.Count > 0 && emitted < maxItems)
        {
            var (value, depth) = queue.Dequeue();
            if (value == null)
                continue;

            if (value is string text)
            {
                emitted++;
                yield return text;
                continue;
            }

            var type = value.GetType();
            if (type.IsValueType || type.IsEnum)
                continue;
            if (!visited.Add(value))
                continue;
            if (depth >= maxDepth)
                continue;

            if (value is System.Collections.IDictionary dictionary)
            {
                foreach (System.Collections.DictionaryEntry entry in dictionary)
                {
                    if (entry.Key != null) queue.Enqueue((entry.Key, depth + 1));
                    if (entry.Value != null) queue.Enqueue((entry.Value, depth + 1));
                }
                continue;
            }

            if (value is System.Collections.IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                    if (item != null) queue.Enqueue((item, depth + 1));
                continue;
            }

            foreach (var prop in type.GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public))
            {
                if (prop.GetIndexParameters().Length != 0)
                    continue;

                object? propertyValue;
                try
                {
                    propertyValue = prop.GetValue(value);
                }
                catch
                {
                    continue;
                }

                if (propertyValue != null)
                    queue.Enqueue((propertyValue, depth + 1));
            }
        }
    }

    private static void AddArIecLogicalNodeFallbackTargets(
        ICollection<SignalDefinition> signals,
        object logicalDevice,
        LiveIedLogicalNodeModel logicalNode,
        string logicalDeviceDomainHint,
        DateTime now)
    {
        var lnClass = (logicalNode.LnClass ?? string.Empty).ToUpperInvariant();
        if (lnClass is not ("MMXU" or "MMXN" or "GGIO" or "CSWI" or "XCBR" or "XSWI" or "PTOC" or "PTRC" or "PDIF" or "PDIS" or "PIOC" or "PTOV" or "PTUV" or "PTEF" or "PDEF" or "RBRF"))
            return;

        var lnReference = ResolveArIecLogicalNodeReference(logicalDevice, logicalNode, logicalDeviceDomainHint);
        if (string.IsNullOrWhiteSpace(lnReference) || !lnReference.Contains('/'))
            return;

        foreach (var point in BuildArIecLogicalNodeFallbackPoints(lnClass))
        {
            if (lnClass is "MMXU" or "MMXN" &&
                logicalNode.DataObjects.Count > 0 &&
                !logicalNode.DataObjects.Any(dataObject => dataObject.Name.Equals(point.DataObject, StringComparison.OrdinalIgnoreCase)))
                continue;

            var reference = $"{lnReference}.{point.Path}";
            if (signals.Any(s => ReferencesEqual(s.ObjectReference, reference)))
                continue;

            signals.Add(CreateArIecSignal(
                reference,
                point.FunctionalConstraint,
                point.Category,
                lnClass,
                point.DataObject,
                string.Empty,
                now,
                "ARIEC61850 LN profile fallback"));
        }
    }

    private readonly record struct ArIecFallbackPoint(string DataObject, string Path, string FunctionalConstraint, string Category);

    private static IEnumerable<ArIecFallbackPoint> BuildArIecLogicalNodeFallbackPoints(string lnClass)
    {
        if (lnClass is "MMXU" or "MMXN")
        {
            yield return new("PhV", "PhV.phsA.cVal.mag.f", "MX", "Measurement");
            yield return new("PhV", "PhV.phsA.instCVal.mag.f", "MX", "Measurement");
            yield return new("PhV", "PhV.phsB.cVal.mag.f", "MX", "Measurement");
            yield return new("PhV", "PhV.phsB.instCVal.mag.f", "MX", "Measurement");
            yield return new("PhV", "PhV.phsC.cVal.mag.f", "MX", "Measurement");
            yield return new("PhV", "PhV.phsC.instCVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsA.cVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsA.instCVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsB.cVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsB.instCVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsC.cVal.mag.f", "MX", "Measurement");
            yield return new("A", "A.phsC.instCVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsAB.cVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsAB.instCVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsBC.cVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsBC.instCVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsCA.cVal.mag.f", "MX", "Measurement");
            yield return new("PPV", "PPV.phsCA.instCVal.mag.f", "MX", "Measurement");
            yield return new("Hz", "Hz.mag.f", "MX", "Measurement");
            yield break;
        }

        if (lnClass == "GGIO")
        {
            for (var i = 1; i <= 32; i++)
                yield return new($"Ind{i}", $"Ind{i}.stVal", "ST", "Status");
            for (var i = 1; i <= 16; i++)
                yield return new($"AnIn{i}", $"AnIn{i}.mag.f", "MX", "Measurement");
            yield break;
        }

        if (lnClass is "CSWI" or "XCBR" or "XSWI")
        {
            yield return new("Pos", "Pos.stVal", "ST", "Position");
            yield break;
        }

        if (lnClass == "PTRC")
        {
            yield return new("Tr", "Tr.general", "ST", "Protection");
            yield break;
        }

        if (lnClass == "RBRF")
        {
            yield return new("OpEx", "OpEx.general", "ST", "Protection");
            yield break;
        }

        if (lnClass.StartsWith("P", StringComparison.OrdinalIgnoreCase))
        {
            yield return new("Op", "Op.general", "ST", "Protection");
            yield return new("Str", "Str.general", "ST", "Protection");
        }
    }

    private static string ExtractDomain(string? reference)
    {
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        return slash > 0 ? text[..slash] : string.Empty;
    }

    private static string ResolveLogicalDeviceDomainHint(object logicalDevice)
    {
        var domain = ResolveArIecLogicalDeviceDomain(logicalDevice);
        if (!string.IsNullOrWhiteSpace(domain))
            return domain;

        // Fallback: derive the MMS domain from the first fully-qualified data-object reference
        // inside this logical device. This keeps generated fallback points in the same real
        // MMS domain as the discovered LN, instead of producing LD01/... when the domain is
        // actually vendor-prefixed.
        var logicalNodesProperty = logicalDevice.GetType().GetProperty("LogicalNodes");
        if (logicalNodesProperty?.GetValue(logicalDevice) is System.Collections.IEnumerable logicalNodes)
        {
            foreach (var ln in logicalNodes)
            {
                var dataObjectsProperty = ln?.GetType().GetProperty("DataObjects");
                if (dataObjectsProperty?.GetValue(ln) is not System.Collections.IEnumerable dataObjects)
                    continue;

                foreach (var dataObject in dataObjects)
                {
                    var reference = ReadStringProperty(dataObject!, "Reference", "ObjectReference", "IecReference", "Path", "MmsReference");
                    var extracted = ExtractDomain(reference);
                    if (!string.IsNullOrWhiteSpace(extracted))
                        return extracted;
                }
            }
        }

        return string.Empty;
    }

    private static string ResolveArIecLogicalNodeReference(object logicalDevice, object logicalNode, string logicalDeviceDomainHint = "")
    {
        var lnReference = ReadStringProperty(logicalNode, "Reference", "ObjectReference", "IecReference", "Path", "MmsReference");
        var lnClass = ReadStringProperty(logicalNode, "LnClass", "LogicalNodeClass", "Class", "LogicalNodeType");
        var inst = ReadStringProperty(logicalNode, "Inst", "Instance", "InstanceNumber", "LnInst", "LogicalNodeInstance", "InstanceId");

        if (!string.IsNullOrWhiteSpace(lnReference) && lnReference.Contains('/'))
        {
            var normalized = lnReference.Trim().Replace('$', '.');
            if (!string.IsNullOrWhiteSpace(inst) && !string.IsNullOrWhiteSpace(lnClass))
                normalized = EnsureLogicalNodeInstanceSuffix(normalized, lnClass, inst);
            return normalized;
        }

        var lnName = ReadStringProperty(logicalNode, "Name", "LogicalNodeName", "LnName", "InstanceName", "FullName", "NodeName");
        lnName = BuildLogicalNodeName(lnName, lnClass, inst, logicalNode);

        if (string.IsNullOrWhiteSpace(lnName))
            return string.Empty;
        if (lnName.Contains('/'))
            return lnName.Trim().Replace('$', '.');

        var domain = string.IsNullOrWhiteSpace(logicalDeviceDomainHint)
            ? ResolveArIecLogicalDeviceDomain(logicalDevice)
            : logicalDeviceDomainHint;
        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        return $"{domain.Trim().Replace('$', '.')}/{lnName.Trim().Replace('$', '.')}";
    }

    private static string BuildLogicalNodeName(string lnName, string lnClass, string inst, object logicalNode)
    {
        lnName = (lnName ?? string.Empty).Trim().Replace('$', '.');
        lnClass = (lnClass ?? string.Empty).Trim();
        inst = NormalizeInstanceText(inst);

        // ARIEC61850 model builders and vendor MMS trees do not all expose the same property
        // shape. Some expose Name="MMXU" plus InstanceNumber=2 instead of Name="MMXU2".
        // If we do not append the instance, multiple LN instances collapse into one synthetic
        // class-only reference and the wizard appears to discover only the first LN.
        if (string.IsNullOrWhiteSpace(lnName))
        {
            var prefix = ReadStringProperty(logicalNode, "Prefix", "LnPrefix", "LogicalNodePrefix");
            lnName = string.IsNullOrWhiteSpace(inst) ? $"{prefix}{lnClass}" : $"{prefix}{lnClass}{inst}";
        }
        else if (!string.IsNullOrWhiteSpace(lnClass) && !string.IsNullOrWhiteSpace(inst))
        {
            lnName = EnsureLogicalNodeInstanceSuffix(lnName, lnClass, inst);
        }

        return lnName;
    }

    private static string EnsureLogicalNodeInstanceSuffix(string text, string lnClass, string inst)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(lnClass) || string.IsNullOrWhiteSpace(inst))
            return text ?? string.Empty;

        inst = NormalizeInstanceText(inst);
        if (string.IsNullOrWhiteSpace(inst))
            return text;

        var normalized = text.Trim().Replace('$', '.');
        var slash = normalized.LastIndexOf('/');
        var prefix = slash >= 0 ? normalized[..(slash + 1)] : string.Empty;
        var leaf = slash >= 0 ? normalized[(slash + 1)..] : normalized;
        var dot = leaf.IndexOf('.');
        var ln = dot >= 0 ? leaf[..dot] : leaf;
        var suffix = dot >= 0 ? leaf[dot..] : string.Empty;

        var classIndex = ln.IndexOf(lnClass, StringComparison.OrdinalIgnoreCase);
        if (classIndex < 0)
            return normalized;

        var afterClass = classIndex + lnClass.Length;
        if (afterClass < ln.Length && char.IsDigit(ln[afterClass]))
            return normalized;

        var fixedLn = ln.Insert(afterClass, inst);
        return prefix + fixedLn + suffix;
    }

    private static string ResolveArIecLogicalDeviceDomain(object logicalDevice)
    {
        var domain = ReadStringProperty(logicalDevice,
            "Reference", "ObjectReference", "IecReference", "Domain", "DomainName", "MmsDomain",
            "MmsDomainName", "Name", "LogicalDeviceName", "LdName", "LdInst", "Inst",
            "Instance", "InstanceName", "Id", "Identifier");

        if (string.IsNullOrWhiteSpace(domain))
            return string.Empty;

        domain = domain.Trim().Replace('$', '.');
        if (domain.Contains('/'))
            domain = domain[..domain.IndexOf('/')];
        return domain;
    }

    private static string NormalizeInstanceText(string value)
    {
        value = (value ?? string.Empty).Trim();
        if (value.Length == 0) return string.Empty;
        if (int.TryParse(value, out var number))
            return number <= 0 ? string.Empty : number.ToString(CultureInfo.InvariantCulture);
        return value;
    }

    private static string ReadStringProperty(object source, params string[] propertyNames)
    {
        if (source == null) return string.Empty;
        var type = source.GetType();
        foreach (var name in propertyNames)
        {
            var prop = type.GetProperty(
                name,
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.Public |
                System.Reflection.BindingFlags.IgnoreCase);
            if (prop == null) continue;
            object? raw;
            try { raw = prop.GetValue(source); }
            catch { continue; }
            if (raw == null) continue;
            var value = raw switch
            {
                string text => text,
                int i => i.ToString(CultureInfo.InvariantCulture),
                long l => l.ToString(CultureInfo.InvariantCulture),
                short sh => sh.ToString(CultureInfo.InvariantCulture),
                byte b => b.ToString(CultureInfo.InvariantCulture),
                uint ui => ui.ToString(CultureInfo.InvariantCulture),
                ulong ul => ul.ToString(CultureInfo.InvariantCulture),
                ushort us => us.ToString(CultureInfo.InvariantCulture),
                _ when prop.PropertyType.IsEnum => raw.ToString() ?? string.Empty,
                _ => string.Empty
            };
            if (!string.IsNullOrWhiteSpace(value)) return value.Trim();
        }
        return string.Empty;
    }

    private static readonly HashSet<string> ControlServiceLeafNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "ctlModel", "ctlVal", "ctlNum", "stSeld", "SBO", "SBOw", "Oper", "Cancel",
        "origin", "T", "Test", "Check", "operTm", "sboClass", "sboTimeout", "operTimeout"
    };

    private static readonly HashSet<string> ControllableCdcs = new(StringComparer.OrdinalIgnoreCase)
    {
        "SPC", "DPC", "INC", "BSC", "ISC", "APC", "BAC"
    };

    private static bool IsControlServiceLeaf(string? name)
    {
        var value = (name ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        if (string.IsNullOrWhiteSpace(value))
            return false;
        var leaf = value[(value.LastIndexOf('.') + 1)..];
        return ControlServiceLeafNames.Contains(leaf);
    }

    private static bool IsValidControlObjectReference(string? reference)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.').TrimEnd('.');
        if (string.IsNullOrWhiteSpace(normalized) || !normalized.Contains('/'))
            return false;
        return !IsControlServiceLeaf(normalized);
    }

    private static void AddArIecControlTarget(
        ICollection<SignalDefinition> signals,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        DateTime now)
    {
        var attributes = dataObject.Attributes ?? Array.Empty<LiveIedDataAttributeModel>();
        var hasControlOperation = attributes.Any(attribute =>
        {
            var fc = attribute.FunctionalConstraint ?? string.Empty;
            var path = attribute.AttributePath ?? string.Empty;
            return fc.Equals("CO", StringComparison.OrdinalIgnoreCase) &&
                   (path.Contains("Oper", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("SBO", StringComparison.OrdinalIgnoreCase) ||
                    path.Contains("Cancel", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith("ctlVal", StringComparison.OrdinalIgnoreCase));
        });

        var controlModelAttribute = attributes.FirstOrDefault(attribute =>
        {
            var path = attribute.AttributePath ?? string.Empty;
            var reference = attribute.ObjectReference ?? string.Empty;
            return path.EndsWith("ctlModel", StringComparison.OrdinalIgnoreCase) ||
                   reference.EndsWith(".ctlModel", StringComparison.OrdinalIgnoreCase);
        });

        var hasControlStateEvidence = attributes.Any(attribute =>
        {
            var path = attribute.AttributePath ?? string.Empty;
            var reference = attribute.ObjectReference ?? string.Empty;
            return path.Contains("stSeld", StringComparison.OrdinalIgnoreCase) ||
                   path.Contains("ctlNum", StringComparison.OrdinalIgnoreCase) ||
                   reference.Contains(".stSeld", StringComparison.OrdinalIgnoreCase) ||
                   reference.Contains(".ctlNum", StringComparison.OrdinalIgnoreCase);
        });

        var cdc = (dataObject.InferredCdc ?? string.Empty).Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(cdc))
            cdc = InferControlCdc(logicalNode, dataObject, attributes, hasControlOperation);

        if (!hasControlOperation && !hasControlStateEvidence && controlModelAttribute == null && !ControllableCdcs.Contains(cdc))
            return;

        var reference = (dataObject.Reference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(reference) || !IsValidControlObjectReference(reference))
            return;

        var objectLeaf = reference[(reference.LastIndexOf('.') + 1)..];
        if (IsControlServiceLeaf(objectLeaf) || IsControlServiceLeaf(dataObject.Name))
        {
            // ctlModel/ctlVal/ctlNum/Oper/SBOw are attributes of a control object,
            // not independent command targets. Rootless shallow discovery is ignored;
            // the validated status-to-control projection will add the parent DO when
            // Pos.stVal, TapOpR.stVal, or another standard status object is available.
            return;
        }

        var controlModelReference = controlModelAttribute?.ObjectReference;
        if (string.IsNullOrWhiteSpace(controlModelReference))
            controlModelReference = controlModelAttribute?.MmsReference;
        if (string.IsNullOrWhiteSpace(controlModelReference))
            controlModelReference = $"{reference}.ctlModel";

        var ln = string.IsNullOrWhiteSpace(logicalNode.Name) ? ExtractLogicalNode(reference) : logicalNode.Name;
        var objectName = string.IsNullOrWhiteSpace(dataObject.Name)
            ? reference[(reference.LastIndexOf('.') + 1)..]
            : dataObject.Name;
        var type = string.IsNullOrWhiteSpace(cdc) ? "Control" : $"{cdc} Control";

        signals.Add(new SignalDefinition
        {
            Name = $"{ln} {objectName} Control".Replace("  ", " ").Trim(),
            ObjectReference = reference,
            FunctionalConstraint = "CO",
            DataType = type,
            Category = "Control",
            Confidence = hasControlOperation ? "High" : "Medium",
            IsSelected = false,
            IsControlSignal = true,
            ControlCdc = cdc,
            ControlModelReference = controlModelReference.Replace('$', '.'),
            ControlStatusReference = BuildControlStatusReference(reference, cdc),
            ControlModelText = "Auto-detect on command",
            ControlValueType = InferControlValueType(cdc),
            IsReportCapable = false,
            ReportCoverage = "Command object",
            ReportCoverageReason = hasControlOperation
                ? "ARIEC61850 discovered an IEC 61850 CO operation structure for this controllable data object."
                : "ARIEC61850 inferred a controllable CDC/ctlModel. Command capability must be validated before operate.",
            Source = "ARIEC61850 live control-object discovery",
            Value = "Control object",
            Quality = "-",
            DeviceTimestamp = "-",
            ProbeStatus = "Control discovered",
            Timestamp = now
        });
    }

    private static string InferControlValueType(string cdc)
        => (cdc ?? string.Empty).Trim().ToUpperInvariant() switch
        {
            "DPC" => "Dbpos",
            "SPC" => "Boolean",
            "INC" or "ISC" => "Integer",
            "BSC" => "ValWithTrans",
            "APC" => "Float32",
            "BAC" => "Unknown",
            _ => "Unknown"
        };

    private static void AddArIecSmartTargets(
        ICollection<SignalDefinition> signals,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        DateTime now)
    {
        var targets = Iec61850SmartReadPlanBuilder.BuildForDataObject(dataObject);
        foreach (var target in targets)
        {
            if (string.IsNullOrWhiteSpace(target.Reference) || string.IsNullOrWhiteSpace(target.FunctionalConstraint))
                continue;

            var reference = NormalizeStructuredStatusTargetReference(target.Reference, dataObject.Name, dataObject.InferredCdc);
            var signal = CreateArIecSignal(
                reference,
                target.FunctionalConstraint,
                target.Purpose,
                logicalNode.LnClass,
                dataObject.Name,
                dataObject.InferredCdc,
                now,
                "ARIEC61850 live model read plan");

            signals.Add(signal);
        }
    }

    private static string NormalizeStructuredStatusTargetReference(string reference, string dataObjectName, string cdc)
    {
        var normalized = (reference ?? string.Empty).Trim().Replace('$', '.');
        if (string.IsNullOrWhiteSpace(normalized) || string.IsNullOrWhiteSpace(dataObjectName))
            return normalized;

        if (normalized.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".general", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".q", StringComparison.OrdinalIgnoreCase) ||
            normalized.EndsWith(".t", StringComparison.OrdinalIgnoreCase))
        {
            return normalized;
        }

        if (IsStatusCdc(cdc) && EndsWithPathSegment(normalized, dataObjectName))
            return normalized + ".stVal";

        return normalized;
    }

    private static bool IsStatusCdc(string cdc)
        => cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase) ||
           cdc.Equals("SPC", StringComparison.OrdinalIgnoreCase) ||
           cdc.Equals("SPS", StringComparison.OrdinalIgnoreCase) ||
           cdc.Equals("INS", StringComparison.OrdinalIgnoreCase) ||
           cdc.Equals("ENS", StringComparison.OrdinalIgnoreCase) ||
           cdc.Equals("BSC", StringComparison.OrdinalIgnoreCase);

    private static bool EndsWithPathSegment(string reference, string segment)
    {
        var text = reference.Replace('$', '.').TrimEnd('.');
        var dot = text.LastIndexOf('.');
        var slash = text.LastIndexOf('/');
        var start = Math.Max(dot, slash) + 1;
        return start >= 0 &&
               start < text.Length &&
               text[start..].Equals(segment, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsTesterReadableSignal(SignalDefinition signal)
    {
        if (signal.IsScadaCoreSignal)
            return true;

        var r = NormalizeReference(signal.ObjectReference);
        var dataType = signal.DataType ?? string.Empty;

        if (dataType.Equals("Quality", StringComparison.OrdinalIgnoreCase))
            return r.EndsWith(".q");
        if (dataType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase))
            return r.EndsWith(".t") || r.EndsWith(".tm");
        if (dataType.Equals("Float32", StringComparison.OrdinalIgnoreCase) || dataType.Equals("Double", StringComparison.OrdinalIgnoreCase))
            return r.EndsWith(".f") || r.EndsWith(".mag.f") || r.EndsWith(".ang.f");
        if (dataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase))
            return r.EndsWith(".pos.stval") || r.EndsWith(".stval");
        if (dataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            return r.EndsWith(".stval") || r.EndsWith(".general") || r.EndsWith(".ctlval");
        if (dataType.Equals("Enum", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Integer", StringComparison.OrdinalIgnoreCase))
        {
            return r.EndsWith(".stval") ||
                   r.EndsWith(".posval") ||
                   r.EndsWith(".actval") ||
                   r.EndsWith(".setval") ||
                   r.EndsWith(".ctlmodel");
        }

        return false;
    }

    private static void AddArIecAvrSemanticTargets(
        ICollection<SignalDefinition> signals,
        LiveIedLogicalNodeModel logicalNode,
        LiveIedDataObjectModel dataObject,
        DateTime now)
    {
        var lnClass = logicalNode.LnClass.ToUpperInvariant();
        if (lnClass is not ("ATCC" or "AVC" or "AVCO"))
            return;

        if (dataObject.Name.Equals("TapChg", StringComparison.OrdinalIgnoreCase))
        {
            signals.Add(CreateArIecSignal(
                $"{dataObject.Reference}.valWTr.posVal",
                "ST",
                "IntegerStepPosition",
                lnClass,
                dataObject.Name,
                "BSC",
                now,
                "ARIEC61850 AVR semantic profile"));
        }
    }

    private static SignalDefinition CreateArIecSignal(
        string reference,
        string functionalConstraint,
        string semanticKind,
        string logicalNodeClass,
        string dataObjectName,
        string cdc,
        DateTime now,
        string source)
    {
        var dataType = InferArIecDataType(reference, functionalConstraint, semanticKind, dataObjectName, cdc);
        var category = InferArIecCategory(reference, functionalConstraint, dataType, logicalNodeClass, semanticKind);
        var unit = InferArIecUnit(reference);
        var ln = ExtractLogicalNode(reference);
        var isCore = SignalDefinition.IsCoreScadaSignal(reference, SignalDefinition.DetectLogicalNodeClass(ln), dataType, category);

        var normalizedReference = reference.Trim().Replace('$', '.');
        TryBuildCompanionReference(normalizedReference, "q", out var qRef);
        TryBuildCompanionReference(normalizedReference, "t", out var tRef);

        return new SignalDefinition
        {
            Name = MakeArIecFriendlyName(reference, dataObjectName, category, semanticKind),
            ObjectReference = normalizedReference,
            FunctionalConstraint = functionalConstraint.Trim().ToUpperInvariant(),
            DataType = dataType,
            Category = category,
            Unit = unit,
            Confidence = isCore || source.Contains("semantic", StringComparison.OrdinalIgnoreCase) ? "High" : "Medium",
            IsSelected = isCore,
            IsReportCapable = false,
            ReportCoverage = "Polling fallback",
            ReportCoverageReason = "ARIEC61850 identified this as a SCADA value. Report DataSet/RCB coverage will be auto-planned separately.",
            QualityReference = qRef,
            TimestampReference = tRef,
            Source = source,
            Value = "Pending read",
            Quality = "Pending",
            Timestamp = now
        };
    }

    private static bool TryBuildCompanionReference(string reference, string companion, out string companionReference)
    {
        companionReference = string.Empty;
        if (!companion.Equals("q", StringComparison.OrdinalIgnoreCase) && !companion.Equals("t", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.IsNullOrWhiteSpace(reference)) return false;

        var normalized = reference.Replace('$', '.').Trim();
        if (normalized.EndsWith(".q", StringComparison.OrdinalIgnoreCase) || normalized.EndsWith(".t", StringComparison.OrdinalIgnoreCase)) return false;

        var parent = normalized;
        if (parent.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase)) parent = parent[..^14];
        else if (parent.EndsWith(".stVal", StringComparison.OrdinalIgnoreCase)) parent = parent[..^6];
        else if (parent.EndsWith(".general", StringComparison.OrdinalIgnoreCase)) parent = parent[..^8];
        else if (parent.EndsWith(".instCVal.mag.f", StringComparison.OrdinalIgnoreCase)) parent = parent[..^15];
        else if (parent.EndsWith(".cVal.mag.f", StringComparison.OrdinalIgnoreCase)) parent = parent[..^11];
        else if (parent.EndsWith(".mag.f", StringComparison.OrdinalIgnoreCase)) parent = parent[..^6];
        else
        {
            var slash = parent.IndexOf('/');
            var dot = parent.LastIndexOf('.');
            if (dot <= slash) return false;
            parent = parent[..dot];
        }

        if (string.IsNullOrWhiteSpace(parent)) return false;
        companionReference = $"{parent}.{companion.ToLowerInvariant()}";
        return true;
    }

    private static string InferArIecDataType(string reference, string functionalConstraint, string semanticKind, string dataObjectName, string cdc)
    {
        var r = NormalizeReference(reference);
        var semantic = semanticKind ?? string.Empty;

        if (r.EndsWith(".q")) return "Quality";
        if (r.EndsWith(".t") || r.EndsWith(".tm")) return "Timestamp";
        if (r.EndsWith(".ctlmodel")) return "Enum";
        if (semantic.Contains("DoublePoint", StringComparison.OrdinalIgnoreCase) || cdc.Equals("DPC", StringComparison.OrdinalIgnoreCase)) return "Dbpos";
        if (r.EndsWith(".posval") || r.EndsWith(".actval") || r.EndsWith(".pulsqty")) return "Int32";
        if (r.EndsWith(".mag.f") || r.EndsWith(".ang.f") || r.EndsWith(".f")) return "Float32";
        if (r.EndsWith(".general") || semantic.Contains("Boolean", StringComparison.OrdinalIgnoreCase)) return "Boolean";
        if (r.EndsWith(".stval") && (dataObjectName.Contains("Cnt", StringComparison.OrdinalIgnoreCase) || cdc is "INS" or "INC" or "BCR")) return "Int32";
        if (r.EndsWith(".stval")) return "Enum";
        if (functionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase)) return "Float32";
        return "Enum";
    }

    private static string InferArIecCategory(string reference, string functionalConstraint, string dataType, string logicalNodeClass, string semanticKind)
    {
        var r = NormalizeReference(reference);
        var cls = logicalNodeClass.ToUpperInvariant();
        if (r.Contains(".pos.") || dataType == "Dbpos") return "Position";
        if (dataType == "Float32" || functionalConstraint.Equals("MX", StringComparison.OrdinalIgnoreCase)) return "Measurement";
        if (cls.StartsWith("P", StringComparison.OrdinalIgnoreCase) || r.EndsWith(".op.general") || r.EndsWith(".str.general") || r.EndsWith(".tr.general")) return "Protection";
        if (cls is "ATCC" or "AVC" or "AVCO" or "YPTR" or "GGIO") return "Status";
        return semanticKind.Contains("Quality", StringComparison.OrdinalIgnoreCase) ? "Quality" : "Status";
    }

    private static string InferArIecUnit(string reference)
    {
        var r = NormalizeReference(reference);
        if (r.Contains(".a.") || r.Contains("loda") || r.Contains("circa") || r.Contains("limloda")) return "A";
        if (r.Contains(".phv.") || r.Contains(".ppv.") || r.Contains("ctlv") || r.Contains("bndctr") || r.Contains("ctldv")) return "V";
        if (r.Contains("phang") || r.EndsWith(".ang.f")) return "deg";
        if (r.Contains("tms")) return "s";
        if (r.Contains(".hz")) return "Hz";
        return string.Empty;
    }

    private static string MakeArIecFriendlyName(string reference, string dataObjectName, string category, string semanticKind)
    {
        var ln = ExtractLogicalNode(reference);
        var path = reference.Contains('.') ? reference[(reference.IndexOf('.') + 1)..] : reference;
        path = path
            .Replace("valWTr.posVal", "Position", StringComparison.OrdinalIgnoreCase)
            .Replace("cVal.mag.f", "Value", StringComparison.OrdinalIgnoreCase)
            .Replace("mag.f", "Value", StringComparison.OrdinalIgnoreCase)
            .Replace("stVal", "Status", StringComparison.OrdinalIgnoreCase)
            .Replace("general", "General", StringComparison.OrdinalIgnoreCase);

        return $"{ln} {path}".Replace('.', ' ').Replace("  ", " ").Trim();
    }

    private static int ConfidenceScore(string confidence) => confidence switch
    {
        "High" => 3,
        "Medium" => 2,
        _ => 1
    };

    private static bool ReferencesEqual(string left, string right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.').Replace("..", ".").ToLowerInvariant();

    private static string ExtractLogicalNode(string reference)
    {
        var slash = reference.IndexOf('/');
        if (slash < 0 || slash >= reference.Length - 1) return string.Empty;
        var after = reference[(slash + 1)..];
        var dot = after.IndexOf('.');
        return dot > 0 ? after[..dot] : after;
    }

    private static NativeMmsDiscoverySnapshot ToNativeSnapshot(ArMms.MmsDiscoverySnapshot snapshot)
        => new()
        {
            DomainVariables = snapshot.DomainVariables,
            DomainVariableLists = snapshot.DomainVariableLists
        };

    private static NativeReportInventory ToNativeInventory(ArMms.MmsReportInventory inventory)
        => new()
        {
            DataSets = inventory.DataSets.Select(x => new NativeDataSetCandidate
            {
                Domain = x.Domain,
                LogicalNode = x.LogicalNode,
                Name = x.Name,
                Reference = x.Reference,
                RawMmsName = x.RawMmsName
            }).ToList(),
            ReportControls = inventory.ReportControls.Select(x => new NativeReportControlCandidate
            {
                Domain = x.Domain,
                LogicalNode = x.LogicalNode,
                FunctionalConstraint = x.FunctionalConstraint,
                Name = x.Name,
                Reference = x.Reference,
                Buffered = x.Buffered,
                DataSetReference = x.DataSetReference,
                ReportId = x.ReportId,
                ConfRev = x.ConfRev,
                IntegrityPeriodMs = x.IntegrityPeriodMs,
                EnabledState = x.EnabledState,
                Status = x.Status,
                Attributes = x.Attributes.ToList()
            }).ToList()
        };

    private readonly record struct ReadReferenceCandidate(
        string Reference,
        string FunctionalConstraint,
        string Label,
        bool UseSmartDirectory);

    private static IReadOnlyList<ReadReferenceCandidate> BuildReadReferenceCandidates(string objectReference, string functionalConstraint)
    {
        var fc = NormalizeFunctionalConstraint(functionalConstraint, objectReference);
        var candidates = new List<ReadReferenceCandidate>();

        void Add(string reference, string label, bool useSmartDirectory = true)
        {
            if (string.IsNullOrWhiteSpace(reference))
                return;
            if (candidates.Any(x => x.Reference.Equals(reference, StringComparison.OrdinalIgnoreCase) &&
                                    x.FunctionalConstraint.Equals(fc, StringComparison.OrdinalIgnoreCase)))
                return;
            candidates.Add(new ReadReferenceCandidate(reference.Trim(), fc, label, useSmartDirectory));
        }

        var reference = CanonicalizeIecReferenceCase(objectReference.Trim().Replace('$', '.'));
        Add(reference, "requested");

        if (TryRemoveSuffix(reference, ".TapChg.stVal", out var tapChangeOwner))
        {
            Add($"{tapChangeOwner}.TapChg.valWTr.posVal", "legacy-avr-tapchg-stval-alias");
            Add($"{tapChangeOwner}.TapChg", "legacy-avr-tapchg-parent", useSmartDirectory: false);
        }

        if (TryGetDataObjectReference(reference, out var rootDataObjectReference) &&
            !ReferencesEqual(rootDataObjectReference, reference))
        {
            Add(rootDataObjectReference, "parent-data-object-schema", useSmartDirectory: false);
        }

        if (TryRemoveSuffix(reference, ".valWTr.posVal", out var valWithTransParent))
        {
            Add(valWithTransParent, "parent-do-for-valwtr-posval", useSmartDirectory: false);
            Add($"{valWithTransParent}.valWTr", "parent-valwtr-for-posval", useSmartDirectory: false);
        }
        if (TryRemoveSuffix(reference, ".posVal", out var posValParent))
            Add(posValParent, "parent-do-for-posval", useSmartDirectory: false);
        if (TryRemoveSuffix(reference, ".stVal", out var stValParent))
            Add(stValParent, "parent-do-for-stVal", useSmartDirectory: false);
        if (TryRemoveSuffix(reference, ".q", out var qParent))
            Add(qParent, "parent-do-for-q", useSmartDirectory: false);
        if (TryRemoveSuffix(reference, ".t", out var tParent))
            Add(tParent, "parent-do-for-t", useSmartDirectory: false);
        if (TryRemoveSuffix(reference, ".cVal.mag.f", out var cValParent))
        {
            Add(cValParent, "parent-do-for-cval", useSmartDirectory: false);
            Add($"{cValParent}.cVal", "parent-cval-for-f", useSmartDirectory: false);
            Add($"{cValParent}.cVal.mag", "parent-cval-mag-for-f", useSmartDirectory: false);
        }
        if (TryRemoveSuffix(reference, ".mag.f", out var magParent))
        {
            Add(magParent, "parent-do-for-mag-f", useSmartDirectory: false);
            Add($"{magParent}.mag", "parent-mag-for-f", useSmartDirectory: false);
        }
        if (TryRemoveSuffix(reference, ".ang.f", out var angParent))
        {
            Add(angParent, "parent-do-for-ang-f", useSmartDirectory: false);
            Add($"{angParent}.ang", "parent-ang-for-f", useSmartDirectory: false);
        }
        if (TryRemoveSuffix(reference, ".f", out var fParent))
            Add(fParent, "parent-for-f", useSmartDirectory: false);

        return candidates;
    }

    private static string CanonicalizeIecReferenceCase(string reference)
    {
        return (reference ?? string.Empty)
            .Replace(".ValWTr", ".valWTr", StringComparison.OrdinalIgnoreCase)
            .Replace(".CtlDITms", ".CtlDlTms", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryGetDataObjectReference(string reference, out string dataObjectReference)
    {
        dataObjectReference = string.Empty;
        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash < 0 || slash >= text.Length - 1)
            return false;

        var domain = text[..slash];
        var segments = text[(slash + 1)..]
            .Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length < 2)
            return false;

        dataObjectReference = $"{domain}/{segments[0]}.{segments[1]}";
        return true;
    }

    private static bool TryRemoveSuffix(string value, string suffix, out string result)
    {
        if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
        {
            result = value[..^suffix.Length];
            return !string.IsNullOrWhiteSpace(result);
        }

        result = string.Empty;
        return false;
    }

    private static string NormalizeFunctionalConstraint(string functionalConstraint, string reference)
    {
        var fc = (functionalConstraint ?? string.Empty).Trim().Trim('[', ']', '(', ')').ToUpperInvariant();
        if (fc.StartsWith("FC_", StringComparison.OrdinalIgnoreCase))
            fc = fc[3..];
        if (!string.IsNullOrWhiteSpace(fc) && fc != "-")
            return fc;

        var r = reference.Replace('$', '.');
        if (r.Contains(".mag.", StringComparison.OrdinalIgnoreCase) ||
            r.EndsWith(".f", StringComparison.OrdinalIgnoreCase) ||
            r.Contains(".cVal", StringComparison.OrdinalIgnoreCase))
            return "MX";
        if (r.Contains(".ctlVal", StringComparison.OrdinalIgnoreCase) ||
            r.Contains(".ctlModel", StringComparison.OrdinalIgnoreCase) ||
            r.Contains(".Oper", StringComparison.OrdinalIgnoreCase))
            return "CO";
        if (r.Contains(".set", StringComparison.OrdinalIgnoreCase))
            return "SP";
        if (r.Contains(".NamPlt", StringComparison.OrdinalIgnoreCase))
            return "DC";
        return "ST";
    }

    private Iec61850ReadValue? ProjectReadValue(ArMms.MmsDataValue? value, string dataType, string readReference, string requestedReference)
    {
        if (TryProjectStructuredLeafBySemantic(value, dataType, readReference, requestedReference, out var semanticProjection, out var semanticStatus))
            return semanticProjection;

        if (TryProjectWithArIecBinding(value, dataType, readReference, requestedReference, out var boundProjection, out var bindingStatus))
            return boundProjection;

        if (value == null)
        {
            LastErrorMessage = string.IsNullOrWhiteSpace(bindingStatus)
                ? $"ARIEC61850 read returned no MMS value for {requestedReference} via {readReference}."
                : bindingStatus;
            return null;
        }

        if (RequiresSchemaProjection(value, dataType, readReference, requestedReference))
        {
            LastErrorMessage = BuildSchemaProjectionBlockedMessage(value, readReference, requestedReference, FirstUsefulText(semanticStatus, bindingStatus));
            return null;
        }

        var projection = SelectProjectedValue(value, dataType, requestedReference);
        var rawValue = ConvertProjectedValue(projection.Value, dataType, requestedReference);
        var display = FormatProjectedDisplay(rawValue, dataType);

        return new Iec61850ReadValue
        {
            Value = rawValue,
            DisplayValue = display,
            Quality = projection.Quality,
            DeviceTimestamp = projection.Timestamp,
            SourceReference = requestedReference,
            ReadReference = readReference,
            Projection = projection.Description
        };
    }

    private bool TryProjectWithArIecBinding(
        ArMms.MmsDataValue? value,
        string dataType,
        string readReference,
        string requestedReference,
        out Iec61850ReadValue projected,
        out string bindingStatus)
    {
        projected = new Iec61850ReadValue();
        bindingStatus = string.Empty;
        if (value == null)
        {
            bindingStatus = "MMS value is null.";
            return false;
        }

        if (_liveModel == null)
        {
            bindingStatus = "live IEC 61850 model is not available; run discovery before projecting parent DA/FCD structures.";
            return false;
        }

        if (!TryFindLiveDataObject(requestedReference, out var dataObject))
        {
            bindingStatus = $"data object schema not found for {requestedReference}.";
            return false;
        }

        var rootSchema = Iec61850DataObjectSchemaBuilder.FromLiveDataObject(dataObject).ToRootNode();
        var readSchema = TryFindSchemaNode(rootSchema, readReference, out var schemaNode)
            ? schemaNode
            : ReferencesEqual(readReference, dataObject.Reference)
                ? rootSchema
                : null;
        if (readSchema == null)
        {
            bindingStatus = $"read reference {readReference} is outside discovered schema {rootSchema.Reference}.";
            return false;
        }

        var binding = Iec61850ValueBindingEngine.Bind(readSchema, value);
        var diagnostics = FormatBindingDiagnostics(binding.Diagnostics);
        if (binding.HasMismatch && RequiresSchemaProjection(value, dataType, readReference, requestedReference))
        {
            bindingStatus = $"schema/value mismatch from ARIEC61850 binding engine: {diagnostics}";
            return false;
        }

        if (!TryFindBoundRow(binding.Root, requestedReference, out var targetRow, out var ancestors))
        {
            if (ReferencesEqual(binding.Root.Reference, requestedReference))
            {
                targetRow = binding.Root;
                ancestors = Array.Empty<Iec61850BoundValueRow>();
            }
            else
            {
                bindingStatus = $"target leaf {requestedReference} was not found under bound schema {readSchema.Reference}. {diagnostics}";
                return false;
            }
        }

        if (IsStructuralDisplay(targetRow.Value))
        {
            bindingStatus = $"target {targetRow.Reference} resolved to structural value '{targetRow.Value}', not a scalar leaf. {diagnostics}";
            return false;
        }

        var rawValue = ConvertBoundDisplayValue(targetRow.Value, dataType, requestedReference);
        var display = rawValue is string text && !string.IsNullOrWhiteSpace(text)
            ? text
            : FormatProjectedDisplay(rawValue, dataType);

        projected = new Iec61850ReadValue
        {
            Value = rawValue,
            DisplayValue = display,
            Quality = FirstUseful(
                targetRow.Quality,
                ancestors.Reverse().Select(x => x.Quality).ToArray(),
                binding.Root.Quality),
            DeviceTimestamp = FirstUseful(
                targetRow.Timestamp,
                ancestors.Reverse().Select(x => x.Timestamp).ToArray(),
                binding.Root.Timestamp),
            SourceReference = requestedReference,
            ReadReference = readReference,
            Projection = $"ARIEC61850 schema bind: {readSchema.Reference} -> {targetRow.Reference}; confidence={targetRow.Confidence}; {diagnostics}"
        };
        bindingStatus = $"schema-bound {readSchema.Reference} -> {targetRow.Reference}; confidence={targetRow.Confidence}; {diagnostics}";
        return true;
    }

    private bool TryFindLiveDataObject(string reference, out LiveIedDataObjectModel dataObject)
    {
        dataObject = new LiveIedDataObjectModel();
        if (_liveModel == null || !TryGetDataObjectReference(reference, out var dataObjectReference))
            return false;

        foreach (var candidate in _liveModel.LogicalDevices
                     .SelectMany(ld => ld.LogicalNodes)
                     .SelectMany(ln => ln.DataObjects))
        {
            if (ReferencesEqual(candidate.Reference, dataObjectReference))
            {
                dataObject = candidate;
                return true;
            }
        }

        return false;
    }

    private static bool TryFindSchemaNode(Iec61850ValueSchemaNode node, string reference, out Iec61850ValueSchemaNode result)
    {
        if (ReferencesEqual(node.Reference, reference))
        {
            result = node;
            return true;
        }

        foreach (var child in node.Children)
        {
            if (TryFindSchemaNode(child, reference, out result))
                return true;
        }

        result = new Iec61850ValueSchemaNode();
        return false;
    }

    private static bool TryFindBoundRow(
        Iec61850BoundValueRow root,
        string reference,
        out Iec61850BoundValueRow row,
        out IReadOnlyList<Iec61850BoundValueRow> ancestors)
    {
        var path = new List<Iec61850BoundValueRow>();
        return TryFindBoundRow(root, reference, path, out row, out ancestors);
    }

    private static bool TryFindBoundRow(
        Iec61850BoundValueRow current,
        string reference,
        List<Iec61850BoundValueRow> path,
        out Iec61850BoundValueRow row,
        out IReadOnlyList<Iec61850BoundValueRow> ancestors)
    {
        path.Add(current);
        if (ReferencesEqual(current.Reference, reference))
        {
            row = current;
            ancestors = path.Take(path.Count - 1).ToArray();
            path.RemoveAt(path.Count - 1);
            return true;
        }

        foreach (var child in current.Children)
        {
            if (TryFindBoundRow(child, reference, path, out row, out ancestors))
            {
                path.RemoveAt(path.Count - 1);
                return true;
            }
        }

        path.RemoveAt(path.Count - 1);
        row = new Iec61850BoundValueRow();
        ancestors = Array.Empty<Iec61850BoundValueRow>();
        return false;
    }

    private static object? ConvertBoundDisplayValue(string value, string dataType, string reference)
    {
        var text = (value ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(text) || text == "-")
            return null;

        if (bool.TryParse(text, out var boolean))
            return boolean;

        var hint = dataType ?? string.Empty;
        var r = NormalizeReference(reference);
        if (hint.Equals("Float32", StringComparison.OrdinalIgnoreCase) ||
            hint.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
            r.EndsWith(".f") ||
            r.EndsWith(".mag.f") ||
            r.EndsWith(".ang.f"))
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                return number;
        }

        if (hint.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
            hint.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
            hint.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
            r.EndsWith(".posval") ||
            r.EndsWith(".actval") ||
            r.EndsWith(".pulsqty"))
        {
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
                return integer;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var numeric))
                return numeric;
        }

        return text;
    }

    private static bool IsStructuralDisplay(string value)
        => value.StartsWith("Struct(", StringComparison.OrdinalIgnoreCase) ||
           value.StartsWith("Array(", StringComparison.OrdinalIgnoreCase);

    private static bool RequiresSchemaProjection(ArMms.MmsDataValue? value, string dataType, string readReference, string requestedReference)
    {
        if (value == null || value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return false;

        if (!ReferencesEqual(readReference, requestedReference))
            return true;

        return IsScalarLeafReference(requestedReference) || IsScalarDataTypeHint(dataType);
    }

    private static bool IsScalarLeafReference(string reference)
    {
        var r = NormalizeReference(reference);
        return r.EndsWith(".stval") ||
               r.EndsWith(".general") ||
               r.EndsWith(".valwtr.posval") ||
               r.EndsWith(".posval") ||
               r.EndsWith(".ctlval") ||
               r.EndsWith(".actval") ||
               r.EndsWith(".setval") ||
               r.EndsWith(".ctlmodel") ||
               r.EndsWith(".q") ||
               r.EndsWith(".t") ||
               r.EndsWith(".f") ||
               r.EndsWith(".i");
    }

    private static bool IsScalarDataTypeHint(string dataType)
    {
        var hint = (dataType ?? string.Empty).Trim();
        return hint.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Enum", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Float32", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Double", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Quality", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("UtcTime", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatBindingDiagnostics(IReadOnlyList<string> diagnostics)
    {
        if (diagnostics.Count == 0)
            return "no binding diagnostics";

        var text = string.Join("; ", diagnostics.Take(4));
        if (diagnostics.Count > 4)
            text += $"; +{diagnostics.Count - 4} more";
        return text;
    }

    private static string BuildSchemaProjectionBlockedMessage(
        ArMms.MmsDataValue? value,
        string readReference,
        string requestedReference,
        string bindingStatus)
    {
        var status = string.IsNullOrWhiteSpace(bindingStatus) ? "ARIEC61850 binding engine did not return a usable scalar projection." : bindingStatus;
        var shape = value == null ? "null" : $"{value.Kind} child-count={value.Children.Count.ToString(CultureInfo.InvariantCulture)}";
        var raw = value == null ? "-" : ArMms.MmsDataValueRenderer.ToCompactString(value, readReference);
        return $"ARIEC61850 schema binding required for structured MMS value {readReference} -> {requestedReference}, but binding was not usable: {status}. Raw shape={shape}, raw={Truncate(raw, 240)}. Value was not published to avoid wrong DA/leaf mapping.";
    }

    private static string Truncate(string value, int maxLength)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= maxLength)
            return value;
        if (maxLength <= 3)
            return value[..maxLength];
        return value[..(maxLength - 3)] + "...";
    }

    private static string FirstUseful(string primary, IReadOnlyList<string> inherited, string fallback)
    {
        if (IsUsefulColumn(primary))
            return primary;
        foreach (var value in inherited)
        {
            if (IsUsefulColumn(value))
                return value;
        }
        return IsUsefulColumn(fallback) ? fallback : string.Empty;
    }

    private static bool IsUsefulColumn(string value)
        => !string.IsNullOrWhiteSpace(value) && value != "-";

    private static string FirstUsefulText(params string[] values)
        => values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v) && v != "-") ?? string.Empty;

    private static bool TryProjectStructuredLeafBySemantic(
        ArMms.MmsDataValue? value,
        string dataType,
        string readReference,
        string requestedReference,
        out Iec61850ReadValue projected,
        out string status)
    {
        projected = new Iec61850ReadValue();
        status = string.Empty;

        if (value == null || value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return false;

        string[] segments;
        if (IsScalarLeafReference(requestedReference))
        {
            if (!TryGetRelativeLeafSegments(readReference, requestedReference, out segments))
                return false;
        }
        else if (IsStatusScalarDataTypeHint(dataType))
        {
            segments = new[] { "stVal" };
        }
        else
        {
            return false;
        }

        if (IsLegacyAvrTapChangeStatusReference(requestedReference) &&
            EndsWithPathSegment(readReference, "TapChg"))
        {
            segments = new[] { "valWTr", "posVal" };
        }

        var leaf = segments[^1];
        if (!IsSemanticLeafProjectionCandidate(leaf, requestedReference))
            return false;

        var branch = value;
        if (segments.Length == 2 && IsKnownSingleStructBranch(segments[0]) && TryGetOnlyMeaningfulStructChild(value, out var childBranch))
            branch = childBranch;
        else if (segments.Length > 1)
            return false;

        if (!TrySelectSemanticLeaf(branch, leaf, dataType, requestedReference, out var selected, out var reason) || selected == null)
        {
            status = $"semantic structured projection did not find a trustworthy {leaf} child for {requestedReference}; raw structure was not published.";
            return false;
        }

        var rawValue = ConvertProjectedValue(selected, dataType, requestedReference);
        var display = FormatProjectedDisplay(rawValue, dataType);
        var quality = DecodeQuality(FindQualityChild(branch) ?? branch);
        var timestamp = DecodeTimestamp(FindTimestampChild(branch) ?? branch);

        if (IsQualityHint(dataType, requestedReference))
            quality = FirstUsefulText(DecodeQuality(selected), quality);
        if (IsTimestampHint(dataType, requestedReference))
            timestamp = FirstUsefulText(DecodeTimestamp(selected), timestamp);

        projected = new Iec61850ReadValue
        {
            Value = rawValue,
            DisplayValue = display,
            Quality = quality,
            DeviceTimestamp = timestamp,
            SourceReference = requestedReference,
            ReadReference = readReference,
            Projection = $"semantic MMS structure projection: {reason}; read={readReference}; source={requestedReference}"
        };
        status = projected.Projection;
        return true;
    }

    private static bool TryGetRelativeLeafSegments(string readReference, string requestedReference, out string[] segments)
    {
        segments = Array.Empty<string>();
        var read = NormalizeReference(readReference);
        var requested = NormalizeReference(requestedReference);
        if (string.IsNullOrWhiteSpace(requested))
            return false;

        if (ReferencesEqual(readReference, requestedReference))
        {
            var leaf = LastSegment(requestedReference);
            if (string.IsNullOrWhiteSpace(leaf))
                return false;
            segments = new[] { leaf };
            return true;
        }

        var prefix = read.EndsWith('.') ? read : read + ".";
        if (!requested.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var suffix = requested[prefix.Length..];
        segments = suffix.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length > 0;
    }

    private static bool IsSemanticLeafProjectionCandidate(string leaf, string requestedReference)
    {
        if (leaf.Equals("q", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("t", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("stVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("general", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("posVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("ctlVal", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return requestedReference.EndsWith(".valWTr.posVal", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsLegacyAvrTapChangeStatusReference(string requestedReference)
        => NormalizeReference(requestedReference).EndsWith(".tapchg.stval", StringComparison.OrdinalIgnoreCase);

    private static bool IsStatusScalarDataTypeHint(string dataType)
    {
        var hint = (dataType ?? string.Empty).Trim();
        return hint.Equals("Boolean", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Bool", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Enum", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
               hint.Equals("Integer", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsKnownSingleStructBranch(string segment)
        => segment.Equals("ValWTr", StringComparison.OrdinalIgnoreCase);

    private static bool TryGetOnlyMeaningfulStructChild(ArMms.MmsDataValue value, out ArMms.MmsDataValue child)
    {
        child = value;
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return false;

        var candidates = value.Children
            .Where(x => x.Kind is ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array)
            .Where(x => FindQualityChild(x) == null || x.Children.Any(c => c.Kind is not ArMms.MmsDataKind.BitString))
            .ToArray();
        if (candidates.Length != 1)
            return false;

        child = candidates[0];
        return true;
    }

    private static bool TrySelectSemanticLeaf(
        ArMms.MmsDataValue value,
        string leaf,
        string dataType,
        string requestedReference,
        out ArMms.MmsDataValue? selected,
        out string reason)
    {
        selected = null;
        reason = string.Empty;
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return false;

        var children = value.Children.ToArray();
        if (children.Length == 0)
            return false;

        if (leaf.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            selected = FindQualityChild(value);
            reason = "quality child selected by IEC 61850 quality bit-string shape";
            return selected != null;
        }

        if (leaf.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            selected = FindTimestampChild(value);
            reason = "timestamp child selected by MMS UTC/BinaryTime kind";
            return selected != null;
        }

        var payloadChildren = children
            .Where(x => !IsTimestampValue(x))
            .Where(x => !LooksLikeQualityBitString(x))
            .ToArray();
        if (payloadChildren.Length == 0)
            return false;

        if (leaf.Equals("general", StringComparison.OrdinalIgnoreCase) || dataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            selected = payloadChildren.FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.Boolean)
                ?? payloadChildren.FirstOrDefault(x => x.Kind is ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned);
            reason = "Boolean/status child selected after excluding q/t siblings";
            return selected != null;
        }

        if (IsDbposHint(dataType, requestedReference))
        {
            selected = payloadChildren.FirstOrDefault(IsShortStatusBitString)
                ?? payloadChildren.FirstOrDefault(x => x.Kind is ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned)
                ?? payloadChildren.FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.Boolean)
                ?? payloadChildren.FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.BitString);
            reason = "Dbpos/status child selected after excluding quality/timestamp siblings";
            return selected != null;
        }

        if (leaf.Equals("posVal", StringComparison.OrdinalIgnoreCase) ||
            leaf.Equals("ctlVal", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Int32", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("UInt32", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Integer", StringComparison.OrdinalIgnoreCase) ||
            dataType.Equals("Enum", StringComparison.OrdinalIgnoreCase))
        {
            selected = payloadChildren.FirstOrDefault(x => x.Kind is ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned)
                ?? payloadChildren.FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.Boolean)
                ?? payloadChildren.FirstOrDefault(IsShortStatusBitString)
                ?? payloadChildren.FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.BitString);
            reason = "integer/enum status child selected after excluding q/t siblings";
            return selected != null;
        }

        selected = payloadChildren.FirstOrDefault(x => x.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array));
        reason = "first scalar payload child selected after excluding q/t siblings";
        return selected != null;
    }

    private static ArMms.MmsDataValue? FindQualityChild(ArMms.MmsDataValue value)
    {
        if (LooksLikeQualityBitString(value))
            return value;
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return null;

        return value.Children.FirstOrDefault(LooksLikeQualityBitString)
            ?? value.Children.Select(FindQualityChild).FirstOrDefault(x => x != null);
    }

    private static ArMms.MmsDataValue? FindTimestampChild(ArMms.MmsDataValue value)
    {
        if (IsTimestampValue(value))
            return value;
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return null;

        return value.Children.FirstOrDefault(IsTimestampValue)
            ?? value.Children.Select(FindTimestampChild).FirstOrDefault(x => x != null);
    }

    private static bool IsTimestampValue(ArMms.MmsDataValue value)
        => value.Kind is ArMms.MmsDataKind.UtcTime or ArMms.MmsDataKind.BinaryTime ||
           (value.Kind == ArMms.MmsDataKind.Unknown && value.UnknownTagNumber == 12);

    private static bool LooksLikeQualityBitString(ArMms.MmsDataValue value)
        => value.Kind == ArMms.MmsDataKind.BitString &&
           BitStringBitLength(value) >= 12 &&
           Iec61850QualityDecoder.Decode(value).IsDecoded;

    private static bool IsShortStatusBitString(ArMms.MmsDataValue value)
        => value.Kind == ArMms.MmsDataKind.BitString &&
           BitStringBitLength(value) > 0 &&
           BitStringBitLength(value) < 12;

    private static int BitStringBitLength(ArMms.MmsDataValue value)
    {
        if (value.Kind != ArMms.MmsDataKind.BitString || value.RawValue.Count == 0)
            return 0;
        var unusedBits = value.RawValue[0];
        var dataBytes = Math.Max(0, value.RawValue.Count - 1);
        return Math.Max(0, dataBytes * 8 - unusedBits);
    }

    private sealed record MmsValueProjection(
        ArMms.MmsDataValue? Value,
        string Quality,
        string Timestamp,
        string Description);

    private static MmsValueProjection SelectProjectedValue(ArMms.MmsDataValue? value, string dataType, string requestedReference)
    {
        if (value == null)
            return new MmsValueProjection(null, string.Empty, string.Empty, "null");

        var quality = DecodeQuality(value);
        var timestamp = DecodeTimestamp(value);
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return new MmsValueProjection(value, quality, timestamp, "scalar");

        var r = NormalizeReference(requestedReference);
        var hint = dataType ?? string.Empty;

        if (IsQualityHint(hint, requestedReference))
        {
            var qValue = FindFirst(value, v => Iec61850QualityDecoder.Decode(v).IsDecoded);
            return new MmsValueProjection(qValue ?? value, quality, timestamp, "projected-quality");
        }

        if (IsTimestampHint(hint, requestedReference))
        {
            var tValue = FindFirst(value, v => Iec61850TimestampDecoder.Decode(v).IsDecoded);
            return new MmsValueProjection(tValue ?? value, quality, timestamp, "projected-timestamp");
        }

        if (r.Contains(".tapchg.") && (r.EndsWith(".valwtr.posval") || r.EndsWith(".posval") || r.EndsWith(".stval") || hint.Equals("Int32", StringComparison.OrdinalIgnoreCase)))
        {
            var branch = SelectValueBranch(value, requestedReference);
            var tap = FindFirstInteger(branch) ?? FindFirstFloating(branch) ?? FindFirstScalar(branch);
            return new MmsValueProjection(tap ?? value, quality, timestamp, "projected-avr-tapchg-posval");
        }

        if (r.EndsWith(".stval"))
        {
            var branch = SelectValueBranch(value, requestedReference);
            var selected = SelectStatusScalar(branch, hint);
            return new MmsValueProjection(selected ?? value, quality, timestamp, "projected-stval");
        }

        if (r.EndsWith(".f") || r.EndsWith(".mag.f") || hint.Equals("Float32", StringComparison.OrdinalIgnoreCase))
        {
            var branch = SelectValueBranch(value, requestedReference);
            var selected = FindFirstFloating(branch) ?? FindFirstInteger(branch);
            return new MmsValueProjection(selected ?? value, quality, timestamp, "projected-analogue");
        }

        if (IsDbposHint(hint, requestedReference))
        {
            var selected = FindFirst(value, v => v.Kind is ArMms.MmsDataKind.BitString or ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned);
            return new MmsValueProjection(selected ?? value, quality, timestamp, "projected-dbpos");
        }

        if (hint.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
        {
            var branch = SelectValueBranch(value, requestedReference);
            var selected = FindFirst(branch, v => v.Kind == ArMms.MmsDataKind.Boolean);
            return new MmsValueProjection(selected ?? value, quality, timestamp, "projected-boolean");
        }

        if (hint.Equals("Int32", StringComparison.OrdinalIgnoreCase) || hint.Equals("UInt32", StringComparison.OrdinalIgnoreCase))
        {
            var branch = SelectValueBranch(value, requestedReference);
            var selected = FindFirstInteger(branch);
            return new MmsValueProjection(selected ?? value, quality, timestamp, "projected-integer");
        }

        return new MmsValueProjection(FindFirstScalar(value) ?? value, quality, timestamp, "projected-first-meaningful-scalar");
    }

    private static object? ConvertProjectedValue(ArMms.MmsDataValue? value, string dataType, string reference)
    {
        if (value == null)
            return null;

        var hint = dataType ?? string.Empty;

        if (IsQualityHint(hint, reference))
        {
            var quality = Iec61850QualityDecoder.Decode(value);
            return quality.IsDecoded ? quality.Validity : ArMms.MmsDataValueRenderer.ToCompactString(value, reference);
        }

        if (IsTimestampHint(hint, reference))
        {
            var timestamp = Iec61850TimestampDecoder.Decode(value);
            return timestamp.IsDecoded ? timestamp.DisplayTime : ArMms.MmsDataValueRenderer.ToCompactString(value, reference);
        }

        if (reference.EndsWith(".ctlModel", StringComparison.OrdinalIgnoreCase))
            return Iec61850EnumValueDecoder.DecodeControlModel(value);

        if (IsDbposHint(hint, reference))
            return DecodeDbposToTesterValue(value);

        if (TryDecodeStandardEnum(value, reference, out var enumText))
            return enumText;

        switch (value.Kind)
        {
            case ArMms.MmsDataKind.Boolean:
                return value.Value is bool b && b;
            case ArMms.MmsDataKind.Integer:
                return Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
            case ArMms.MmsDataKind.Unsigned:
            {
                var unsigned = Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture);
                return unsigned <= long.MaxValue ? (long)unsigned : unsigned.ToString(CultureInfo.InvariantCulture);
            }
            case ArMms.MmsDataKind.FloatingPoint:
                return Convert.ToDouble(value.Value, CultureInfo.InvariantCulture);
            case ArMms.MmsDataKind.VisibleString:
            case ArMms.MmsDataKind.MmsString:
                return Convert.ToString(value.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            case ArMms.MmsDataKind.BitString:
                return ArMms.MmsDataValueRenderer.ToCompactString(value, reference);
            case ArMms.MmsDataKind.UtcTime:
            case ArMms.MmsDataKind.BinaryTime:
            case ArMms.MmsDataKind.OctetString:
                return ArMms.MmsDataCodec.ToDisplayString(value);
            default:
                return ArMms.MmsDataValueRenderer.ToCompactString(value, reference);
        }
    }

    private static string FormatProjectedDisplay(object? value, string dataType)
    {
        if (value == null)
            return "-";
        if (value is string text)
            return text;
        return Iec61850ValueFormatter.Format(value, dataType, string.Empty);
    }

    private static string DecodeQuality(ArMms.MmsDataValue value)
    {
        var quality = Iec61850QualityDecoder.Decode(value);
        return quality.IsDecoded ? quality.Validity : string.Empty;
    }

    private static string DecodeTimestamp(ArMms.MmsDataValue value)
    {
        var timestamp = Iec61850TimestampDecoder.Decode(value);
        return timestamp.IsDecoded ? timestamp.DisplayTime : string.Empty;
    }

    private static ArMms.MmsDataValue SelectValueBranch(ArMms.MmsDataValue value, string requestedReference)
    {
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array) || value.Children.Count == 0)
            return value;

        var r = NormalizeReference(requestedReference);
        if (r.EndsWith(".valwtr.posval") || r.EndsWith(".posval"))
            return FirstChildOrSelf(FirstChildOrSelf(value));

        if (r.EndsWith(".cval.mag.f") || r.EndsWith(".instcval.mag.f"))
            return FirstChildOrSelf(FirstChildOrSelf(FirstChildOrSelf(value)));

        if (r.EndsWith(".mag.f") || r.EndsWith(".instmag.f") || r.EndsWith(".ang.f"))
            return FirstChildOrSelf(FirstChildOrSelf(value));

        if (r.EndsWith(".stval") || r.EndsWith(".general"))
            return FirstChildOrSelf(value);

        return value;
    }

    private static ArMms.MmsDataValue FirstChildOrSelf(ArMms.MmsDataValue value)
        => value.Kind is ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array
            ? value.Children.FirstOrDefault() ?? value
            : value;

    private static ArMms.MmsDataValue? SelectStatusScalar(ArMms.MmsDataValue value, string dataType)
    {
        if (dataType.Equals("Boolean", StringComparison.OrdinalIgnoreCase))
            return FindFirst(value, v => v.Kind == ArMms.MmsDataKind.Boolean) ?? FindFirstInteger(value);

        return FindFirstInteger(value) ??
               FindFirst(value, v => v.Kind == ArMms.MmsDataKind.BitString) ??
               FindFirst(value, v => v.Kind == ArMms.MmsDataKind.Boolean) ??
               FindFirstScalar(value);
    }

    private static ArMms.MmsDataValue? FindFirstInteger(ArMms.MmsDataValue value)
        => FindFirst(value, v => v.Kind is ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned);

    private static ArMms.MmsDataValue? FindFirstFloating(ArMms.MmsDataValue value)
        => FindFirst(value, v => v.Kind == ArMms.MmsDataKind.FloatingPoint);

    private static ArMms.MmsDataValue? FindFirst(ArMms.MmsDataValue value, Func<ArMms.MmsDataValue, bool> predicate)
    {
        if (predicate(value))
            return value;

        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
            return null;

        foreach (var child in value.Children)
        {
            var match = FindFirst(child, predicate);
            if (match != null)
                return match;
        }

        return null;
    }

    private static bool TryDecodeStandardEnum(ArMms.MmsDataValue value, string reference, out string text)
    {
        text = string.Empty;
        if (value.Kind is not (ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned))
            return false;

        if (!TryParseReferenceParts(reference, out var logicalNodeClass, out var dataObjectName, out var attributeName))
            return false;

        if (!Iec61850StandardEnumRegistry.TryResolve(logicalNodeClass, dataObjectName, "INS", attributeName, out var enumDefinition) &&
            !Iec61850StandardEnumRegistry.TryResolve(logicalNodeClass, dataObjectName, "INC", attributeName, out enumDefinition) &&
            !Iec61850StandardEnumRegistry.TryResolve(logicalNodeClass, dataObjectName, "ENC", attributeName, out enumDefinition))
        {
            return false;
        }

        var numeric = value.Kind == ArMms.MmsDataKind.Integer
            ? Convert.ToInt64(value.Value, CultureInfo.InvariantCulture)
            : unchecked((long)Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture));
        var match = enumDefinition.Values.FirstOrDefault(v => v.Ord == numeric);
        if (match == null)
            return false;

        text = match.Symbol;
        return true;
    }

    private static bool TryParseReferenceParts(string reference, out string logicalNodeClass, out string dataObjectName, out string attributeName)
    {
        logicalNodeClass = string.Empty;
        dataObjectName = string.Empty;
        attributeName = string.Empty;

        var text = (reference ?? string.Empty).Trim().Replace('$', '.');
        var slash = text.IndexOf('/');
        if (slash < 0 || slash >= text.Length - 1)
            return false;

        var path = text[(slash + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (path.Length < 2)
            return false;

        logicalNodeClass = SignalDefinition.DetectLogicalNodeClass(path[0]);
        dataObjectName = path[1];
        attributeName = path[^1];
        return !string.IsNullOrWhiteSpace(dataObjectName) && !string.IsNullOrWhiteSpace(attributeName);
    }

    private static object DecodeDbposToTesterValue(ArMms.MmsDataValue value)
    {
        if (value.Kind == ArMms.MmsDataKind.BitString)
        {
            var raw = value.RawValue.ToArray();
            if (raw.Length >= 2)
            {
                var code = (raw[1] >> 6) & 0x03;
                return code;
            }
        }

        if (value.Kind == ArMms.MmsDataKind.Integer)
        {
            var numeric = Convert.ToInt64(value.Value, CultureInfo.InvariantCulture);
            if (numeric is >= 0 and <= 3)
                return (int)numeric;
        }

        if (value.Kind == ArMms.MmsDataKind.Unsigned)
        {
            var numeric = Convert.ToUInt64(value.Value, CultureInfo.InvariantCulture);
            if (numeric <= 3)
                return (int)numeric;
        }

        if (value.Kind == ArMms.MmsDataKind.Boolean)
            return value.Value is bool b && b ? 2 : 1;

        return Iec61850EnumValueDecoder.DecodeDbpos(value);
    }

    private static bool TrySelectStructuredChild(ArMms.MmsDataValue value, string reference, string dataType, out ArMms.MmsDataValue? child)
    {
        child = null;
        if (value.Children.Count == 0)
            return false;

        var leaf = LastSegment(reference);
        if (leaf.Equals("stVal", StringComparison.OrdinalIgnoreCase))
        {
            var qIndex = FindQualityIndex(value.Children);
            var index = qIndex > 0 ? qIndex - 1 : 0;
            child = value.Children[Math.Clamp(index, 0, value.Children.Count - 1)];
            return true;
        }

        if (leaf.Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            var qIndex = FindQualityIndex(value.Children);
            if (qIndex >= 0)
            {
                child = value.Children[qIndex];
                return true;
            }
        }

        if (leaf.Equals("t", StringComparison.OrdinalIgnoreCase))
        {
            var tIndex = FindTimestampIndex(value.Children);
            if (tIndex >= 0)
            {
                child = value.Children[tIndex];
                return true;
            }
        }

        if (leaf.Equals("f", StringComparison.OrdinalIgnoreCase))
        {
            var scalar = FlattenScalars(value).FirstOrDefault(x => x.Kind == ArMms.MmsDataKind.FloatingPoint);
            if (scalar != null)
            {
                child = scalar;
                return true;
            }
        }

        if (IsDbposHint(dataType, reference))
        {
            var dbpos = value.Children.FirstOrDefault(x =>
                x.Kind is ArMms.MmsDataKind.BitString or ArMms.MmsDataKind.Integer or ArMms.MmsDataKind.Unsigned or ArMms.MmsDataKind.Boolean);
            if (dbpos != null)
            {
                child = dbpos;
                return true;
            }
        }

        return false;
    }

    private static ArMms.MmsDataValue? FindFirstScalar(ArMms.MmsDataValue value)
        => FlattenScalars(value).FirstOrDefault();

    private static IEnumerable<ArMms.MmsDataValue> FlattenScalars(ArMms.MmsDataValue value)
    {
        if (value.Kind is not (ArMms.MmsDataKind.Structure or ArMms.MmsDataKind.Array))
        {
            yield return value;
            yield break;
        }

        foreach (var child in value.Children)
        {
            foreach (var scalar in FlattenScalars(child))
                yield return scalar;
        }
    }

    private static int FindQualityIndex(IReadOnlyList<ArMms.MmsDataValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Iec61850QualityDecoder.Decode(values[i]).IsDecoded)
                return i;
        }

        return -1;
    }

    private static int FindTimestampIndex(IReadOnlyList<ArMms.MmsDataValue> values)
    {
        for (var i = 0; i < values.Count; i++)
        {
            if (Iec61850TimestampDecoder.Decode(values[i]).IsDecoded)
                return i;
        }

        return -1;
    }

    private static bool IsQualityHint(string dataType, string reference)
        => dataType.Equals("Quality", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".q", StringComparison.OrdinalIgnoreCase);

    private static bool IsTimestampHint(string dataType, string reference)
        => dataType.Equals("Timestamp", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".t", StringComparison.OrdinalIgnoreCase);

    private static bool IsDbposHint(string dataType, string reference)
        => dataType.Equals("Dbpos", StringComparison.OrdinalIgnoreCase) ||
           reference.EndsWith(".Pos.stVal", StringComparison.OrdinalIgnoreCase) ||
           reference.Contains(".Pos.stVal", StringComparison.OrdinalIgnoreCase);


    private static bool ReadOptionalBooleanProperty(object instance, string propertyName, bool fallback)
    {
        if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            return fallback;

        try
        {
            var property = instance.GetType().GetProperty(propertyName);
            return property?.PropertyType == typeof(bool) && property.GetValue(instance) is bool value
                ? value
                : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    private static int ReadOptionalInt32Property(object instance, string propertyName)
    {
        if (instance == null || string.IsNullOrWhiteSpace(propertyName))
            return 0;

        try
        {
            var property = instance.GetType().GetProperty(propertyName);
            var value = property?.GetValue(instance);
            return value switch
            {
                int number => number,
                long number when number is >= int.MinValue and <= int.MaxValue => (int)number,
                _ => 0
            };
        }
        catch
        {
            return 0;
        }
    }

    private bool HasArIedReportCompatibilityApi()
    {
        var updateType = typeof(ArMms.MmsReportSignalUpdate);
        var sessionType = _session.GetType();
        return updateType.GetProperty("HasValue")?.PropertyType == typeof(bool) &&
               updateType.GetProperty("HasQuality")?.PropertyType == typeof(bool) &&
               updateType.GetProperty("HasTimestamp")?.PropertyType == typeof(bool) &&
               sessionType.GetProperty("UnroutedPersistentReportCount") != null;
    }

    private static string LastSegment(string reference)
    {
        var text = (reference ?? string.Empty).Replace('$', '.');
        var slash = text.LastIndexOf('/');
        var start = slash >= 0 ? slash + 1 : 0;
        var dot = text.LastIndexOf('.');
        return dot >= start && dot < text.Length - 1 ? text[(dot + 1)..] : text[start..];
    }
}
