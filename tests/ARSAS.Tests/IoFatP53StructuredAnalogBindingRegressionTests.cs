using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatP53StructuredAnalogBindingRegressionTests
{
    [Fact]
    public void MandatoryInventory_PreservesStaticPhaseIdentityAndUsesResolvedMagnitudeRuntimeLeaf()
    {
        const string dataObject = "IEDLD0/MMXU1.A";
        const string staticMember = dataObject + ".phsA";
        const string runtimeLeaf = staticMember + ".cVal.mag.f";
        var design = BuildDesign(
            dataObject,
            staticMember,
            Attribute(runtimeLeaf, "phsA.cVal.mag.f"),
            Attribute(staticMember + ".instCVal.mag.f", "phsA.instCVal.mag.f"),
            Attribute(staticMember + ".cVal.ang.f", "phsA.cVal.ang.f"),
            Attribute(staticMember + ".q", "phsA.q", "Quality"),
            Attribute(staticMember + ".t", "phsA.t", "Timestamp"),
            Attribute(dataObject + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"));
        var signals = new List<SignalDefinition>();

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, design);

        Assert.Equal(1, result.MandatoryCatalogCount);
        var signal = Assert.Single(signals);
        Assert.Equal(staticMember, signal.DisplayReference);
        Assert.Equal(runtimeLeaf, signal.ObjectReference);
        Assert.Equal("MX", signal.FunctionalConstraint);
        Assert.Contains("mandatory static DataSet member", signal.Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("primary leaf unresolved", signal.Source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void MandatoryInventory_DoesNotCrossStructuredPhaseBoundary()
    {
        const string dataObject = "IEDLD0/MMXU1.PPV";
        const string staticMember = dataObject + ".phsAB";
        const string runtimeLeaf = staticMember + ".cVal.mag.f";
        var design = BuildDesign(
            dataObject,
            staticMember,
            Attribute(runtimeLeaf, "phsAB.cVal.mag.f"),
            Attribute(dataObject + ".phsBC.cVal.mag.f", "phsBC.cVal.mag.f"));
        var signals = new List<SignalDefinition>();

        Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, design);

        var signal = Assert.Single(signals);
        Assert.Equal(staticMember, signal.DisplayReference);
        Assert.Equal(runtimeLeaf, signal.ObjectReference);
        Assert.DoesNotContain("phsBC", signal.ObjectReference, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("A", "phsA", "phsB", "phsC")]
    [InlineData("PPV", "phsAB", "phsBC", "phsCA")]
    public void MandatoryInventory_DoesNotCollapseStructuredPhaseSiblingsThroughSharedParentAlias(
        string dataObjectName,
        string phase1,
        string phase2,
        string phase3)
    {
        var dataObject = "IEDLD0/MMXU1." + dataObjectName;
        var phases = new[] { phase1, phase2, phase3 };
        var members = phases.Select(phase => dataObject + "." + phase).ToArray();
        var attributes = phases
            .SelectMany(phase => new[]
            {
                Attribute(dataObject + "." + phase + ".cVal.mag.f", phase + ".cVal.mag.f"),
                Attribute(dataObject + "." + phase + ".q", phase + ".q", "Quality"),
                Attribute(dataObject + "." + phase + ".t", phase + ".t", "Timestamp")
            })
            .ToArray();
        var design = BuildDesign(dataObject, members, attributes);
        var signals = new List<SignalDefinition>();

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, design);

        Assert.Equal(3, result.MandatoryCatalogCount);
        Assert.Equal(3, result.AddedCount);
        Assert.Equal(3, signals.Count);
        Assert.Equal(3, signals.Select(signal => signal.DisplayReference).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        Assert.Equal(3, signals.Select(signal => signal.ObjectReference).Distinct(StringComparer.OrdinalIgnoreCase).Count());

        foreach (var phase in phases)
        {
            var staticMember = dataObject + "." + phase;
            var runtimeLeaf = staticMember + ".cVal.mag.f";
            var signal = Assert.Single(signals.Where(candidate =>
                candidate.DisplayReference.Equals(staticMember, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(runtimeLeaf, signal.ObjectReference);
        }

        var ied = new IoTestIedPlan
        {
            IedName = "IED",
            IpAddress = "192.0.2.10",
            IedRole = "Regression",
            TestPoints = phases.Select(phase =>
            {
                var staticMember = dataObject + "." + phase;
                return new IoTestPointPlan
                {
                    TestPointId = "THD-" + phase,
                    IedName = "IED",
                    IpAddress = "192.0.2.10",
                    SignalName = phase,
                    ObjectReference = staticMember + ".cVal.mag.f",
                    SourceIecReference = staticMember,
                    ReportDisplayReference = staticMember,
                    FunctionalConstraint = "MX",
                    ExpectedOnText = "Value 1",
                    ExpectedOffText = "Value 2",
                    ImportReady = true,
                    TestEnabled = true
                };
            }).ToList()
        };
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            SclIedName = "IED",
            IpAddress = "192.0.2.10",
            Port = 102
        };
        device.Signals.AddRange(signals);

        var selection = new IoTestSignalSelectionService().Resolve(ied, device);

        Assert.True(selection.Succeeded, selection.Message);
        Assert.Equal(3, selection.Matches.Count);
        Assert.Equal(3, selection.Matches.Select(match => match.Signal).Distinct().Count());
        Assert.Empty(selection.MissingPoints);
        Assert.Empty(selection.AmbiguousPoints);
    }

    private static LiveIedModelDiscoveryDocument BuildDesign(
        string dataObjectReference,
        string memberReference,
        params LiveIedDataAttributeModel[] attributes)
        => BuildDesign(dataObjectReference, new[] { memberReference }, attributes);

    private static LiveIedModelDiscoveryDocument BuildDesign(
        string dataObjectReference,
        IReadOnlyList<string> memberReferences,
        params LiveIedDataAttributeModel[] attributes)
    {
        var slash = dataObjectReference.IndexOf('/');
        var domain = dataObjectReference[..slash];
        var path = dataObjectReference[(slash + 1)..];
        var dot = path.IndexOf('.');
        var logicalNode = path[..dot];
        var dataObjectName = path[(dot + 1)..];
        return new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = logicalNode,
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = dataObjectReference,
                                    Name = dataObjectName,
                                    InferredCdc = dataObjectName.Equals("PPV", StringComparison.OrdinalIgnoreCase) ? "DEL" : "WYE",
                                    Attributes = attributes
                                }
                            }
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = domain + "/LLN0.Analog",
                    Domain = domain,
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = memberReferences.Count,
                    Members = memberReferences.Select((memberReference, index) =>
                        new LiveIedDataSetMemberModel
                        {
                            Index = index,
                            Reference = memberReference,
                            FunctionalConstraint = "MX",
                            MmsReference = BuildMmsReference(memberReference),
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }).ToArray()
                }
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(
        string reference,
        string attributePath,
        string sclBType = "FLOAT32")
        => new()
        {
            ObjectReference = reference,
            AttributePath = attributePath,
            FunctionalConstraint = "MX",
            MmsReference = BuildMmsReference(reference),
            MmsItemName = BuildMmsReference(reference).Split('/', 2)[1],
            SclBType = sclBType,
            MmsType = sclBType == "FLOAT32" ? "floating-point" : string.Empty,
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static string BuildMmsReference(string reference)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var path = reference[(slash + 1)..];
        var dot = path.IndexOf('.');
        var logicalNode = path[..dot];
        var objectPath = path[(dot + 1)..].Replace('.', '$');
        return $"{domain}/{logicalNode}$MX${objectPath}";
    }
}
