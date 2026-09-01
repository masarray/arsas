using System.Xml.Linq;
using AR.Iec61850.Scl.Engineering;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatPhysicalBindingRegressionTests
{
    private readonly IoTestSignalSelectionService _selection = new();

    [Fact]
    public void SparsePresentationInventory_IsRestoredFromAriecDataSetAuthorityBeforeFatMatching()
    {
        var model = SclLiveModelProjectionBuilder.Build(BuildSingleAnalogFixture(), "physical-fat.cid");
        var workspace = new SclIedWorkspace
        {
            IedName = "IED1",
            AccessPointName = "E",
            DesignModel = model
        };

        var authoritativeSignals = new List<SignalDefinition>();
        var authority = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(authoritativeSignals, model);
        var expected = Assert.Single(authoritativeSignals);
        Assert.Equal(1, authority.MandatoryCatalogCount);

        var device = new Iec61850MonitorDevice
        {
            Name = "IED1",
            SclIedName = "IED1",
            IpAddress = "192.168.81.103",
            Port = 102,
            SclWorkspace = workspace
        };
        Assert.Empty(device.Signals);

        var point = DataSetPoint(
            "MEM-001",
            expected.ObjectReference,
            expected.DisplayReference,
            expected.FunctionalConstraint,
            "DataSet-A",
            1);
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.168.81.103",
            TestPoints = [point]
        };

        var result = _selection.Resolve(ied, device);

        Assert.True(result.Succeeded, result.Message);
        Assert.Single(result.Matches);
        Assert.NotEmpty(device.Signals);
        Assert.Contains(device.Signals, signal =>
            string.Equals(signal.ObjectReference, expected.ObjectReference, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void DistinctStaticDataSetMemberships_CanFanOutFromOneResolvedLiveSignal()
    {
        const string liveReference = "IED1MEAS/MMXU1.A.cVal.mag.f";
        var point1 = DataSetPoint("MEM-001", liveReference, "IED1MEAS/MMXU1.A", "MX", "DataSet-A", 1);
        var point2 = DataSetPoint("MEM-002", liveReference, "IED1MEAS/MMXU1.A", "MX", "DataSet-B", 7);
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.168.81.103",
            TestPoints = [point1, point2]
        };
        var signal = new SignalDefinition
        {
            Name = "A phase current",
            ObjectReference = liveReference,
            FunctionalConstraint = "MX"
        };
        var device = new Iec61850MonitorDevice
        {
            Name = "IED1",
            SclIedName = "IED1",
            IpAddress = "192.168.81.103",
            Port = 102
        };
        device.Signals.Add(signal);

        var result = _selection.Resolve(ied, device);
        var preflight = IoTestSessionPreflight.Validate(ied);

        Assert.True(result.Succeeded, result.Message);
        Assert.Equal(2, result.Matches.Count);
        Assert.All(result.Matches, match => Assert.Same(signal, match.Signal));
        Assert.True(preflight.Succeeded, preflight.Message);
    }

    [Fact]
    public void DuplicateStaticMembershipIdentity_RemainsBlockedEvenWithSclAuthority()
    {
        const string liveReference = "IED1ADD/GGIO1.Alm.stVal";
        var point1 = DataSetPoint("MEM-001", liveReference, "IED1ADD/GGIO1.Alm", "ST", "DataSet-A", 1);
        var point2 = DataSetPoint("MEM-002", liveReference, "IED1ADD/GGIO1.Alm", "ST", "DataSet-A", 1);
        var ied = new IoTestIedPlan
        {
            IedName = "IED1",
            IpAddress = "192.168.81.103",
            TestPoints = [point1, point2]
        };

        var preflight = IoTestSessionPreflight.Validate(ied);

        Assert.False(preflight.Succeeded);
        Assert.Contains("multiple enabled test points", preflight.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IoTestPointPlan DataSetPoint(
        string id,
        string runtimeReference,
        string staticReference,
        string functionalConstraint,
        string dataSet,
        int memberRow) => new()
    {
        TestPointId = id,
        IedName = "IED1",
        IpAddress = "192.168.81.103",
        SignalName = id,
        ObjectReference = runtimeReference,
        SourceIecReference = staticReference,
        ReportDisplayReference = staticReference,
        FunctionalConstraint = functionalConstraint,
        SignalAddress = "source-physical",
        DataSetName = dataSet,
        SourceRow = memberRow,
        ImportReady = true,
        TestEnabled = true,
        BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus
    };

    private static XDocument BuildSingleAnalogFixture()
    {
        XNamespace ns = "http://www.iec.ch/61850/2003/SCL";
        return new XDocument(
            new XElement(ns + "SCL",
                new XAttribute("version", "2007"),
                new XAttribute("revision", "B"),
                new XElement(ns + "Header", new XAttribute("id", "PHYSICAL_FAT")),
                new XElement(ns + "IED",
                    new XAttribute("name", "IED1"),
                    new XElement(ns + "AccessPoint",
                        new XAttribute("name", "E"),
                        new XElement(ns + "Server",
                            new XElement(ns + "LDevice",
                                new XAttribute("inst", "Application"),
                                new XElement(ns + "LN0",
                                    new XAttribute("lnClass", "LLN0"),
                                    new XAttribute("lnType", "LLN0Type"),
                                    new XElement(ns + "DataSet",
                                        new XAttribute("name", "Analog"),
                                        new XElement(ns + "FCDA",
                                            new XAttribute("ldInst", "MEAS"),
                                            new XAttribute("lnClass", "MMXU"),
                                            new XAttribute("lnInst", "1"),
                                            new XAttribute("doName", "A"),
                                            new XAttribute("fc", "MX"))))),
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
                        new XAttribute("id", "MMXUType"),
                        new XAttribute("lnClass", "MMXU"),
                        new XElement(ns + "DO",
                            new XAttribute("name", "A"),
                            new XAttribute("type", "MvType"))),
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
