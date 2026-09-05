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

        var projectionModel = device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel;
        if (projectionModel is null)
        {
            return StaticPlanningUnavailable(
                points,
                "Static DataSet report-only requires an opened/live SCL model with exact DataSet and RCB configuration.");
        }

        if (!_session.IsMmsInitiated)
        {
            return StaticPlanningUnavailable(
                points,
                $"Static DataSet report-only requires an initiated MMS association. Current state: {_session.State}.");
        }

        SetSemanticReportProjectionAuthority(projectionModel);

        // Keep SCL design configuration authoritative even when a partial live model exists.
        // Fast/cached reconnects can populate LiveDiscoveryModel without reproducing every
        // configured ReportControl. Selecting `LiveDiscoveryModel ?? DesignModel` for RCB
        // configuration silently erased valid SCL evidence (field symptom: SCL saw Buffer02
        // for Digital, while runtime planning reported staticBRCB=0). The union below keeps
        // exact design authority and augments it with any report controls learned live.
        var configurationModels = new[]
            {
                device.SclWorkspace?.DesignModel,
                device.LiveDiscoveryModel
            }
            .Where(model => model is not null)
            .Cast<LiveIedModelDiscoveryDocument>()
            .ToArray();

        // Fresh report discovery is verification, not permission policy. In particular we
        // deliberately do NOT classify a configured BRCB through the Hybrid availability
        // confidence gate. Some perfectly usable servers expose RptEna and DatSet but omit
        // enough reservation metadata for that adaptive gate to call the RCB 'Available'.
        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery is null)
        {
            return StaticPlanningUnavailable(
                points,
                string.IsNullOrWhiteSpace(LastErrorMessage)
                    ? "Fresh report discovery was unavailable."
                    : LastErrorMessage);
        }

        var reportPlans = new List<ReportControlPlan>();
        var warnings = new List<string>();
        var coveredPointKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var selectedByDataSet = points
            .Where(point => !string.IsNullOrWhiteSpace(point.DataSetReference))
            .GroupBy(point => NormalizeStaticReference(point.DataSetReference), StringComparer.OrdinalIgnoreCase)
            .Where(group => !string.IsNullOrWhiteSpace(group.Key))
            .OrderBy(group => group.Key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var dataSetGroup in selectedByDataSet)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var configuredReports = configurationModels
                .SelectMany(configurationModel => configurationModel.ReportControls)
                .Where(report => SameStaticReference(report.DataSetReference, dataSetGroup.Key))
                .GroupBy(
                    report => $"{NormalizeStaticReference(report.Reference)}|{NormalizeStaticReference(report.DataSetReference)}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderByDescending(report => report.Buffered)
                .ThenBy(report => report.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (configuredReports.Length == 0)
            {
                warnings.Add(
                    $"{dataSetGroup.First().DataSetReference}: no configured BRCB/URCB in the SCL/live model union; {dataSetGroup.Count()} selected point(s) remain explicitly unavailable. No MMS process polling was substituted.");
                continue;
            }

            if (configuredReports.Length > 1)
            {
                warnings.Add(
                    $"{dataSetGroup.First().DataSetReference}: {configuredReports.Length} configured RCBs reference this DataSet; deterministic Static mode selected {configuredReports[0].Reference} (BRCB preferred, then literal reference order)." );
            }

            var configured = configuredReports[0];
            var dataSetReference = dataSetGroup.First().DataSetReference.Trim();

            // SCL can represent an indexed ReportControl family while the MMS server exposes
            // concrete instances (for example Buffer -> Buffer01/Buffer02). Exact identity
            // remains rank 0. The only fallback accepted here is a decimal indexed instance
            // of that same literal RCB family and the same DataSet; arbitrary same-DataSet
            // RCB substitution is forbidden.
            var matchedLiveCandidates = discovery.ReportInventory.ReportControls
                .Select(candidate => new
                {
                    Candidate = candidate,
                    Rank = Iec61850StaticRcbReferenceMatcher.MatchRank(configured.Reference, candidate.Reference)
                })
                .Where(item => item.Rank != int.MaxValue)
                .Where(item =>
                    string.IsNullOrWhiteSpace(item.Candidate.DataSetReference) ||
                    SameStaticReference(item.Candidate.DataSetReference, dataSetReference))
                .OrderBy(item => item.Rank)
                .ThenBy(item => item.Candidate.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            // An indexed family can expose several concrete RCBs. Literal instance order is
            // not an activation policy: Buffer01 may already be enabled/reserved while
            // Buffer02 is the usable instance. Reject only explicit occupancy; unknown state
            // remains eligible because some relays omit reservation metadata. Among equally
            // matched candidates prefer an explicitly disabled RCB, then stable literal order.
            var liveCandidates = matchedLiveCandidates
                .Where(item => !ArMms.MmsReportSubscriptionPlanner.IsExplicitlyEnabled(item.Candidate))
                .Where(item => !ArMms.MmsReportSubscriptionPlanner.IsReservedByOtherClient(item.Candidate))
                .OrderBy(item => item.Rank)
                .ThenByDescending(item => ArMms.MmsReportSubscriptionPlanner.IsExplicitlyDisabled(item.Candidate))
                .ThenBy(item => item.Candidate.Reference, StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (liveCandidates.Length == 0)
            {
                if (matchedLiveCandidates.Length > 0)
                {
                    var occupied = string.Join(
                        ", ",
                        matchedLiveCandidates.Take(8).Select(item =>
                            $"{item.Candidate.Reference}[RptEna={item.Candidate.EnabledState}, Resv={item.Candidate.ReservationState}, ResvTms={item.Candidate.ReservationTimeSeconds}, Owner={item.Candidate.Owner}]"));
                    warnings.Add(
                        $"{configured.Reference}: exact/indexed-family RCB objects were found, but every concrete instance was explicitly enabled or reserved ({occupied}). Static mode will not steal an occupied RCB and did not poll process values.");
                    continue;
                }

                var sameDataSet = discovery.ReportInventory.ReportControls
                    .Where(candidate =>
                        !string.IsNullOrWhiteSpace(candidate.DataSetReference) &&
                        SameStaticReference(candidate.DataSetReference, dataSetReference))
                    .Select(candidate => candidate.Reference)
                    .Where(reference => !string.IsNullOrWhiteSpace(reference))
                    .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase)
                    .Take(8)
                    .ToArray();
                var observed = sameDataSet.Length == 0
                    ? "none"
                    : string.Join(", ", sameDataSet);
                warnings.Add(
                    $"{configured.Reference}: configured RCB was proven by SCL but live MMS discovery exposed no exact/indexed-family instance. Same-DataSet live RCBs: {observed}. Static mode refused arbitrary substitution and did not poll process values.");
                continue;
            }

            var bestRank = liveCandidates[0].Rank;
            var bestCandidates = liveCandidates.Where(item => item.Rank == bestRank).ToArray();
            if (bestCandidates.Length > 1)
            {
                warnings.Add(
                    $"{configured.Reference}: configured indexed RCB family matched {bestCandidates.Length} non-occupied concrete live instance(s); selected {bestCandidates[0].Candidate.Reference} by explicit-disabled preference then literal instance order. No unrelated RCB was substituted.");
            }

            var liveSource = bestCandidates[0].Candidate;
            var liveRcb = CloneReportControlForPlanning(liveSource);

            if (!string.IsNullOrWhiteSpace(liveRcb.DataSetReference) &&
                !SameStaticReference(liveRcb.DataSetReference, dataSetReference))
            {
                warnings.Add(
                    $"{configured.Reference}: SCL binds {dataSetReference}, but live DatSet reports {liveRcb.DataSetReference}. Static mode refused the mismatch instead of guessing or polling.");
                continue;
            }

            // Missing live DatSet text is not treated as a reason to discard correct SCL
            // configuration. The exact DataSet directory below is still required and is the
            // ordered mapping authority for InformationReport values.
            if (string.IsNullOrWhiteSpace(liveRcb.DataSetReference))
                liveRcb.DataSetReference = dataSetReference;

            var directories = await RunMmsOperationAsync(
                () => _session.GetDataSetDirectoriesAsync(
                    new[] { dataSetReference },
                    discovery.IedDirectory,
                    cancellationToken),
                cancellationToken).ConfigureAwait(false);
            var directory = directories.SingleOrDefault(result =>
                result.IsSuccess && SameStaticReference(result.DataSetReference, dataSetReference));

            if (directory is null || directory.Members.Count == 0)
            {
                var detail = directories.FirstOrDefault()?.Message ?? "no directory response";
                warnings.Add(
                    $"{dataSetReference}: live DataSet directory could not prove an ordered non-empty member list ({detail}). RCB was not armed because report-index mapping would be unsafe; MMS process polling remains disabled.");
                continue;
            }

            var bindings = dataSetGroup
                .GroupBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (bindings.Count == 0)
                continue;

            var concreteReportReference = string.IsNullOrWhiteSpace(liveRcb.Reference)
                ? configured.Reference
                : liveRcb.Reference;
            var plan = new ReportControlPlan
            {
                RelayId = device.DeviceId,
                RelayName = device.Name,
                RelayIpAddress = device.IpAddress,
                IedName = device.Name,
                ReportControlReference = concreteReportReference,
                DataSetReference = dataSetReference,
                Mode = "Static DataSet • deterministic configured RCB",
                AllowDynamicDataSetWrites = false,
                Buffered = configured.Buffered,
                ReportId = liveRcb.ReportId,
                IntegrityPeriodMs = ParseStaticInteger(liveRcb.IntegrityPeriodMs),
                TriggerOptions = liveRcb.TriggerOptions,
                OptionalFields = liveRcb.OptionalFields,
                Status = "Deterministic static report planned",
                IsEngineAuthoritative = true,
                EngineAcquisitionKind = configured.Buffered ? "StaticBrcb" : "StaticUrcb",
                Bindings = bindings
            };

            var subscriptionWarnings = new List<string>();
            if (!Iec61850StaticRcbReferenceMatcher.IsExact(configured.Reference, concreteReportReference))
            {
                subscriptionWarnings.Add(
                    $"SCL ReportControl family {configured.Reference} resolved to concrete live indexed instance {concreteReportReference}.");
            }
            if (string.IsNullOrWhiteSpace(liveSource.DataSetReference))
            {
                subscriptionWarnings.Add(
                    "Live RCB DatSet text was not returned; exact SCL RCB->DataSet configuration plus the successfully read live DataSet directory are the deterministic authority.");
            }

            var subscription = new ArMms.MmsReportSubscriptionPlan
            {
                Mode = ArMms.MmsReportSubscriptionPlanMode.StaticDataSet,
                Status = ArMms.MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
                ReportControl = liveRcb,
                DataSetReference = dataSetReference,
                Members = directory.Members,
                DynamicPoints = Array.Empty<ArMms.MmsFcResolvedPoint>(),
                Steps = new[]
                {
                    $"Verify configured RCB {configured.Reference} as live object {concreteReportReference}.",
                    $"Use exact ordered live DataSet directory {dataSetReference} ({directory.Members.Count} members).",
                    "Install InformationReport receiver before enabling the RCB.",
                    "Write RptEna=true, then request GI=true.",
                    "Map report values by ordered DataSet member index; never substitute cyclic MMS process reads."
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
            Authority = "Deterministic Static DataSet configured-RCB path",
            Status = reportPlans.Count > 0 ? "StaticReportReady" : "StaticReportingUnavailable",
            Summary = reportPlans.Count > 0
                ? $"Deterministic Static DataSet path prepared {reportPlans.Count} configured RCB plan(s), covering {coveredPointKeys.Count}/{points.Count} selected point(s). Hybrid planning, dynamic DataSet writes and cyclic MMS process polling were bypassed."
                : $"No configured Static DataSet RCB could be armed safely for {points.Count} selected point(s). Hybrid planning and cyclic MMS process polling were not used.",
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
            () => _session.StartPersistentReportMonitorWithAttemptEvidenceAsync(
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
