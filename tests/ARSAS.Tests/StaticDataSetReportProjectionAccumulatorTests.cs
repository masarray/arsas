using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetReportProjectionAccumulatorTests
{
    [Fact]
    public void ThdA_SemanticLeaves_ReconstructOneStaticParentValue()
    {
        const string parent = "IEDLD0/I_MHAI1.ThdA";
        var model = Model(DataObject(
            parent,
            "ThdA",
            Attribute(parent + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(parent + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(parent + ".phsC.cVal.mag.f", "phsC.cVal.mag.f")));
        var point = Point(parent);
        var accumulator = new StaticDataSetReportProjectionAccumulator();

        Assert.Empty(accumulator.Project(model, new[] { point }, Update(parent + ".phsA.cVal.mag.f", "1.25")));
        Assert.Empty(accumulator.Project(model, new[] { point }, Update(parent + ".phsB.cVal.mag.f", "2.5")));

        var projected = Assert.Single(accumulator.Project(
            model,
            new[] { point },
            Update(parent + ".phsC.cVal.mag.f", "3.75")));

        Assert.Equal(parent, projected.Reference, ignoreCase: true);
        Assert.Equal("A=1.25, B=2.5, C=3.75", projected.Value);
        Assert.Equal("good", projected.Quality, ignoreCase: true);
        Assert.Equal("2026-09-04 16:00:00.000", projected.Timestamp);
        Assert.Equal("schema-safe-report-three-phase-aggregate", projected.ProjectionStatus);
        Assert.True(projected.HasValue);
        Assert.True(projected.HasQuality);
        Assert.True(projected.HasTimestamp);
    }

    [Fact]
    public void ThdA_InstantaneousSibling_DoesNotOverrideCanonicalCValPlan()
    {
        const string parent = "IEDLD0/I_MHAI1.ThdA";
        var model = Model(DataObject(
            parent,
            "ThdA",
            Attribute(parent + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(parent + ".phsA.instCVal.mag.f", "phsA.instCVal.mag.f"),
            Attribute(parent + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(parent + ".phsC.cVal.mag.f", "phsC.cVal.mag.f")));
        var accumulator = new StaticDataSetReportProjectionAccumulator();

        Assert.Empty(accumulator.Project(
            model,
            new[] { Point(parent) },
            Update(parent + ".phsA.instCVal.mag.f", "99")));
    }

    [Fact]
    public void DmdWhMV_ExactMagnitude_ReconstructsParentWithoutMmsRead()
    {
        const string parent = "IEDLD0/XPRE_MMTR1.DmdWhMV";
        var model = Model(DataObject(
            parent,
            "DmdWhMV",
            Attribute(parent + ".instMag.f", "instMag.f"),
            Attribute(parent + ".mag.f", "mag.f")));
        var accumulator = new StaticDataSetReportProjectionAccumulator();

        Assert.Empty(accumulator.Project(
            model,
            new[] { Point(parent) },
            Update(parent + ".instMag.f", "9.9")));

        var projected = Assert.Single(accumulator.Project(
            model,
            new[] { Point(parent) },
            Update(parent + ".mag.f", "1.055")));

        Assert.Equal(parent, projected.Reference, ignoreCase: true);
        Assert.Equal("1.055", projected.Value);
        Assert.Equal("schema-safe-report-demand-energy-aggregate", projected.ProjectionStatus);
    }

    [Fact]
    public void ExactSelectedChild_IsPreservedAlongsideCompletedParentAggregate()
    {
        const string parent = "IEDLD0/I_MHAI1.ThdA";
        const string child = "IEDLD0/I_MHAI1.ThdA.phsC.cVal.mag.f";
        var model = Model(DataObject(
            parent,
            "ThdA",
            Attribute(parent + ".phsA.cVal.mag.f", "phsA.cVal.mag.f"),
            Attribute(parent + ".phsB.cVal.mag.f", "phsB.cVal.mag.f"),
            Attribute(child, "phsC.cVal.mag.f")));
        var parentPoint = Point(parent);
        var childPoint = Point(child);
        var accumulator = new StaticDataSetReportProjectionAccumulator();

        accumulator.Project(model, new[] { parentPoint, childPoint }, Update(parent + ".phsA.cVal.mag.f", "1"));
        accumulator.Project(model, new[] { parentPoint, childPoint }, Update(parent + ".phsB.cVal.mag.f", "2"));
        var updates = accumulator.Project(model, new[] { parentPoint, childPoint }, Update(child, "3"));

        Assert.Equal(2, updates.Count);
        Assert.Contains(updates, update => update.Reference.Equals(child, StringComparison.OrdinalIgnoreCase));
        Assert.Contains(updates, update => update.Reference.Equals(parent, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuntimeSource_StaticMode_UsesSchemaSafeReportAuthority_AndNoMmsFallback()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));

        Assert.Contains("StaticDataSetReportProjectionAccumulator StaticReportProjection", source, StringComparison.Ordinal);
        Assert.Contains("!session.StaticDataSetReportOnly && RequiresExactMmsValueAuthority", source, StringComparison.Ordinal);
        Assert.Contains("HasSchemaProvenReportValueAuthority(point, update)", source, StringComparison.Ordinal);
        Assert.Contains("Engine fallback candidates are diagnostic only and are not scheduled as MMS process polling", source, StringComparison.Ordinal);
        Assert.DoesNotContain("structured report projection is not yet schema-proven; unsafe MMS fallback is disabled", source, StringComparison.Ordinal);
    }

    private static NativeReportValueUpdate Update(string reference, string value)
        => new()
        {
            Reference = reference,
            FunctionalConstraint = "MX",
            Value = value,
            Quality = "good",
            Timestamp = "2026-09-04 16:00:00.000",
            Reason = "integrity",
            ProjectionStatus = "semantic-structured-leaf",
            HasValue = true,
            HasQuality = true,
            HasTimestamp = true,
            DataSetReference = "IEDLD0/LLN0.Analog",
            ReportControlReference = "IEDLD0/LLN0.RP.Unbuffer02",
            UpdatedAt = new DateTimeOffset(2026, 9, 4, 16, 0, 0, TimeSpan.FromHours(7))
        };

    private static Iec61850MonitorPoint Point(string reference)
        => new()
        {
            DeviceId = "IED",
            DeviceName = "IED",
            SignalName = reference.Split('.').Last(),
            IecReference = reference,
            FunctionalConstraint = "MX",
            IecDataType = "FLOAT32",
            Category = "DataSet",
            DataSetReference = "IEDLD0/LLN0.Analog"
        };

    private static LiveIedModelDiscoveryDocument Model(params LiveIedDataObjectModel[] objects)
        => new()
        {
            Source = "Static report regression",
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
                            Name = "MHAI1",
                            LnClass = "MHAI",
                            LnInst = "1",
                            DataObjects = objects
                        }
                    ]
                }
            ]
        };

    private static LiveIedDataObjectModel DataObject(
        string reference,
        string name,
        params LiveIedDataAttributeModel[] attributes)
        => new()
        {
            Reference = reference,
            Name = name,
            InferredCdc = "MV",
            Attributes = attributes
        };

    private static LiveIedDataAttributeModel Attribute(string reference, string path)
        => new()
        {
            ObjectReference = reference,
            AttributePath = path,
            FunctionalConstraint = "MX",
            MmsReference = reference.Replace('.', '$'),
            SclBType = "FLOAT32",
            MmsType = "floating-point",
            Source = "SCL.DataTypeTemplates",
            TypeSource = "SCL.DataTypeTemplates",
            TypeConfidence = LiveIedDiscoveryConfidenceLevel.Exact
        };

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
