namespace ARSAS.Tests;

public sealed class G26P1CommandFocusRequalificationRegressionTests
{
    [Fact]
    public void Recovery_StagesAwayFromLiveProfile_AndCommitsOnlyAfterFreshCleanupClosure()
    {
        var source = Read("Services/DynamicReportCommandFocusRequalificationCommissioningService.cs");

        var stagingRoot = source.IndexOf("g26-p1-command-focus-", StringComparison.Ordinal);
        var stagingStore = source.IndexOf("new DynamicReportQualificationProfileStore(stagingRoot)", StringComparison.Ordinal);
        var activation = source.IndexOf("new DynamicReportActivationCommissioningServiceV2(stagingStore)", StringComparison.Ordinal);
        var closure = source.IndexOf("new DynamicReportCleanupClosureCommissioningService(stagingStore)", StringComparison.Ordinal);
        var closureGate = source.IndexOf("if (!closure.IsSuccess)", StringComparison.Ordinal);
        var concurrencyGate = source.IndexOf("SameProfileEvidence(originalProfile, currentLoad.Profile)", StringComparison.Ordinal);
        var liveSave = source.IndexOf("await _liveProfileStore.SaveAsync(finalProfile", StringComparison.Ordinal);

        Assert.True(stagingRoot >= 0);
        Assert.True(stagingStore > stagingRoot);
        Assert.True(activation > stagingStore);
        Assert.True(closure > activation);
        Assert.True(closureGate > closure);
        Assert.True(concurrencyGate > closureGate);
        Assert.True(liveSave > concurrencyGate);
        Assert.Contains("atomic replacement", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Recovery_NeverIssuesControl_AndCannotProduceProductionEligible()
    {
        var source = Read("Services/DynamicReportCommandFocusRequalificationCommissioningService.cs");

        Assert.DoesNotContain("ExecuteControlAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible", source, StringComparison.Ordinal);
        Assert.Contains("profile.State == ArMms.MmsDynamicReportQualificationState.ProductionEligible", source, StringComparison.Ordinal);
        Assert.Contains("forbidden in P1 recovery", source, StringComparison.Ordinal);
        Assert.Contains("ZERO control execution", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_RequiresExactCommandStatusMember_ToSurviveQualificationAndG24()
    {
        var source = Read("Services/DynamicReportCommandFocusRequalificationCommissioningService.cs");

        Assert.Contains("ResolveCommandStatusPoints", source, StringComparison.Ordinal);
        Assert.Contains("commandStatusReferences", source, StringComparison.Ordinal);
        Assert.Contains("acceptedStatuses.Length == 0", source, StringComparison.Ordinal);
        Assert.Contains("FinalProfileMatchesCommandFocus", source, StringComparison.Ordinal);
        Assert.Contains("final G2.4 exact member sequence has no retained command-status member", source, StringComparison.Ordinal);
        Assert.Contains("MaximumCommandFocusMembers = DynamicReportActivationCommissioningService.MaximumG24Members", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Recovery_UsesExistingG23QualificationPrimitive_AndExistingG24PhysicalProof()
    {
        var source = Read("Services/DynamicReportCommandFocusRequalificationCommissioningService.cs");

        Assert.Contains("RunDynamicDataSetQualificationCommissioningAsync", source, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicDataSetQualificationExecutionMode.ExplicitCommissioning", source, StringComparison.Ordinal);
        Assert.Contains("AcceptExactEnvelope", source, StringComparison.Ordinal);
        Assert.Contains("CreateEnvelopeQualifiedProfile", source, StringComparison.Ordinal);
        Assert.Contains("DynamicReportActivationCommissioningServiceV2", source, StringComparison.Ordinal);
        Assert.Contains("DynamicReportCleanupClosureCommissioningService", source, StringComparison.Ordinal);
    }

    [Fact]
    public void A3Ui_OffersRecoveryOnlyAfterReadOnlyAssessment_ThenReassessesBeforeAutomaticArm()
    {
        var ui = Read("DynamicReportCommandBoundWitnessUiBehavior.cs");

        var assess = ui.IndexOf("recovery.AssessAsync", StringComparison.Ordinal);
        var offer = ui.IndexOf("Run transactional command-focus recovery now?", StringComparison.Ordinal);
        var run = ui.IndexOf("recovery.RunAsync", StringComparison.Ordinal);
        var post = ui.IndexOf("postRecovery = await recovery.AssessAsync", StringComparison.Ordinal);
        var a3 = ui.IndexOf("new DynamicReportCommandBoundDataChangeCommissioningService", StringComparison.Ordinal);

        Assert.True(assess >= 0);
        Assert.True(offer > assess);
        Assert.True(run > offer);
        Assert.True(post > run);
        Assert.True(a3 > post);
        Assert.Contains("DO NOT command until the exact A3 READY marker appears", ui, StringComparison.Ordinal);
        Assert.Contains("keep the current InformationReportProven live profile untouched on ANY failure", ui, StringComparison.Ordinal);
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