using System.IO;

namespace ARSAS.Tests;

/// <summary>
/// Prevent a public RC preview from becoming the production updater's
/// latest stable release or mutating stable publication evidence.
/// </summary>
public sealed class PublicPrereleaseIsolationRegressionTests
{
    [Fact]
    public void ManualRcRelease_IsAlwaysPrerelease_NeverLatestAndNeverWritesStableLedger()
    {
        var workflow = Read(".github/workflows/release-windows.yml");
        var create = workflow.IndexOf("- name: Create or update GitHub Release", StringComparison.Ordinal);
        var ledger = workflow.IndexOf("- name: Record verified release publication", StringComparison.Ordinal);
        Assert.True(create >= 0 && ledger > create);

        var publish = workflow[create..ledger];
        Assert.Contains("$isStagingManual = $env:GITHUB_EVENT_NAME -eq \"workflow_dispatch\"", publish);
        Assert.Contains("$env:GITHUB_REF -ne \"refs/heads/main\"", publish);
        Assert.Contains("$isPreview = $env:RELEASE_VERSION.Contains(\"-\") -or $isStagingManual", publish);
        Assert.Contains("if ($isPreview)", publish);
        Assert.Contains("$arguments += \"--prerelease\"", publish);
        Assert.Contains("else {\n            $arguments += \"--latest\"\n          }", publish);
        Assert.DoesNotContain("\"--generate-notes\",\n            \"--latest\"", publish);
        Assert.Equal(1, publish.Split("$arguments += \"--latest\"").Length - 1);
        Assert.Contains("inputs.publish_release == true", publish);

        var stable = workflow[ledger..];
        Assert.Contains("github.ref == 'refs/heads/main'", stable);
        Assert.Contains("inputs.prerelease != true", stable);
        Assert.Contains("!contains(steps.release.outputs.version, '-')", stable);
        Assert.Contains("channel = \"stable\"", stable);
        Assert.Contains("branch = \"main\"", stable);
        Assert.Contains("contents/.release/published.json", stable);
    }

    private static string Read(string file)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var path = Path.Combine(dir.FullName, file);
            if (File.Exists(path)) return File.ReadAllText(path).Replace("\r\n", "\n");
        }
        throw new FileNotFoundException(file);
    }
}
