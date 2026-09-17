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
        Assert.True(contract.GetProperty("RequireEngineHeadCiSuccess").GetBoolean());
        Assert.True(contract.GetProperty("AllowEngineHeadDescendantWhenCriticalDiscoveryPathsUnchanged").GetBoolean());
        Assert.True(contract.GetProperty("RequireProductionSwitchFalseUntilAuthority").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactPromotionAuthoritySha256Binding").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactValidatedEngineHeadBinding").GetBoolean());
        Assert.True(contract.GetProperty("RequireGenericBuildSuccessBeforeReadyForReview").GetBoolean());
        Assert.True(contract.GetProperty("RequireNoUnresolvedReviewThreadsBeforeReadyForReview").GetBoolean());
        Assert.True(contract.GetProperty("RequirePrRemainDraftUntilAllReadyGatesPass").GetBoolean());
    }

    [Fact]
    public void P05g_DefaultBuildDoesNotPromoteFieldRoute()
    {
        var props = XDocument.Load(FindRepoFile("evidence/SmartDiscoveryPromotion.props"));
        var promoted = props.Descendants("SmartDiscoveryProductionPromoted").Single().Value.Trim();
        Assert.Equal("false", promoted, ignoreCase: true);

        var targets = File.ReadAllText(FindRepoFile("Directory.Build.targets"));
        Assert.Contains("GITHUB_WORKFLOW", targets, StringComparison.Ordinal);
        Assert.Contains("Smart Discovery Field Capture Build", targets, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryProductionPromoted", targets, StringComparison.Ordinal);
        Assert.Contains("EnableSmartDiscoveryCaptureRoute", targets, StringComparison.Ordinal);
        Assert.Contains(">false</EnableSmartDiscoveryCaptureRoute>", targets, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_ReadinessBindsPhysicalAuthorityEvidenceCompatibleEngineAndExactPromotionProps()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-production-readiness.ps1"));

        Assert.Contains("P0-5f physical-finalized authority is missing", source, StringComparison.Ordinal);
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
        Assert.Contains("SchemaVersion = 2", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_PromotionWriterHasNoFixtureBypassAndRequiresReadyProof()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-production-promotion-authority.ps1"));

        Assert.Contains("READY_TO_PROMOTE", source, StringComparison.Ordinal);
        Assert.Contains("physical-finalized P0-5f authority", source, StringComparison.Ordinal);
        Assert.Contains("EngineHeadIsEvidenceCompatibleDescendant", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryProductionPromoted>true", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
        Assert.Contains("P0-5g-authority", source, StringComparison.Ordinal);
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
