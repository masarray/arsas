using System.Globalization;

namespace ArIED61850Tester.Services;

internal sealed class DynamicReportShadowEventObservation
{
    public int EventOrdinal { get; init; }
    public int DataSetIndex { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string ReportValue { get; init; } = string.Empty;
    public DateTimeOffset ReportObservedAtUtc { get; init; }
    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();
}

internal sealed class DynamicReportShadowReadObservation
{
    public int EventOrdinal { get; init; }
    public int DataSetIndex { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public bool IsSuccess { get; init; }
    public string DirectReadValue { get; init; } = string.Empty;
    public DateTimeOffset ReadObservedAtUtc { get; init; }
}

internal sealed class DynamicReportShadowEventAgreement
{
    public bool IsSuccess { get; init; }
    public int EventOrdinal { get; init; }
    public int DataSetIndex { get; init; }
    public string MemberReference { get; init; } = string.Empty;
    public string ReportValue { get; init; } = string.Empty;
    public string DirectReadValue { get; init; } = string.Empty;
    public TimeSpan VerificationLag { get; init; }
    public string Reason { get; init; } = string.Empty;
}

internal sealed class DynamicReportShadowVerificationResult
{
    public bool IsSuccess { get; init; }
    public bool IsBlocked { get; init; }
    public bool PrerequisiteAccepted { get; init; }
    public int RequiredAgreementCount { get; init; }
    public int AgreementCount { get; init; }
    public int MismatchCount { get; init; }
    public string Summary { get; init; } = string.Empty;
    public IReadOnlyList<DynamicReportShadowEventAgreement> Agreements { get; init; } = Array.Empty<DynamicReportShadowEventAgreement>();
    public IReadOnlyList<string> EvidenceLines { get; init; } = Array.Empty<string>();
}

/// <summary>
/// G2.5-B shadow-verification contract.
///
/// This phase does not activate production dynamic reporting and intentionally has no
/// operator shortcut yet. It is a fail-closed verifier that may only be entered after
/// a physical G2.5-A1 result proves both a real qualified-member transition and a
/// correlated spontaneous dchg report. Shadow observations compare report values with
/// independent direct MMS reads from the same exact member/index within a bounded lag.
///
/// Runtime wiring is intentionally withheld until G2.5-A1 physical acceptance. This
/// keeps PR #226/A1 immutable and prevents G2.5-B from becoming an accidental bypass.
/// </summary>
internal sealed class DynamicReportShadowVerificationService
{
    internal const int RequiredConsecutiveAgreements = 3;
    internal const int MaximumAllowedMismatches = 0;
    internal static readonly TimeSpan MaximumVerificationLag = TimeSpan.FromSeconds(2);

    internal static bool IsPrerequisiteAccepted(
        DynamicReportStimulusWitnessCommissioningResult? prerequisite,
        out string reason)
    {
        if (prerequisite is null)
        {
            reason = "G2.5-B requires a physical G2.5-A1 result; none was supplied.";
            return false;
        }

        if (!prerequisite.IsSuccess)
        {
            reason = "G2.5-A1 did not PASS.";
            return false;
        }

        if (!prerequisite.StimulusWitnessProven || !prerequisite.Witness.ChangeObserved)
        {
            reason = "G2.5-A1 did not prove that a qualified member actually changed.";
            return false;
        }

        if (!prerequisite.ReportCorrelationProven || prerequisite.CorrelatedIndexes.Count == 0)
        {
            reason = "G2.5-A1 did not prove correlation between the witnessed transition and the spontaneous dchg report.";
            return false;
        }

        if (!prerequisite.CoreResult.IsSuccess ||
            !prerequisite.CoreResult.ActivationProven ||
            !prerequisite.CoreResult.SpontaneousDataChangeProven ||
            !prerequisite.CoreResult.MonitorCleanupSucceeded ||
            !prerequisite.CoreResult.ProofFieldRestoreSucceeded ||
            !prerequisite.CoreResult.FreshCleanupClosureSucceeded)
        {
            reason = "The underlying G2.5-A physical proof or cleanup closure is incomplete.";
            return false;
        }

        reason = "Physical G2.5-A1 prerequisite accepted: stimulus transition + correlated spontaneous dchg + complete cleanup are proven.";
        return true;
    }

