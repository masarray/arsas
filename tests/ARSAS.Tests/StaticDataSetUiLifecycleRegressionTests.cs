using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetUiLifecycleRegressionTests
{
    [Fact]
    public void SiemensLikeStaticMembers_SurviveOperatorPresentationDecision()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildFixture(), "Siprotec_ui_lifecycle.cid");
        var workspace = new SclIedWorkspace
        {
            IedName = "AA1C1F13R4",
            AccessPointName = "E",
            DesignModel = model
        };

        var signals = SclWorkspaceSignalMapper.BuildSignals(workspace).ToList();
        var before = Iec61850DataSetCompletenessDiagnostic.Evaluate(model, signals);

        var presented = signals.Where(SasOperationalUiPolicy.IsPresentationVisible).ToArray();
        var after = Iec61850DataSetCompletenessDiagnostic.Evaluate(model, signals);

        Assert.Equal(2, before.StaticMemberCount);
        Assert.Equal(2, before.RepresentedCount);
        Assert.Equal(0, before.MissingCount);
        Assert.Equal(2, after.RepresentedCount);
        Assert.Equal(0, after.MissingCount);

        Assert.Contains(presented, signal => signal.DisplayReference == "AA1C1F13R4ADD/GGIO6.CBOpnd");
        Assert.Contains(presented, signal => signal.DisplayReference == "AA1C1F13R4MEAS/MMXU1.A.phsA");
        Assert.Equal(signals.Count, signals.Count); // presentation decision must not mutate source inventory
    }

    private static XDocument BuildFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "SIEMENS_UI_LIFECYCLE")),
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
                                    new XElement(ns + "DataSet",
                                        new XAttribute("name", "Mixed"),
                                        new XElement(ns + "FCDA",
                                            new XAttribute("ldInst", "ADD"),
                                            new XAttribute("lnClass", "GGIO"),
                                            new XAttribute("lnInst", "6"),
                                            new XAttribute("doName", "CBOpnd"),
                                            new XAttribute("fc", "ST")),
                                        new XElement(ns + "FCDA",
                                            new XAttribute("ldInst", "MEAS"),
                                            new XAttribute("lnClass", "MMXU"),
                                            new XAttribute("lnInst", "1"),
                                            new XAttribute("doName", "A.phsA"),
                                            new XAttribute("fc", "MX"))))),
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "ADD"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type")),
                                new XElement(ns + "LN",
                                    new XAttribute("lnClass", "GGIO"),
                                    new XAttribute("inst", "6"),
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
                        new XElement(ns + "DO", new XAttribute("name", "CBOpnd"), new XAttribute("type", "SpsType"))),
                    new XElement(ns + "LNodeType",
                        new XAttribute("id", "MMXUType"),
                        new XAttribute("lnClass", "MMXU"),
                        new XElement(ns + "DO", new XAttribute("name", "A"), new XAttribute("type", "WyeType"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "SpsType"),
                        new XAttribute("cdc", "SPS"),
                        new XElement(ns + "DA",
                            new XAttribute("name", "stVal"),
                            new XAttribute("bType", "BOOLEAN"),
                            new XAttribute("fc", "ST"))),
                    new XElement(ns + "DOType",
                        new XAttribute("id", "WyeType"),
                        new XAttribute("cdc", "WYE"),
                        new XElement(ns + "SDO", new XAttribute("name", "phsA"), new XAttribute("type", "MvType"))),
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
