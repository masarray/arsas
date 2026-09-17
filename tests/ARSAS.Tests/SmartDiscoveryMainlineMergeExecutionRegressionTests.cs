using System.Text.Json;

namespace ARSAS.Tests;

public sealed class SmartDiscoveryMainlineMergeExecutionRegressionTests
{
    [Fact]
    public void P05h_TargetLocksOrderedMergeCommitExecution()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("evidence/smart-discovery-mainline-merge-target.json")));
        var root = document.RootElement;

        Assert.Equal("P0-5h", root.GetProperty("Phase").GetString());
        Assert.Equal("merge", root.GetProperty("MergeMethod").GetString());
        Assert.Equal(new[] { "engine", "arsas" }, root.GetProperty("MergeOrder").EnumerateArray().Select(x => x.GetString()).ToArray());

        var contract = root.GetProperty("ExecutionContract");
        Assert.True(contract.GetProperty("RequireP05gReadyForReview").GetBoolean());
        Assert.True(contract.GetProperty("RequireExactExpectedHeadShaOnMerge").GetBoolean());
        Assert.True(contract.GetProperty("RequireBaseShaUnchangedFromMergeManifest").GetBoolean());
        Assert.True(contract.GetProperty("RequireEngineMergeBeforeArsasMerge").GetBoolean());
        Assert.True(contract.GetProperty("RequireMergeCommitMethod").GetBoolean());
        Assert.True(contract.GetProperty("ForbidFixtureOrForceBypass").GetBoolean());
    }

    [Fact]
    public void P05h_ManifestWriterHasNoBypassAndRequiresProductionAuthorities()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-mainline-merge-manifest.ps1"));

        Assert.Contains("READY_FOR_REVIEW", source, StringComparison.Ordinal);
        Assert.Contains("physical-finalized", source, StringComparison.Ordinal);
        Assert.Contains("production-promoted", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedHeadSha", source, StringComparison.Ordinal);
        Assert.Contains("ExpectedBaseSha", source, StringComparison.Ordinal);
        Assert.Contains("MergeMethod = 'merge'", source, StringComparison.Ordinal);
        Assert.Contains("engine-merge-first", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowFixtureEvidence", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Force", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05h_PostMergeVerifierRequiresValidatedHeadsOnMainAndAuthorityBinding()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-post-merge-production.ps1"));

        Assert.Contains("merge-base --is-ancestor", source, StringComparison.Ordinal);
        Assert.Contains("Validated engine PR head is not an ancestor of engine main", source, StringComparison.Ordinal);
        Assert.Contains("Validated ARSAS PR head is not an ancestor of ARSAS main", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryProductionPromoted", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryPromotionAuthoritySha256", source, StringComparison.Ordinal);
        Assert.Contains("SmartDiscoveryValidatedEngineHead", source, StringComparison.Ordinal);
        Assert.Contains("P0-5h-post-merge", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05g_PostPhysicalAllowlistPermitsOnlyTheP05hManifestAddition()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("evidence/smart-discovery-production-promotion-target.json")));
        var paths = document.RootElement.GetProperty("AllowedPostPhysicalAuthorityPaths")
            .EnumerateArray().Select(x => x.GetString()).ToArray();

        Assert.Contains("evidence/smart-discovery-mainline-merge-manifest.json", paths);
        Assert.DoesNotContain(paths, path => path is not null && path.StartsWith("Services/", StringComparison.Ordinal));
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
