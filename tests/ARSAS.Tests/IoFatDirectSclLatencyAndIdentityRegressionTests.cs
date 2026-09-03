using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Models.IoTesting;
using ArIED61850Tester.Services;
using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class IoFatDirectSclLatencyAndIdentityRegressionTests
{
    [Fact]
    public void StructuredStaticMember_KeepsDedicatedIdentityAndPublishesResolvedThdScalarToRuntime()
    {
        const string dataObject = "IEDLD0/I_MHAI1.ThdA";
        const string staticMember = dataObject + ".phsA";
        const string runtimeLeaf = staticMember + ".cVal.mag.f";
        const string dataSet = "IEDLD0/LLN0.Analog";

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
                                    Attributes = new[]
                                    {
                                        new LiveIedDataAttributeModel
                                        {
                                            ObjectReference = runtimeLeaf,
                                            AttributePath = "phsA.cVal.mag.f",
                                            FunctionalConstraint = "MX",
                                            MmsReference = "IEDLD0/I_MHAI1$MX$ThdA$phsA$cVal$mag$f",
                                            MmsItemName = "I_MHAI1$MX$ThdA$phsA$cVal$mag$f",
                                            SclBType = "FLOAT32",
                                            MmsType = "floating-point",
                                            Source = "SCL.DataTypeTemplates",
                                            TypeSource = "SCL.DataTypeTemplates",
                                            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
                                        }
                                    }
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
                    Reference = dataSet,
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = 1,
                    Members = new[]
                    {
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = staticMember,
                            FunctionalConstraint = "MX",
                            MmsReference = "IEDLD0/I_MHAI1$MX$ThdA$phsA",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    }
                }
            }
        };

        var genericScalar = new SignalDefinition
        {
            Name = "ThdA phsA mag",
            ObjectReference = runtimeLeaf,
            DisplayReference = runtimeLeaf,
            FunctionalConstraint = "MX",
            DataType = "FLOAT32",
            Category = "Measurement",
            Source = "Cached live model"
        };
        var signals = new List<SignalDefinition> { genericScalar };

        var merge = Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(signals, model);

        Assert.Equal(1, merge.MandatoryCatalogCount);
        Assert.Equal(1, merge.AddedCount);
        Assert.Equal(2, signals.Count);
        Assert.Equal(runtimeLeaf, genericScalar.DisplayReference);

        var membershipSignal = Assert.Single(signals.Where(signal =>
            signal.DisplayReference.Equals(staticMember, StringComparison.OrdinalIgnoreCase)));
        Assert.NotSame(genericScalar, membershipSignal);
        Assert.Equal(runtimeLeaf, membershipSignal.ObjectReference);
        Assert.Equal(dataSet, membershipSignal.DataSetReference);

        // A checked Engineering THD phase magnitude and the explicit static DataSet
        // membership row must both cross the SignalDefinition -> monitor-point runtime
        // gate. Harmonic spectra/configuration stay filtered; this exact FLOAT32 process
        // value is the operator-visible live measurement.
        Assert.True(genericScalar.CanPublishToRuntime);
        Assert.True(membershipSignal.IsExplicitDataSetRuntimeValue);
        Assert.True(membershipSignal.CanPublishAsSignal);
        Assert.True(membershipSignal.CanPublishToRuntime);

        var structuredParent = new SignalDefinition
        {
            Name = "ThdA",
            ObjectReference = dataObject,
            DisplayReference = dataObject,
            FunctionalConstraint = "MX",
            DataType = "structure",
            Category = "DataSet",
            DataSetReference = dataSet,
            Source = "ARIEC static DataSet parent"
        };
        Assert.False(structuredParent.IsExplicitDataSetRuntimeValue);
        // The object-level DataSet row is also a live composite value (phsA/B/C).
        // It remains distinct from a scalar member but must not stay Unknown in FAT.
        Assert.True(structuredParent.CanPublishToRuntime);

        var point = new IoTestPointPlan
        {
            TestPointId = "THD-A",
            IedName = "IED",
            IpAddress = "192.0.2.10",
            SignalName = "ThdA.phsA",
            ObjectReference = runtimeLeaf,
            FunctionalConstraint = "MX",
            ExpectedOnText = "Value 1",
            ExpectedOffText = "Value 2",
            SourceIecReference = staticMember,
            ReportDisplayReference = staticMember,
            EventLogSearchReference = staticMember,
            DataSetName = dataSet,
            BindingStatus = IoTestSignalSelectionService.SclDataSetAuthorityBindingStatus,
            ImportReady = true,
            TestEnabled = true,
            SignalKind = FatSignalKind.Analog,
            CaptureMode = FatCaptureMode.OperatorSnapshot
        };
        var ied = new IoTestIedPlan
        {
            IedName = "IED",
            IpAddress = "192.0.2.10",
            TestPoints = new List<IoTestPointPlan> { point }
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
        var match = Assert.Single(selection.Matches);
        Assert.Same(membershipSignal, match.Signal);
        Assert.True(match.Signal.CanPublishToRuntime);
        Assert.Empty(selection.MissingPoints);
        Assert.Empty(selection.AmbiguousPoints);
    }

    [Fact]
    public void DirectSclFat_ReconciliationCannotGateAcquisitionAtAll()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestReconciliationCache.cs"));
        var directSclGate = source.IndexOf("if (UsesDirectSclFatAuthority(device))", StringComparison.Ordinal);
        var connectedOwnerGate = source.IndexOf("if (!NativeIec61850Client.HasReconciliationOwner", StringComparison.Ordinal);

        Assert.True(directSclGate >= 0, "Direct SCL FAT must have an explicit nonblocking reconciliation path.");
        Assert.True(connectedOwnerGate > directSclGate,
            "Direct SCL FAT must return before native reconciliation ownership is considered.");
        Assert.Contains("Entries.TryRemove(device, out _);", source, StringComparison.Ordinal);
        Assert.Contains("return Task.CompletedTask;", source, StringComparison.Ordinal);
        Assert.Contains("UsesDirectSclFatAuthority", source, StringComparison.Ordinal);
        Assert.Contains("FAT SCL", source, StringComparison.Ordinal);

        var gateBodyStart = source.IndexOf('{', directSclGate);
        var gateBodyEnd = source.IndexOf('}', gateBodyStart + 1);
        var gateBody = source.Substring(gateBodyStart, gateBodyEnd - gateBodyStart + 1);
        Assert.DoesNotContain("RefreshModelOnlyAsync", gateBody, StringComparison.Ordinal);
        Assert.DoesNotContain("ReconcileConnectedAsync", gateBody, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
