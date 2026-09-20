using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Export;

namespace ARSAS.Tests;

public sealed class SclExportOnlySemanticIsolationRegressionTests
{
    [Fact]
    public void SaveScl_CorrectsCounterMetadataWithoutMutatingTheLiveRuntimeModel()
    {
        var model = BuildModel();

        var liveCounter = Assert.Single(
            model.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects));
        Assert.Equal("SPS", liveCounter.InferredCdc);

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model" });

        var ns = document.Root!.Name.Namespace;
        var lNodeType = Assert.Single(
            document.Descendants(ns + "LNodeType"),
            element => (string?)element.Attribute("lnClass") == "GGIO");
        var dataObject = Assert.Single(
            lNodeType.Elements(ns + "DO"),
            element => (string?)element.Attribute("name") == "CBClsCounter");
        var doTypeId = (string?)dataObject.Attribute("type") ?? string.Empty;
        var doType = Assert.Single(
            document.Descendants(ns + "DOType"),
            element => (string?)element.Attribute("id") == doTypeId);

        Assert.Equal("INS", (string?)doType.Attribute("cdc"));
        Assert.Equal(
            "INT32",
            (string?)Assert.Single(
                doType.Elements(ns + "DA"),
                element => (string?)element.Attribute("name") == "stVal")
                .Attribute("bType"));

        Assert.Equal("SPS", liveCounter.InferredCdc);

        var reopened = SclLiveModelProjectionBuilder.Build(document, "generated.iid");
        var reopenedCounter = Assert.Single(
            reopened.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects),
            item => item.Name == "CBClsCounter");

        Assert.Equal("INS", reopenedCounter.InferredCdc);
        Assert.Equal(
            "INT32",
            Assert.Single(
                reopenedCounter.Attributes,
                attribute => attribute.AttributePath == "stVal").SclBType);
    }

    [Fact]
    public void CandidateEvidence_RequiresZeroDiscoveryRuntimeSourceDrift()
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(FindRepoFile("evidence/scl-export-only-semantic-candidate.json")));
        var replacement = document.RootElement.GetProperty("replacementCandidate");

        Assert.True(replacement.GetProperty("discoveryRuntimeSourceMustMatchPhysicalBaseline").GetBoolean());
        Assert.False(replacement.GetProperty("liveDiscoveryModelMutationAllowed").GetBoolean());
        Assert.False(replacement.GetProperty("acquisitionBehaviorChangeAllowed").GetBoolean());

        var allowed = replacement.GetProperty("allowedEngineDiff")
            .EnumerateArray()
            .Select(item => item.GetString())
            .ToArray();
        Assert.Equal(3, allowed.Length);
        Assert.Contains("src/AR.Iec61850/Scl/Export/LiveIedSclExporter.cs", allowed);
        Assert.Contains("tests/AR.Iec61850.Tests/Scl/LiveIedSclExportOnlySemanticAuthorityTests.cs", allowed);\n        Assert.Contains("src/AR.Iec61850/Discovery/Iec61850StandardModelRegistry.cs", allowed);
    }

    private static LiveIedModelDiscoveryDocument BuildModel()
        => new()
        {
            IedName = "IED",
            AccessPointName = "AP1",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDADD",
                    Inst = "ADD",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            ProposedLnTypeId = "LN_GGIO_GGIO1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDADD/GGIO1.CBClsCounter",
                                    Name = "CBClsCounter",
                                    ProposedDoTypeId = "DO_SPS_GGIO_CBClsCounter",
                                    InferredCdc = "SPS",
                                    CdcConfidence = 0.78,
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Medium,
                                    Attributes =
                                    [
                                        Attribute("stVal", "INT32", "integer"),
                                        Attribute("q", "Quality", "bit-string"),
                                        Attribute("t", "Timestamp", "utc-time")
                                    ]
                                }
                            ]
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataAttributeModel Attribute(
        string path,
        string sclBType,
        string mmsType)
        => new()
        {
            ObjectReference = "IEDADD/GGIO1.CBClsCounter." + path,
            AttributePath = path,
            FunctionalConstraint = "ST",
            MmsReference = "IEDADD/GGIO1$ST$CBClsCounter$" + path,
            MmsItemName = "GGIO1$ST$CBClsCounter$" + path,
            Source = "GetVariableAccessAttributes",
            SclBType = sclBType,
            MmsType = mmsType,
            TypeDiscoveryStatus = "Exact",
            TypeSource = "GetVariableAccessAttributes",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

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

        throw new FileNotFoundException(relativePath);
    }
}
