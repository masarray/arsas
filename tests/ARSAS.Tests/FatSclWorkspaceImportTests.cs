using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatSclWorkspaceImportTests
{
    [Fact]
    public void MultipleSclWorkspaces_AggregateAllRows_AndFingerprintIgnoresSelectionOrder()
    {
        var first = Source("relay-a.cid", Hash('a'), BuildWorkspace("IED_A", "S1", "Dig01"));
        var second = Source("relay-b.cid", Hash('b'), BuildWorkspace("IED_B", "S1", "Dig02"));

        var forward = FatSclWorkspaceImportService.Import(new[] { first, second });
        var reverse = FatSclWorkspaceImportService.Import(new[] { second, first });

        Assert.Equal(2, forward.Project.Signals.Count);
        Assert.Equal(2, forward.Sources.Count);
        Assert.Equal(forward.SourceFingerprint, reverse.SourceFingerprint);
        Assert.Contains(forward.Project.Signals, signal => signal.IedName == "IED_A");
        Assert.Contains(forward.Project.Signals, signal => signal.IedName == "IED_B");
    }

    [Fact]
    public void SameIedAccessPoint_FromDifferentSourceHashes_IsBlocked()
    {
        var first = Source("relay-old.cid", Hash('a'), BuildWorkspace("IED_A", "S1", "Dig01"));
        var second = Source("relay-new.cid", Hash('b'), BuildWorkspace("IED_A", "S1", "Dig01"));

        var error = Assert.Throws<InvalidDataException>(() =>
            FatSclWorkspaceImportService.Import(new[] { first, second }));

        Assert.Contains("Conflicting SCL sources", error.Message, StringComparison.Ordinal);
        Assert.Contains("IED_A|S1", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ExactDuplicateWorkspaceContent_CollapsesWithoutDuplicatingFatRows()
    {
        var workspace = BuildWorkspace("IED_A", "S1", "Dig01");
        var first = Source("relay-a.cid", Hash('a'), workspace);
        var duplicate = Source("copy-of-relay-a.cid", Hash('a'), workspace);

        var result = FatSclWorkspaceImportService.Import(new[] { duplicate, first });

        Assert.Single(result.Sources);
        Assert.Single(result.Project.Signals);
    }

    private static FatSclWorkspaceSource Source(string fileName, string hash, SclIedWorkspace workspace)
        => new(fileName, hash, workspace);

    private static string Hash(char value) => new(value, 64);

    private static SclIedWorkspace BuildWorkspace(string iedName, string accessPointName, string dataObjectName)
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        var document = new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "FAT_MULTI")),
                new XElement(ns + "IED",
                    new XAttribute("name", iedName),
                    new XElement(ns + "AccessPoint",
                        new XAttribute("name", accessPointName),
                        new XElement(ns + "Server",
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "Application"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type"),
                                    new XElement(ns + "DataSet",
                                        new XAttribute("name", "FAT"),
                                        new XElement(ns + "FCDA",
                                            new XAttribute("ldInst", "ADD"),
                                            new XAttribute("lnClass", "GGIO"),
                                            new XAttribute("lnInst", "1"),
                                            new XAttribute("doName", dataObjectName),
                                            new XAttribute("fc", "ST"))))),
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "ADD"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type")),
                                new XElement(ns + "LN",
                                    new XAttribute("lnClass", "GGIO"),
                                    new XAttribute("inst", "1"),
                                    new XAttribute("lnType", "GGIOType")))))),
                new XElement(ns + "DataTypeTemplates",
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "LLN0Type"),
                        new XAttribute("lnClass", "LLN0")),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "GGIOType"),
                        new XAttribute("lnClass", "GGIO"),
                        new XElement(ns + "DO",
                            new XAttribute("name", dataObjectName),
                            new XAttribute("type", "SpsType"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "SpsType"),
                        new XAttribute("cdc", "SPS"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "stVal"),
                            new XAttribute("bType", "BOOLEAN"),
                            new XAttribute("fc", "ST"))))));

        return new SclIedWorkspace
        {
            IedName = iedName,
            AccessPointName = accessPointName,
            DesignModel = SclLiveModelProjectionBuilder.Build(document, $"{iedName}.cid")
        };
    }
}
