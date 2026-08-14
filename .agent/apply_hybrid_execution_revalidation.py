from pathlib import Path

path = Path("Services/NativeIec61850Client.HybridReporting.cs")
text = path.read_text(encoding="utf-8")


def replace_once(old: str, new: str, label: str) -> None:
    global text
    count = text.count(old)
    if count != 1:
        raise SystemExit(f"{label}: expected exactly one source match, found {count}")
    text = text.replace(old, new, 1)

replace_once(
    """    private sealed record AuthoritativeHybridSubscription(
        ArMms.MmsReportSubscriptionPlan Subscription,
        ArMms.MmsHybridAcquisitionKind Kind);""",
    """    private sealed record AuthoritativeHybridSubscription(
        ArMms.MmsHybridAcquisitionKind Kind,
        string ReportControlReference,
        Iec61850SignalCatalogDocument Catalog,
        IReadOnlyList<Iec61850SignalDescriptor> Signals,
        ArMms.MmsHybridReportAcquisitionOptions Options);""",
    "store-revalidation-authority")

replace_once(
    """        var enginePlan = ArMms.MmsHybridReportAcquisitionPlanner.Build(
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
            });""",
    """        var plannerOptions = new ArMms.MmsHybridReportAcquisitionOptions
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
            plannerOptions);""",
    "planner-options-are-reusable-for-revalidation")

replace_once(
    """            _authoritativeHybridSubscriptions[appPlan.PlanId] = new AuthoritativeHybridSubscription(
                segment.ReportPlan,
                segment.Kind);""",
    """            _authoritativeHybridSubscriptions[appPlan.PlanId] = new AuthoritativeHybridSubscription(
                segment.Kind,
                segment.ReportControlReference,
                catalog,
                segment.Signals.ToArray(),
                plannerOptions);""",
    "store-segment-revalidation-inputs")

replace_once(
    """        var subscription = authoritative.Subscription;
        if (!subscription.IsReady)""",
    """        // P2.2 planning is intentionally an intent, not permission to write forever.
        // Re-read the exact selected RCB immediately before execution, then ask the same
        // ARIEC planner to classify that fresh evidence again. ARSAS never reimplements
        // RptEna/reservation/DataSet safety semantics here.
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
        if (!subscription.IsReady)""",
    "fresh-engine-revalidation-before-write")

replace_once(
    """    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();""",
    """    private static bool SameLiteralReference(string? left, string? right)
        => string.Equals(LiteralReference(left), LiteralReference(right), StringComparison.OrdinalIgnoreCase);

    private static string LiteralReference(string? reference)
        => (reference ?? string.Empty).Trim();""",
    "literal-reference-equality-helper")

path.write_text(text, encoding="utf-8")
print("Applied guarded fresh ARIEC execution revalidation.")
