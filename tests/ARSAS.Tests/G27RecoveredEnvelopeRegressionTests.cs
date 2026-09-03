namespace ARSAS.Tests;

public sealed class G27RecoveredEnvelopeRegressionTests
{
    [Fact]
    public void P17_G23Recovery_RetainsLargestPriorCleanEnvelopeBeforeAnyFullRetry()
    {
        var source = Read("Services/DynamicReportQualificationFreshRecoveryCommissioningService.cs");

        Assert.Contains("LargestCleanupSafeMultiMemberAttempt", source, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicDataSetQualificationLadder.AcceptExactEnvelope", source, StringComparison.Ordinal);
        Assert.Contains("MmsDynamicReportQualificationProfilePolicy.CreateEnvelopeQualifiedProfile", source, StringComparison.Ordinal);
        Assert.Contains("retained the largest prior cleanup-safe multi-member envelope", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("later failed larger milestone is not generalized", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("SaveAsync", source, StringComparison.Ordinal);
        Assert.Contains("exactly one new G2.3 commissioning run", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no earlier cleanup-safe multi-member envelope", source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MarkProductionEligible(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P17_G23Recovery_PropagatesExactFieldEvidenceForDiagnosis()
    {
        var source = Read("Services/DynamicReportQualificationFreshRecoveryCommissioningService.cs");

        Assert.Contains("failedAttempt=", source, StringComparison.Ordinal);
        Assert.Contains("failureStage=", source, StringComparison.Ordinal);
        Assert.Contains("associationSurvived=", source, StringComparison.Ordinal);
        Assert.Contains("cleanupSucceeded=", source, StringComparison.Ordinal);
        Assert.Contains("namespaceAbsenceBefore=", source, StringComparison.Ordinal);
        Assert.Contains("directoryAbsenceBefore=", source, StringComparison.Ordinal);
        Assert.Contains("namespaceAbsenceAfter=", source, StringComparison.Ordinal);
        Assert.Contains("directoryAbsenceAfter=", source, StringComparison.Ordinal);
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
