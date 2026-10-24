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

    [Fact]
    public void ManualPortablePublish_UsesSameStepPathBeforeExportingToSubsequentSteps()
    {
        var workflow = Read(".github/workflows/release-windows.yml");
        var start = workflow.IndexOf("- name: Publish real portable single EXE for manual fallback", StringComparison.Ordinal);
        var end = workflow.IndexOf("- name: Stage unified release regression evidence", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var step = workflow[start..end];

        // GitHub writes to GITHUB_ENV take effect only in the next Actions step.
        // Looking up $env:RELEASE_PORTABLE_PATH before then made the manual
        // release fail after a successful, expensive portable compilation.
        var assign = step.IndexOf("$releasePortable = Join-Path $env:GITHUB_WORKSPACE", StringComparison.Ordinal);
        var exists = step.IndexOf("Test-Path $releasePortable -PathType Leaf", StringComparison.Ordinal);
        var hash = step.IndexOf("Get-FileHash $releasePortable -Algorithm SHA256", StringComparison.Ordinal);
        var exportPath = step.IndexOf("\"RELEASE_PORTABLE_PATH=$releasePortable\" | Out-File $env:GITHUB_ENV", StringComparison.Ordinal);
        var exportSha = step.IndexOf("\"RELEASE_CANONICAL_PORTABLE_SHA256=$manualPortableSha\" | Out-File $env:GITHUB_ENV", StringComparison.Ordinal);
        Assert.True(assign >= 0 && exists > assign && hash > exists &&
                    exportPath > hash && exportSha > exportPath);
        Assert.DoesNotContain("Get-FileHash $env:RELEASE_PORTABLE_PATH", step);

        var smoke = workflow.IndexOf("- name: Smoke-test exact release portable single EXE", StringComparison.Ordinal);
        Assert.True(smoke > end);
        Assert.Contains("$exe = $env:RELEASE_PORTABLE_PATH", workflow[smoke..]);
        Assert.Contains("$actualSha -ne $env:RELEASE_CANONICAL_PORTABLE_SHA256", workflow[smoke..]);
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
