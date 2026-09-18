using System.Xml.Linq;
using AR.Iec61850.Discovery;
using AR.Iec61850.Scl;
using AR.Iec61850.Scl.Export;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class CanonicalSclReloadValidatorTests
{
    [Theory]
    [InlineData(SclSchemaProfile.Edition2V31)]
    [InlineData(SclSchemaProfile.Edition1V16)]
    public void ExportedCanonicalScl_ReopensThroughArsasWorkspaceWithoutStructuralDrift(
        SclSchemaProfile schema)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-canonical-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(
            root,
            schema == SclSchemaProfile.Edition2V31 ? "relay.iid" : "relay.icd");

        try
        {
            var canonical = CreateCanonical();
            var result = CanonicalLiveIedSclExporter.WriteFiles(
                canonical,
                path,
                schema,
                profile: "safe-connection");

            var workspace = CanonicalSclReloadValidator.Validate(
                new SclWorkspaceService(),
                canonical,
                result);

            Assert.Equal("IED", workspace.IedName);
            Assert.Equal("AP1", workspace.AccessPointName);
            Assert.NotNull(workspace.PreferredEndpoint);
            Assert.Equal("10.20.30.40", workspace.PreferredEndpoint!.IpAddress);
            Assert.Equal(102, workspace.PreferredEndpoint.Port);
            Assert.Equal(result.LogicalDeviceCount, workspace.DesignModel.Coverage.LogicalDeviceCount);
            Assert.Equal(result.LogicalNodeCount, workspace.DesignModel.Coverage.LogicalNodeCount);
            Assert.Equal(result.DataSetCount, workspace.DataSets.Count);
            Assert.Equal(result.ReportControlCount, workspace.ReportControls.Count);
            Assert.Equal(1, result.LogicalDeviceCount);
            Assert.Equal(1, result.LogicalNodeCount);
            Assert.Equal(1, result.ReportControlCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData(SclSchemaProfile.Edition2V31)]
    [InlineData(SclSchemaProfile.Edition1V16)]
    public void GoldenRcbShape_RoundTripsThirtyFourRuntimeAsThirtyTwoLogicalWithPhysicalCapacity(
        SclSchemaProfile schema)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-canonical-rcb-reload-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(
            root,
            schema == SclSchemaProfile.Edition2V31 ? "relay-rcb.iid" : "relay-rcb.icd");

        try
        {
            var canonical = CreateGoldenRcbCanonical();
            Assert.Equal(34, canonical.Discovery.ReportControls.Count);

            var result = CanonicalLiveIedSclExporter.WriteFiles(
                canonical,
                path,
                schema,
                profile: "safe-connection");
            var workspace = CanonicalSclReloadValidator.Validate(
                new SclWorkspaceService(),
                canonical,
                result);

            Assert.Equal(32, result.ReportControlCount);
            Assert.Equal(32, workspace.ReportControls.Count);

            var document = XDocument.Load(result.SclPath);
            var ns = document.Root!.Name.Namespace;
            var conf = document.Descendants(ns + "ConfReportControl").Single();
            Assert.Equal("34", (string?)conf.Attribute("max"));

            var buffer = document.Descendants(ns + "ReportControl")
                .Single(element => (string?)element.Attribute("name") == "Buffer");
            var unbuffer = document.Descendants(ns + "ReportControl")
                .Single(element => (string?)element.Attribute("name") == "Unbuffer");
            Assert.Equal("true", (string?)buffer.Attribute("indexed"));
            Assert.Equal("2", (string?)buffer.Element(ns + "RptEnabled")?.Attribute("max"));
            Assert.Equal("true", (string?)unbuffer.Attribute("indexed"));
            Assert.Equal("2", (string?)unbuffer.Element(ns + "RptEnabled")?.Attribute("max"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static LiveIedCanonicalModel CreateGoldenRcbCanonical()
    {
        var baseline = CreateCanonical();
        var controls = Enumerable.Range(1, 30)
            .Select(index => RuntimeControl(
                $"Standalone_{index}_X",
                buffered: false,
                reportId: $"RID_{index}_X"))
            .Concat(
            [
                RuntimeControl("Buffer01", buffered: true, reportId: "RID_Buffer01"),
                RuntimeControl("Buffer02", buffered: true, reportId: "RID_Buffer02"),
                RuntimeControl("Unbuffer01", buffered: false, reportId: "RID_Unbuffer01"),
                RuntimeControl("Unbuffer02", buffered: false, reportId: "RID_Unbuffer02")
            ])
            .ToArray();

        return new LiveIedCanonicalModel
        {
            Discovery = new LiveIedModelDiscoveryDocument
            {
                Host = baseline.Discovery.Host,
                Port = baseline.Discovery.Port,
                IedName = baseline.Discovery.IedName,
                AccessPointName = baseline.Discovery.AccessPointName,
                LogicalDevices = baseline.Discovery.LogicalDevices,
                ReportControls = controls
            },
            Communication = baseline.Communication
        };
    }

    private static LiveIedReportControlModel RuntimeControl(
        string name,
        bool buffered,
        string reportId)
        => new()
        {
            Reference = $"IEDLD0/LLN0${(buffered ? "BR" : "RP")}${name}",
            Domain = "IEDLD0",
            LogicalNode = "LLN0",
            Name = name,
            Buffered = buffered,
            DataSetReference = string.Empty,
            ReportId = reportId,
            ConfRev = "1",
            TriggerOptions = "dchg,qchg,gi",
            OptionalFields = "seqnum,timestamp,dataset,dataref",
            BufferTimeMs = buffered ? "10" : "0",
            IntegrityPeriodMs = "1000"
        };


    private static LiveIedCanonicalModel CreateCanonical()
        => new()
        {
            Discovery = new LiveIedModelDiscoveryDocument
            {
                Host = "10.20.30.40",
                Port = 102,
                IedName = "IED",
                AccessPointName = "AP1",
                LogicalDevices =
                [
                    new LiveIedLogicalDeviceModel
                    {
                        MmsDomain = "IEDLD0",
                        Inst = "IEDLD0",
                        LogicalNodes =
                        [
                            new LiveIedLogicalNodeModel
                            {
                                Name = "LLN0",
                                LnClass = "LLN0",
                                LnInst = string.Empty,
                                ProposedLnTypeId = "LN_LLN0_1"
                            }
                        ]
                    }
                ],
                ReportControls =
                [
                    new LiveIedReportControlModel
                    {
                        Reference = "IEDLD0/LLN0$BR$BRCB01",
                        Domain = "IEDLD0",
                        LogicalNode = "LLN0",
                        Name = "BRCB01",
                        Buffered = true,
                        Indexed = false,
                        ReportId = "RID_BRCB01",
                        ConfRev = "1",
                        TriggerOptions = "dchg,qchg,gi",
                        OptionalFields = "seqnum,timestamp,dataset,dataref",
                        BufferTimeMs = "10",
                        IntegrityPeriodMs = "1000"
                    }
                ]
            },
            Communication = new LiveIedCommunicationEvidence
            {
                Source = "AcceptedAssociationWireProfile",
                AssociationProfileName = "BalancedApTitle",
                Host = "10.20.30.40",
                Port = 102,
                AccessPointName = "AP1",
                Association = new SclIsoAssociationAddress
                {
                    ApTitle = "1,1,1,999,1",
                    AeQualifierText = "12",
                    AeQualifier = 12,
                    PresentationSelector = "00000001",
                    SessionSelector = "0001",
                    TransportSelector = "0001"
                }
            }
        };
}
