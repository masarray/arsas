namespace ARSAS.Tests;

public sealed class StableReleaseImmutabilityRegressionTests
{
    [Fact]
    public void CanonicalRelease_RespectsManualPublishOptOut_AndDoesNotClobberExistingAssets()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github/workflows/release-windows.yml"));

        Assert.Contains("github.event_name == 'workflow_dispatch' && inputs.publish_release == true", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'push'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release upload", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("--clobber", workflow, StringComparison.Ordinal);
        Assert.Contains("rev-list -n 1 $env:RELEASE_TAG", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release download $env:RELEASE_TAG", workflow, StringComparison.Ordinal);
        Assert.Contains("Get-FileHash -LiteralPath $existingAsset -Algorithm SHA256", workflow, StringComparison.Ordinal);
        Assert.Contains("RELEASE_ALREADY_PUBLISHED=true", workflow, StringComparison.Ordinal);
        Assert.Contains("env.RELEASE_ALREADY_PUBLISHED != 'true'", workflow, StringComparison.Ordinal);
        Assert.Contains("use a new version/tag", workflow, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AlternateVerifiedPublisher_RefusesToReplaceAnExistingStableTag()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github/workflows/publish-verified-release.yml"));

        Assert.DoesNotContain("--clobber", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release upload", workflow, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite existing stable release", workflow, StringComparison.Ordinal);
        Assert.Contains("gh release create", workflow, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Cannot locate repository file: {relativePath}");
    }
}
