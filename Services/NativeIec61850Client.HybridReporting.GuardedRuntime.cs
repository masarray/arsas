using System.Collections.Concurrent;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

public sealed partial class NativeIec61850Client
{
    private readonly Dictionary<string, ArMms.MmsDynamicReportGuardedRuntimePlanningContext> _guardedRuntimeContexts =
        new(StringComparer.OrdinalIgnoreCase);

    // P1.7 keeps the native per-IED cleanup witness separate from the qualification profile.
    // The engine policy revalidates this evidence every time a plan (including isolated
    // execution revalidation) is built, so a DataChange profile by itself is never enough.
    private readonly ConcurrentDictionary<string, ArMms.MmsDynamicReportNativeFieldCapabilityEvidence> _nativeFieldCapabilityEvidence =
        new(StringComparer.OrdinalIgnoreCase);

    private sealed record GuardedRuntimeContextLoadResult(
        ArMms.MmsDynamicReportGuardedRuntimePlanningContext? Context,
        string Reason)
    {
        public bool IsAuthorizedCandidate => Context is not null;
    }

    private async Task<GuardedRuntimeContextLoadResult> TryLoadGuardedRuntimeContextAsync(
        Iec61850MonitorDevice device,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(device);
        cancellationToken.ThrowIfCancellationRequested();

        try
        {
            var identity = DynamicReportQualificationIdentity.Build(device, device.Signals.ToArray());
            _nativeFieldCapabilityEvidence.TryRemove(identity.StableIdentityKey, out _);

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
                var nativeLoad = await new DynamicReportNativeFieldCapabilityWitnessStore()
                    .LoadAsync(identity, cancellationToken)
                    .ConfigureAwait(false);
                if (!nativeLoad.IsValid || nativeLoad.Evidence is null)
                {
                    return new GuardedRuntimeContextLoadResult(
                        null,
                        "P1.7 native DataChange profile is present but general Dynamic RCB runtime remains withheld: " + nativeLoad.Reason);
                }

                if (!ArMms.MmsGuardedDynamicReportNativeFieldCapabilityPolicy.TryValidate(
                        sourceContext,
                        nativeLoad.Evidence,
                        out var nativeReason))
                {
                    return new GuardedRuntimeContextLoadResult(
                        null,
                        "P1.7 native field-capability witness was present but ARIEC rejected its exact identity/profile/activation/report/cleanup binding: " + nativeReason);
                }

                _nativeFieldCapabilityEvidence[identity.StableIdentityKey] = nativeLoad.Evidence;
                return new GuardedRuntimeContextLoadResult(
                    sourceContext,
                    "Smart Dynamic RCB P1.7 native per-IED field-capability runtime candidate loaded. Physical dchg + cleanup proves the dynamic reporting mechanism, not permanent member scope. ProductionEligible certification remains separate. " + nativeReason);
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

            // P1.6 legacy load-time authorization uses the same field-capability policy that
            // owns normal planning. This retained path exists only for the reviewed historical
            // AA1C1F08R4 GI-classified profile + later Q0/A3 physical dchg witness.
            if (!ArMms.MmsGuardedDynamicReportFieldCapabilityPolicy.TryValidate(
                    sourceContext,
                    legacyEvidence,
                    out var compatibilityReason))
            {
                return new GuardedRuntimeContextLoadResult(
                    null,
                    "P1.6 field-capability witness was present but ARIEC rejected its exact identity/profile/witness/cleanup binding: " + compatibilityReason);
            }

            return new GuardedRuntimeContextLoadResult(
                sourceContext,
                $"Smart Dynamic RCB P1.6 legacy field-capability runtime candidate loaded. Q0/A3 proves capability, not member scope. {registryReason} {compatibilityReason}");
        }
        catch (Exception ex) when (ex is InvalidOperationException or ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new GuardedRuntimeContextLoadResult(
                null,
                $"Guarded Smart Dynamic runtime profile could not be trusted: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private ArMms.MmsCapabilityAwareHybridReportAcquisitionPlan BuildCapabilityPlanWithGuardedRuntime(
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

        // P1.7 native path. A DataChange profile no longer falls through to the historical
        // one-envelope guarded planner. General Dynamic RCB coverage is allowed only when the
        // separately persisted per-IED dchg + cleanup witness is loaded, and the ARIEC planner
        // itself revalidates that exact binding plus fresh live members/RCB availability.
        if (guardedContext.Profile.InformationReportProof?.Kind == ArMms.MmsDynamicInformationReportKind.DataChange)
        {
            if (_nativeFieldCapabilityEvidence.TryGetValue(
                    guardedContext.CurrentIdentity.StableIdentityKey,
                    out var nativeEvidence))
            {
                return ArMms.MmsGuardedDynamicReportNativeFieldCapabilityStableRuntimePlanner.Build(
                    catalog,
                    requestedSignals,
                    inventory,
                    availability,
                    liveDirectory,
                    negotiatedCapabilities,
                    options,
                    guardedContext,
                    nativeEvidence);
            }

            // Defensive fail-closed fallback. In normal flow TryLoadGuardedRuntimeContextAsync
            // never returns a native DataChange context without the matching witness.
            return ArMms.MmsCapabilityAwareHybridReportAcquisitionPlanner.Build(
                catalog,
                requestedSignals,
                inventory,
                availability,
                liveDirectory,
                negotiatedCapabilities,
                options);
        }

        // P1.6 historical path: resolve the exact physical Q0/A3 dchg witness again at every
        // planning/execution-revalidation call, then allow general member coverage on fresh
        // verified-free RCBs with deterministic per-RCB temporary DataSet identities.
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
