using System.Text.Json;

namespace ARSAS.Tests;

public sealed class QualifiedReleaseSourceRegressionTests
{
    [Fact]
    public void QualificationManifest_LocksExactFieldTestedLineageAndArtifact()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(FindRepoFile(".release/qualified-source.json")));
        var root = document.RootElement;
        var source = root.GetProperty("fieldQualifiedSource");
        var candidate = root.GetProperty("currentReleaseCandidate");

        Assert.Equal(1, root.GetProperty("schemaVersion").GetInt32());
        Assert.Equal(324, source.GetProperty("sourcePullRequest").GetInt32());
        Assert.Equal("b0f25569a398c7bbdc3a3a34409dbd74ebf49444", source.GetProperty("sourceHeadCommit").GetString());
        Assert.Equal("2b8e8adfd019bd087dc338dcc32bffefec53370f", source.GetProperty("sourceMergeCommit").GetString());
        Assert.Equal("cccb50607161f8637e597b43d71038ffd4becfdc", source.GetProperty("sourceTree").GetString());
        Assert.Equal(35412542175, source.GetProperty("workflowRunId").GetInt64());
        Assert.Equal(10575075419, source.GetProperty("artifactId").GetInt64());
        Assert.Equal("6805b878f91f3d526cc50f3e81e3eb9a1f72500217dacc8921e50bdc349c5fcc", source.GetProperty("portableSha256").GetString());
        Assert.Equal(78383289, source.GetProperty("portableSizeBytes").GetInt64());
        Assert.Matches("^[0-9a-f]{64}$", candidate.GetProperty("releaseSensitiveFingerprint").GetString()!);
        Assert.True(candidate.GetProperty("releaseSensitiveFileCount").GetInt32() > 100);
    }

    [Fact]
    public void ReleaseWorkflow_VerifiesQualificationAndNeverPublishesFromAnOrdinaryMainPush()
    {
        var workflow = File.ReadAllText(FindRepoFile(".github/workflows/release-windows.yml"));
        var windowsManifest = File.ReadAllText(FindRepoFile(".release/windows.json"));

        Assert.Contains("verify-qualified-release-source.py", workflow, StringComparison.Ordinal);
        Assert.Contains("github.event_name == 'workflow_dispatch'", workflow, StringComparison.Ordinal);
        Assert.Contains("inputs.publish_release == true", workflow, StringComparison.Ordinal);
        Assert.Contains("github.ref == 'refs/heads/main'", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("github.ref_type == 'tag' || github.ref == 'refs/heads/main'", workflow, StringComparison.Ordinal);
        Assert.Contains("\"qualificationManifest\": \".release/qualified-source.json\"", windowsManifest, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file was not found: {relativePath}");
    }
}
