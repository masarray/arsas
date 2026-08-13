using System.Buffers.Binary;
using System.Globalization;
using AR.Iec61850.Binding;
using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using AR.Iec61850.Scl.Workspace;
using ArIED61850Tester;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class Iec61850EngineTimestampIntegrationTests
{
    [Fact]
    public void PinnedEngine_Preserves_2006000_Fraction_And_Arsas_Rounds_It_To_201ms()
    {
        var seconds = new DateTimeOffset(2026, 8, 13, 10, 0, 31, TimeSpan.Zero).ToUnixTimeSeconds();
        Span<byte> bytes = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(bytes[..4], checked((uint)seconds));

        // IEC 61850 fractional-second bytes from the engine regression case:
        // 0x335A86 / 2^24 = 0.200600028... s -> 31.2006000 at .NET tick resolution.
        bytes[4] = 0x33;
        bytes[5] = 0x5A;
        bytes[6] = 0x86;
        bytes[7] = 0x00;

        var utc = Iec61850UtcTime.FromBytes(bytes);
        var decoded = Iec61850TimestampDecoder.Decode(MmsDataValue.UtcTime(utc));

        Assert.True(decoded.IsDecoded);
        Assert.True(decoded.DisplayTime.EndsWith("31.2006000", StringComparison.Ordinal), decoded.DisplayTime);

        Assert.True(
            DateTime.TryParseExact(
                decoded.DisplayTime,
                "yyyy-MM-dd HH:mm:ss.fffffff",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var parsed),
            decoded.DisplayTime);

        Assert.Equal(
            "31.201",
            Iec61850TimestampPresentation.FormatMilliseconds(parsed, "ss.fff"));
    }
}

public sealed class Iec61850DataSetSemanticIntegrationTests
{
    [Fact]
    public void SclMapper_UsesEngineBcrBinding_ForFcdEnergyMember()
    {
        const string dataSetReference = "IEDLD/LLN0$DS$Energy";
        var objectReference = "IEDLD/MMTR1.SupWh";
        var workspace = BuildWorkspace(
            "MMTR1",
            "MMTR",
            new LiveIedDataObjectModel
            {
                Reference = objectReference,
                Name = "SupWh",
                InferredCdc = "BCR",
                Attributes = new[]
                {
                    Attribute(objectReference, "actVal", "INT64"),
                    Attribute(objectReference, "frVal", "INT64"),
                    Attribute(objectReference, "q", "Quality"),
                    Attribute(objectReference, "t", "Timestamp")
                }
            },
            dataSetReference,
            "IED/LD/MMTR1.SupWh [ST]");

        var signal = Assert.Single(SclWorkspaceSignalMapper.BuildSignals(workspace));

        Assert.Equal("IEDLD/MMTR1.SupWh.actVal", signal.ObjectReference);
        Assert.Equal(dataSetReference, signal.DataSetReference);
        Assert.Equal("IEDLD/MMTR1.SupWh.q", signal.QualityReference);
        Assert.Equal("IEDLD/MMTR1.SupWh.t", signal.TimestampReference);
    }

    [Fact]
    public void SclMapper_StaticDataSetPrimaryValue_OverridesGenericVisibilityPruning()
    {
        const string dataSetReference = "IEDLD/LLN0$DS$Temperature";
        var objectReference = "IEDLD/TTMP1.TmpAlm";
        var workspace = BuildWorkspace(
            "TTMP1",
            "TTMP",
            new LiveIedDataObjectModel
            {
                Reference = objectReference,
                Name = "TmpAlm",
                InferredCdc = "SPS",
                Attributes = new[]
                {
                    Attribute(objectReference, "stVal", "BOOLEAN"),
                    Attribute(objectReference, "q", "Quality"),
                    Attribute(objectReference, "t", "Timestamp")
                }
            },
            dataSetReference,
            "IED/LD/TTMP1.TmpAlm [ST]");

        var signal = Assert.Single(SclWorkspaceSignalMapper.BuildSignals(workspace));

        Assert.Equal("IEDLD/TTMP1.TmpAlm.stVal", signal.ObjectReference);
        Assert.Equal(dataSetReference, signal.DataSetReference);
        Assert.Equal("IEDLD/TTMP1.TmpAlm.q", signal.QualityReference);
        Assert.Equal("IEDLD/TTMP1.TmpAlm.t", signal.TimestampReference);
    }

    private static SclIedWorkspace BuildWorkspace(
        string logicalNodeName,
        string logicalNodeClass,
        LiveIedDataObjectModel dataObject,
        string dataSetReference,
        string memberReference)
        => new()
        {
            IedName = "IED",
            DesignModel = new LiveIedModelDiscoveryDocument
            {
                IedName = "IED",
                Source = "SclOfflineProjection",
                LogicalDevices = new[]
                {
                    new LiveIedLogicalDeviceModel
                    {
                        MmsDomain = "IEDLD",
                        Inst = "LD",
                        LogicalNodes = new[]
                        {
                            new LiveIedLogicalNodeModel
                            {
                                Name = logicalNodeName,
                                LnClass = logicalNodeClass,
                                DataObjects = new[] { dataObject }
                            }
                        }
                    }
                },
                DataSets = new[]
                {
                    new LiveIedDataSetModel
                    {
                        Reference = dataSetReference,
                        Members = new[]
                        {
                            new LiveIedDataSetMemberModel
                            {
                                Index = 1,
                                Reference = memberReference,
                                FunctionalConstraint = "ST"
                            }
                        }
                    }
                }
            }
        };

    private static LiveIedDataAttributeModel Attribute(string objectReference, string path, string bType)
        => new()
        {
            ObjectReference = $"{objectReference}.{path}",
            AttributePath = path,
            FunctionalConstraint = "ST",
            SclBType = bType,
            Source = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };
}
