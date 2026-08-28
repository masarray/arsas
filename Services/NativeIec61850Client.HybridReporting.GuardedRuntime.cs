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

            if (!ArMms.MmsGuardedDynamicReportLegacyCompatibilityPolicy.TryBuildCompatibleContext(
                    sourceContext,
                    legacyEvidence,
                    out var compatibleContext,
                    out var compatibilityReason))
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    "P1.5 legacy compatibility evidence was present but ARIEC rejected the exact compatibility view: " + compatibilityReason);
            }

            return new GuardedRuntimeContextLoadResult(
                compatibleContext,
                $"Smart Dynamic RCB guarded runtime candidate loaded through P1.5 legacy compatibility. {registryReason} {compatibilityReason}");
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
        => guardedContext is null
            ? ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options)
            : ArMms.MmsGuardedDynamicReportRuntimePlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options,
                guardedContext);

    private bool TryGetGuardedRuntimeContext(
        string planId,
        out ArMms.MmsDynamicReportGuardedRuntimePlanningContext context)
        => _guardedRuntimeContexts.TryGetValue(planId, out context!);
}
