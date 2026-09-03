using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class FatDataSetProjectionRegressionTests
{
    [Fact]
    public void StaticDataSet_DigitalAndAnalogMembers_AllBecomeFatRows()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildFixture(), "fat_digital_analog.cid");

        var rows = FatDataSetSignalProjectionService.Project("IED1", "S1", model);

        Assert.Equal(2, model.DataSets.Sum(dataSet => dataSet.Members.Count));
        Assert.Equal(2, rows.Count);
        Assert.All(rows, row => Assert.True(row.IsIncludedInFat));

        var digital = Assert.Single(rows.Where(row => row.StaticMemberReference.Contains("GGIO1.Dig01", StringComparison.Ordinal)));
        Assert.Equal(FatSignalKind.Discrete, digital.SignalKind);
        Assert.Equal(FatCaptureMode.AutomaticTransition, digital.CaptureMode);
        Assert.Equal("ST", digital.FunctionalConstraint);

        var analog = Assert.Single(rows.Where(row => row.StaticMemberReference.Contains("MMXU1.Ana01", StringComparison.Ordinal)));
        Assert.Equal(FatSignalKind.Analog, analog.SignalKind);
        Assert.Equal(FatCaptureMode.OperatorSnapshot, analog.CaptureMode);
        Assert.Equal("MX", analog.FunctionalConstraint);
    }

    [Fact]
    public void SameRuntimeObject_InTwoDataSets_RemainsTwoFatMembershipRows()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildDuplicateMembershipFixture(), "fat_duplicate_membership.cid");

        var rows = FatDataSetSignalProjectionService.Project("IED1", "S1", model);

        Assert.Equal(2, rows.Count);
        Assert.Equal(2, rows.Select(row => row.DataSetReference).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(2, rows.Select(row => row.SignalId).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.All(rows, row => Assert.Contains("GGIO1.Dig01", row.StaticMemberReference, StringComparison.Ordinal));
    }

    private static XDocument BuildFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return BuildScl(ns,
            new XElement(ns + "DataSet",
                new XAttribute("name", "FAT"),
                Fcda(ns, "ADD", "GGIO", "1", "Dig01", "ST"),
                Fcda(ns, "MEAS", "MMXU", "1", "Ana01", "MX")));
    }

    private static XDocument BuildDuplicateMembershipFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return BuildScl(ns,
            new XElement(ns + "DataSet",
                new XAttribute("name", "FAT_A"),
                Fcda(ns, "ADD", "GGIO", "1", "Dig01", "ST")),
            new XElement(ns + "DataSet",
                new XAttribute("name", "FAT_B"),
                Fcda(ns, "ADD", "GGIO", "1", "Dig01", "ST")));
    }

    private static XElement Fcda(
        XNamespace ns,
        string ldInst,
        string lnClass,
        string lnInst,
        string doName,
        string fc)
        => new(ns + "FCDA",
            new XAttribute("ldInst", ldInst),
            new XAttribute("lnClass", lnClass),
            new XAttribute("lnInst", lnInst),
            new XAttribute("doName", doName),
            new XAttribute("fc", fc));

    private static XDocument BuildScl(XNamespace ns, params XElement[] dataSets)
    {
        var ln0 = new XElement(ns + "LN0",
            new XAttribute("lnClass", "LLN0"),
            new XAttribute("lnType", "LLN0Type"));
        foreach (var dataSet in dataSets)
            ln0.Add(dataSet);

        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "FAT_V2")),
                new XElement(ns + "IED",
                    new XAttribute("name", "IED1"),
                    new XElement(ns + "AccessPoint",
                        new XAttribute("name", "S1"),
                        new XElement(ns + "Server",
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "Application"),
                                ln0),
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "ADD"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type")),
                                new XElement(ns + "LN",
                                    new XAttribute("lnClass", "GGIO"),
                                    new XAttribute("inst", "1"),
                                    new XAttribute("lnType", "GGIOType"))),
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "MEAS"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type")),
                                new XElement(ns + "LN",
                                    new XAttribute("lnClass", "MMXU"),
                                    new XAttribute("inst", "1"),
                                    new XAttribute("lnType", "MMXUType")))))),
                new XElement(ns + "DataTypeTemplates",
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "LLN0Type"),
                        new XAttribute("lnClass", "LLN0")),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "GGIOType"),
                        new XAttribute("lnClass", "GGIO"),
                        new XElement(ns + "DO",
                            new XAttribute("name", "Dig01"),
                            new XAttribute("type", "SpsType"))),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "MMXUType"),
                        new XAttribute("lnClass", "MMXU"),
                        new XElement(ns + "DO",
                            new XAttribute("name", "Ana01"),
                            new XAttribute("type", "MvType"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "SpsType"),
                        new XAttribute("cdc", "SPS"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "stVal"),
                            new XAttribute("bType", "BOOLEAN"),
                            new XAttribute("fc", "ST"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "MvType"),
                        new XAttribute("cdc", "MV"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "mag"),
                            new XAttribute("bType", "Struct"),
                            new XAttribute("type", "AnalogueValue"),
                            new XAttribute("fc", "MX"))),
                    new XElement(ns + "DAType",
                        new XAttribute("id", "AnalogueValue"),
                        new XElement(ns + "BDA",
                            new XAttribute("name", "f"),
                            new XAttribute("bType", "FLOAT32"))))));
    }
}
