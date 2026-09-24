using System.Text.Json;

namespace ARSAS.Tests;

public sealed class InteroperabilityReferenceEvidenceRegressionTests
{
    [Fact]
    public void NeutralAuthority_PreservesOriginalPhysicalAcceptanceMetrics()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(FindRepositoryFile("evidence/interoperability-reference-target.json")));
        var root = document.RootElement;
        var reference = root.GetProperty("physicalReference");
        var capture = reference.GetProperty("externalReferenceCapture");
        var scl = reference.GetProperty("externalSclReference");

        Assert.Equal(32, reference.GetProperty("logicalDevices").GetInt32());
        Assert.Equal(119, reference.GetProperty("logicalNodes").GetInt32());
        Assert.Equal(2, reference.GetProperty("dataSets").GetInt32());
        Assert.Equal(1, capture.GetProperty("associations").GetInt32());
        Assert.Equal(417, capture.GetProperty("confirmedMmsRequests").GetInt32());
        Assert.Equal(119, capture.GetProperty("getVariableAccessAttributes").GetInt32());
        Assert.Equal(156, capture.GetProperty("reads").GetInt32());
        Assert.Equal(58, scl.GetProperty("fcda").GetInt32());

        var rejected = reference.GetProperty("rejectedLegacyArsasCapture");
        Assert.Equal(2, rejected.GetProperty("associations").GetInt32());
        Assert.Equal(30552, rejected.GetProperty("confirmedMmsRequests").GetInt32());

        var r10 = root.GetProperty("physicalEvidence").GetProperty("arsasR10");
        foreach (var edition in new[] { "reuseEdition1", "reuseEdition2" })
        {
            var evidence = r10.GetProperty(edition);
            Assert.Equal(58, evidence.GetProperty("staticMembers").GetInt32());
            Assert.Equal(58, evidence.GetProperty("reportBackedRuntimePoints").GetInt32());
            Assert.Equal(0, evidence.GetProperty("unresolvedRuntimePoints").GetInt32());
            Assert.Equal(0, evidence.GetProperty("projectionErrors").GetInt32());
            Assert.Equal(0, evidence.GetProperty("cyclicMmsProcessPolling").GetInt32());
            Assert.True(evidence.GetProperty("actualInformationReportObserved").GetBoolean());
        }
    }

    [Fact]
    public void ActiveReferenceAndOriginalHistoricalProvenance_AreBothDiscoverable()
    {
        var documentation = File.ReadAllText(
            FindRepositoryFile("docs/INTEROPERABILITY_REFERENCE_CONTRACT.md"));
        var workflow = File.ReadAllText(
            FindRepositoryFile(".github/workflows/interoperability-reference-guard.yml"));
        var sourceClean = File.ReadAllText(FindRepositoryFile("scripts/verify-source-clean.ps1"));

        Assert.Contains("36a4b87a3c2f6d34f73fa36c8a8a6a59763bd435", documentation, StringComparison.Ordinal);
        Assert.Contains("a853fd0ea115b320a648b1b2a52fe9a7f6af94cd", documentation, StringComparison.Ordinal);
        Assert.Contains("name: interoperability-reference-contract", workflow, StringComparison.Ordinal);
        Assert.Contains("externalReferenceCapture", workflow, StringComparison.Ordinal);
        Assert.Contains("evidence/interoperability-reference-target.json", workflow, StringComparison.Ordinal);
        Assert.Contains("evidence/interoperability-reference-target.json", documentation, StringComparison.Ordinal);
        Assert.DoesNotContain("ApprovedConvergenceIdentifierPaths", sourceClean, StringComparison.Ordinal);
        Assert.Contains("No tracked path receives a whole-file external-identifier exemption", sourceClean, StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string path)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null)
        {
            var candidate = Path.Combine(root.FullName, path);
            if (File.Exists(candidate))
                return candidate;
            root = root.Parent;
        }

        throw new FileNotFoundException(path);
    }
}
