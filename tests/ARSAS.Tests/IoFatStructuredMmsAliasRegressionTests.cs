using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class IoFatStructuredMmsAliasRegressionTests
{
    [Fact]
    public void CachedStructuredMmsContainerAlias_CannotAbsorbDistinctPhaseMembers()
    {
        const string dataObject = "IEDLD0/I_MHAI1.ThdA";
        const string sharedStructuredMms = "IEDLD0/I_MHAI1$MX$ThdA";
        var phases = new[] { "phsA", "phsB", "phsC" };
        var members = phases.Select(phase => dataObject + "." + phase).ToArray();
        var attributes = phases.Select(phase => new LiveIedDataAttributeModel
        {
            ObjectReference = dataObject + "." + phase + ".cVal.mag.f",
            AttributePath = phase + ".cVal.mag.f",
            FunctionalConstraint = "MX",
            // Reproduce the dangerous cache/report shape: several scalar semantic
            // descendants can be observed through one structured MMS container alias.
            MmsReference = sharedStructuredMms,
            MmsItemName = "I_MHAI1$MX$ThdA",
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            Source = "Regression structured MMS container",
            TypeSource = "Regression",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        }).ToArray();

        var model = new LiveIedModelDiscoveryDocument
        {
            Source = "SclWorkspace",
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
                            Name = "I_MHAI1",
                            LnClass = "MHAI",
                            LnInst = "1",
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = dataObject,
                                    Name = "ThdA",
                                    InferredCdc = "WYE",
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
                    Reference = "IEDLD0/LLN0.Analog",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = members.Length,
                    Members = members.Select((member, index) => new LiveIedDataSetMemberModel
                    {
                        Index = index,
                        Reference = member,
                        FunctionalConstraint = "MX",
                        MmsReference = "IEDLD0/I_MHAI1$MX$ThdA$" + phases[index],
                        Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                    }).ToArray()
                }
            }
        };

        // Fast reconnect can already contain a cached structured parent/container row.
        // It must remain separate from the static phase members imported for FAT.
        var signals = new List<SignalDefinition>
        {
            new()
            {
                Name = "ThdA",
                ObjectReference = sharedStructuredMms,
                DisplayReference = sharedStructuredMms,
                FunctionalConstraint = "MX",
                DataType = "structure",
                Source = "Saved model structured container"
            }
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, model);

        Assert.Equal(3, result.MandatoryCatalogCount);
        Assert.Equal(3, result.AddedCount);
        Assert.Equal(4, signals.Count);
        Assert.Contains(signals, signal => signal.ObjectReference == sharedStructuredMms);

        foreach (var phase in phases)
        {
            var staticMember = dataObject + "." + phase;
            var runtimeLeaf = staticMember + ".cVal.mag.f";
            var signal = Assert.Single(signals.Where(candidate =>
                candidate.DisplayReference.Equals(staticMember, StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(runtimeLeaf, signal.ObjectReference);
            Assert.NotEqual(sharedStructuredMms, signal.ObjectReference);
        }
    }
}
