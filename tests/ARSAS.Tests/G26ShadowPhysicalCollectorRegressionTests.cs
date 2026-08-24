namespace ARSAS.Tests;

public sealed class G26ShadowPhysicalCollectorRegressionTests
{
    [Fact]
    public void Collector_UsesIndependentReadOnlyPollingAndExactDchgReportAssociation()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.Contains("new ArMms.MmsClientSession()", source, StringComparison.Ordinal);
        Assert.Contains("probeReportAttributes: false", source, StringComparison.Ordinal);
        Assert.Contains("maxReportAttributeProbes: 0", source, StringComparison.Ordinal);
        Assert.Contains("ReadSingleVariableAsync", source, StringComparison.Ordinal);
        Assert.Contains("PrepareDynamicRcbCommissioningFieldsAsync", source, StringComparison.Ordinal);
        Assert.Contains("TemporaryTriggerOptions = \"dchg\"", source, StringComparison.Ordinal);
        Assert.Contains("triggerGeneralInterrogation: false", source, StringComparison.Ordinal);
        Assert.Contains("ValidateSpontaneousDataChangeFrame", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_RequiresTwoPhasesAndOneDeliberateReconnectWithBothPathsRecovered()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.Contains("RunPhaseAsync(\n            1", source, StringComparison.Ordinal);
        Assert.Contains("recorder.RecordReconnectAttempt()", source, StringComparison.Ordinal);
        Assert.Contains("RunPhaseAsync(\n            2", source, StringComparison.Ordinal);
        Assert.Contains("recorder.RecordReconnectSuccess", source, StringComparison.Ordinal);
        Assert.Contains("reportResubscribed: phase2.ActivationProven", source, StringComparison.Ordinal);
        Assert.Contains("pollReferenceRecovered: phase2.PollReferenceRecovered", source, StringComparison.Ordinal);
        Assert.Contains("ReconnectAttempts == 1", source, StringComparison.Ordinal);
        Assert.Contains("SuccessfulReconnects == 1", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_DoesNotSynthesizeQualityTimestampOrUseHeaderTimeAsDeviceTimestamp()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.Contains("missing report-side quality/timestamp evidence is never inferred", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MmsReportValueProjector.Project(frame)", source, StringComparison.Ordinal);
        Assert.Contains("projected?.HasQuality == true", source, StringComparison.Ordinal);
        Assert.Contains("projected?.HasTimestamp == true", source, StringComparison.Ordinal);
        Assert.Contains("quality: null", source, StringComparison.Ordinal);
        Assert.Contains("deviceTimestampUtc: null", source, StringComparison.Ordinal);
        Assert.DoesNotContain("frame.Header.TimeOfEntry", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_PerformsMandatoryMonitorProofFieldAndFreshAssociationCleanup()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.Contains("StopPersistentReportMonitorAsync", source, StringComparison.Ordinal);
        Assert.Contains("RestoreDynamicRcbCommissioningFieldsAsync", source, StringComparison.Ordinal);
        Assert.Contains("IsTemporaryDataSetAbsentFromNameList", source, StringComparison.Ordinal);
        Assert.Contains("IsFreshCleanupClosed", source, StringComparison.Ordinal);
        Assert.Contains("directoryAbsent = !directory.IsSuccess", source, StringComparison.Ordinal);
        Assert.Contains("monitorCleanup && fieldRestore && freshClosure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Collector_NeverIssuesControlOrPromotesProfile()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.DoesNotContain("ExecuteControlAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteControl", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("_profileStore.SaveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
        Assert.Contains("ZERO control commands", source, StringComparison.Ordinal);
        Assert.Contains("Shadow PASS != ProductionEligible", source, StringComparison.Ordinal);
        Assert.Contains("production automatic dynamic reporting remains OFF", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Collector_FeedsStrictAcceptanceButLeavesIndependentRegressionsExplicit()
    {
        var source = Read("Services/DynamicReportShadowVerificationCommissioningService.cs");

        Assert.Contains("bool controlRegressionPassed = false", source, StringComparison.Ordinal);
        Assert.Contains("bool staticReportingRegressionPassed = false", source, StringComparison.Ordinal);
        Assert.Contains("_acceptanceService.EvaluateAsync", source, StringComparison.Ordinal);
        Assert.Contains("controlRegressionPassed,", source, StringComparison.Ordinal);
        Assert.Contains("staticReportingRegressionPassed,", source, StringComparison.Ordinal);
        Assert.Contains("strictProductionCandidate", source, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relativePath);
    }
}
