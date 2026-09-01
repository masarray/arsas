namespace ARSAS.Tests;

public sealed class G26P16GeneralDynamicRuntimeRegressionTests
{
    [Fact]
    public void LegacyFieldWitness_IsCapabilityProof_NotPermanentMemberWhitelist()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");

        Assert.Contains("Q0/A3 proves capability, not member scope", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MmsGuardedDynamicReportFieldCapabilityStableRuntimePlanner.Build", guarded, StringComparison.Ordinal);
        Assert.Contains("every still-uncovered exact-resolved selected signal", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("MmsGuardedDynamicReportLegacySubsetRuntimePlanner.Build", guarded, StringComparison.Ordinal);
    }

    [Fact]
    public void P16_StillRequiresExactReviewedWitnessAtPlanningAndRevalidation()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");

        Assert.True(Count(guarded, "DynamicReportGuardedLegacyCompatibilityEvidenceRegistry.TryResolve") >= 2);
        Assert.Contains("MmsGuardedDynamicReportFieldCapabilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsGuardedDynamicReportLegacySubsetCompatibilityPolicy.TryValidate", guarded, StringComparison.Ordinal);
        Assert.Contains("same field-capability policy that", guarded, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("freshly verified free RCBs", guarded, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P16_PlanningDiagnosticsExposeGeneralDynamicGroupsForFieldEvidence()
    {
        var runtime = Read("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("Dynamic groups=", runtime, StringComparison.Ordinal);
        Assert.Contains("dynamic signals=", runtime, StringComparison.Ordinal);
        Assert.Contains("MMS fallback=", runtime, StringComparison.Ordinal);
        Assert.Contains("Q0/A3 is capability proof, not a permanent member whitelist", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("P1.6 dynamic group", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DataSet={segment.DataSetReference}", runtime, StringComparison.Ordinal);
        Assert.Contains("members={segment.Signals.Count}", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("Only the exact proven RCB/member envelope may be mutated", runtime, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("runtime=InformationReportProven exact envelope", runtime, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P16_EngineLockPinsGeneralRuntimeAndStableMultiRcbIdentity()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.Contains("4d7a896c606194c5533322bf975a2c9c57da7c64", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"sourcePullRequest\": 105", engineLock, StringComparison.Ordinal);
        Assert.Contains("PR #104", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("all still-uncovered exact-resolved selected signals", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #105", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("deterministic AR_HYB_<SHA256-prefix>", engineLock, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("MMS polling only for genuine residuals", engineLock, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void P16_DoesNotCrossProductionCertificationBoundary()
    {
        var guarded = Read("Services/NativeIec61850Client.HybridReporting.GuardedRuntime.cs");
        var engineLock = Read("engines/ARIEC61850.lock.json");

        Assert.DoesNotContain("MarkProductionEligible(", guarded, StringComparison.Ordinal);
        Assert.DoesNotContain("SaveAsync(", guarded, StringComparison.Ordinal);
        Assert.Contains("ProductionEligible certification remains separate", guarded, StringComparison.Ordinal);
        Assert.Contains("ProductionEligible as a separate certification boundary", engineLock, StringComparison.OrdinalIgnoreCase);
    }

    private static int Count(string source, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = source.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
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
