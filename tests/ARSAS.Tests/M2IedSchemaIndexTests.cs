using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M2IedSchemaIndexTests
{
    private const string Position = "LD/CSWI1.Pos";
    private const string DataSet = "LD/LLN0.DS";

    private static LiveIedDataAttributeModel Attribute(string leaf, string fc = "ST")
        => new()
        {
            ObjectReference = Position, AttributePath = "Pos." + leaf,
            FunctionalConstraint = fc,
            MmsReference = "LD/CSWI1$" + fc + "$Pos$" + leaf.Replace('.', '$')
        };

    private static LiveIedModelDiscoveryDocument Model(
        IReadOnlyList<LiveIedDataAttributeModel>? attributes = null,
        IReadOnlyList<LiveIedDataSetMemberModel>? members = null)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "CSWI1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "Pos", Reference = Position,
                        Attributes = attributes?.ToList() ?? [Attribute("stVal"), Attribute("q")]
                    }]
                }]
            }],
            DataSets = [new LiveIedDataSetModel
            {
                Name = "DS", Reference = DataSet,
                Members = members?.ToList() ??
                    [new LiveIedDataSetMemberModel
                    {
                        Index = 0, Reference = Position, FunctionalConstraint = "ST"
                    }]
            }]
        };

    [Fact]
    public void ReportFc_IsResolvedThroughDeclaredParentAncestry_NotNamePrefixes()
    {
        var index = new IedBrowserSchemaIndex(Model());
        Assert.True(index.TryResolveReportFc(DataSet, Position + ".stVal", out var fc));
        Assert.Equal("ST", fc);
        Assert.True(index.TryResolveReportFc("LD/LLN0.DS", "LD/CSWI1$ST$Pos$q", out fc));
        Assert.Equal("ST", fc);
        Assert.False(index.TryResolveReportFc(DataSet, "LD/CSWI10.Pos.stVal", out _));
        Assert.False(index.TryResolveReportFc(DataSet, "LD/cswi1.Pos.stVal", out _));
        Assert.False(index.TryResolveReportFc("LD/LLN0.ds", Position + ".stVal", out _));
        Assert.False(index.TryResolveReportFc(DataSet, "LD/CSWI1.Position.stVal", out _));
        Assert.False(index.TryResolveReportFc("LD/LLN0.Other", Position + ".stVal", out _));
    }

    [Fact]
    public void OverlappingMembershipWithConflictingFc_FailsClosed()
    {
        var model = Model(members:
        [
            new() { Reference = Position, FunctionalConstraint = "ST" },
            new() { Reference = Position + ".stVal", FunctionalConstraint = "MX" }
        ]);
        var index = new IedBrowserSchemaIndex(model);
        Assert.False(index.TryResolveReportFc(DataSet, Position + ".stVal", out _));
        Assert.True(index.TryResolveReportFc(DataSet, Position + ".q", out var fc));
        Assert.Equal("ST", fc);
        // Previously Read admitted either FC here while Report rejected
        // both. The same ambiguous FCD/FCDA ancestry must now fail closed.
        Assert.False(index.ContainsDataSetMember(DataSet, Position + ".stVal", "ST"));
        Assert.False(index.ContainsDataSetMember(DataSet, Position + ".stVal", "MX"));
        Assert.False(index.ContainsDataSetMember(DataSet, "LD/CSWI10.Pos", "ST"));
    }

    [Fact]
    public void SchemaProvenSingleScalarAlias_DoesNotGuessUndeclaredLeaves()
    {
        var model = Model();
        var index = new IedBrowserSchemaIndex(model);
        var ledger = new IedBrowserLiveValues();
        var assoc = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(assoc, Position + ".stVal", "ST",
            "Open [01]", "", "", "Report"));

        // Without explicit source schema, a DO name is not proof of any DA value.
        Assert.False(ledger.TryGet(Position, "ST", out _));
        Assert.True(ledger.TryGet(Position, "ST", out var value, schema: index));
        Assert.Equal("Open [01]", value.Value);
        Assert.False(ledger.TryGet(Position + ".q", "ST", out _, schema: index));
        Assert.False(ledger.TryGet("LD/CSWI10.Pos", "ST", out _, schema: index));
        Assert.False(ledger.TryGet(Position, "MX", out _, schema: index));

        Assert.True(index.TryGetPrimaryLeaf(Position, "ST", out var leaf));
        Assert.Equal(Position + ".stVal", leaf);
        Assert.False(index.TryGetPrimaryLeaf("LD/CSWI1.Position", "ST", out _));
        Assert.False(index.TryGetPrimaryLeaf(Position + ".q", "ST", out _));
    }

    [Fact]
    public void MultipleProcessLeavesOfSameFc_ProhibitDoScalarAlias()
    {
        var model = Model(attributes:
            [Attribute("stVal"), Attribute("posVal"), Attribute("q")]);
        var index = new IedBrowserSchemaIndex(model);
        Assert.False(index.TryGetPrimaryLeaf(Position, "ST", out _));
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        ledger.Publish(Position + ".stVal", "ST", "Open [01]", "", "", "Report");
        Assert.False(ledger.TryGet(Position, "ST", out _, schema: index));
        Assert.True(ledger.TryGet(Position + ".stVal", "ST", out _, schema: index));
    }

    [Fact]
    public void QualityCompanion_RequiresExactOwningDoAndMatchingFc()
    {
        var model = Model(attributes:
            [Attribute("stVal"), Attribute("q"), Attribute("mag.f", "MX")]);
        var index = new IedBrowserSchemaIndex(model);
        Assert.True(index.TryGetQualityCompanion(Position + ".stVal", "ST", out var quality));
        Assert.Equal(Position + ".q", quality);
        Assert.True(index.TryGetQualityCompanion(Position, "ST", out quality));
        Assert.Equal(Position + ".q", quality);
        Assert.True(index.TryGetDerivedQualityCompanion(Position + ".stVal", "ST",
            out quality));
        Assert.Equal(Position + ".q", quality);
        Assert.False(index.TryGetDerivedQualityCompanion(Position + ".q", "ST", out _));
        Assert.False(index.TryGetQualityCompanion(Position + ".mag.f", "MX", out _));
        Assert.False(index.TryGetQualityCompanion("LD/CSWI10.Pos.stVal", "ST", out _));
        Assert.False(index.TryGetQualityCompanion("LD/CSWI1.pos.stVal", "ST", out _));
    }

    [Fact]
    public void ExplicitReadDataSet_UsesExactFcIndexedMembershipOnly()
    {
        var selected = Attribute("stVal");
        var otherFc = Attribute("mag.f", "MX");
        var wrongCase = new LiveIedDataAttributeModel
        {
            ObjectReference = Position,
            AttributePath = "Pos.stval",
            FunctionalConstraint = "ST",
            MmsReference = "LD/CSWI1$ST$Pos$stval"
        };
        var model = Model([selected, otherFc, wrongCase]);
        var page = new IedBrowserTreeRow("dataset/DS", "DS", "DataSet", 1, model.DataSets[0]);
        var subset = IedBrowserReadPlan.Build(model, page, readAll: false);
        Assert.Equal(2, subset.Count);
        Assert.Contains(subset, a => a.Reference == selected.MmsReference && a.FunctionalConstraint == "ST");
        Assert.Contains(subset, a => a.Reference == wrongCase.MmsReference && a.FunctionalConstraint == "ST");
        Assert.DoesNotContain(subset, a => a.FunctionalConstraint == "MX");
    }

    [Fact]
    public void LargeDataSetReportMembership_RemainsDeterministicForLastMember()
    {
        var members = Enumerable.Range(0, 8192).Select(i => new LiveIedDataSetMemberModel
        {
            Reference = "LD/GGIO1.Ind" + i, FunctionalConstraint = "ST", Index = i
        }).ToArray();
        var index = new IedBrowserSchemaIndex(Model(members: members));
        Assert.True(index.TryResolveReportFc(DataSet, "LD/GGIO1.Ind8191.stVal", out var fc));
        Assert.Equal("ST", fc);
        Assert.False(index.TryResolveReportFc(DataSet, "LD/GGIO1.Ind8192.stVal", out _));
        Assert.False(index.TryResolveReportFc(DataSet, "LD/GGIO1.Ind8191Extra.stVal", out _));
    }
}
