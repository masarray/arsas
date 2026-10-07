using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Deterministic acquisition path used only by Static DataSet report-only mode.
///
/// Static mode already has complete configuration authority in the opened/live SCL model:
/// exact DataSet membership and configured RCB -> DataSet bindings. Do not run those facts
/// through the adaptive Hybrid acquisition planner. The live association is used only to
/// verify that the configured RCB (or a concrete indexed instance of that configured family)
/// and exact DataSet directory exist, then ARIEC's persistent monitor installs the
/// InformationReport receiver, enables RptEna and requests GI. No dynamic DataSet write and
/// no cyclic process-value MMS read is permitted here.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private readonly Dictionary<string, ArMms.MmsReportSubscriptionPlan> _deterministicStaticSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    public async Task<NativeHybridReportPlanningResult> BuildStaticDataSetReportPlansAsync(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<Iec61850MonitorPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();

        _deterministicStaticSubscriptions.Clear();
        ResetSemanticReportProjectionContext();

        var projectionModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel;
        if (projectionModel is null)
        {
            return StaticPlanningUnavailable(
                points,
                "Static DataSet report-only requires an opened/live model with exact configured DataSet membership.");
        }

        if (!_session.IsMmsInitiated)
        {
            return StaticPlanningUnavailable(
                points,
                $"Static DataSet report-only requires an initiated MMS association. Current state: {_session.State}.");
        }

        var canonicalModel = ResolveCanonicalRuntimeModel(device, out var canonicalError);
        if (canonicalModel is null)
            return StaticPlanningUnavailable(points, canonicalError);

        SetSemanticReportProjectionAuthority(projectionModel);
        var modelDataSetDirectories = BuildModelDataSetDirectories(
            projectionModel,
            device.SclWorkspace?.DesignModel is not null ? "SclDesignModel" : "LiveDiscoveryModel");

        var selections = BuildCanonicalStaticSelections(points);
        if (selections.Length == 0)
        {
            return StaticPlanningUnavailable(
                points,
                "No exact IEC 61850 signal identity was available for canonical static acquisition.");
        }

        var callerOwnedRcbReferences = _reportMonitorSessions.Values
            .Select(session => NormalizeStaticReference(session.ReportControl.Reference))
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // P6.1 engine-owned hot path:
        // local canonical coverage -> targeted live RCB reconciliation -> exact-target JIT
        // availability. Full discovery and application-side indexed-family fabrication are
        // intentionally excluded from Static DataSet acquisition.
        var smart = await _session.PrepareCanonicalStaticAcquisitionSmartAsync(
                canonicalModel,
                selections,
                directory: null,
                reconciliationOptions: new ArMms.MmsCanonicalStaticLiveReconciliationOptions
                {
                    MaxDomains = 16,
                    MaxVariableNamesPerDomain = 20000,
                    MaxNameListPages = 64,
                    MaxConcurrentDomains = 4,
                    UnknownPeerMaxConcurrentDomains = 2,
                    MaxReportControlCandidates = 64
                },
                acquisitionOptions: new ArMms.MmsCanonicalStaticAcquisitionProbeOptions
                {
                    MaxExactTargets = 64,
                    ReadDataSetDirectories = true,
                    CallerOwnedRcbReferences = callerOwnedRcbReferences
                },
                cancellationToken)
            .ConfigureAwait(false);

        var warnings = smart.Reconciliation.Warnings
            .Concat(smart.Acquisition.Warnings)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        var availability = smart.Acquisition.Availability;
        if (availability is null)
        {
            return StaticPlanningUnavailable(
                points,
                $"{smart.Summary} No exact live RCB availability evidence was produced.");
        }

        var reportPlans = new List<ReportControlPlan>();
        var coveredPointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var segment in smart.Coverage.Segments)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var targetSegment = smart.Acquisition.Plan.TargetResolution.Segments
                .SingleOrDefault(item =>
                    SameStaticReference(item.DataSetReference, segment.DataSetReference));
            var exactTargets = targetSegment?.ExactLiveReportControlReferences
                .Select(NormalizeStaticReference)
                .Where(reference => reference.Length > 0)
                .ToHashSet(StringComparer.OrdinalIgnoreCase)
                ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var eligible = availability.ReportControls
                .Where(snapshot =>
                    exactTargets.Contains(NormalizeStaticReference(snapshot.Reference)))
                .Select(snapshot => new
                {
                    Snapshot = snapshot,
                    Eligibility = ArMms.MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
                        snapshot,
                        allowCallerOwned: true,
                        allowReducedMissingReservationEvidence: true)
                })
                .Where(item => item.Eligibility.IsEligible)
                .OrderBy(item => item.Eligibility.Kind switch
                {
                    ArMms.MmsConfiguredStaticRcbEligibilityKind.CallerOwned => 0,
                    ArMms.MmsConfiguredStaticRcbEligibilityKind.ExplicitFree => 1,
                    ArMms.MmsConfiguredStaticRcbEligibilityKind.ReducedMissingReservationEvidence => 2,
                    _ => 9
                })
                .ThenByDescending(item => item.Snapshot.Buffered)
                .ThenBy(item => item.Snapshot.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (eligible.Length == 0)
            {
                var rejected = availability.ReportControls
                    .Where(snapshot =>
                        exactTargets.Contains(NormalizeStaticReference(snapshot.Reference)))
                    .Select(snapshot =>
                    {
                        var evaluation = ArMms.MmsConfiguredStaticRcbEligibilityPolicy.Evaluate(
                            snapshot,
                            allowCallerOwned: true,
                            allowReducedMissingReservationEvidence: true);
                        return $"{snapshot.Reference}[{snapshot.Availability}/{snapshot.Confidence}: {evaluation.Reason}]";
                    })
                    .Take(8)
                    .ToArray();
                warnings.Add(
                    $"{segment.DataSetReference}: engine resolved configured static coverage but no exact live RCB is eligible. " +
                    (rejected.Length == 0 ? "No exact live target evidence." : string.Join(", ", rejected)));
                continue;
            }

            var selected = eligible[0];
            var liveRcb = CandidateFromAvailability(selected.Snapshot);
            var configured = segment.ReportControls
                .Where(report => report.Buffered == selected.Snapshot.Buffered)
                .OrderBy(report => report.Reference, StringComparer.Ordinal)
                .FirstOrDefault()
                ?? segment.ReportControls.FirstOrDefault();

            if (configured is null)
            {
                warnings.Add(
                    $"{segment.DataSetReference}: canonical coverage contained no configured ReportControl metadata; target was not armed.");
                continue;
            }

            var modelDirectory = modelDataSetDirectories.SingleOrDefault(result =>
                result.IsSuccess &&
                SameStaticReference(result.DataSetReference, segment.DataSetReference));
            if (modelDirectory is null || modelDirectory.Members.Count == 0)
            {
                warnings.Add(
                    $"{segment.DataSetReference}: canonical model has no ordered DataSet members. Unsafe positional projection was refused.");
                continue;
            }

            var liveDirectory = DirectoryFromAvailability(selected.Snapshot);
            if (!liveDirectory.IsSuccess || liveDirectory.Members.Count == 0)
            {
                warnings.Add(
                    $"{segment.DataSetReference}: targeted live availability did not prove a populated DataSet directory. RCB was not armed.");
                continue;
            }

            if (!TryVerifyStaticDataSetMemberOrder(modelDirectory, liveDirectory, out var directoryMismatch))
            {
                warnings.Add(
                    $"{segment.DataSetReference}: canonical/live DataSet member order mismatch ({directoryMismatch}). RCB was not armed.");
                continue;
            }

            var bindings = points
                .Where(point =>
                    SameStaticReference(point.DataSetReference, segment.DataSetReference) ||
                    segment.SelectedSignalReferences.Any(reference =>
                        string.Equals(
                            NormalizeStaticReference(reference),
                            NormalizeStaticReference(point.IecReference),
                            StringComparison.OrdinalIgnoreCase)))
                .GroupBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (bindings.Count == 0)
                continue;

            var plan = new ReportControlPlan
            {
                RelayId = device.DeviceId,
                RelayName = device.Name,
                RelayIpAddress = device.IpAddress,
                IedName = device.Name,
                ReportControlReference = selected.Snapshot.Reference,
                DataSetReference = segment.DataSetReference,
                Mode = "Static DataSet • engine canonical configured RCB",
                AllowDynamicDataSetWrites = false,
                Buffered = selected.Snapshot.Buffered,
                ReportId = selected.Snapshot.ReportId,
                IntegrityPeriodMs = ParseStaticInteger(selected.Snapshot.IntegrityPeriodMs),
                TriggerOptions = selected.Snapshot.TriggerOptions,
                OptionalFields = selected.Snapshot.OptionalFields,
                Status = "Engine canonical static report planned",
                IsEngineAuthoritative = true,
                EngineAcquisitionKind = selected.Snapshot.Buffered ? "StaticBrcb" : "StaticUrcb",
                Bindings = bindings
            };

            var subscriptionWarnings = new List<string>
            {
                $"Engine eligibility={selected.Eligibility.Kind}: {selected.Eligibility.Reason}",
                $"Targeted reconciliation={smart.Reconciliation.Status}; no full report discovery was used."
            };
            if (selected.Eligibility.UsesReducedEvidence)
            {
                subscriptionWarnings.Add(
                    "Reservation metadata is missing/reduced. Activation remains fail-closed and must prove RptEna/readback; report traffic is post-activation proof.");
            }

            var subscription = new ArMms.MmsReportSubscriptionPlan
            {
                Mode = ArMms.MmsReportSubscriptionPlanMode.StaticDataSet,
                Status = ArMms.MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
                ReportControl = liveRcb,
                DataSetReference = segment.DataSetReference,
                Members = modelDirectory.Members,
                DynamicPoints = Array.Empty<ArMms.MmsFcResolvedPoint>(),
                Steps = new[]
                {
                    $"Use canonical configured static coverage for {segment.DataSetReference}.",
                    $"Use exact live RCB {selected.Snapshot.Reference} proven by engine reconciliation.",
                    $"Use {modelDirectory.Members.Count} ordered canonical DataSet member(s), verified against targeted live directory evidence.",
                    "Install InformationReport receiver before RptEna mutation.",
                    "Use engine configured-static transactional activation and optional GI.",
                    "Treat traffic as post-activation evidence; never enable cyclic MMS process polling for this static segment."
                },
                Warnings = subscriptionWarnings
            };

            _deterministicStaticSubscriptions[plan.PlanId] = subscription;
            reportPlans.Add(plan);
            foreach (var binding in bindings)
                coveredPointKeys.Add(binding.PointKey);
        }

        var uncovered = points
            .Where(point => !coveredPointKeys.Contains(point.PointKey))
            .Select(point => point.PointKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var staticBrcb = reportPlans.Where(plan => plan.Buffered).Sum(plan => plan.BindingCount);
        var staticUrcb = reportPlans.Where(plan => !plan.Buffered).Sum(plan => plan.BindingCount);

        return new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = "ARIEC canonical static acquisition",
            Status = reportPlans.Count > 0 ? "StaticReportReady" : "StaticReportingUnavailable",
            Summary = reportPlans.Count > 0
                ? $"Engine canonical static path prepared {reportPlans.Count} RCB plan(s), covering {coveredPointKeys.Count}/{points.Count} selected point(s). {smart.Summary}"
                : $"No exact configured static RCB could be armed safely for {points.Count} selected point(s). {smart.Summary}",
            ReportPlans = reportPlans,
            PollingPointKeys = Array.Empty<string>(),
            UncoveredPointKeys = uncovered,
            UnmappedPointKeys = Array.Empty<string>(),
            PointAttemptEvidence = Array.Empty<NativeHybridPointAttemptEvidence>(),
            Warnings = warnings,
            RequestedPointCount = points.Count,
            CatalogMappedPointCount = points.Count,
            StaticBrcbSignalCount = staticBrcb,
            StaticUrcbSignalCount = staticUrcb,
            DynamicBrcbSignalCount = 0,
            DynamicUrcbSignalCount = 0,
            PollingFallbackSignalCount = 0,
            UncoveredSignalCount = uncovered.Length
        };
    }

    public async Task<NativeReportMonitorStartResult> StartStaticDataSetReportMonitorAsync(
        ReportControlPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_deterministicStaticSubscriptions.TryGetValue(plan.PlanId, out var subscription))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = "Deterministic Static DataSet subscription evidence is missing. RCB was not armed; MMS process polling remains disabled.",
                FailureReason = "StaticSubscriptionEvidenceMissing"
            };
        }

        if (_reportMonitorSessions.ContainsKey(plan.PlanId))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = true,
                PlanId = plan.PlanId,
                Message = $"Deterministic Static DataSet monitor already active for {plan.DisplayReference}.",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                ReportControlReference = plan.ReportControlReference,
                DataSetReference = plan.DataSetReference,
                AcquisitionLabel = $"Static DataSet: {plan.EngineAcquisitionKind}",
                CoveredReferences = _reportMonitorCoverage.TryGetValue(plan.PlanId, out var existingCoverage)
                    ? existingCoverage
                    : Array.Empty<string>()
            };
        }

        if (!_session.IsMmsInitiated)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"Deterministic Static DataSet monitor requires an initiated MMS association. Current state: {_session.State}.",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                FailureReason = "TransportUnavailable"
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = string.IsNullOrWhiteSpace(LastErrorMessage) ? "Fresh report discovery unavailable." : LastErrorMessage,
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                FailureReason = "FreshReportDiscoveryUnavailable"
            };
        }

        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);
        var attempt = await RunMmsOperationAsync(
            () => _session.StartPersistentReportMonitorClientCompatibleAsync(
                subscription,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: false,
                discovery.IedDirectory,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var start = attempt.StartResult;
        var warnings = start.Warnings
            .Concat(subscription.Warnings)
            .Concat(attempt.CleanupWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!attempt.IsSuccess || start.Session is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"Deterministic Static DataSet activation failed for {plan.DisplayReference}: {start.Message}",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = false,
                DynamicAttempted = false,
                DynamicAttemptState = "NotApplicable",
                FailureReason = attempt.FailureReason.ToString(),
                CleanupAttempted = attempt.CleanupAttempted,
                CleanupSucceeded = attempt.CleanupSucceeded,
                ReportControlReference = plan.ReportControlReference,
                DataSetReference = plan.DataSetReference,
                CoveredReferences = coveredReferences,
                Warnings = warnings
            };
        }

        if (!string.IsNullOrWhiteSpace(start.Session.ReportControl.Reference))
            plan.ReportControlReference = start.Session.ReportControl.Reference;
        if (!string.IsNullOrWhiteSpace(start.Session.Plan.DataSetReference))
            plan.DataSetReference = start.Session.Plan.DataSetReference;

        _reportMonitorSessions[plan.PlanId] = start.Session;
        _reportMonitorCoverage[plan.PlanId] = coveredReferences;

        return new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = plan.PlanId,
            Message = $"Deterministic Static DataSet {plan.EngineAcquisitionKind} active. {start.Message}",
            SubscriptionSummary = subscription.Summary,
            MemberCount = subscription.Members.Count,
            WriteStepCount = start.WriteSteps.Count,
            UsedDynamicDataSet = false,
            DynamicAttempted = false,
            DynamicAttemptState = "NotApplicable",
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            AcquisitionLabel = $"Static DataSet: {plan.EngineAcquisitionKind}",
            CoveredReferences = coveredReferences,
            Warnings = warnings
        };
    }

    private static NativeHybridReportPlanningResult StaticPlanningUnavailable(
        IReadOnlyCollection<Iec61850MonitorPoint> points,
        string reason)
        => new()
        {
            IsAuthoritative = true,
            Authority = "Deterministic Static DataSet configured-RCB path",
            Status = "StaticReportingUnavailable",
            Summary = reason + " Cyclic MMS process polling was not enabled.",
            RequestedPointCount = points.Count,
            CatalogMappedPointCount = points.Count,
            PollingPointKeys = Array.Empty<string>(),
            UncoveredPointKeys = points.Select(point => point.PointKey).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
            UnmappedPointKeys = Array.Empty<string>(),
            PointAttemptEvidence = Array.Empty<NativeHybridPointAttemptEvidence>(),
            Warnings = new[] { reason },
            PollingFallbackSignalCount = 0,
            UncoveredSignalCount = points.Count
        };

    private static bool TryVerifyStaticDataSetMemberOrder(
        ArMms.MmsDataSetDirectoryResult modelDirectory,
        ArMms.MmsDataSetDirectoryResult liveDirectory,
        out string mismatch)
    {
        mismatch = string.Empty;
        if (modelDirectory.Members.Count != liveDirectory.Members.Count)
        {
            mismatch = $"count model={modelDirectory.Members.Count}, live={liveDirectory.Members.Count}";
            return false;
        }

        for (var index = 0; index < modelDirectory.Members.Count; index++)
        {
            var modelMember = modelDirectory.Members[index];
            var liveMember = liveDirectory.Members[index];
            var mmsMatches =
                !string.IsNullOrWhiteSpace(modelMember.MmsReference) &&
                !string.IsNullOrWhiteSpace(liveMember.MmsReference) &&
                SameStaticReference(modelMember.MmsReference, liveMember.MmsReference);
            var userMatches =
                !string.IsNullOrWhiteSpace(modelMember.UserReference) &&
                !string.IsNullOrWhiteSpace(liveMember.UserReference) &&
                SameStaticReference(modelMember.UserReference, liveMember.UserReference);

            if (mmsMatches || userMatches)
                continue;

            mismatch =
                $"member[{index}] model={modelMember.UserReference} ({modelMember.MmsReference}), " +
                $"live={liveMember.UserReference} ({liveMember.MmsReference})";
            return false;
        }

        return true;
    }

    private static int StaticRcbAvailabilityRank(ArMms.MmsRcbOperationalAvailability availability)
        => availability switch
        {
            ArMms.MmsRcbOperationalAvailability.UsedByCaller => 0,
            ArMms.MmsRcbOperationalAvailability.Available => 1,
            ArMms.MmsRcbOperationalAvailability.Unknown => 2,
            _ => int.MaxValue
        };

    private static bool SameStaticReference(string? left, string? right)
        => string.Equals(
            NormalizeStaticReference(left),
            NormalizeStaticReference(right),
            StringComparison.OrdinalIgnoreCase);

    private static string NormalizeStaticReference(string? reference)
        => Iec61850StaticRcbReferenceMatcher.Normalize(reference);

    private static int ParseStaticInteger(string? value)
        => int.TryParse(value, out var parsed) && parsed > 0 ? parsed : 0;
}
