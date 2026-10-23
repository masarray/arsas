namespace ARSAS.Tests;

public sealed class SmartDiscoverySemanticEnrichmentRegressionTests
{
    [Fact]
    public void SmartCapture_KeepsDataSetAndReportEnrichmentEnabled()
    {
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));

        Assert.Contains("ProbeReportAttributes = true", capture, StringComparison.Ordinal);
        Assert.Contains("MaxReportAttributeProbes = 64", capture, StringComparison.Ordinal);
        Assert.Contains("ReadDataSetDirectories = true", capture, StringComparison.Ordinal);
        Assert.Contains("MaxDataSetDirectoryReads = 64", capture, StringComparison.Ordinal);

        Assert.DoesNotContain("ProbeReportAttributes = false", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadDataSetDirectories = false", capture, StringComparison.Ordinal);
        Assert.DoesNotContain("eager report attributes, DataSet directories", capture, StringComparison.Ordinal);
    }

    [Fact]
    public void SmartCapture_StillDefersOnlyBroadFallbackPasses()
    {
        var capture = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));

        Assert.Contains(
            "Deferred: supplemental GetNameList, reflection fallback, adaptive sibling/equipment/reference/unit probes.",
            capture,
            StringComparison.Ordinal);
        Assert.Contains("association flight=single-owner", capture, StringComparison.Ordinal);
        Assert.Contains("app MMS gate=exclusive", capture, StringComparison.Ordinal);
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
