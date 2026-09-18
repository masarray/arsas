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
            Assert.Equal(2, result.LogicalNodeCount);
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
    public void ExportedCanonicalScl_PreparesExactSclAssistedAssociationPlan(
        SclSchemaProfile schema)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-canonical-association-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(
            root,
            schema == SclSchemaProfile.Edition2V31 ? "relay-association.iid" : "relay-association.icd");

        try
        {
            var canonical = CreateCanonical();
            var result = CanonicalLiveIedSclExporter.WriteFiles(
                canonical,
                path,
                schema,
                profile: "safe-connection");

            var preparation = SclAssistedConnectionPreparationBuilder.Build(
                File.ReadAllText(result.SclPath),
                canonical.IedName,
                canonical.AccessPointName,
                canonical.Communication.Host,
                canonical.Communication.Port);

            Assert.True(
                preparation.IsSuccess,
                string.Join(" | ", preparation.Errors));
            var plan = Assert.IsType<AR.Iec61850.Scl.SclAssistedMmsAssociationPlan>(
                preparation.AssociationPlan);

            Assert.Equal(canonical.IedName, plan.IedName);
            Assert.Equal(canonical.AccessPointName, plan.AccessPointName);
            Assert.Equal(canonical.Communication.Host, plan.Host);
            Assert.Equal(102, plan.Port);

            Assert.Equal("0001", Convert.ToHexString(plan.Cotp.DestinationTsap));
            Assert.Equal("00000001", Convert.ToHexString(plan.Association.Called.PresentationSelector));
            Assert.Equal("0001", Convert.ToHexString(plan.Association.Called.SessionSelector));
            Assert.Equal(new uint[] { 1, 1, 1, 999, 1 }, plan.Association.Called.ApTitle);
            Assert.Equal(12, plan.Association.Called.AeQualifier);
            Assert.NotEmpty(plan.CotpConnectRequest);
            Assert.NotEmpty(plan.SessionPresentationAcseMmsRequest);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }


    [Theory]
    [InlineData("OSI-AP-Title", "1,1,1,999,2")]
    [InlineData("OSI-AE-Qualifier", "13")]
    [InlineData("OSI-PSEL", "00000002")]
    [InlineData("OSI-SSEL", "0002")]
    [InlineData("OSI-TSEL", "0002")]
    public void WorkspaceReload_RejectsValidButDifferentAssociationIdentity(
        string parameterType,
        string replacementValue)
    {
        var root = Path.Combine(
            Path.GetTempPath(),
            "arsas-canonical-association-drift-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, "relay-association-drift.iid");

        try
        {
            var canonical = CreateCanonical();
            var result = CanonicalLiveIedSclExporter.WriteFiles(
                canonical,
                path,
                SclSchemaProfile.Edition2V31,
                profile: "safe-connection");

            var document = XDocument.Load(result.SclPath);
            var ns = document.Root!.Name.Namespace;
            var parameter = document.Descendants(ns + "P")
                .Single(element =>
                    string.Equals(
                        (string?)element.Attribute("type"),
                        parameterType,
                        StringComparison.Ordinal));
            parameter.Value = replacementValue;
            document.Save(result.SclPath);

            var error = Assert.Throws<InvalidOperationException>(() =>
                CanonicalSclReloadValidator.Validate(
                    new SclWorkspaceService(),
                    canonical,
                    result));

            Assert.Contains(
                "Generated SCL reconnect association drifted from accepted canonical wire evidence",
                error.Message,
                StringComparison.Ordinal);
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
                            },
                            new LiveIedLogicalNodeModel
                            {
                                Name = "GGIO1",
                                LnClass = "GGIO",
                                LnInst = "1",
                                ProposedLnTypeId = "LN_GGIO_1",
                                DataObjects =
                                [
                                    new LiveIedDataObjectModel
                                    {
                                        Reference = "IEDLD0/GGIO1.Ind1",
                                        Name = "Ind1",
                                        ProposedDoTypeId = "DO_SPS_Ind1",
                                        InferredCdc = "SPS",
                                        CdcConfidence = 0.99,
                                        ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.High,
                                        Attributes =
                                        [
                                            new LiveIedDataAttributeModel
                                            {
                                                ObjectReference = "IEDLD0/GGIO1.Ind1.stVal",
                                                AttributePath = "stVal",
                                                FunctionalConstraint = "ST",
                                                MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                                                MmsItemName = "GGIO1$ST$Ind1$stVal",
                                                Source = "GetNameList",
                                                SclBType = "BOOLEAN",
                                                MmsType = "Boolean",
                                                TypeDiscoveryStatus = "Exact",
                                                TypeSource = "GetVariableAccessAttributes",
                                                TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
                                            }
                                        ]
                                    }
                                ]
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
