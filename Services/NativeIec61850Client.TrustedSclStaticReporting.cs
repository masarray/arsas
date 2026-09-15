using ArIED61850Tester.Models;
using ArMms = AR.Iec61850.Mms;

namespace ArIED61850Tester.Services;

/// <summary>
/// Golden-wire static-report adapter used only after a successful trusted-SCL online
/// connection. DataSet membership and configured RCB identity come from the exact SCL
/// source that established the association. No live DataSet-directory browse, dynamic
/// DataSet mutation, or implicit GI is permitted on this path.
/// </summary>
public sealed partial class NativeIec61850Client
{
    public async Task<NativeReportMonitorStartResult> StartTrustedSclStaticReportMonitorAsync(
        ReportControlPlan plan,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        cancellationToken.ThrowIfCancellationRequested();

        if (!HasTrustedSclOnlineAuthority)
        {
            return TrustedSclStaticFailure(
                plan,
                "Trusted SCL online authority is not active for this MMS association.",
                "TrustedSclAuthorityUnavailable");
        }

        if (!_session.IsMmsInitiated)
        {
            return TrustedSclStaticFailure(
                plan,
                $"Trusted SCL static report monitor requires an initiated MMS association. Current state: {_session.State}.",
                "TransportUnavailable");
        }

        if (_reportMonitorSessions.ContainsKey(plan.PlanId))
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = true,
                PlanId = plan.PlanId,
                Message = $"Trusted SCL static report monitor already active for {plan.DisplayReference}.",
                ReportControlReference = plan.ReportControlReference,
                DataSetReference = plan.DataSetReference,
                AcquisitionLabel = $"Trusted SCL: {(plan.Buffered ? "StaticBrcb" : "StaticUrcb")}",
                CoveredReferences = _reportMonitorCoverage.TryGetValue(plan.PlanId, out var existingCoverage)
                    ? existingCoverage
                    : Array.Empty<string>()
            };
        }

        if (string.IsNullOrWhiteSpace(plan.DataSetReference) ||
            !TryGetTrustedSclDataSetDirectory(plan.DataSetReference, out var directory) ||
            !directory.IsSuccess ||
            directory.Members.Count == 0)
        {
            return TrustedSclStaticFailure(
                plan,
                $"Trusted SCL DataSet '{plan.DataSetReference}' is missing or has no ordered members. " +
                "The client refused network DataSet-directory discovery and did not arm an RCB.",
                "TrustedSclDataSetUnavailable");
        }

        var configuredReference = (plan.ReportControlReference ?? string.Empty).Trim();
        var candidates = TrustedSclReportControls
            .Where(candidate =>
                string.IsNullOrWhiteSpace(candidate.DataSetReference) ||
                SameStaticReference(candidate.DataSetReference, directory.DataSetReference))
            .Select(candidate => new
            {
                Candidate = candidate,
                Rank = string.IsNullOrWhiteSpace(configuredReference)
                    ? 0
                    : Iec61850StaticRcbReferenceMatcher.MatchRank(configuredReference, candidate.Reference)
            })
            .Where(item => item.Rank != int.MaxValue)
            .OrderBy(item => item.Rank)
            .ThenByDescending(item => item.Candidate.Buffered)
            .ThenBy(item => item.Candidate.Reference, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            return TrustedSclStaticFailure(
                plan,
                $"No trusted SCL RCB matches configured reference '{plan.ReportControlReference}' and DataSet '{directory.DataSetReference}'. " +
                "No peer RCB substitution or live RCB directory discovery was attempted.",
                "TrustedSclRcbUnavailable");
        }

        var bestRank = candidates[0].Rank;
        var best = candidates.Where(item => item.Rank == bestRank).ToArray();
        if (best.Length != 1)
        {
            return TrustedSclStaticFailure(
                plan,
                $"Trusted SCL RCB selection is ambiguous for '{plan.ReportControlReference}': {best.Length} equally ranked candidate(s). " +
                "The client failed closed without enabling any RCB.",
                "TrustedSclRcbAmbiguous");
        }

        var rcb = CloneReportControlForPlanning(best[0].Candidate);
        if (string.IsNullOrWhiteSpace(rcb.DataSetReference))
            rcb.DataSetReference = directory.DataSetReference;
        if (!SameStaticReference(rcb.DataSetReference, directory.DataSetReference))
        {
            return TrustedSclStaticFailure(
                plan,
                $"Trusted SCL RCB {rcb.Reference} binds DataSet '{rcb.DataSetReference}', not '{directory.DataSetReference}'. " +
                "RCB activation was withheld.",
                "TrustedSclRcbDataSetMismatch");
        }

        var subscription = new ArMms.MmsReportSubscriptionPlan
        {
            Mode = ArMms.MmsReportSubscriptionPlanMode.StaticDataSet,
            Status = ArMms.MmsReportSubscriptionPlanStatus.ReadyRequiresWrite,
            ReportControl = rcb,
            DataSetReference = directory.DataSetReference,
            Members = directory.Members,
            DynamicPoints = Array.Empty<ArMms.MmsFcResolvedPoint>(),
            Steps = new[]
            {
                $"Use trusted SCL RCB {rcb.Reference} and DataSet {directory.DataSetReference}.",
                $"Use {directory.Members.Count} ordered DataSet member(s) from the verified SCL source.",
                "Install InformationReport receiver before RCB activation.",
                "Primary wire sequence: whole-RCB Read, optional URCB Resv, RptEna=true, two whole-RCB readbacks.",
                "BRCB ResvTms is retry-only after a real direct-RptEna rejection; GI remains off."
            },
            Warnings = Array.Empty<string>()
        };

        var coveredReferences = ExtractSubscriptionMemberReferences(subscription.Members);
        var start = await RunMmsOperationAsync(
            () => _session.StartStaticSclReportMonitorAsync(
                subscription,
                triggerGeneralInterrogation: false,
                cancellationToken),
            cancellationToken).ConfigureAwait(false);

        var warnings = start.Warnings
            .Concat(subscription.Warnings)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!start.IsSuccess || start.Session is null)
        {
            return new NativeReportMonitorStartResult
            {
                IsSuccess = false,
                PlanId = plan.PlanId,
                Message = $"Trusted SCL static report activation failed for {plan.DisplayReference}: {start.Message}",
                SubscriptionSummary = subscription.Summary,
                MemberCount = subscription.Members.Count,
                WriteStepCount = start.WriteSteps.Count,
                UsedDynamicDataSet = false,
                DynamicAttempted = false,
                DynamicAttemptState = "NotApplicable",
                FailureReason = "TrustedSclStaticActivationFailed",
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
        plan.Buffered = start.Session.ReportControl.Buffered;
        plan.IsEngineAuthoritative = true;
        plan.EngineAcquisitionKind = plan.Buffered ? "StaticBrcb" : "StaticUrcb";

        _reportMonitorSessions[plan.PlanId] = start.Session;
        _reportMonitorCoverage[plan.PlanId] = coveredReferences;

        return new NativeReportMonitorStartResult
        {
            IsSuccess = true,
            PlanId = plan.PlanId,
            Message = $"Trusted SCL {plan.EngineAcquisitionKind} monitor active. {start.Message}",
            SubscriptionSummary = subscription.Summary,
            MemberCount = subscription.Members.Count,
            WriteStepCount = start.WriteSteps.Count,
            UsedDynamicDataSet = false,
            DynamicAttempted = false,
            DynamicAttemptState = "NotApplicable",
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            AcquisitionLabel = $"Trusted SCL: {plan.EngineAcquisitionKind}",
            CoveredReferences = coveredReferences,
            Warnings = warnings
        };
    }

    private static NativeReportMonitorStartResult TrustedSclStaticFailure(
        ReportControlPlan plan,
        string message,
        string failureReason)
        => new()
        {
            IsSuccess = false,
            PlanId = plan.PlanId,
            Message = message,
            UsedDynamicDataSet = false,
            DynamicAttempted = false,
            DynamicAttemptState = "NotApplicable",
            FailureReason = failureReason,
            ReportControlReference = plan.ReportControlReference,
            DataSetReference = plan.DataSetReference,
            CoveredReferences = Array.Empty<string>()
        };
}
