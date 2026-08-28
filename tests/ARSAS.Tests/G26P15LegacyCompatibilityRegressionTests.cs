namespace ARSAS.Tests;

public sealed class G26P15LegacyCompatibilityRegressionTests
{
    [Fact]
    public void GuardedRuntime_UsesTypedAriecSubsetCompatibilityWithoutRewritingStoredReportKind()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");

        Assert.Contains("DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.TryResolve", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.Contains("MmsGuardedDynamicReportLegacySubsetRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("InformationReportProof.Kind == ArMms.MmsDynamicInformationReportKind.DataChange", guarded, StringComparison.Ordinal);
        Assert.Contains("No in-memory DataChange rewrite is performed", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("TryBuildCompatibleContext", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("InformationReportProof = load.Profile.InformationReportProof with", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRegistry_IsExactSixMemberPersistedChainPlusTwoMemberDchgSubset()
    {
        var registry = Read("Services/DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.cs");

        Assert.Contains("ied:AA1C1F08R4", registry, StringComparison.Ordinal);
        Assert.Contains("sha256:50c691318c6d6a16b68b121ac48627c26e6e32b937836d559dca1b9eb559f0d9", registry, StringComparison.Ordinal);
        Assert.Contains("e5f7fe9b93524f8019ff7cd01f042fc1827ef32e8b930262a2eafbf20ef357c0", registry, StringComparison.Ordinal);
        Assert.Contains("AA1C1F08R4ADD/LLN0.RP.A_URCB01", registry, StringComparison.Ordinal);
        Assert.Contains("ExpectedPersistedMemberReferences", registry, StringComparison.Ordinal);
        Assert.Contains("ExpectedDataChangeSubsetMemberReferences", registry, StringComparison.Ordinal);
        Assert.Contains("AA1C1F08R4Q0/CSWI1$ST$Beh$stVal", registry, StringComparison.Ordinal);
        Assert.Contains("AA1C1F08R4Q0/CSWI1$ST$Health$stVal", registry, StringComparison.Ordinal);
        Assert.Contains("AA1C1F08R4Q0/CSWI1$ST$Loc$stVal", registry, StringComparison.Ordinal);
        Assert.Contains("AA1C1F08R4Q0/CSWI1$ST$LocKey$stVal", registry, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicInformationReportKind.GeneralInterrogation", registry, StringComparison.Ordinal);
        Assert.Contains("ExactSequence(activation.MemberReferences, ExpectedPersistedMemberReferences)", registry, StringComparison.Ordinal);
        Assert.Contains("ExactSequence(report.MemberReferences, ExpectedPersistedMemberReferences)", registry, StringComparison.Ordinal);
        Assert.Contains("ExactSequence(envelope.ExactProvenMemberReferences, ExpectedPersistedMemberReferences)", registry, StringComparison.Ordinal);
        Assert.Contains("IsOrderedSubset(ExpectedDataChangeSubsetMemberReferences, report.MemberReferences)", registry, StringComparison.Ordinal);
        Assert.Contains("IsOrderedSubset(ExpectedDataChangeSubsetMemberReferences, envelope.ExactProvenMemberReferences)", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyRegistry_RecordsOnlyReviewedNoGiDchgSubsetAndCleanupFacts()
    {
        var registry = Read("Services/DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.cs");

        Assert.Contains("ActualInformationReportReceived = true", registry, StringComparison.Ordinal);
        Assert.Contains("DataChangeReasonVerified = true", registry, StringComparison.Ordinal);
        Assert.Contains("GeneralInterrogationDisabled = true", registry, StringComparison.Ordinal);
        Assert.Contains("ExactMemberMappingVerified = true", registry, StringComparison.Ordinal);
        Assert.Contains("AssociationHealthyAfterReport = true", registry, StringComparison.Ordinal);
        Assert.Contains("CleanupSucceeded = true", registry, StringComparison.Ordinal);
        Assert.Contains("included DataSet indexes [0,1]", registry, StringComparison.Ordinal);
        Assert.Contains("AR_G25A_4E20EC7E", registry, StringComparison.Ordinal);
        Assert.Contains("MemberReferences = ExpectedDataChangeSubsetMemberReferences", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void LegacyCompatibility_DoesNotChangeProductionCertificationBoundary()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var registry = Read("Services/DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.cs");
        var bridge = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("ProductionEligible certification remains separate", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkProductionEligible(", bridge, StringComparison.Ordinal);
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
