using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DynamicReportShadowVerificationServiceTests
{
    [Fact]
    public void Prerequisite_AcceptsOnlyCompletePhysicalA1Proof()
    {
        var ok = DynamicReportShadowVerificationService.IsPrerequisiteAccepted(PassedPrerequisite(), out var reason);
        Assert.True(ok, reason);
        Assert.Contains("prerequisite accepted", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Prerequisite_RejectsMissingStimulusCorrelationOrCleanup()
    {
        Assert.False(DynamicReportShadowVerificationService.IsPrerequisiteAccepted(null, out _));

        Assert.False(DynamicReportShadowVerificationService.IsPrerequisiteAccepted(
            BuildPrerequisite(stimulusWitnessProven: false, changeObserved: false), out _));

        Assert.False(DynamicReportShadowVerificationService.IsPrerequisiteAccepted(
            BuildPrerequisite(reportCorrelationProven: false, correlatedIndexes: Array.Empty<int>()), out _));

        Assert.False(DynamicReportShadowVerificationService.IsPrerequisiteAccepted(
            BuildPrerequisite(freshCleanupClosureSucceeded: false), out _));
    }

    [Theory]
    [InlineData("true", "TRUE")]
    [InlineData("Native MMS Confirmed-Read decoded value: false.", "false")]
    [InlineData("12.5000", "12.5000")]
    public void CompareEvent_AcceptsExactNormalizedDchgAgreement(string reportValue, string directValue)
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T05:00:00Z");
        var agreement = DynamicReportShadowVerificationService.CompareEvent(
            Report(1, 2, "LD0/GGIO1$ST$A$stVal", reportValue, t0, ["data-change"]),
            Read(1, 2, "LD0/GGIO1$ST$A$stVal", directValue, t0.AddMilliseconds(250)));

        Assert.True(agreement.IsSuccess, agreement.Reason);
        Assert.True(agreement.VerificationLag <= DynamicReportShadowVerificationService.MaximumVerificationLag);
    }

    [Fact]
    public void CompareEvent_RejectsMismatchWrongReasonAndLateVerifier()
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T05:00:00Z");

        var mismatch = DynamicReportShadowVerificationService.CompareEvent(
            Report(1, 0, "LD0/A", "true", t0, ["data-change"]),
            Read(1, 0, "LD0/A", "false", t0.AddMilliseconds(100)));
        Assert.False(mismatch.IsSuccess);
        Assert.Contains("mismatch", mismatch.Reason, StringComparison.OrdinalIgnoreCase);

        var gi = DynamicReportShadowVerificationService.CompareEvent(
            Report(1, 0, "LD0/A", "true", t0, ["data-change", "general-interrogation"]),
            Read(1, 0, "LD0/A", "true", t0.AddMilliseconds(100)));
        Assert.False(gi.IsSuccess);
        Assert.Contains("non-dchg", gi.Reason, StringComparison.OrdinalIgnoreCase);

        var late = DynamicReportShadowVerificationService.CompareEvent(
            Report(1, 0, "LD0/A", "true", t0, ["data-change"]),
            Read(1, 0, "LD0/A", "true", t0.AddSeconds(3)));
        Assert.False(late.IsSuccess);
        Assert.Contains("exceeds", late.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateSeries_RequiresThreeAgreementsAndZeroMismatch()
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T05:00:00Z");
        var reports = new[]
        {
            Report(1, 0, "LD0/A", "true", t0, ["data-change"]),
            Report(2, 1, "LD0/B", "false", t0.AddSeconds(5), ["data-change"]),
            Report(3, 2, "LD0/C", "1", t0.AddSeconds(10), ["data-change"])
        };
        var reads = new[]
        {
            Read(1, 0, "LD0/A", "true", t0.AddMilliseconds(100)),
            Read(2, 1, "LD0/B", "false", t0.AddSeconds(5.2)),
            Read(3, 2, "LD0/C", "1.0", t0.AddSeconds(10.4))
        };

        var result = DynamicReportShadowVerificationService.EvaluateSeries(PassedPrerequisite(), reports, reads);
        Assert.True(result.IsSuccess, result.Summary);
        Assert.Equal(3, result.AgreementCount);
        Assert.Equal(0, result.MismatchCount);
    }

    [Fact]
    public void EvaluateSeries_FailsClosedOnSingleMismatchOrTooFewEvents()
    {
        var t0 = DateTimeOffset.Parse("2026-08-21T05:00:00Z");
        var reports = new[]
        {
            Report(1, 0, "LD0/A", "true", t0, ["data-change"]),
            Report(2, 1, "LD0/B", "true", t0.AddSeconds(5), ["data-change"]),
            Report(3, 2, "LD0/C", "true", t0.AddSeconds(10), ["data-change"])
        };
        var reads = new[]
        {
            Read(1, 0, "LD0/A", "true", t0.AddMilliseconds(50)),
            Read(2, 1, "LD0/B", "false", t0.AddSeconds(5.1)),
            Read(3, 2, "LD0/C", "true", t0.AddSeconds(10.1))
        };

        var mismatch = DynamicReportShadowVerificationService.EvaluateSeries(PassedPrerequisite(), reports, reads);
        Assert.False(mismatch.IsSuccess);
        Assert.Equal(1, mismatch.MismatchCount);

        var twoGoodReports = new[]
        {
            Report(1, 0, "LD0/A", "true", t0, ["data-change"]),
            Report(2, 1, "LD0/B", "false", t0.AddSeconds(5), ["data-change"])
        };
        var twoGoodReads = new[]
        {
            Read(1, 0, "LD0/A", "true", t0.AddMilliseconds(50)),
            Read(2, 1, "LD0/B", "false", t0.AddSeconds(5.1))
        };

        var tooFew = DynamicReportShadowVerificationService.EvaluateSeries(
            PassedPrerequisite(),
            twoGoodReports,
            twoGoodReads);
        Assert.False(tooFew.IsSuccess);
        Assert.Equal(2, tooFew.AgreementCount);
        Assert.Equal(0, tooFew.MismatchCount);
    }

    [Fact]
    public void Source_IsShadowOnlyAndNotExposedBeforePhysicalA1Acceptance()
    {
        var root = RepoRoot();
        var service = File.ReadAllText(Path.Combine(root, "Services", "DynamicReportShadowVerificationService.cs"));
        var ui = File.ReadAllText(Path.Combine(root, "DynamicReportQualificationUiBehavior.cs"));
        var runtime = File.ReadAllText(Path.Combine(root, "Services", "Iec61850MonitorRuntime.cs"));

        Assert.Contains("RequiredConsecutiveAgreements = 3", service, StringComparison.Ordinal);
        Assert.Contains("MaximumAllowedMismatches = 0", service, StringComparison.Ordinal);
        Assert.Contains("SHADOW ONLY", service, StringComparison.Ordinal);
        Assert.Contains("G2.5-A1", service, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", service, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", service, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteReportAttributeAsync", service, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitor", service, StringComparison.Ordinal);
        Assert.DoesNotContain("RunG25B", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Key.B", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static DynamicReportStimulusWitnessCommissioningResult PassedPrerequisite()
        => BuildPrerequisite();

    private static DynamicReportStimulusWitnessCommissioningResult BuildPrerequisite(
        bool stimulusWitnessProven = true,
        bool changeObserved = true,
        bool reportCorrelationProven = true,
        IReadOnlyList<int>? correlatedIndexes = null,
        bool freshCleanupClosureSucceeded = true)
        => new()
        {
            IsSuccess = stimulusWitnessProven && changeObserved && reportCorrelationProven && freshCleanupClosureSucceeded,
            StimulusWitnessProven = stimulusWitnessProven,
            ReportCorrelationProven = reportCorrelationProven,
            CorrelatedIndexes = correlatedIndexes ?? [1],
            CoreResult = PassedCore(freshCleanupClosureSucceeded),
            Witness = new DynamicReportStimulusWitnessResult
            {
                ChangeObserved = changeObserved,
                BaselineCaptured = true,
                AssociationHealthy = true,
                Transitions = changeObserved
                    ?
                    [
                        new DynamicReportStimulusWitnessTransition
                        {
                            Index = 1,
                            MemberReference = "LD0/B",
                            BeforeValue = "false",
                            AfterValue = "true",
                            ObservedAtUtc = DateTimeOffset.UtcNow
                        }
                    ]
                    : Array.Empty<DynamicReportStimulusWitnessTransition>()
            }
        };

    private static DynamicReportSpontaneousDataChangeCommissioningResult PassedCore(bool freshCleanupClosureSucceeded = true)
        => new()
        {
            IsSuccess = freshCleanupClosureSucceeded,
            ActivationProven = true,
            SpontaneousDataChangeProven = true,
            AssociationHealthyAfterReport = true,
            MonitorCleanupSucceeded = true,
            ProofFieldRestoreSucceeded = true,
            FreshCleanupClosureSucceeded = freshCleanupClosureSucceeded,
            IncludedIndexes = [1],
            IncludedMemberReferences = ["LD0/B"],
            Reasons = ["data-change"]
        };

    private static DynamicReportShadowEventObservation Report(
        int ordinal,
        int index,
        string member,
        string value,
        DateTimeOffset at,
        IReadOnlyList<string> reasons)
        => new()
        {
            EventOrdinal = ordinal,
            DataSetIndex = index,
            MemberReference = member,
            ReportValue = value,
            ReportObservedAtUtc = at,
            Reasons = reasons
        };

    private static DynamicReportShadowReadObservation Read(
        int ordinal,
        int index,
        string member,
        string value,
        DateTimeOffset at)
        => new()
        {
            EventOrdinal = ordinal,
            DataSetIndex = index,
            MemberReference = member,
            IsSuccess = true,
            DirectReadValue = value,
            ReadObservedAtUtc = at
        };

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
