using System.Collections.Concurrent;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Session-owned bridge from the ARIEC typed signal catalog and hybrid acquisition planner
/// into ARSAS runtime plans. ARSAS preserves the exact engine subscription plan and does
/// not recreate RCB capability, DataSet semantics, or vendor aliases locally.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private sealed record AuthoritativeHybridSubscription(
        ArMms.MmsHybridAcquisitionKind Kind,
        string ReportControlReference,
        Iec61850SignalCatalogDocument Catalog,
        IReadOnlyList<Iec61850SignalDescriptor> Signals,
        ArMms.MmsHybridReportAcquisitionOptions Options);

    private readonly Dictionary<string, AuthoritativeHybridSubscription> _authoritativeHybridSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    // P6 field-stability circuit breaker. Some IEDs advertise Define/DeleteNamedVariableList
    // but abort the MMS association when a dynamic DataSet write is attempted. One real
    // failed dynamic activation is stronger evidence than the advertised service bit for
    // the lifetime of this application process. Static reporting remains eligible; only
    // further dynamic writes for the same device are suppressed to stop reconnect/write loops.
    private static readonly ConcurrentDictionary<string, string> DynamicWriteCircuitByDevice =
        new(StringComparer.OrdinalIgnoreCase);

    private static LiveIedModelDiscoveryDocument? ResolveHybridPlanningModel(Iec61850MonitorDevice? device)
        => device?.LiveDiscoveryModel ?? device?.SclWorkspace?.DesignModel;

    internal bool CanUseHybridReportPlanner(Iec61850MonitorDevice device)
        => ResolveHybridPlanningModel(device) is not null;

    public async Task<NativeHybridReportPlanningResult> BuildHybridReportPlansAsync(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<Iec61850MonitorPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();

        _authoritativeHybridSubscriptions.Clear();

        var planningModel = ResolveHybridPlanningModel(device);
        if (planningModel is null)
        {
            return new NativeHybridReportPlanningResult
            {
                IsAuthoritative = false,
                Authority = "Legacy compatibility",
                Status = "Typed catalog unavailable",
                Summary = "ARIEC hybrid planning was not used because neither a live-discovery model nor an opened SCL design model is attached to this device.",
                RequestedPointCount = points.Count
            };
        }

        if (!_session.IsMmsInitiated)
        {
            return new NativeHybridReportPlanningResult
            {
                IsAuthoritative = true,
                Authority = "ARIEC61850 hybrid acquisition",
                Status = "Transport unavailable",
                Summary = $"ARIEC hybrid planning was withheld because the MMS association is not initiated ({_session.State}). All points remain on polling/reconnect safety handling; no missing conclusion was made.",
                RequestedPointCount = points.Count,
                PollingPointKeys = points.Select(point => point.PointKey).ToArray(),
                PointAttemptEvidence = points.Select(point => SkippedAttemptEvidence(
                    point,
                    "TransportUnavailable",
                    "No MMS association exists, so dynamic report configuration cannot be attempted. Polling/reconnect handling is safety-only until association is restored.")).ToArray(),
                PollingFallbackSignalCount = points.Count
            };
        }

        // A fast SCL connect deliberately skips full live-model discovery. The opened SCL
        // design model is still an ARIEC-typed model and is therefore valid as the catalog
        // authority for literal signal mapping. It is NOT treated as live RCB evidence:
        // fresh report discovery and availability checks below remain mandatory.
        var catalog = Iec61850SignalCatalogBuilder.Build(planningModel);
        var catalogAuthority = device.LiveDiscoveryModel is not null
            ? "live-discovery model"
            : "opened SCL design model";

        // P6: static DataSet membership is protocol evidence and must win before the broad
        // signal catalog. The member-centric projection preserves the FCDA/FCD identity and
        // binds it to its resolved primary value leaf. This closes the field regression in
        // which diagnostics correctly showed 6/6 static members represented while those same
        // six selected points were excluded as CatalogMappingUnavailable before static RCB
        // coverage could even be evaluated.
        var mandatoryStaticSignals = Iec61850DataSetSignalInventoryProjection
            .GetMandatorySignals(planningModel)
            .Where(signal => signal.IsStaticDataSetMandatory)
            .Where(signal => signal.DataSetMemberships.Any(membership => membership.IsPrimaryValueForMember))
            .ToArray();
        var staticIndex = BuildLiteralCatalogIndex(mandatoryStaticSignals);
        var index = BuildLiteralCatalogIndex(catalog);
        var descriptorPoints = new Dictionary<Iec61850SignalDescriptor, Iec61850MonitorPoint>();
        var unmapped = new List<Iec61850MonitorPoint>();
        var staticInventoryMappedCount = 0;

        foreach (var point in points.OrderBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase))
        {
            if (TryResolveLiteralCatalogSignal(staticIndex, point.IecReference, out var staticDescriptor))
            {
                descriptorPoints.TryAdd(staticDescriptor, point);
                staticInventoryMappedCount++;
            }
            else if (TryResolveLiteralCatalogSignal(index, point.IecReference, out var descriptor))
            {
                descriptorPoints.TryAdd(descriptor, point);
            }
            else
            {
                unmapped.Add(point);
            }
        }

        if (descriptorPoints.Count == 0)
        {
            return new NativeHybridReportPlanningResult
            {
                IsAuthoritative = true,
                Authority = $"ARIEC61850 typed signal catalog ({catalogAuthority})",
                Status = "No exact catalog mapping",
                Summary = "No selected point had one unambiguous literal match to an ARIEC catalog descriptor. ARSAS did not guess an IEC reference; bounded MMS polling remains active.",
                RequestedPointCount = points.Count,
                CatalogMappedPointCount = 0,
                PollingPointKeys = points.Select(point => point.PointKey).ToArray(),
                UnmappedPointKeys = points.Select(point => point.PointKey).ToArray(),
                PointAttemptEvidence = points.Select(UnmappedAttemptEvidence).ToArray(),
                PollingFallbackSignalCount = points.Count,
                Warnings = ["Hybrid report planning was conservatively skipped for unmapped points; this is not signal-absence evidence."]
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery is null)
        {
            return new NativeHybridReportPlanningResult
            {
                IsAuthoritative = true,
                Authority = "ARIEC61850 hybrid acquisition",
                Status = "Fresh report discovery unavailable",
                Summary = string.IsNullOrWhiteSpace(LastErrorMessage)
                    ? "Fresh ARIEC report discovery was unavailable. Points remain on MMS polling fallback."
                    : LastErrorMessage,
                RequestedPointCount = points.Count,
                CatalogMappedPointCount = descriptorPoints.Count,
                PollingPointKeys = points.Select(point => point.PointKey).ToArray(),
                UnmappedPointKeys = unmapped.Select(point => point.PointKey).ToArray(),
                PointAttemptEvidence = points.Select(point => SkippedAttemptEvidence(
                    point,
                    "FreshReportDiscoveryUnavailable",
                    "Fresh report inventory/availability evidence was unavailable, so no safe dynamic write was attempted.")).ToArray(),
                PollingFallbackSignalCount = points.Count
            };
        }

        var callerOwned = _reportMonitorSessions.Values
            .Select(session => session.ReportControl.Reference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var availability = await RunMmsOperationAsync(
            () => _session.CheckReportControlAvailabilityAsync(
                discovery.ReportInventory,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions
                {
                    MaxReportControls = 512,
                    ReadDataSetDirectories = true,
                    CallerOwnedRcbReferences = callerOwned
                },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var dynamicWriteCircuitOpen = DynamicWriteCircuitByDevice.TryGetValue(device.DeviceId, out var dynamicCircuitReason);
        var allowDynamicWrites = device.AllowDynamicDataSetWrites && !dynamicWriteCircuitOpen;
        var plannerOptions = new ArMms.MmsHybridReportAcquisitionOptions
        {
            AllowStaticBrcb = true,
            AllowStaticUrcb = true,
            AllowDynamicBrcb = allowDynamicWrites,
            AllowDynamicUrcb = allowDynamicWrites,
            // Existing caller-owned RCB reuse needs session aliasing semantics in ARSAS.
            // Until that is explicit, fail closed instead of starting a second monitor
            // against an RCB already owned by this association.
            AllowCallerOwnedReports = false,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = true
        };

        // P3: the protocol engine owns capability interpretation. ARSAS supplies the
        // current association evidence and consumes the resulting acquisition plan; it
        // does not recreate MMS service-bit, RCB ownership, or writability policy locally.
        var capabilityAwarePlan = ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            catalog,
            descriptorPoints.Keys,
            discovery.ReportInventory,
            availability,
            discovery.IedDirectory,
            _session.LastNegotiatedCapabilities,
            plannerOptions);
        var enginePlan = capabilityAwarePlan.AcquisitionPlan;
        var associationCapability = capabilityAwarePlan.AssociationCapability;
        var p4AttemptEvidence = ArMms.MmsHybridDynamicAttemptEvidenceBuilder.Build(capabilityAwarePlan, plannerOptions);

        var reportPlans = new List<ReportControlPlan>();
        foreach (var segment in enginePlan.Segments.Where(segment => segment.IsReportBacked))
        {
            if (segment.ReportPlan is null)
                continue;

            var bindings = segment.Signals
                .Where(descriptorPoints.ContainsKey)
                .Select(signal => descriptorPoints[signal])
                .GroupBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(point => point.IecReference, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (bindings.Count == 0)
                continue;

            var appPlan = new ReportControlPlan
            {
                RelayId = device.DeviceId,
                RelayName = device.Name,
                RelayIpAddress = device.IpAddress,
                IedName = device.Name,
                ReportControlReference = segment.ReportControlReference,
                DataSetReference = segment.DataSetReference,
                Mode = $"ARIEC Hybrid • {segment.Kind}",
                AllowDynamicDataSetWrites = segment.Kind is ArMms.MmsHybridAcquisitionKind.DynamicBrcb or ArMms.MmsHybridAcquisitionKind.DynamicUrcb,
                Buffered = segment.Kind is ArMms.MmsHybridAcquisitionKind.StaticBrcb or ArMms.MmsHybridAcquisitionKind.DynamicBrcb,
                Status = $"{segment.Kind} planned",
                IsEngineAuthoritative = true,
                EngineAcquisitionKind = segment.Kind.ToString(),
                Bindings = bindings
            };

            _authoritativeHybridSubscriptions[appPlan.PlanId] = new AuthoritativeHybridSubscription(
                segment.Kind,
                segment.ReportControlReference,
                catalog,
                segment.Signals.ToArray(),
                plannerOptions);
            reportPlans.Add(appPlan);
        }

        var activationPlans = OrderHybridActivationPlans(reportPlans);

        var polling = enginePlan.Assignments
            .Where(assignment => assignment.Kind == ArMms.MmsHybridAcquisitionKind.MmsPollingFallback)
            .Select(assignment => FindPointKeyForAssignment(assignment.SignalReference, descriptorPoints))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Concat(unmapped.Select(point => point.PointKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var uncovered = enginePlan.Assignments
            .Where(assignment => assignment.Kind == ArMms.MmsHybridAcquisitionKind.Uncovered)
            .Select(assignment => FindPointKeyForAssignment(assignment.SignalReference, descriptorPoints))
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var pointAttemptEvidence = p4AttemptEvidence
            .Select(evidence => ProjectAttemptEvidence(evidence, descriptorPoints))
            .Where(evidence => !string.IsNullOrWhiteSpace(evidence.PointKey))
            .Concat(unmapped.Select(UnmappedAttemptEvidence))
            .GroupBy(evidence => evidence.PointKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();

        var p6Warnings = new List<string>();
        if (staticInventoryMappedCount > 0)
        {
            p6Warnings.Add(
                $"P6 static inventory bridge mapped {staticInventoryMappedCount} selected point(s) through ARIEC mandatory DataSet member evidence before broad catalog matching.");
        }
        if (activationPlans.Count > 1 && activationPlans[0].AllowDynamicDataSetWrites && activationPlans.Any(plan => !plan.AllowDynamicDataSetWrites))
        {
            p6Warnings.Add(
                "P6.1 mixed-plan safety: dynamic DataSet mutation is probationed before static RCB activation so a relay-aborting dynamic write cannot orphan an already-armed static RCB. Static coverage priority is unchanged.");
        }
        if (dynamicWriteCircuitOpen)
        {
            p6Warnings.Add(
                $"Dynamic DataSet writes are suppressed for this device after a previous real activation failure ({dynamicCircuitReason}). Static reporting remains eligible; residual points stay on bounded MMS polling instead of repeating a destabilizing write.");
        }

        var warnings = capabilityAwarePlan.Warnings
            .Concat(capabilityAwarePlan.Blockers)
            .Concat(availability.Warnings)
            .Concat(p6Warnings)
            .Concat(unmapped.Count == 0
                ? Array.Empty<string>()
                : [$"{unmapped.Count} selected point(s) had no unique literal ARIEC catalog match and remain on bounded MMS polling. No absence conclusion was made."])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = $"ARIEC61850 capability-aware hybrid acquisition ({catalogAuthority})",
            Status = enginePlan.Status.ToString(),
            Summary = $"{enginePlan.Summary} {associationCapability.Summary}" +
                      (staticInventoryMappedCount > 0 ? $" Static inventory bridge={staticInventoryMappedCount}." : string.Empty) +
                      (dynamicWriteCircuitOpen ? " Dynamic writes circuit-broken after field failure evidence." : string.Empty),
            ReportPlans = activationPlans,
            PollingPointKeys = polling,
            UncoveredPointKeys = uncovered,
            UnmappedPointKeys = unmapped.Select(point => point.PointKey).ToArray(),
            PointAttemptEvidence = pointAttemptEvidence,
            Warnings = warnings,
            RequestedPointCount = points.Count,
            CatalogMappedPointCount = descriptorPoints.Count,
            StaticBrcbSignalCount = enginePlan.StaticBrcbSignalCount,
            StaticUrcbSignalCount = enginePlan.StaticUrcbSignalCount,
            DynamicBrcbSignalCount = enginePlan.DynamicBrcbSignalCount,
            DynamicUrcbSignalCount = enginePlan.DynamicUrcbSignalCount,
            PollingFallbackSignalCount = enginePlan.PollingFallbackSignalCount + unmapped.Count,
            UncoveredSignalCount = enginePlan.UncoveredSignalCount
        };
    }

    internal static IReadOnlyList<ReportControlPlan> OrderHybridActivationPlans(IEnumerable<ReportControlPlan> plans)
    {
        var materialized = plans.ToArray();
        var hasDynamic = materialized.Any(plan => plan.IsEngineAuthoritative && plan.AllowDynamicDataSetWrites);
        var hasStatic = materialized.Any(plan => plan.IsEngineAuthoritative && !plan.AllowDynamicDataSetWrites);
        if (!hasDynamic || !hasStatic)
            return materialized;

        // Coverage precedence remains static -> dynamic -> polling. This ordering controls
        // only activation side effects. A dynamic DefineNamedVariableList write is the risky
        // operation on IEDs that abort the association. Run that probation before arming the
        // already-proven static RCB so a failed dynamic attempt cannot leave that static RCB
        // transiently enabled/owned and then unavailable to the immediate reconnect.
        return materialized
            .OrderByDescending(plan => plan.IsEngineAuthoritative && plan.AllowDynamicDataSetWrites)
            .ToArray();
    }

    public async Task<NativeReportMonitorStartResult> StartHybridReportMonitorAsync(
        ReportControlPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!plan.IsEngineAuthoritative)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = "Hybrid report start rejected: the plan is not marked ARIEC-authoritative. No local re-planning was performed.",
                DynamicAttemptState = "Skipped",
                PollingFallbackReason = "PlanNotEngineAuthoritative"
            };
        }

        if (!_session.IsMmsInitiated)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC hybrid report monitor requires an initiated MMS association. Current state: {_session.State}.",
                DynamicAttemptState = "Skipped",
                PollingFallbackReason = "TransportUnavailable"
            };
        }

        if (_reportMonitorSessions.ContainsKey(plan.PlanId))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = true,
                PlanId = plan.PlanId,
                Message = $"ARIEC hybrid report monitor already active for {plan.DisplayReference}.",
                ReportControlReference = plan.ReportControlReference,
                DataSetReference = plan.DataSetReference,
                AcquisitionLabel = $"ARIEC Hybrid: {plan.EngineAcquisitionKind}",
                CoveredReferences = _reportMonitorCoverage.TryGetValue(plan.PlanId, out var existingCoverage)
                    ? existingCoverage
                    : Array.Empty<string>()
            };
        }

        if (!_authoritativeHybridSubscriptions.TryGetValue(plan.PlanId, out var authoritative))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = "ARIEC-authoritative subscription evidence is no longer present for this plan. ARSAS refused to rebuild the RCB/DataSet plan locally; MMS polling remains the safe fallback.",
                DynamicAttemptState = "Skipped",
                PollingFallbackReason = "AuthoritativeSubscriptionEvidenceMissing"
            };
        }

        var isAuthoritativeDynamic = authoritative.Kind is
            ArMms.MmsHybridAcquisitionKind.DynamicBrcb or
            ArMms.MmsHybridAcquisitionKind.DynamicUrcb;
        if (isAuthoritativeDynamic &&
            !string.IsNullOrWhiteSpace(plan.RelayId) &&
            DynamicWriteCircuitByDevice.TryGetValue(plan.RelayId, out var circuitReason))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"P6 dynamic-write circuit breaker withheld {authoritative.Kind} after a previous real activation failure ({circuitReason}). No further dynamic DataSet/RCB write is allowed for this device in this application run; MMS polling remains the bounded fallback.",
                UsedDynamicDataSet = true,
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "DynamicWriteCircuitOpen",
                PollingFallbackReason = "DynamicWriteCircuitOpen"
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage,
                DynamicAttemptState = "Skipped",
                PollingFallbackReason = "FreshReportDiscoveryUnavailable"
            };
        }

        // Planning is intentionally an intent, not permission to write forever.
        // Re-read the exact selected RCB immediately before execution, then ask the same
        // ARIEC capability-aware planner to classify that fresh association evidence again.
        // This is especially important for SCL fast-connect, where the typed catalog may be
        // design-sourced but execution authority must always be live-sourced.
        var callerOwned = _reportMonitorSessions.Values
            .Select(session => session.ReportControl.Reference)
            .Where(reference => !string.IsNullOrWhiteSpace(reference))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var freshAvailability = await RunMmsOperationAsync(
            () => _session.CheckReportControlAvailabilityAsync(
                discovery.ReportInventory,
                discovery.IedDirectory,
                new ArMms.MmsRcbAvailabilityOptions
                {
                    MaxReportControls = 512,
                    ReadDataSetDirectories = true,
                    CallerOwnedRcbReferences = callerOwned
                },
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var selectedSnapshots = freshAvailability.ReportControls
            .Where(snapshot => SameLiteralReference(snapshot.Reference, authoritative.ReportControlReference))
            .ToArray();
        if (selectedSnapshots.Length != 1)
        {
            var message = $"ARIEC execution revalidation withheld {authoritative.Kind}: expected one fresh availability snapshot for {authoritative.ReportControlReference}, found {selectedSnapshots.Length}.";
            if (IsStaticHybridKind(authoritative.Kind))
                return await TryStartDynamicRecoveryAfterStaticFailureP4Async(plan, authoritative, discovery, freshAvailability, message, cancellationToken).ConfigureAwait(false);

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"{message} No RCB/DataSet write was attempted; MMS polling remains active.",
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "FreshRcbRevalidationFailed",
                PollingFallbackReason = "FreshRcbEvidenceUnavailable",
                Warnings = freshAvailability.Warnings
            };
        }

        var selectedAvailability = new ArMms.MmsRcbAvailabilityResult
        {
            CheckedAtUtc = freshAvailability.CheckedAtUtc,
            ReportControls = selectedSnapshots,
            Warnings = freshAvailability.Warnings
        };
        var revalidatedCapabilityAwarePlan = ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
            authoritative.Catalog,
            authoritative.Signals,
            discovery.ReportInventory,
            selectedAvailability,
            discovery.IedDirectory,
            _session.LastNegotiatedCapabilities,
            authoritative.Options);
        var revalidatedPlan = revalidatedCapabilityAwarePlan.AcquisitionPlan;
        var revalidatedSegment = revalidatedPlan.Segments.FirstOrDefault(segment =>
            segment.IsReportBacked &&
            segment.ReportPlan is not null &&
            segment.Kind == authoritative.Kind &&
            SameLiteralReference(segment.ReportControlReference, authoritative.ReportControlReference));
        if (revalidatedSegment?.ReportPlan is null)
        {
            var message = $"ARIEC execution revalidation withheld {authoritative.Kind} on {authoritative.ReportControlReference}: fresh capability-aware engine evidence no longer reproduces the planned report segment.";
            if (IsStaticHybridKind(authoritative.Kind))
                return await TryStartDynamicRecoveryAfterStaticFailureP4Async(plan, authoritative, discovery, freshAvailability, message, cancellationToken).ConfigureAwait(false);

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"{message} No RCB/DataSet write was attempted; MMS polling remains active.",
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "FreshCapabilityRevalidationFailed",
                PollingFallbackReason = "DynamicRevalidationWithheld",
                Warnings = freshAvailability.Warnings
                    .Concat(revalidatedCapabilityAwarePlan.Warnings)
                    .Concat(revalidatedCapabilityAwarePlan.Blockers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        var subscription = revalidatedSegment.ReportPlan;
        if (!subscription.IsReady)
        {
            var message = $"ARIEC authoritative subscription is not ready: {subscription.Summary}";
            if (IsStaticHybridKind(authoritative.Kind))
                return await TryStartDynamicRecoveryAfterStaticFailureP4Async(plan, authoritative, discovery, freshAvailability, message, cancellationToken).ConfigureAwait(false);

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = message,
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                DynamicAttempted = false,
                DynamicAttemptState = "Skipped",
                FailureReason = "SubscriptionNotReady",
                PollingFallbackReason = "DynamicSubscriptionNotReady",
                Warnings = subscription.Warnings.Concat(subscription.Blockers).ToArray()
            };
        }

        var isDynamic = authoritative.Kind is ArMms.MmsHybridAcquisitionKind.DynamicBrcb or ArMms.MmsHybridAcquisitionKind.DynamicUrcb;
        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);
        var attempt = await RunMmsOperationAsync(
            () => _session.StartPersistentReportMonitorWithAttemptEvidenceAsync(
                subscription,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: isDynamic,
                discovery.IedDirectory,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);
        var start = attempt.StartResult;
        var attemptWarnings = start.Warnings
            .Concat(subscription.Warnings)
            .Concat(attempt.CleanupWarnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!attempt.IsSuccess || start.Session is null)
        {
            var message = $"ARIEC hybrid report activation failed for {plan.DisplayReference}: {start.Message}";
            if (!isDynamic)
                return await TryStartDynamicRecoveryAfterStaticFailureP4Async(
                    plan,
                    authoritative,
                    discovery,
                    freshAvailability,
                    message,
                    cancellationToken,
                    staticCleanupProven: attempt.CleanupSucceeded).ConfigureAwait(false);

            if (attempt.DynamicAttempted && !string.IsNullOrWhiteSpace(plan.RelayId))
            {
                var reason = attempt.FailureReason.ToString();
                if (string.IsNullOrWhiteSpace(reason) || reason.Equals("None", StringComparison.OrdinalIgnoreCase))
                    reason = "DynamicActivationFailed";
                DynamicWriteCircuitByDevice[plan.RelayId] = reason;
            }

            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = message,
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = true,
                DynamicAttempted = attempt.DynamicAttempted,
                DynamicAttemptState = attempt.DynamicAttemptState.ToString(),
                FailureReason = attempt.FailureReason.ToString(),
                PollingFallbackReason = attempt.DynamicAttempted ? "DynamicActivationFailed" : "DynamicActivationNotAttempted",
                CleanupAttempted = attempt.CleanupAttempted,
                CleanupSucceeded = attempt.CleanupSucceeded,
                CoveredReferences = coveredReferences,
                Warnings = attemptWarnings
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
            Message = $"ARIEC authoritative {authoritative.Kind} monitor active. {start.Message}",
            SubscriptionSummary = subscription.Summary,
            MemberCount = subscription.Members.Count,
            WriteStepCount = start.WriteSteps.Count,
            UsedDynamicDataSet = isDynamic,
            DynamicAttempted = attempt.DynamicAttempted,
            DynamicAttemptState = attempt.DynamicAttemptState.ToString(),
            FailureReason = string.Empty,
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            AcquisitionLabel = $"ARIEC Hybrid: {authoritative.Kind}",
            CoveredReferences = coveredReferences,
            Warnings = attemptWarnings
        };
    }

    internal static Dictionary<string, Iec61850SignalDescriptor[]> BuildLiteralCatalogIndex(
        Iec61850SignalCatalogDocument catalog)
        => BuildLiteralCatalogIndex(catalog.Signals);

    internal static Dictionary<string, Iec61850SignalDescriptor[]> BuildLiteralCatalogIndex(
        IEnumerable<Iec61850SignalDescriptor> descriptors)
        => descriptors
            .SelectMany(descriptor => EngineReferenceCandidates(descriptor)
                .Select(reference => new { Reference = reference, Descriptor = descriptor }))
            .GroupBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => PreferLiteralMatches(
                    group.Key,
                    group.Select(item => item.Descriptor).Distinct().ToArray()),
                StringComparer.OrdinalIgnoreCase);

    internal static bool TryResolveLiteralCatalogSignal(
        IReadOnlyDictionary<string, Iec61850SignalDescriptor[]> index,
        string? pointReference,
        out Iec61850SignalDescriptor descriptor)
    {
        var key = LiteralReference(pointReference);
        if (key.Length > 0 && index.TryGetValue(key, out var matches) && matches.Length == 1)
        {
            descriptor = matches[0];
            return true;
        }

        descriptor = null!;
        return false;
    }

    private static Iec61850SignalDescriptor[] PreferLiteralMatches(
        string reference,
        IReadOnlyCollection<Iec61850SignalDescriptor> matches)
    {
        if (matches.Count <= 1)
            return matches.ToArray();

        // Quality/timestamp descriptors deliberately carry their owning process value in
        // PrimaryValueReference. That relationship must not make the process value look
        // ambiguous. Prefer the descriptor whose own literal reference matches first.
        var direct = matches
            .Where(descriptor => DirectReferenceCandidates(descriptor)
                .Contains(reference, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (direct.Length > 0)
            return direct;

        var primary = matches
            .Where(descriptor => descriptor.SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue)
            .Where(descriptor => PrimaryReferenceCandidates(descriptor)
                .Contains(reference, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (primary.Length > 0)
            return primary;

        // ARIEC's DataSet semantic binding is exact model evidence, not a heuristic alias.
        // A parent FCDA/member reference may legitimately resolve to stVal together with q/t.
        // Prefer the descriptor that ARIEC marked as the primary value for that exact member;
        // if more than one primary survives, keep the mapping ambiguous and fail closed.
        var dataSetPrimary = matches
            .Where(descriptor => descriptor.SemanticRole == Iec61850DataAttributeSemanticRole.PrimaryValue)
            .Where(descriptor => descriptor.DataSetMemberships.Any(membership =>
                membership.IsPrimaryValueForMember &&
                DataSetMemberReferenceCandidates(membership)
                    .Contains(reference, StringComparer.OrdinalIgnoreCase)))
            .ToArray();
        return dataSetPrimary.Length > 0 ? dataSetPrimary : matches.ToArray();
    }

    private static string FindPointKeyForAssignment(
        string? signalReference,
        IReadOnlyDictionary<Iec61850SignalDescriptor, Iec61850MonitorPoint> descriptorPoints)
    {
        var key = LiteralReference(signalReference);
        if (key.Length == 0)
            return string.Empty;

        var matches = descriptorPoints
            .Where(pair => EngineReferenceCandidates(pair.Key).Contains(key, StringComparer.OrdinalIgnoreCase))
            .Select(pair => pair.Value.PointKey)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return matches.Length == 1 ? matches[0] : string.Empty;
    }

    private static IEnumerable<string> EngineReferenceCandidates(Iec61850SignalDescriptor descriptor)
    {
        var values = new[]
        {
            descriptor.PrimaryValueReference,
            descriptor.DesignReference,
            descriptor.ObservedReference,
            descriptor.PrimaryValueMmsReference,
            descriptor.CanonicalMmsReference,
            descriptor.EffectiveMmsReference,
            descriptor.ObservedMmsReference
        };

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(LiteralReference)
            .Concat(descriptor.DataSetMemberships
                .Where(membership => membership.IsPrimaryValueForMember)
                .SelectMany(DataSetMemberReferenceCandidates))
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> DirectReferenceCandidates(Iec61850SignalDescriptor descriptor)
    {
        var values = new[]
        {
            descriptor.DesignReference,
            descriptor.ObservedReference,
            descriptor.CanonicalMmsReference,
            descriptor.EffectiveMmsReference,
            descriptor.ObservedMmsReference
        };

        return values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(LiteralReference)
            .Distinct(StringComparer.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> PrimaryReferenceCandidates(Iec61850SignalDescriptor descriptor)
        => new[] { descriptor.PrimaryValueReference, descriptor.PrimaryValueMmsReference }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(LiteralReference)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> DataSetMemberReferenceCandidates(Iec61850SignalDataSetMembership membership)
        => new[] { membership.CanonicalMemberReference, membership.OriginalMemberReference }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(LiteralReference)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool SameLiteralReference(string? left, string? right)
        => string.Equals(LiteralReference(left), LiteralReference(right), StringComparison.OrdinalIgnoreCase);

    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();
}
