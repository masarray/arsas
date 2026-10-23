namespace ARSAS.Tests;

/// <summary>
/// Locks the CI build to the event's immutable revision. A moving PR branch must
/// never silently change which application source is built, tested or packaged.
/// </summary>
public sealed class ExactCiSourceRevisionContractTests
{
    [Fact]
    public void WindowsBuild_ChecksOutAndVerifiesTheExactTriggerCommit()
    {
        var workflow = File.ReadAllText(FindRepositoryFile(".github/workflows/build.yml"));

        Assert.Contains("ref: ${{ github.sha }}", workflow, StringComparison.Ordinal);
        Assert.Contains("path: ArIED61850Tester", workflow, StringComparison.Ordinal);
        Assert.Contains("persist-credentials: false", workflow, StringComparison.Ordinal);
        Assert.Contains("git -C .\\ArIED61850Tester rev-parse HEAD", workflow, StringComparison.Ordinal);
        Assert.Contains("$env:GITHUB_SHA.Trim().ToLowerInvariant()", workflow, StringComparison.Ordinal);
        Assert.Contains("CI source revision mismatch", workflow, StringComparison.Ordinal);
        Assert.Contains("ARSAS_SOURCE_COMMIT=$actual", workflow, StringComparison.Ordinal);
        Assert.DoesNotContain("git clone --quiet --depth 1 --branch $ref", workflow, StringComparison.Ordinal);
        Assert.Contains("dotnet test .\\ArIED61850Tester\\tests\\ARSAS.Tests", workflow, StringComparison.Ordinal);
        Assert.Contains("scripts\\publish-windows-portable.ps1", workflow, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Repository file not found: {relativePath}");
    }
}
