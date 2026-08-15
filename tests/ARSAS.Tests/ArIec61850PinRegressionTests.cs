using System.Reflection;
using System.Text.Json;
using AR.Iec61850.Discovery;

namespace ARSAS.Tests;

public sealed class ArIec61850PinRegressionTests
{
    [Fact]
    public void LoadedEngineAssembly_MatchesArsasIntegrationLock()
    {
        var lockPath = FindRepoFile("engines/ARIEC61850.lock.json");
        using var document = JsonDocument.Parse(File.ReadAllText(lockPath));
        var expected = document.RootElement.GetProperty("commit").GetString();

        Assert.False(string.IsNullOrWhiteSpace(expected));

        var informationalVersion = typeof(Iec61850DataSetSignalInventoryProjection)
            .Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? string.Empty;

        Assert.Contains(expected!, informationalVersion, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ApplicationBuild_ValidatesSiblingEngineGitHeadBeforeProjectReferences()
    {
        var project = File.ReadAllText(FindRepoFile("ArIED61850Tester.csproj"));
        var validator = File.ReadAllText(FindRepoFile("scripts/validate-ariec61850-lock.ps1"));

        Assert.Contains("ValidateArIec61850Revision", project, StringComparison.Ordinal);
        Assert.Contains("BeforeTargets=\"ResolveProjectReferences\"", project, StringComparison.Ordinal);
        Assert.Contains("validate-ariec61850-lock.ps1", project, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rev-parse HEAD", validator, StringComparison.Ordinal);
        Assert.Contains("$actual -ne $expected", validator, StringComparison.Ordinal);
        Assert.Contains("refused to compile against an unpinned ARIEC61850 engine", validator, StringComparison.Ordinal);
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

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
