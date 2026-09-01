using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;

namespace ARSAS.Tests;

public sealed class SemanticReportProductionWiringRegressionTests
{
    [Fact]
    public void ProductionReceivePath_UsesModelBackedSemanticOverlay()
    {
        var native = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var helper = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.SemanticReporting.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);
        var hybrid = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.HybridReporting.cs"))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

        var receiveStart = native.IndexOf(
            "public async Task<NativeReportMonitorSliceResult> ReceiveReportMonitorSliceAsync",
            StringComparison.Ordinal);
        var receiveEnd = native.IndexOf(
            "public async Task<IReadOnlyList<NativeReportMonitorStopResult>> StopReportMonitorsAsync",
            receiveStart,
            StringComparison.Ordinal);
        Assert.True(receiveStart >= 0 && receiveEnd > receiveStart);
        var receive = native[receiveStart..receiveEnd];

        Assert.Contains("var projection = ProjectReportValue(report);", receive, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsReportValueProjector.Project(report)", receive, StringComparison.Ordinal);
        Assert.Contains("MmsSemanticReportValueProjector.Project", helper, StringComparison.Ordinal);
        Assert.Contains("MmsReportSemanticProjectionContext.Create", helper, StringComparison.Ordinal);
        Assert.Contains("_semanticReportProjectionAuthorityModel ?? _liveModel", helper, StringComparison.Ordinal);
        Assert.Contains("REPORT_SEMANTIC_FALLBACK", helper, StringComparison.Ordinal);
        Assert.Contains("SetSemanticReportProjectionAuthority(planningModel);", hybrid, StringComparison.Ordinal);
        Assert.Contains("ResetSemanticReportProjectionContext();", native, StringComparison.Ordinal);
    }

    [Fact]
    public void PinnedEngine_FansOutWholeThreePhaseStructuredMember_WithoutChoosingPrimaryPhase()
    {
        const string objectReference = "IEDLD0/MHAI1.ThdA";
        const string dataSetReference = "IEDLD0/LLN0.Analog";
        var attributes = new[]
        {
            Attribute(objectReference + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(objectReference + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(objectReference + ".phsC.cVal.mag.f", "phsC.cVal.mag.f")
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            Source = "ARSAS regression",
            IedName = "IED",
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "IEDLD0",
                    LogicalNodes =
                    [
                        new LiveIedLogicalNodeModel
                        {
                            Name = "MHAI1",
                            LnClass = "MHAI",
                            LnInst = "1",
                            DataObjects =
                            [
                                new LiveIedDataObjectModel
                                {
                                    Reference = objectReference,
                                    Name = "ThdA",
                                    InferredCdc = "WYE",
                                    Attributes = attributes
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
                    Reference = dataSetReference,
                    Domain = "IEDLD0",
                    LogicalNode = "LLN0",
                    Name = "Analog",
                    MemberCount = 1,
                    Members =
                    [
                        new LiveIedDataSetMemberModel
                        {
                            Index = 0,
                            Reference = objectReference,
                            FunctionalConstraint = "MX",
                            MmsReference = "IEDLD0/MHAI1$MX$ThdA",
                            Confidence = LiveIedDiscoveryConfidenceLevel.Exact
                        }
                    ]
                }
            ]
        };

        var binding = Assert.Single(Iec61850DataSetSemanticBindingResolver.Resolve(model).Members);
        Assert.Equal(LiveIedDataSetMemberResolutionStatus.Ambiguous, binding.ResolutionStatus);
        Assert.Null(binding.PrimaryValue);

        var frame = new MmsReportFrame
        {
            ReceivedAt = new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            Header = new MmsReportHeader { DataSetReference = dataSetReference },
            Values =
            [
                new MmsReportValue
                {
                    Index = 0,
                    Member = new MmsDataSetDirectoryMember
                    {
                        UserReference = objectReference,
                        FunctionalConstraint = "MX"
                    },
                    Value = MmsDataValue.Structure([Phase(1.25), Phase(2.5), Phase(3.75)]),
                    ReasonForInclusion = ["data-change"]
                }
            ]
        };

        var projection = MmsSemanticReportValueProjector.Project(
            frame,
            MmsReportSemanticProjectionContext.Create(model));

        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsA.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsB.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(projection.Updates, update =>
            update.Reference.Equals(objectReference + ".phsC.cVal.mag.f", StringComparison.OrdinalIgnoreCase));
        Assert.All(projection.Updates, update =>
            Assert.Equal("semantic-structured-leaf", update.ProjectionStatus));
        Assert.DoesNotContain(projection.Updates, update =>
            update.Reference.Equals(objectReference, StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(projection.Warnings, warning =>
            warning.StartsWith("REPORT_RAW_STRUCT:", StringComparison.OrdinalIgnoreCase));
    }

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            MmsReference = reference.Replace('.', '$'),
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            Source = "ARSAS regression",
            TypeSource = "ARSAS regression",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

    private static MmsDataValue Phase(double value)
        => MmsDataValue.Structure(
        [
            MmsDataValue.Structure(
            [
                MmsDataValue.Structure([MmsDataValue.FloatingPoint(value)])
            ])
        ]);

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

        throw new FileNotFoundException(relativePath);
    }
}
