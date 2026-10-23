using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticWholeFcdPhaseMeasurementTests
{
    [Fact]
    public void WholeFcd_ThirteenExactPhaseMagnitudes_AndScalarAuxV_AreReportEligible()
    {
        var model = Model();
        var device = new Iec61850MonitorDevice { Name = "IED", LiveDiscoveryModel = model };
        var inventory = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        Assert.Equal(4, inventory.MandatoryCatalogCount);

        var expected = new[]
        {
            "A.phsA", "A.phsB", "A.phsC", "A.neut", "A.net", "A.res",
            "PhV.phsA", "PhV.phsB", "PhV.phsC", "PhV.neut",
            "PPV.phsAB", "PPV.phsBC", "PPV.phsCA"
        }.Select(p => "IEDLD0/MMXU1." + p + ".cVal.mag.f").ToArray();

        var selected = Iec61850StaticDataSetAuthoritySelection.Build(device);
        foreach (var reference in expected)
        {
            var signal = Assert.Single(device.Signals, signal =>
                signal.ObjectReference.Equals(reference, StringComparison.OrdinalIgnoreCase) &&
                signal.DataSetReference.Equals("IEDLD0/LLN0.Analog", StringComparison.OrdinalIgnoreCase));
            Assert.True(signal.CanPublishToRuntime, signal.ObjectReference);
            Assert.Equal("FLOAT32", signal.DataType);
            Assert.EndsWith(".q", signal.QualityReference, StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith(".t", signal.TimestampReference, StringComparison.OrdinalIgnoreCase);
            Assert.Contains(selected, chosen => ReferenceEquals(signal, chosen));
        }
        Assert.Equal(13, selected.Count(signal => expected.Contains(signal.ObjectReference, StringComparer.OrdinalIgnoreCase)));
        Assert.Contains(selected, signal =>
            signal.ObjectReference.Equals("IEDLD0/MMXU1.AuxV.cVal.mag.f", StringComparison.OrdinalIgnoreCase));

        Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device);
        foreach (var reference in expected)
            Assert.Single(device.Signals, signal =>
                signal.ObjectReference.Equals(reference, StringComparison.OrdinalIgnoreCase) &&
                signal.DataSetReference.Equals("IEDLD0/LLN0.Analog", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void WholeFcd_ReorderedTypedAttributes_MissingPhase_NotInvented()
    {
        var original = Model();
        var objectA = original.LogicalDevices[0].LogicalNodes[0].DataObjects[0];
        var sparse = new LiveIedDataObjectModel
        {
            Reference = objectA.Reference, Name = objectA.Name, InferredCdc = objectA.InferredCdc,
            Attributes = objectA.Attributes
                .Where(attribute => !attribute.AttributePath.StartsWith("phsB.", StringComparison.OrdinalIgnoreCase))
                .Reverse()
                .ToArray()
        };
        var rebuilt = Model(sparse);
        var resolved = Iec61850StaticPhaseLeafProjection.Resolve(rebuilt, sparse.Reference, "MX");
        Assert.Equal(5, resolved.Count);
        Assert.DoesNotContain(resolved, leaf => leaf.Phase == "phsB");
        Assert.Empty(Iec61850StaticPhaseLeafProjection.Resolve(rebuilt, sparse.Reference, "CO"));
        Assert.Empty(Iec61850StaticPhaseLeafProjection.Resolve(rebuilt, sparse.Reference + ".phsA", "MX"));
    }

    private static LiveIedModelDiscoveryDocument Model(LiveIedDataObjectModel? overrideA = null)
    {
        const string prefix = "IEDLD0/MMXU1.";
        LiveIedDataObjectModel Object(string name, string cdc, params string[] phases) => new()
        {
            Reference = prefix + name, Name = name, InferredCdc = cdc,
            Attributes = phases.SelectMany(phase => new[]
            {
                Attribute(prefix + name + "." + phase + ".instCVal.mag.f", phase + ".instCVal.mag.f"),
                Attribute(prefix + name + "." + phase + ".q", phase + ".q", "Quality"),
                Attribute(prefix + name + "." + phase + ".cVal.mag.f", phase + ".cVal.mag.f"),
                Attribute(prefix + name + "." + phase + ".t", phase + ".t", "Timestamp")
            }).ToArray()
        };
        var objects = new[]
        {
            overrideA ?? Object("A", "WYE", "phsA", "phsB", "phsC", "neut", "net", "res"),
            Object("PhV", "WYE", "phsA", "phsB", "phsC", "neut"),
            Object("PPV", "DEL", "phsAB", "phsBC", "phsCA"),
            new LiveIedDataObjectModel
            {
                Reference = prefix + "AuxV", Name = "AuxV", InferredCdc = "CMV",
                Attributes = new[]
                {
                    Attribute(prefix + "AuxV.instCVal.mag.f", "instCVal.mag.f"),
                    Attribute(prefix + "AuxV.cVal.mag.f", "cVal.mag.f")
                }
            }
        };
        return new LiveIedModelDiscoveryDocument
        {
            Source = "Synthetic SCL", IedName = "IED",
            LogicalDevices = new[]
            {
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0", Inst = "LD0",
                    LogicalNodes = new[]
                    {
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MMXU1", LnClass = "MMXU", LnInst = "1", DataObjects = objects
                        }
                    }
                }
            },
            DataSets = new[]
            {
                new LiveIedDataSetModel
                {
                    Reference = "IEDLD0/LLN0.Analog", Domain = "IEDLD0",
                    LogicalNode = "LLN0", Name = "Analog", MemberCount = 4,
                    Members = objects.Select((obj, index) => new LiveIedDataSetMemberModel
                    {
                        Index = index, Reference = obj.Reference, FunctionalConstraint = "MX",
                        MmsReference = "IEDLD0/MMXU1$MX$" + obj.Name,
                        Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                    }).ToArray()
                }
            },
            ReportControls = new[]
            {
                new LiveIedReportControlModel
                {
                    Reference = "IEDLD0/LLN0.RP.Unbuffer01",
                    DataSetReference = "IEDLD0/LLN0.Analog", Buffered = false
                }
            }
        };
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path, string type = "FLOAT32")
        => new()
        {
            ObjectReference = reference, AttributePath = path, FunctionalConstraint = "MX",
            SclBType = type, MmsType = type == "FLOAT32" ? "floating-point" : "",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact,
            TypeSource = "SCL.DataTypeTemplates", Source = "SCL.DataTypeTemplates"
        };
}
