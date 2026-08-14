using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Session-owned bridge from the ARIEC typed signal catalog and hybrid report acquisition
/// planner into ARSAS runtime plans. ARSAS performs no RCB capability inference here: it
/// preserves the exact engine subscription plan and only maps engine-classified signals
/// back to the already-selected application points.
/// </summary>
public sealed partial class NativeIec61850Client
{
    private sealed record AuthoritativeHybridSubscription(
        ArMms.MmsReportSubscriptionPlan Subscription,
        ArMms.MmsHybridAcquisitionKind Kind);

    private readonly Dictionary<string, AuthoritativeHybridSubscription> _authoritativeHybridSubscriptions =
        new(StringComparer.OrdinalIgnoreCase);

    internal bool CanUseHybridReportPlanner(Iec61850MonitorDevice device)
        => device?.LiveDiscoveryModel is not null;

    public async Task<NativeHybridReportPlanningResult> BuildHybridReportPlansAsync(
        Iec61850MonitorDevice device,
        IReadOnlyCollection<Iec61850MonitorPoint> points,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        ArgumentNullException.ThrowIfNull(points);
        cancellationToken.ThrowIfCancellationRequested();

        RemoveInactiveHybridSubscriptions();

        if (device.LiveDiscoveryModel is null)
        {
            return new NativeHybridReportPlanningResult
            {
                IsAuthoritative = false,
                Authority = "Legacy compatibility",
                Status = "Typed catalog unavailable",
                Summary = "ARIEC hybrid planning was not used because no engine live-discovery model is attached to this device.",
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
                PollingPointKeys = points.Select(point => point.PointKey).ToArray()
            };
        }

        var catalog = Iec61850SignalCatalogBuilder.Build(device.LiveDiscoveryModel);
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
                Authority = "ARIEC61850 typed signal catalog",
                Status = "No exact catalog mapping",
                Summary = "No selected point had one unambiguous literal match to an ARIEC catalog descriptor. ARSAS did not guess an IEC reference; bounded MMS polling remains active.",
                RequestedPointCount = points.Count,
                CatalogMappedPointCount = 0,
                PollingPointKeys = points.Select(point => point.PointKey).ToArray(),
                UnmappedPointKeys = points.Select(point => point.PointKey).ToArray(),
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
                UnmappedPointKeys = unmapped.Select(point => point.PointKey).ToArray()
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

        var enginePlan = ArMms.MmsHybridReportAcquisitionPlanner.Build(
            catalog,
            descriptorPoints.Keys,
            discovery.ReportInventory,
            availability,
            discovery.IedDirectory,
            new ArMms.MmsHybridReportAcquisitionOptions
            {
                AllowStaticBrcb = true,
                AllowStaticUrcb = true,
                AllowDynamicBrcb = device.AllowDynamicDataSetWrites,
                AllowDynamicUrcb = device.AllowDynamicDataSetWrites,
                AllowCallerOwnedReports = true,
                AllowPollingFallback = true,
                RequireExactAvailabilityEvidence = true
            });

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
                segment.ReportPlan,
                segment.Kind);
            reportPlans.Add(appPlan);
        }

        var pointByDescriptor = descriptorPoints.ToDictionary(pair => pair.Key, pair => pair.Value.PointKey);
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
            Authority = "ARIEC61850 MmsHybridReportAcquisitionPlanner",
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

    private bool TryGetAuthoritativeHybridSubscription(
        string planId,
        out ArMms.MmsReportSubscriptionPlan subscription,
        out ArMms.MmsHybridAcquisitionKind kind)
    {
        if (_authoritativeHybridSubscriptions.TryGetValue(planId, out var value))
        {
            subscription = value.Subscription;
            kind = value.Kind;
            return true;
        }

        subscription = null!;
        kind = default;
        return false;
    }

    private void RemoveInactiveHybridSubscriptions()
    {
        foreach (var planId in _authoritativeHybridSubscriptions.Keys.ToArray())
        {
            if (!_reportMonitorSessions.ContainsKey(planId))
                _authoritativeHybridSubscriptions.Remove(planId);
        }
    }

    private static Dictionary<string, Iec61850SignalDescriptor[]> BuildLiteralCatalogIndex(
        Iec61850SignalCatalogDocument catalog)
        => catalog.Signals
            .SelectMany(descriptor => EngineReferenceCandidates(descriptor)
                .Select(reference => new { Reference = reference, Descriptor = descriptor }))
            .GroupBy(item => item.Reference, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Select(item => item.Descriptor).Distinct().ToArray(),
                StringComparer.OrdinalIgnoreCase);

    private static bool TryResolveLiteralCatalogSignal(
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

    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();
}
