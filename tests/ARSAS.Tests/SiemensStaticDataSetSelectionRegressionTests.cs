using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SiemensStaticDataSetSelectionRegressionTests
{
    [Fact]
    public void SiemensLike_58Member_CrossLd_StaticDataSets_AreRepresented58Of58()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildFixture(), "Siprotec_58_member.cid");
        var signals = new List<SignalDefinition>();

        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, model);
        var snapshot = Iec61850DataSetCompletenessDiagnostic.Evaluate(model, signals);

        Assert.Equal(2, model.DataSets.Count);
        Assert.Equal(58, model.DataSets.Sum(dataSet => dataSet.Members.Count));
        Assert.Equal(58, merge.MandatoryCatalogCount);
        Assert.Equal(58, signals.Count);
        Assert.Equal(58, snapshot.StaticMemberCount);
        Assert.Equal(58, snapshot.MandatoryInventoryCount);
        Assert.Equal(58, snapshot.RepresentedCount);
        Assert.Equal(0, snapshot.MissingCount);
        Assert.True(snapshot.IsComplete);

        Assert.Contains(signals, signal => signal.DisplayReference == "AA1C1F13R4ADD/GGIO1.Dig01");
        Assert.Contains(signals, signal => signal.DisplayReference == "AA1C1F13R4MEAS/MMXU1.Ana22");
        Assert.All(signals, signal =>
        {
            Assert.DoesNotContain("[ST]", signal.DisplayReference, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("[MX]", signal.DisplayReference, StringComparison.OrdinalIgnoreCase);
        });
    }

    [Fact]
    public void SiemensLike_58Member_CrossLd_StaticDataSets_Survive_Real_SclMapper_Path()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildFixture(), "Siprotec_58_member.cid");
        var workspace = new SclIedWorkspace
        {
            IedName = "AA1C1F13R4",
            AccessPointName = "E",
            DesignModel = model
        };

        var signals = SclWorkspaceSignalMapper.BuildSignals(workspace);
        var snapshot = Iec61850DataSetCompletenessDiagnostic.Evaluate(model, signals);

        Assert.Equal(58, snapshot.StaticMemberCount);
        Assert.Equal(58, snapshot.MandatoryInventoryCount);
        Assert.Equal(58, snapshot.RepresentedCount);
        Assert.Equal(0, snapshot.MissingCount);
        Assert.True(snapshot.IsComplete);
        Assert.Contains(signals, signal => signal.DisplayReference == "AA1C1F13R4ADD/GGIO1.Dig01");
        Assert.Contains(signals, signal => signal.DisplayReference == "AA1C1F13R4MEAS/MMXU1.Ana22");
    }

    private static XDocument BuildFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";

        var digitalDataSet = new XElement(ns + "DataSet", new XAttribute("name", "Digital"));
        foreach (var index in Enumerable.Range(1, 36))
        {
            digitalDataSet.Add(new XElement(ns + "FCDA",
                new XAttribute("ldInst", "ADD"),
                new XAttribute("lnClass", "GGIO"),
                new XAttribute("lnInst", "1"),
                new XAttribute("doName", $"Dig{index:00}"),
                new XAttribute("fc", "ST")));
        }

        var analogDataSet = new XElement(ns + "DataSet", new XAttribute("name", "Analog"));
        foreach (var index in Enumerable.Range(1, 22))
        {
            analogDataSet.Add(new XElement(ns + "FCDA",
                new XAttribute("ldInst", "MEAS"),
                new XAttribute("lnClass", "MMXU"),
                new XAttribute("lnInst", "1"),
                new XAttribute("doName", $"Ana{index:00}"),
                new XAttribute("fc", "MX")));
        }

        var ggioType = new XElement(ns + "LNodeType",
            new XAttribute("id", "GGIOType"),
            new XAttribute("lnClass", "GGIO"));
        foreach (var index in Enumerable.Range(1, 36))
        {
            ggioType.Add(new XElement(ns + "DO",
                new XAttribute("name", $"Dig{index:00}"),
                new XAttribute("type", "SpsType")));
        }

        var mmxuType = new XElement(ns + "LNodeType",
            new XAttribute("id", "MMXUType"),
            new XAttribute("lnClass", "MMXU"));
        foreach (var index in Enumerable.Range(1, 22))
        {
            mmxuType.Add(new XElement(ns + "DO",
                new XAttribute("name", $"Ana{index:00}"),
                new XAttribute("type", "MvType")));
        }

        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "SIEMENS_58")),
                new XElement(ns + "IED",
                    new XAttribute("name", "AA1C1F13R4"),
                    new XElement(ns + "AccessPoint",
                        new XAttribute("name", "E"),
                        new XElement(ns + "Server",
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "Application"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type"),
                                    analogDataSet,
                                    digitalDataSet)),
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
                    ggioType,
                    mmxuType,
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
