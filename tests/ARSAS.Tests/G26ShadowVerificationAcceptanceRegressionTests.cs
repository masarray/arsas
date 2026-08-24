namespace ARSAS.Tests;

public sealed class G26ShadowVerificationAcceptanceRegressionTests
{
    [Fact]
    public void ShadowAcceptance_RequiresExactInformationReportProvenProfileAndMemberSequence()
    {
        var source = Read("Services/DynamicReportShadowVerificationAcceptanceService.cs");

        Assert.Contains("profile.State != ArMms.MmsDynamicReportQualificationState.InformationReportProven", source, StringComparison.Ordinal);
        Assert.Contains("profile.RcbActivationProof?.IsSuccess != true", source, StringComparison.Ordinal);
        Assert.Contains("profile.InformationReportProof?.IsSuccess != true", source, StringComparison.Ordinal);
        Assert.Contains("ExactSequenceEquals(qualifiedMembers, evidence.MemberReferences)", source, StringComparison.Ordinal);
        Assert.Contains("Shadow evidence member sequence does not exactly match", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowAcceptance_UsesTypedAriecEvaluatorWithStrictPhysicalGates()
    {
        var source = Read("Services/DynamicReportShadowVerificationAcceptanceService.cs");

        Assert.Contains("MmsDynamicReportShadowVerificationPolicy.Evaluate", source, StringComparison.Ordinal);
        Assert.Contains("MinimumReportEdges = 2", source, StringComparison.Ordinal);
        Assert.Contains("RequireQualityEvidence = true", source, StringComparison.Ordinal);
        Assert.Contains("RequireDeviceTimestampEvidence = true", source, StringComparison.Ordinal);
        Assert.Contains("RequireReconnectCycle = true", source, StringComparison.Ordinal);
        Assert.Contains("MaximumDynamicActivationAttemptsPerAssociation = 1", source, StringComparison.Ordinal);
        Assert.Contains("NoMissingReportEdgesPassed", source, StringComparison.Ordinal);
        Assert.Contains("NoDuplicateReportEdgesPassed", source, StringComparison.Ordinal);
        Assert.Contains("PollingAuthorityGuardPassed", source, StringComparison.Ordinal);
        Assert.Contains("ReconnectRegressionPassed", source, StringComparison.Ordinal);
        Assert.Contains("NoRepeatedMutationLoopPassed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowAcceptance_UsesStrictObservedQualityTimestampProductionBridge()
    {
        var source = Read("Services/DynamicReportShadowVerificationAcceptanceService.cs");

        Assert.Contains("MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedQualityEvidence", source, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicReportShadowProductionAcceptancePolicy.HasPairedTimestampEvidence", source, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicReportShadowProductionAcceptancePolicy.BuildStrict", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsDynamicReportShadowVerificationPolicy.BuildProductionAcceptance", source, StringComparison.Ordinal);
        Assert.Contains("absenceCannotPass=true", source, StringComparison.Ordinal);
        Assert.Contains("missing evidence is never synthesized or treated as PASS", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShadowAcceptance_CannotPromoteOrPersistProfile()
    {
        var source = Read("Services/DynamicReportShadowVerificationAcceptanceService.cs");

        Assert.DoesNotContain("_profileStore.SaveAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsDynamicReportQualificationProfilePolicy.MarkProductionEligible(", source, StringComparison.Ordinal);
        Assert.Contains("Shadow PASS != ProductionEligible", source, StringComparison.Ordinal);
        Assert.Contains("production automatic dynamic reporting remains OFF", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("candidate was NOT persisted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ShadowAcceptance_LeavesControlAndStaticRegressionAsIndependentInputs()
    {
        var source = Read("Services/DynamicReportShadowVerificationAcceptanceService.cs");

        Assert.Contains("bool controlRegressionPassed", source, StringComparison.Ordinal);
        Assert.Contains("bool staticReportingRegressionPassed", source, StringComparison.Ordinal);
        Assert.Contains("BuildStrict(", source, StringComparison.Ordinal);
        Assert.Contains("controlRegressionPassed,", source, StringComparison.Ordinal);
        Assert.Contains("staticReportingRegressionPassed);", source, StringComparison.Ordinal);
        Assert.Contains("IsSuccess = shadow.IsSuccess && acceptance.AllPassed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PinsMergedPr99MainAndKeepsProductionOff()
    {
        var lockFile = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("1efad9a2cdb6b4452b13687bbcd8c7ec41a9e53f", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 99", lockFile, StringComparison.Ordinal);
        Assert.Contains("PR #98", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("report-vs-independent-MMS shadow evaluator", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #99", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paired report/poll quality evidence", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("paired report/poll device timestamp evidence", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("current field profile remains InformationReportProven", lockFile, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("production automatic dynamic reporting remains OFF", lockFile, StringComparison.OrdinalIgnoreCase);
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
