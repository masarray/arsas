using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DiscoveryStructuredAnalogParityTests
{
    [Fact]
    public void TwelveStructuredAnalogMembers_AllResolveToUniquePublishableFloatLeaves()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildModel()
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        var expectedMembers = new[]
        {
            "IEDLD0/MMXU1.A.phsA",
            "IEDLD0/MMXU1.A.phsB",
            "IEDLD0/MMXU1.A.phsC",
            "IEDLD0/MMXU1.PPV.phsAB",
            "IEDLD0/MMXU1.PPV.phsBC",
            "IEDLD0/MMXU1.PPV.phsCA",
            "IEDLD0/MMXU1.ThdA.phsA",
            "IEDLD0/MMXU1.ThdA.phsB",
            "IEDLD0/MMXU1.ThdA.phsC",
            "IEDLD0/MMXU1.ThdPPV.phsAB",
            "IEDLD0/MMXU1.ThdPPV.phsBC",
            "IEDLD0/MMXU1.ThdPPV.phsCA"
        };

        Assert.Equal(12, result.MandatoryCatalogCount);
        Assert.Equal(12, device.Signals.Count);
        Assert.Equal(
            expectedMembers.OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase),
            device.Signals
                .Select(signal => signal.DisplayReference)
                .OrderBy(reference => reference, StringComparer.OrdinalIgnoreCase));

        Assert.Equal(
            12,
            device.Signals
                .Select(signal => signal.ObjectReference)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());

        Assert.All(device.Signals, signal =>
        {
            Assert.Equal("FLOAT32", signal.DataType);
            Assert.Equal("IEDLD0/LLN0.Analog", signal.DataSetReference);
            Assert.True(
                signal.ObjectReference.EndsWith(".cVal.mag.f", StringComparison.OrdinalIgnoreCase),
                signal.ObjectReference);
            Assert.True(signal.CanPublishToRuntime, signal.ObjectReference);
            Assert.DoesNotContain(
                "primary leaf unresolved",
                signal.ProbeStatus,
                StringComparison.OrdinalIgnoreCase);
        });
    }

    private static LiveIedModelDiscoveryDocument BuildModel()
    {
        const string logicalNode = "IEDLD0/MMXU1";
        var groups = new (string DataObject, string Cdc, string[] Members)[]
        {
            ("A", "WYE", new[] { "phsA", "phsB", "phsC" }),
            ("PPV", "DEL", new[] { "phsAB", "phsBC", "phsCA" }),
            ("ThdA", "WYE", new[] { "phsA", "phsB", "phsC" }),
            ("ThdPPV", "DEL", new[] { "phsAB", "phsBC", "phsCA" })
        };

        var dataObjects = groups
            .Select(group => new LiveIedDataObjectModel
            {
                Reference = logicalNode + "." + group.DataObject,
                Name = group.DataObject,
                InferredCdc = group.Cdc,
                ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
                Attributes = group.Members
                    .Select(member => new LiveIedDataAttributeModel
                    {
                        ObjectReference = logicalNode + "." + group.DataObject + "." + member + ".cVal.mag.f",
                        AttributePath = member + ".cVal.mag.f",
                        FunctionalConstraint = "MX",
                        SclBType = "FLOAT32",
                        MmsType = "floating-point",
                        TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
                        Source = "LiveMmsTypeSpecification"
                    })
                    .ToArray()
            })
            .ToArray();

        var members = groups
            .SelectMany(group => group.Members.Select(member => (group.DataObject, Member: member)))
            .Select((item, index) => new LiveIedDataSetMemberModel
            {
                Index = index,
                Reference = logicalNode + "." + item.DataObject + "." + item.Member,
                FunctionalConstraint = "MX",
                MmsReference = "IEDLD0/MMXU1$MX$" + item.DataObject + "$" + item.Member,
                Confidence = LiveIedDiscoveryConfidenceLevel.Exact
            })
            .ToArray();

        return new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            IedName = "IED",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "LD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects = dataObjects
                        }
                    ]
                }
            ],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.Analog",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = members.Length,
                    Members = members
                }
            ],
            ReportControls =
            [
                new LiveIedReportControlModel
                {
                    Reference = "IEDLD0/LLN0.RP.Unbuffer01",
                    DataSetReference = "IEDLD0/LLN0.Analog",
                    Buffered = false
                }
            ]
        };
    }
}
