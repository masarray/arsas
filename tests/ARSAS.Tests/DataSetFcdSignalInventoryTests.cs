using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DataSetFcdSignalInventoryTests
{
    [Fact]
    public void FcdOnly_SiemensLike_DataSets_Appear_58Of58_In_Signal_Selector_Inventory()
    {
        var digitalMembers = Enumerable.Range(1, 36)
            .Select(index => Member(index - 1, $"IEDLD0/GGIO1.Dig{index:00}", "ST"))
            .ToArray();
        var analogMembers = Enumerable.Range(1, 22)
            .Select(index => Member(index - 1, $"IEDLD0/MMXU1.Ana{index:00}", "MX"))
            .ToArray();
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildModel(digitalMembers, analogMembers)
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        Assert.Equal(58, result.MandatoryCatalogCount);
        Assert.Equal(58, result.AddedCount);
        Assert.Equal(58, device.Signals.Count);
        Assert.All(device.Signals, signal =>
        {
            Assert.False(signal.IsSelected);
            Assert.Equal("DataSet", signal.Category);
            Assert.True(signal.IsReportCapable);
            Assert.Contains("primary leaf unresolved", signal.ReportCoverage, StringComparison.OrdinalIgnoreCase);
        });
        Assert.Contains(device.Signals, signal => signal.ObjectReference == "IEDLD0/GGIO1.Dig01");
        Assert.Contains(device.Signals, signal => signal.ObjectReference == "IEDLD0/MMXU1.Ana22");
    }

    [Fact]
    public void Reopening_Selector_Does_Not_Duplicate_FcdOnly_DataSet_Members()
    {
        var member = Member(0, "IEDLD0/GGIO1.CBOpnd", "ST");
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildModel(new[] { member }, Array.Empty<LiveIedDataSetMemberModel>())
        };

        var first = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        var second = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        Assert.Equal(1, first.AddedCount);
        Assert.Equal(0, second.AddedCount);
        var signal = Assert.Single(device.Signals);
        Assert.Equal("IEDLD0/GGIO1.CBOpnd", signal.ObjectReference);
        Assert.False(signal.IsSelected);
    }

    private static LiveIedModelDiscoveryDocument BuildModel(
        IReadOnlyList<LiveIedDataSetMemberModel> digitalMembers,
        IReadOnlyList<LiveIedDataSetMemberModel> analogMembers)
    {
        var dataSets = new List<LiveIedDataSetModel>();
        if (digitalMembers.Count > 0)
        {
            dataSets.Add(new LiveIedDataSetModel
            {
                Reference = "IEDLD0/LLN0.Digital",
                Domain = "IEDLD0",
                LogicalNode = "LLN0",
                Name = "Digital",
                MemberCount = digitalMembers.Count,
                Members = digitalMembers
            });
        }
        if (analogMembers.Count > 0)
        {
            dataSets.Add(new LiveIedDataSetModel
            {
                Reference = "IEDLD0/LLN0.Analog",
                Domain = "IEDLD0",
                LogicalNode = "LLN0",
                Name = "Analog",
                MemberCount = analogMembers.Count,
                Members = analogMembers
            });
        }

        return new LiveIedModelDiscoveryDocument
        {
            Source = "LiveMmsDiscovery",
            IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "GGIO1",
                            LnClass = "GGIO",
                            LnInst = "1",
                            DataObjects = digitalMembers.Select(member => DataObject(member.Reference, "SPS")).ToArray()
                        },
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1",
                            LnClass = "MMXU",
                            LnInst = "1",
                            DataObjects = analogMembers.Select(member => DataObject(member.Reference, "MV")).ToArray()
                        }
                    }
                }
            },
            DataSets = dataSets
        };
    }

    private static LiveIedDataSetMemberModel Member(int index, string reference, string functionalConstraint)
    {
        var slash = reference.IndexOf('/');
        var domain = reference[..slash];
        var path = reference[(slash + 1)..];
        var firstDot = path.IndexOf('.');
        var logicalNode = path[..firstDot];
        var objectPath = path[(firstDot + 1)..].Replace('.', '$');
        return new LiveIedDataSetMemberModel
        {
            Index = index,
            Reference = reference,
            FunctionalConstraint = functionalConstraint,
            MmsReference = $"{domain}/{logicalNode}${functionalConstraint}${objectPath}",
            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
    }

    private static LiveIedDataObjectModel DataObject(string reference, string cdc)
        => new()
        {
            Reference = reference,
            Name = reference[(reference.LastIndexOf('.') + 1)..],
            InferredCdc = cdc,
            ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Low,
            Attributes = Array.Empty<LiveIedDataAttributeModel>()
        };
}
