namespace ARSAS.Tests;

/// <summary>
/// Prevents one-off historical recovery jobs from regaining release-write authority.
/// Their evidence JSON is retained; published v1.6.40 assets are never rewritten.
/// </summary>
public sealed class LegacyReleaseWorkflowRetirementTests
{
    [Theory]
    [InlineData(".github/workflows/build-v1.6.38-golden-installer.yml")]
    [InlineData(".github/workflows/publish-v1.6.38-golden-installer.yml")]
    [InlineData(".github/workflows/recover-v1.6.38-golden.yml")]
    public void OneOffHistoricalWorkflow_IsNotAnActiveActionsEntrypoint(string relativePath)
        => Assert.False(File.Exists(RepositoryPath(relativePath)), relativePath);

    [Theory]
    [InlineData(".release/recover-v1.6.38-golden.json")]
    [InlineData(".release/recover-v1.6.38-golden-installer.json")]
    [InlineData("evidence/v1.6.39-physical-rejection.json")]
    [InlineData("evidence/v1.6.40-installed-release-field-verification.json")]
    public void HistoricalAcceptanceOrRejectionEvidence_IsPreserved(string relativePath)
        => Assert.True(File.Exists(RepositoryPath(relativePath)), relativePath);

    [Fact]
    public void ActiveReleasePublishers_DoNotClobberPublishedAssets()
    {
        var canonical = File.ReadAllText(RepositoryPath(".github/workflows/release-windows.yml"));
        var verified = File.ReadAllText(RepositoryPath(".github/workflows/publish-verified-release.yml"));
        var backfill = File.ReadAllText(RepositoryPath(".github/workflows/release-supply-chain.yml"));

        Assert.Contains("RELEASE_ALREADY_PUBLISHED=true", canonical, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release upload", canonical, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite existing stable release", verified, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release upload", verified, StringComparison.Ordinal);
        Assert.Contains("refusing to replace published bytes", backfill, StringComparison.Ordinal);
        Assert.DoesNotContain("gh release delete-asset", backfill, StringComparison.Ordinal);
        Assert.DoesNotContain("--clobber", backfill, StringComparison.Ordinal);
    }

    private static string RepositoryPath(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArIED61850Tester.sln")))
                return Path.Combine(directory.FullName, relativePath);
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate ARSAS repository root.");
    }
}
