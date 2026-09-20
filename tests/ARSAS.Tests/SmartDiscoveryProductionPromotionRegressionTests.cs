using System.Text.Json;
using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class SmartDiscoveryProductionPromotionRegressionTests
{
    private const string EvidenceEngineCommit = "4467124775d8d9d76f3db194f9fbfd97144767a8";

    [Fact]
    public void P05g_TargetKeepsProductionPromotionFailClosed()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("evidence/smart-discovery-production-promotion-target.json")));
        var root = document.RootElement;

        Assert.Equal(2, root.GetProperty("SchemaVersion").GetInt32());
        Assert.Equal("P0-5g", root.GetProperty("Phase").GetString());
        Assert.Equal(EvidenceEngineCommit, root.GetProperty("EvidenceEngineBaselineCommit").GetString());
        Assert.Equal(134, root.GetProperty("EnginePullRequest").GetInt32());
        Assert.Equal(324, root.GetProperty("ArsasPullRequest").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("PromotionAuthority").ValueKind);

        var contract = root.GetProperty("ProductionPromotionContract");
        Assert.True(contract.GetProperty("RequireP05fPhysicalAuthority").GetBoolean());
        Assert.True(contract.GetProperty("RequirePhysicalAuthorityProductionEvidenceOnly").GetBoolean());
        Assert.True(contract.GetProperty("RequireEngineHeadCiSuccess").GetBoolean());
        Assert.True(contract.GetProperty("AllowEngineHeadDescendantWhenCriticalDiscoveryPathsUnchanged").GetBoolean());
        Assert.True(contract.GetProperty("RequireProductionSwitchFalseUntilAuthority").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactPromotionAuthoritySha256Binding").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactValidatedEngineHeadBinding").GetBoolean());
        Assert.True(contract.GetProperty("RequireGenericBuildSuccessBeforeReadyForReview").GetBoolean());
        Assert.True(contract.GetProperty("RequireNoUnresolvedReviewThreadsBeforeReadyForReview").GetBoolean());
        Assert.True(contract.GetProperty("RequireDedicatedMainlineReadinessGateSuccess").GetBoolean());
        Assert.True(contract.GetProperty("RequirePrRemainDraftUntilAllReadyGatesPass").GetBoolean());
    }

    [Fact]
    public void ProductionBuildTracksPhysicalProvenSmartRouteForEveryPackagingLane()
    {
        var native = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.cs"));
        Assert.Contains("if (SmartDiscoveryCaptureModeEnabled)", native, StringComparison.Ordinal);
        Assert.Contains("return await DiscoverSignalsSmartForCaptureAsync(cancellationToken, progress).ConfigureAwait(false);", native, StringComparison.Ordinal);
        Assert.Contains("__P0_5C_CONNECT_RESET__", native, StringComparison.Ordinal);
        Assert.Contains("__P0_5C_DISPOSE_RESET__", native, StringComparison.Ordinal);
        Assert.Contains("_lastDiscovery.Snapshot.DomainVariables", native, StringComparison.Ordinal);

        var targets = File.ReadAllText(FindRepoFile("Directory.Build.targets"));
        Assert.DoesNotContain("GITHUB_WORKFLOW", targets, StringComparison.Ordinal);
        Assert.DoesNotContain("SmartDiscoveryProductionPromoted", targets, StringComparison.Ordinal);
        Assert.Contains("VerifyPhysicalProvenSmartDiscoveryRoute", targets, StringComparison.Ordinal);
        Assert.Contains("-VerifyOnly", targets, StringComparison.Ordinal);

        var verifier = File.ReadAllText(FindRepoFile("scripts/enable-smart-discovery-capture.ps1"));
        Assert.Contains("Production smart discovery route verification passed", verifier, StringComparison.Ordinal);
        Assert.Contains("no longer permitted to be inserted only at build time", verifier, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_ReadinessAllowsMissingPromotionBindingsWhileFailClosed()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-production-readiness.ps1"));

        Assert.Contains("Get-XmlChildText", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$group.SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.DoesNotContain("$group.SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
    }

    [Fact]
    public void R10_DedicatedMainlineGateRequiresPhysicalAuthorityAndExactMergedEngine()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github/workflows/smart-discovery-mainline-readiness.yml"));

        Assert.Contains("Smart Discovery Mainline Readiness", workflow, StringComparison.Ordinal);
        Assert.Contains("physical-retest-passed-merge-ready", workflow, StringComparison.Ordinal);
        Assert.Contains("physicalRetestPassed", workflow, StringComparison.Ordinal);
        Assert.Contains("projectionErrors", workflow, StringComparison.Ordinal);
        Assert.Contains("cacheLoss", workflow, StringComparison.Ordinal);
        Assert.Contains("actualInformationReportObserved", workflow, StringComparison.Ordinal);
        Assert.Contains("engineModelAndScl.mainMerge", workflow, StringComparison.Ordinal);
        Assert.Contains("physicalTestedCommit", workflow, StringComparison.Ordinal);
        Assert.Contains("mergedMainTree", workflow, StringComparison.Ordinal);
        Assert.Contains("Checkout exact merged ARIEC61850 authority", workflow, StringComparison.Ordinal);
        Assert.Contains("Build and test ARSAS candidate", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("perf/smart-ied-discovery", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("EvidenceEngineBaselineCommit", workflow, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_ReadinessBindsAllPromotionProvenance()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-production-readiness.ps1"));

        Assert.Contains("P0-5f physical-finalized authority is missing", source, StringComparison.Ordinal);
        Assert.Contains("Get-PhysicalAuthorityProvenanceErrors", source, StringComparison.Ordinal);
        Assert.Contains("at least three independent associations", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("contains reused", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("merge-base --is-ancestor", source, StringComparison.Ordinal);
        Assert.Contains("DiscoveryCriticalEnginePaths", source, StringComparison.Ordinal);
        Assert.Contains("Engine PR head CI is not green", source, StringComparison.Ordinal);
        Assert.Contains("AllowedPostPhysicalAuthorityPaths", source, StringComparison.Ordinal);
        Assert.Contains("READY_TO_PROMOTE", source, StringComparison.Ordinal);
        Assert.Contains("READY_FOR_REVIEW", source, StringComparison.Ordinal);
        Assert.Contains("production-promoted", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.Contains("Production promotion props are bound to a different P0-5g promotion authority", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
        Assert.Contains("Production promotion props are bound to a different validated engine head", source, StringComparison.Ordinal);
        Assert.Contains("promotion authority is bound to a different promotion target", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("promotion authority is bound to a different engine lock", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PromotionTargetSha256", source, StringComparison.Ordinal);
        Assert.Contains("EngineLockSha256", source, StringComparison.Ordinal);
        Assert.Contains("SchemaVersion = 4", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_PromotionWriterHasNoFixtureBypassAndRequiresProductionPhysicalProvenance()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-production-promotion-authority.ps1"));

        Assert.Contains("READY_TO_PROMOTE", source, StringComparison.Ordinal);
        Assert.Contains("Assert-PhysicalAuthorityProvenance", source, StringComparison.Ordinal);
        Assert.Contains("at least three independent associations", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GoldenLockSha256", source, StringComparison.Ordinal);
        Assert.Contains("RepeatTargetSha256", source, StringComparison.Ordinal);
        Assert.Contains("FinalizationSha256", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryProductionPromoted>true", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
        Assert.Contains("P0-5g-authority", source, StringComparison.Ordinal);
        Assert.Contains("SchemaVersion = 2", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowFixtureEvidence", source, StringComparison.Ordinal);
    }

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

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}'.");
    }
}
