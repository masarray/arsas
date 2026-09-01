using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

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

    private static LiveIedModelDiscoveryDocument BuildDesign(
        string dataObjectReference,
        string memberReference,
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
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = memberReference,
                            FunctionalConstraint = "MX",
                            MmsReference = BuildMmsReference(memberReference),
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    }
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
