using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private readonly Dictionary<string, ArMms.MmsDynamicReportGuardedRuntimePlanningContext> _guardedRuntimeContexts =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record GuardedRuntimeContextLoadResult(
        ArMms.MmsDynamicReportGuardedRuntimePlanningContext? Context,
        string Reason)
    {
        public bool IsAuthorizedCandidate => Context is not null;
    }

    private static async Task<GuardedRuntimeContextLoadResult> TryLoadGuardedRuntimeContextAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var identity = DynamicReportQualificationIdentity.Build(device, device.Signals.ToArray());
            var load = await new DynamicReportQualificationProfileStore()
                .LoadAsync(identity, cancellationToken)
                .ConfigureAwait(false);

            if (!load.IsValid || load.Profile is null)
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    string.IsNullOrWhiteSpace(load.Reason)
                        ? "No valid identity-compatible dynamic qualification profile is available."
                        : load.Reason);
            }

            if (load.Profile.State < ArMms.MmsDynamicReportQualificationState.InformationReportProven)
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    $"Dynamic qualification profile is {load.Profile.State}; guarded Smart Dynamic runtime requires InformationReportProven or stronger evidence.");
            }

            if (load.Profile.RcbActivationProof?.IsSuccess != true ||
                load.Profile.InformationReportProof?.IsSuccess != true)
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    "Stored dynamic qualification evidence does not contain a successful activation + actual InformationReport chain; guarded Smart Dynamic runtime remains withheld.");
            }

            var sourceContext = new ArMms.MmsDynamicReportGuardedRuntimePlanningContext
            {
                Profile = load.Profile,
                CurrentIdentity = identity
            };

            if (load.Profile.InformationReportProof.Kind == ArMms.MmsDynamicInformationReportKind.DataChange)
            {
                return new GuardedRuntimeContextLoadResult(
                    sourceContext,
                    "Smart Dynamic RCB guarded runtime candidate loaded from identity-compatible InformationReportProven data-change evidence. ProductionEligible certification remains separate.");
            }

            if (!DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.TryResolve(
                    identity,
                    load.Profile,
                    out var legacyEvidence,
                    out var registryReason) || legacyEvidence is null)
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    $"Stored InformationReport kind is {load.Profile.InformationReportProof.Kind}; guarded Smart Dynamic runtime remains withheld. {registryReason}");
            }

            if (!ArMms.MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate(
                    sourceContext,
                    legacyEvidence,
                    out var compatibilityReason))
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    "P1.6 field-capability witness was present but ARIEC rejected its identity/RCB/member/cleanup evidence: " + compatibilityReason);
            }

            // P1.6 keeps the original persisted profile unchanged. The reviewed Q0/A3
            // NO-GI dchg subset proves that dynamic DataSet + URCB reporting works for this
            // exact identity/profile/association contract; it is capability evidence, not a
            // permanent member whitelist. Every planning and execution revalidation resolves
            // the exact witness again before general dynamic coverage is allowed.
            return new GuardedRuntimeContextLoadResult(
                sourceContext,
                $"Smart Dynamic RCB P1.6 field-capability runtime candidate loaded. Q0/A3 proves capability, not member scope. {registryReason} {compatibilityReason}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new GuardedRuntimeContextLoadResult(
                null,
                $"Guarded Smart Dynamic runtime profile could not be trusted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static ArMms.MmsCapabilityAwareHybridReportAcquisitionPlan BuildCapabilityPlanWithGuardedRuntime(
        Iec61850SignalCatalogDocument catalog,
        IEnumerable<Iec61850SignalDescriptor> requestedSignals,
        ArMms.MmsReportInventory inventory,
        ArMms.MmsRcbAvailabilityResult availability,
        ArMms.MmsIedModelDirectory liveDirectory,
        AR.Iec61850.Acse.AcseMmsNegotiatedCapabilities? negotiatedCapabilities,
        ArMms.MmsHybridReportAcquisitionOptions options,
        ArMms.MmsDynamicReportGuardedRuntimePlanningContext? guardedContext)
    {
        if (guardedContext is null)
        {
            return ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options);
        }

        // Native stored DataChange profiles continue through the original exact-evidence
        // guarded planner. P1.6 generalization is intentionally tied to the separately
        // reviewed field-capability witness below rather than assuming every DataChange
        // profile proves arbitrary-member dynamic mutation safety.
        if (guardedContext.Profile.InformationReportProof?.Kind == ArMms.MmsDynamicInformationReportKind.DataChange)
        {
            return ArMms.MmsGuardedDynamicReportRuntimePlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options,
                guardedContext);
        }

        // P1.6: resolve the exact physical Q0/A3 dchg witness again at every planning and
        // execution-revalidation call. Once that exact capability evidence matches, static
        // coverage keeps precedence and ARIEC may create bounded dynamic DataSets across
        // freshly verified free RCBs for every still-uncovered exact-resolved selected signal.
        // Stable per-RCB DataSet identities keep multi-RCB isolated revalidation collision-free.
        if (DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.TryResolve(
                guardedContext.CurrentIdentity,
                guardedContext.Profile,
                out var legacyEvidence,
                out _) && legacyEvidence is not null)
        {
            return ArMms.MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options,
                guardedContext,
                legacyEvidence);
        }

        return ArMms.MmsGuardedDynamicReportRuntimePlanner.Build(
            catalog,
            requestedSignals,
            inventory,
            availability,
            liveDirectory,
            negotiatedCapabilities,
            options,
            guardedContext);
    }

    private bool TryGetGuardedRuntimeContext(
        string planId,
        out ArMms.MmsDynamicReportGuardedRuntimePlanningContext context)
        => _guardedRuntimeContexts.TryGetValue(planId, out context!);
}