    internal static DynamicReportShadowEventAgreement CompareEvent(
        DynamicReportShadowEventObservation report,
        DynamicReportShadowReadObservation directRead)
    {
        ArgumentNullException.ThrowIfNull(report);
        ArgumentNullException.ThrowIfNull(directRead);

        if (report.EventOrdinal != directRead.EventOrdinal)
            return Fail(report, directRead, "Event ordinal mismatch.");

        if (report.DataSetIndex != directRead.DataSetIndex)
            return Fail(report, directRead, "DataSet index mismatch.");

        if (!SameReference(report.MemberReference, directRead.MemberReference))
            return Fail(report, directRead, "Member reference mismatch.");

        var reasons = report.Reasons
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (!reasons.Contains("data-change", StringComparer.OrdinalIgnoreCase))
            return Fail(report, directRead, "Shadow event is not reason-for-inclusion=data-change.");

        if (reasons.Any(item =>
                item.Equals("general-interrogation", StringComparison.OrdinalIgnoreCase) ||
                item.Equals("integrity", StringComparison.OrdinalIgnoreCase) ||
                item.Equals("quality-change", StringComparison.OrdinalIgnoreCase) ||
                item.Equals("data-update", StringComparison.OrdinalIgnoreCase)))
        {
            return Fail(report, directRead, "Shadow event carries a non-dchg reason and is rejected fail-closed.");
        }

        if (!directRead.IsSuccess)
            return Fail(report, directRead, "Independent direct MMS verification read failed.");

        var lag = directRead.ReadObservedAtUtc - report.ReportObservedAtUtc;
        if (lag < TimeSpan.Zero)
            return Fail(report, directRead, "Direct-read observation predates the report observation.");

        if (lag > MaximumVerificationLag)
            return Fail(report, directRead, $"Direct-read verification lag {lag.TotalMilliseconds:0} ms exceeds {MaximumVerificationLag.TotalMilliseconds:0} ms.");

        var reportValue = NormalizeValue(report.ReportValue);
        var readValue = NormalizeValue(directRead.DirectReadValue);
        if (reportValue.Length == 0 || readValue.Length == 0)
            return Fail(report, directRead, "Report/direct-read value is empty or not usable for comparison.");

        if (!string.Equals(reportValue, readValue, StringComparison.OrdinalIgnoreCase))
            return Fail(report, directRead, $"Shadow mismatch: report={reportValue}, directRead={readValue}.");

        return new DynamicReportShadowEventAgreement
        {
            IsSuccess = true,
            EventOrdinal = report.EventOrdinal,
            DataSetIndex = report.DataSetIndex,
            MemberReference = report.MemberReference,
            ReportValue = reportValue,
            DirectReadValue = readValue,
            VerificationLag = lag,
            Reason = $"Exact member/index/value agreement within {lag.TotalMilliseconds:0} ms; dchg-only reason confirmed."
        };
    }

