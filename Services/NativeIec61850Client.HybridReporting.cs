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
        var index = BuildLiteralCatalogIndex(catalog);
        var descriptorPoints = new Dictionary<Iec61850SignalDescriptor, Iec61850MonitorPoint>();
        var unmapped = new List<Iec61850MonitorPoint>();

        foreach (var point in points.OrderBy(point => point.PointKey, StringComparer.OrdinalIgnoreCase))
        {
            if (TryResolveLiteralCatalogSignal(index, point.IecReference, out var descriptor))
                descriptorPoints.TryAdd(descriptor, point);
            else
                unmapped.Add(point);
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

        var plannerOptions = new ArMms.MmsHybridReportAcquisitionOptions
        {
            AllowStaticBrcb = true,
            AllowStaticUrcb = true,
            AllowDynamicBrcb = device.AllowDynamicDataSetWrites,
            AllowDynamicUrcb = device.AllowDynamicDataSetWrites,
            // Existing caller-owned RCB reuse needs session aliasing semantics in ARSAS.
            // Until that is explicit, fail closed instead of starting a second monitor
            // against an RCB already owned by this association.
            AllowCallerOwnedReports = false,
            AllowPollingFallback = true,
            RequireExactAvailabilityEvidence = true
        };
        var enginePlan = ArMms.MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            descriptorPoints.Keys,
            discovery.ReportInventory,
            availability,
            discovery.IedDirectory,
            plannerOptions);

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

        var warnings = enginePlan.Warnings
            .Concat(enginePlan.Blockers)
            .Concat(availability.Warnings)
            .Concat(unmapped.Count == 0
                ? Array.Empty<string>()
                : [$"{unmapped.Count} selected point(s) had no unique literal ARIEC catalog match and remain on bounded MMS polling. No absence conclusion was made."])
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            Authority = $"ARIEC61850 MmsHybridReportAcquisitionPlanner ({catalogAuthority})",
            Status = enginePlan.Status.ToString(),
            Summary = enginePlan.Summary,
            ReportPlans = reportPlans,
            PollingPointKeys = polling,
            UncoveredPointKeys = uncovered,
            UnmappedPointKeys = unmapped.Select(point => point.PointKey).ToArray(),
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
                Message = "Hybrid report start rejected: the plan is not marked ARIEC-authoritative. No local re-planning was performed."
            };
        }

        if (!_session.IsMmsInitiated)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC hybrid report monitor requires an initiated MMS association. Current state: {_session.State}."
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
                Message = "ARIEC-authoritative subscription evidence is no longer present for this plan. ARSAS refused to rebuild the RCB/DataSet plan locally; MMS polling remains the safe fallback."
            };
        }

        var discovery = await EnsureDiscoveryForReportingAsync(cancellationToken).ConfigureAwait(false);
        if (discovery is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = LastErrorMessage
            };
        }

        // Planning is intentionally an intent, not permission to write forever.
        // Re-read the exact selected RCB immediately before execution, then ask the same
        // ARIEC planner to classify that fresh evidence again. This is especially important
        // for SCL fast-connect, where the typed catalog may be design-sourced but execution
        // authority must always be live-sourced.
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
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC execution revalidation withheld {authoritative.Kind}: expected one fresh availability snapshot for {authoritative.ReportControlReference}, found {selectedSnapshots.Length}. No RCB/DataSet write was attempted; MMS polling remains active.",
                Warnings = freshAvailability.Warnings
            };
        }

        var selectedAvailability = new ArMms.MmsRcbAvailabilityResult
        {
            CheckedAtUtc = freshAvailability.CheckedAtUtc,
            ReportControls = selectedSnapshots,
            Warnings = freshAvailability.Warnings
        };
        var revalidatedPlan = ArMms.MmsHybridReportAcquisitionPlanner.Build(
            authoritative.Catalog,
            authoritative.Signals,
            discovery.ReportInventory,
            selectedAvailability,
            discovery.IedDirectory,
            authoritative.Options);
        var revalidatedSegment = revalidatedPlan.Segments.FirstOrDefault(segment =>
            segment.IsReportBacked &&
            segment.ReportPlan is not null &&
            segment.Kind == authoritative.Kind &&
            SameLiteralReference(segment.ReportControlReference, authoritative.ReportControlReference));
        if (revalidatedSegment?.ReportPlan is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC execution revalidation withheld {authoritative.Kind} on {authoritative.ReportControlReference}: fresh engine evidence no longer reproduces the planned report segment. No RCB/DataSet write was attempted; MMS polling remains active.",
                Warnings = freshAvailability.Warnings
                    .Concat(revalidatedPlan.Warnings)
                    .Concat(revalidatedPlan.Blockers)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray()
            };
        }

        var subscription = revalidatedSegment.ReportPlan;
        if (!subscription.IsReady)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC authoritative subscription is not ready: {subscription.Summary}",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                Warnings = subscription.Warnings.Concat(subscription.Blockers).ToArray()
            };
        }

        var isDynamic = authoritative.Kind is ArMms.MmsHybridAcquisitionKind.DynamicBrcb or ArMms.MmsHybridAcquisitionKind.DynamicUrcb;
        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);
        var start = await RunMmsOperationAsync(
            () => _session.StartPersistentReportMonitorAsync(
                subscription,
                triggerGeneralInterrogation: true,
                deleteDynamicDataSetOnStop: isDynamic,
                discovery.IedDirectory,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        if (!start.IsSuccess || start.Session is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"ARIEC hybrid report activation failed for {plan.DisplayReference}: {start.Message}",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = isDynamic,
                CoveredReferences = coveredReferences,
                Warnings = start.Warnings.Concat(subscription.Warnings).ToArray()
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
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            AcquisitionLabel = $"ARIEC Hybrid: {authoritative.Kind}",
            CoveredReferences = coveredReferences,
            Warnings = start.Warnings.Concat(subscription.Warnings).ToArray()
        };
    }

    internal static Dictionary<string, Iec61850SignalDescriptor[]> BuildLiteralCatalogIndex(
        Iec61850SignalCatalogDocument catalog)
        => catalog.Signals
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
        return primary.Length > 0 ? primary : matches.ToArray();
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

    private static bool SameLiteralReference(string? left, string? right)
        => string.Equals(LiteralReference(left), LiteralReference(right), StringComparison.OrdinalIgnoreCase);

    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();
}
