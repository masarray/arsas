namespace ARSAS.Tests;

public sealed class SmartDiscoveryGoldenProvenanceRegressionTests
{
    [Fact]
    public void P05e_WriterReverifiesRawCaptureAndBindsExactBuildAndTarget()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/new-smart-discovery-golden-lock.ps1"));

        Assert.Contains("verify-smart-discovery-pcap.ps1", source, StringComparison.Ordinal);
        Assert.Contains("Assert-EquivalentWireProof", source, StringComparison.Ordinal);
        Assert.Contains("Production golden lock requires a raw .pcap or .pcapng capture", source, StringComparison.Ordinal);
        Assert.Contains("BuildManifestPath", source, StringComparison.Ordinal);
        Assert.Contains("ARSAS commit:", source, StringComparison.Ordinal);
        Assert.Contains("ARIEC61850 commit:", source, StringComparison.Ordinal);
        Assert.Contains("BuildManifestSha256", source, StringComparison.Ordinal);
        Assert.Contains("SemanticTargetSha256", source, StringComparison.Ordinal);
        Assert.Contains("RawCaptureReverified", source, StringComparison.Ordinal);
        Assert.Contains("AllowFixtureEvidence", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P05e_VerifierRequiresExactArsasEngineAndSemanticTargetByDefault()
    {
        var source = File.ReadAllText(FindRepoFile("scripts/verify-smart-discovery-golden-lock.ps1"));

        Assert.Contains("CandidateArsasCommit", source, StringComparison.Ordinal);
        Assert.Contains("CandidateEngineCommit", source, StringComparison.Ordinal);
        Assert.Contains("Candidate ARSAS commit differs from the golden build baseline", source, StringComparison.Ordinal);
        Assert.Contains("Candidate engine commit differs from the golden engine baseline", source, StringComparison.Ordinal);
        Assert.Contains("Semantic target hash differs from the golden lock authority", source, StringComparison.Ordinal);
        Assert.Contains("BuildManifestSha256", source, StringComparison.Ordinal);
        Assert.Contains("AllowDifferentArsasCommit", source, StringComparison.Ordinal);
        Assert.Contains("AllowDifferentEngineCommit", source, StringComparison.Ordinal);
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