    internal static DynamicReportShadowVerificationResult EvaluateSeries(
        DynamicReportStimulusWitnessCommissioningResult? prerequisite,
        IReadOnlyList<DynamicReportShadowEventObservation> reports,
        IReadOnlyList<DynamicReportShadowReadObservation> directReads)
    {
        ArgumentNullException.ThrowIfNull(reports);
        ArgumentNullException.ThrowIfNull(directReads);

        var evidence = new List<string>
        {
            "G2.5-B contract: SHADOW ONLY. Dynamic report values are never authoritative for production in this phase.",
            $"G2.5-B acceptance: {RequiredConsecutiveAgreements} exact independent report/direct-read agreements, zero mismatches, dchg-only reasons, bounded verification lag <= {MaximumVerificationLag.TotalMilliseconds:0} ms.",
            "G2.5-B safety: no ProductionEligible mutation and production automatic dynamic reporting remains OFF."
        };

        if (!IsPrerequisiteAccepted(prerequisite, out var prerequisiteReason))
        {
            evidence.Add("G2.5-B prerequisite rejected: " + prerequisiteReason);
            return new DynamicReportShadowVerificationResult
            {
                IsBlocked = true,
                RequiredAgreementCount = RequiredConsecutiveAgreements,
                Summary = prerequisiteReason,
                EvidenceLines = evidence.ToArray()
            };
        }

        evidence.Add("G2.5-B prerequisite accepted: " + prerequisiteReason);

        var agreements = new List<DynamicReportShadowEventAgreement>();
        var mismatchCount = 0;
        var eventOrdinals = reports.Select(item => item.EventOrdinal).OrderBy(item => item).ToArray();
        if (eventOrdinals.Distinct().Count() != eventOrdinals.Length)
        {
            evidence.Add("G2.5-B rejected: duplicate report event ordinals are ambiguous.");
            return FailSeries(evidence, agreements, 1, "Duplicate report event ordinals are not allowed.");
        }

        foreach (var report in reports.OrderBy(item => item.EventOrdinal))
        {
            var candidates = directReads.Where(read => read.EventOrdinal == report.EventOrdinal).ToArray();
            if (candidates.Length != 1)
            {
                mismatchCount++;
                evidence.Add($"G2.5-B event {report.EventOrdinal}: expected exactly one independent direct-read observation, found {candidates.Length}.");
                continue;
            }

            var agreement = CompareEvent(report, candidates[0]);
            agreements.Add(agreement);
            if (!agreement.IsSuccess)
                mismatchCount++;

            evidence.Add($"G2.5-B event {report.EventOrdinal}: success={agreement.IsSuccess}; index={report.DataSetIndex}; member={report.MemberReference}; report={agreement.ReportValue}; direct={agreement.DirectReadValue}; lagMs={agreement.VerificationLag.TotalMilliseconds:0}; reason={agreement.Reason}");
        }

        var successCount = agreements.Count(item => item.IsSuccess);
        var success = mismatchCount <= MaximumAllowedMismatches &&
                      successCount >= RequiredConsecutiveAgreements &&
                      reports.Count >= RequiredConsecutiveAgreements;

        evidence.Add($"G2.5-B result: agreements={successCount}; mismatches={mismatchCount}; requiredAgreements={RequiredConsecutiveAgreements}; maxMismatches={MaximumAllowedMismatches}; success={success}");

        return new DynamicReportShadowVerificationResult
        {
            IsSuccess = success,
            PrerequisiteAccepted = true,
            RequiredAgreementCount = RequiredConsecutiveAgreements,
            AgreementCount = successCount,
            MismatchCount = mismatchCount,
            Summary = success
                ? "G2.5-B PASS: three shadow dchg events agreed exactly with independent direct MMS verification and zero mismatches. This still does not make production dynamic reporting eligible."
                : "G2.5-B did not meet the fail-closed shadow agreement gate. Production dynamic reporting remains OFF.",
            Agreements = agreements.ToArray(),
            EvidenceLines = evidence.ToArray()
        };
    }

    internal static string NormalizeValue(string? value)
    {
        var text = (value ?? string.Empty).Trim();
        if (text.Length == 0)
            return string.Empty;

        const string prefix = "Native MMS Confirmed-Read decoded value:";
        if (text.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            text = text[prefix.Length..].Trim().TrimEnd('.').Trim();

        if (bool.TryParse(text, out var boolean))
            return boolean ? "true" : "false";

        if (decimal.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            return number.ToString("G29", CultureInfo.InvariantCulture);

        return text;
    }

    private static DynamicReportShadowEventAgreement Fail(
        DynamicReportShadowEventObservation report,
        DynamicReportShadowReadObservation directRead,
        string reason)
        => new()
        {
            EventOrdinal = report.EventOrdinal,
            DataSetIndex = report.DataSetIndex,
            MemberReference = report.MemberReference,
            ReportValue = NormalizeValue(report.ReportValue),
            DirectReadValue = NormalizeValue(directRead.DirectReadValue),
            VerificationLag = directRead.ReadObservedAtUtc - report.ReportObservedAtUtc,
            Reason = reason
        };

    private static DynamicReportShadowVerificationResult FailSeries(
        IReadOnlyList<string> evidence,
        IReadOnlyList<DynamicReportShadowEventAgreement> agreements,
        int mismatchCount,
        string summary)
        => new()
        {
            PrerequisiteAccepted = true,
            RequiredAgreementCount = RequiredConsecutiveAgreements,
            AgreementCount = agreements.Count(item => item.IsSuccess),
            MismatchCount = mismatchCount,
            Summary = summary + " Production dynamic reporting remains OFF.",
            Agreements = agreements.ToArray(),
            EvidenceLines = evidence.ToArray()
        };

    private static bool SameReference(string? left, string? right)
        => NormalizeReference(left).Equals(NormalizeReference(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizeReference(string? reference)
        => (reference ?? string.Empty).Trim().Replace('$', '.');
}
