using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class DataSetSignalInventoryTests
{
    [Fact]
    public void MandatoryPrimaryDataSetSignal_IsPresentBeforeUserSelection()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildDataSetModel()
        };

        Assert.Empty(device.Signals);

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        var signal = Assert.Single(device.Signals);
        Assert.Equal(1, result.MandatoryCatalogCount);
        Assert.Equal(1, result.AddedCount);
        Assert.Equal("IEDLD0/GGIO1.Ind1.stVal", signal.ObjectReference);
        Assert.Equal("IEDLD0/LLN0.Events", signal.DataSetReference);
        Assert.Equal("ST", signal.FunctionalConstraint);
        Assert.False(signal.IsSelected);
        Assert.True(signal.IsReportCapable);
        Assert.Equal("Not probed", signal.ProbeStatus);
        Assert.Contains("mandatory primary DataSet signal", signal.ReportCoverageReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReopeningSignalSelection_DoesNotDuplicateMandatoryDataSetSignal()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildDataSetModel()
        };

        var first = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        var second = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        Assert.Equal(1, first.AddedCount);
        Assert.Equal(0, second.AddedCount);
        Assert.Single(device.Signals);
        Assert.False(device.Signals[0].IsSelected);
    }

    [Fact]
    public void ExistingDiscoveredSignal_IsEnrichedInsteadOfDuplicated()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildDataSetModel()
        };
        device.Signals.Add(new SignalDefinition
        {
            Name = "Ind1",
            ObjectReference = "IEDLD0/GGIO1.Ind1.stVal",
            FunctionalConstraint = "ST",
            DataType = "Boolean",
            Category = "Status",
            IsSelected = false
        });

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        var signal = Assert.Single(device.Signals);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.EnrichedExistingCount);
        Assert.Equal("IEDLD0/LLN0.Events", signal.DataSetReference);
        Assert.True(signal.IsReportCapable);
        Assert.False(signal.IsSelected);
    }

    [Fact]
    public void ExistingMmsStyleReference_MatchesLiteralEngineReferenceWithoutAppCanonicalization()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildDataSetModel()
        };
        device.Signals.Add(new SignalDefinition
        {
            Name = "Ind1",
            ObjectReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
            FunctionalConstraint = "ST",
            DataType = "Boolean",
            Category = "Status",
            IsSelected = false
        });

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        var signal = Assert.Single(device.Signals);
        Assert.Equal(0, result.AddedCount);
        Assert.Equal(1, result.EnrichedExistingCount);
        Assert.Equal("IEDLD0/GGIO1$ST$Ind1$stVal", signal.ObjectReference);
        Assert.Equal("IEDLD0/LLN0.Events", signal.DataSetReference);
    }

    [Fact]
    public void StructuredPhaseMember_UsesExactNamedCanonicalMagnitudeWithoutGuessing()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = BuildStructuredPhaseDataSetModel()
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        var signal = Assert.Single(device.Signals);
        Assert.Equal(1, result.MandatoryCatalogCount);
        Assert.Equal("IEDLD0/MMXU1.A.phsA.cVal.mag.f", signal.ObjectReference);
        Assert.Equal("IEDLD0/MMXU1.A.phsA", signal.DisplayReference);
        Assert.Equal("FLOAT32", signal.DataType);
        Assert.Equal("IEDLD0/LLN0.Analog", signal.DataSetReference);
        Assert.True(signal.CanPublishToRuntime);

        // The semantic resolver may already have promoted this exact named scalar leaf
        // before the application-side schema fallback runs. Provenance wording therefore
        // is not the contract; exact member identity, exact scalar runtime binding and
        // publishability are.
        Assert.Contains("mandatory static DataSet member", signal.Source, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("primary leaf unresolved", signal.ProbeStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void NonDataSetSignal_IsNotPromotedIntoMandatoryInventory()
    {
        var model = BuildDataSetModel(includeNonDataSetAttribute: true);
        var device = new Iec61850MonitorDevice
        {
            Name = "IED",
            LiveDiscoveryModel = model
        };

        var result = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);

        Assert.Equal(1, result.MandatoryCatalogCount);
        Assert.Single(device.Signals);
        Assert.DoesNotContain(device.Signals, signal =>
            signal.ObjectReference.Equals("IEDLD0/GGIO1.Beh.stVal", StringComparison.OrdinalIgnoreCase));
    }

    private static LiveIedModelDiscoveryDocument BuildStructuredPhaseDataSetModel()
    {
        const string dataObject = "IEDLD0/MMXU1.A";
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
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = dataObject,
                                    Name = "A",
                                    InferredCdc = "WYE",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
                                    Attributes =
                                    [
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = dataObject + ".phsA.cVal.mag.f",
                                            AttributePath = "phsA.cVal.mag.f",
                                            FunctionalConstraint = "MX",
                                            SclBType = "FLOAT32",
                                            MmsType = "floating-point",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
                                            Source = "LiveMmsTypeSpecification"
                                        },
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = dataObject + ".phsA.instCVal.mag.f",
                                            AttributePath = "phsA.instCVal.mag.f",
                                            FunctionalConstraint = "MX",
                                            SclBType = "FLOAT32",
                                            MmsType = "floating-point",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
                                            Source = "LiveMmsTypeSpecification"
                                        }
                                    ]
                                }
                            ]
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
                    MemberCount = 1,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = dataObject + ".phsA",
                            FunctionalConstraint = "MX",
                            MmsReference = "IEDLD0/MMXU1$MX$A$phsA",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    ]
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

    private static LiveIedModelDiscoveryDocument BuildDataSetModel(bool includeNonDataSetAttribute = false)
    {
        var attributes = new List<LiveIedDataAttributeModel>
        {
            new()
            {
                ObjectReference = "IEDLD0/GGIO1.Ind1.stVal",
                AttributePath = "stVal",
                FunctionalConstraint = "ST",
                MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                MmsItemName = "GGIO1$ST$Ind1$stVal",
                SclBType = "BOOLEAN",
                MmsType = "boolean",
                Source = "LiveMmsDiscovery"
            }
        };

        if (includeNonDataSetAttribute)
        {
            attributes.Add(new LiveIedDataAttributeModel
            {
                ObjectReference = "IEDLD0/GGIO1.Beh.stVal",
                AttributePath = "stVal",
                FunctionalConstraint = "ST",
                MmsReference = "IEDLD0/GGIO1$ST$Beh$stVal",
                MmsItemName = "GGIO1$ST$Beh$stVal",
                SclBType = "Enum",
                MmsType = "integer",
                Source = "LiveMmsDiscovery"
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
                            DataObjects = new[]
                            {
                                new LiveIedDataObjectModel
                                {
                                    Reference = "IEDLD0/GGIO1.Ind1",
                                    Name = "Ind1",
                                    InferredCdc = "SPS",
                                    ConfidenceLevel = LiveIedDiscoveryConfidenceLevel.Exact,
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
                    Reference = "IEDLD0/LLN0.Events",
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Events",
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = "IEDLD0/GGIO1.Ind1.stVal",
                            FunctionalConstraint = "ST",
                            MmsReference = "IEDLD0/GGIO1$ST$Ind1$stVal",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    }
                }
            }
        };
    }
}
