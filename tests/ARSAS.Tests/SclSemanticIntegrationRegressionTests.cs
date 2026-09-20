using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Export;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class SclSemanticIntegrationRegressionTests
{
    [Fact]
    public void ExactIntegerCounter_ExportsAndReopensAsInsInt32_WithIntegerPresentation()
    {
        var model = new LiveIedModelDiscoveryDocument
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
                                    CdcConfidence = 0.38,
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

        var document = LiveIedSclExporter.BuildDocument(
            model,
            new LiveIedSclExportOptions { Profile = "full-model" });

        AssertExportedCounter(document);

        var reopened = SclLiveModelProjectionBuilder.Build(
            document,
            "generated.iid");
        var counter = Assert.Single(
            reopened.LogicalDevices
                .SelectMany(device => device.LogicalNodes)
                .SelectMany(node => node.DataObjects),
            dataObject => dataObject.Name == "CBClsCounter");

        Assert.Equal("INS", counter.InferredCdc);
        var stVal = Assert.Single(
            counter.Attributes,
            attribute => attribute.AttributePath == "stVal");
        Assert.Equal("INT32", stVal.SclBType);
        Assert.Equal(
            LiveIedDiscoveryConfidenceLevel.Exact,
            stVal.TypeConfidence);
        Assert.Equal("I", Iec61850ValueStatePresentation.TypeToken(stVal.SclBType));
    }

    [Fact]
    public void PhysicalP07AcquisitionBaseline_RemainsFrozenDuringSemanticIntegration()
    {
        var runtime = File.ReadAllText(
            FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var save = File.ReadAllText(
            FindRepoFile("MainWindow.xaml.cs"));
        var structured = File.ReadAllText(
            FindRepoFile("tests/ARSAS.Tests/DiscoveryStructuredAnalogParityTests.cs"));

        Assert.Contains("cyclic MMS process polling=0", runtime, StringComparison.Ordinal);
        Assert.Contains("state.NextPollUtc = DateTime.MaxValue", runtime, StringComparison.Ordinal);
        Assert.Contains(
            "monitoring remains active; Save SCL uses the current canonical model",
            save,
            StringComparison.Ordinal);
        Assert.Contains(
            "TwelveStructuredAnalogMembers_AllResolveToUniquePublishableFloatLeaves",
            structured,
            StringComparison.Ordinal);
    }

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
            Source = "LiveMmsTypeSpecification",
            SclBType = sclBType,
            MmsType = mmsType,
            TypeDiscoveryStatus = "Exact",
            TypeSource = "LiveMmsTypeSpecification",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            FunctionalConstraintConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static void AssertExportedCounter(XDocument document)
    {
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
        var stVal = Assert.Single(
            doType.Elements(ns + "DA"),
            element => (string?)element.Attribute("name") == "stVal");
        Assert.Equal("INT32", (string?)stVal.Attribute("bType"));
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

        throw new FileNotFoundException(relativePath);
    }
}
