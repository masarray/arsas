using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserControlStateEvidenceTests
{
    private static LiveIedControlBlockModel Block(
        string kind = "SettingGroupControl", string fc = "SP",
        string name = "SGCB", string status = "AttributeInventoryOnly",
        string? reference = null, string? logicalNode = null,
        IReadOnlyList<string>? fields = null)
    {
        var ln = logicalNode ?? "LLN0";
        return new LiveIedControlBlockModel
        {
            Kind = kind, Domain = "LD", LogicalNode = ln, Name = name,
            FunctionalConstraint = fc, DiscoveryStatus = status,
            Reference = reference ?? $"LD/{ln}.{fc}.{name}",
            Attributes = fields ?? ["NumOfSG", "ActSG", "LActTm", "EditSG", "CnfEdit"]
        };
    }

    [Fact]
    public void SgcbReadsOnlyExactInventoryDeclaredReadOnlyMmsLeaves()
    {
        var block = Block();
        var targets = IedBrowserControlStateEvidence.ReadTargets(block, "SGCB");
        Assert.Equal(["NumOfSG", "ActSG", "LActTm"],
            targets.Select(t => t.Reference[(t.Reference.LastIndexOf('.') + 1)..]));
        Assert.All(targets, t => Assert.Equal("SP", t.FunctionalConstraint));
        Assert.Equal("LD/LLN0.SGCB.NumOfSG", targets[0].Reference);
        Assert.Equal("UInt32", targets[0].DataType);
        Assert.Equal("UtcTime", targets[2].DataType);
        Assert.DoesNotContain(targets, t => t.Reference.Contains("EditSG") ||
            t.Reference.Contains("CnfEdit"));
    }

    [Theory]
    [InlineData("SCL", "SP", "SGCB", "LD/LLN0.SP.SGCB", "LLN0")]
    [InlineData("AttributeInventoryOnly", "CF", "SGCB", "LD/LLN0.CF.SGCB", "LLN0")]
    [InlineData("AttributeInventoryOnly", "SP", "OTHER", "LD/LLN0.SP.OTHER", "LLN0")]
    [InlineData("AttributeInventoryOnly", "SP", "SGCB", "LD/LLN0.SP.SGCB", "PTOC1")]
    [InlineData("AttributeInventoryOnly", "SP", "SGCB", "LD/LLN0.SP.SGCB_WRONG", "LLN0")]
    public void UnverifiedOrConflictingSgcbNeverTriggersProbe(
        string status, string fc, string name, string reference, string logicalNode)
    {
        var block = Block(fc: fc, name: name, status: status,
            reference: reference, logicalNode: logicalNode);
        Assert.Empty(IedBrowserControlStateEvidence.ReadTargets(block, "SGCB"));
    }

    [Fact]
    public void GocbGoEnaAndNdsComAreMmsReads_NotPassiveFrameAssumptions()
    {
        var block = Block("GSEControl", "GO", "BCU", fields:
            ["GoEna", "NdsCom", "DatSet", "GoID", "ConfRev", "DstAddress"]);
        var targets = IedBrowserControlStateEvidence.ReadTargets(block, "GoCB");
        Assert.Equal(["LD/LLN0.BCU.GoEna", "LD/LLN0.BCU.NdsCom"],
            targets.Select(t => t.Reference));
        Assert.All(targets, t => Assert.Equal("GO", t.FunctionalConstraint));
        Assert.All(targets, t => Assert.Equal("Boolean", t.DataType));
        Assert.Empty(IedBrowserControlStateEvidence.ReadTargets(
            Block("GSEControl", "GO", "BCU", status: "SCLOnly",
                fields: ["GoEna"]), "GoCB"));
    }

    [Fact]
    public void ExactReadProjectionUsesCanonicalLedger_WithoutCreatingSecondCache()
    {
        var block = Block();
        var values = new IedBrowserLiveValues();
        var session = values.BeginAssociation();
        Assert.False(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "ActSG", out _, out _));
        Assert.True(values.PublishForAssociation(session, "LD/LLN0$SP$SGCB$ActSG",
            "SP", "3", "", "", "MMS Read"));
        Assert.True(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "ActSG", out var observed, out var reference));
        Assert.Equal("3", observed.Value);
        Assert.Equal("LD/LLN0.SGCB.ActSG", reference);
        // A Report on the same exact IEC reference is not proof of
        // an explicit control-state MMS Read.
        Assert.True(values.PublishForAssociation(session, "LD/LLN0$SP$SGCB$NumOfSG",
            "SP", "9", "", "", "Report"));
        Assert.False(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "NumOfSG", out _, out _));
        Assert.False(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "EditSG", out _, out _));
        values.MarkOffline();
        Assert.True(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "ActSG", out var stale, out _));
        Assert.True(stale.IsStale);
        values.BeginAssociation();
        Assert.False(IedBrowserControlStateEvidence.TryRead(values, block, "SGCB",
            "ActSG", out _, out _));
    }

    [Fact]
    public void BrowserReadPlanAddsSafeControlTargetsOnlyForExplicitSelection()
    {
        var block = Block();
        var node = new IedBrowserTreeRow("SGCB/LD", "SGCB", "SGCB", 3, block);
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }]
        };
        var selected = IedBrowserReadPlan.Build(model, node, readAll: false);
        Assert.Equal(3, selected.Count);
        Assert.Empty(IedBrowserReadPlan.Build(model, node, readAll: true));
        var goose = Block("GSEControl", "GO", "BCU", fields: ["GoEna"]);
        var gNode = new IedBrowserTreeRow("GoCB/BCU", "BCU", "GoCB", 3, goose);
        Assert.Single(IedBrowserReadPlan.Build(model, gNode, readAll: false));
        Assert.Empty(IedBrowserReadPlan.Build(model, new IedBrowserTreeRow(
            "GoCB/SCL", "SCL", "GoCB", 3, Block("GSEControl", "GO", "BCU",
                status: "Model", fields: ["GoEna"])), readAll: false));
    }
    [Fact]
    public void FullLiveInitialHydrationIncludesOnlyVerifiedReadOnlySgcbStatus()
    {
        var verified = Block(fields: ["NumOfSG", "ActSG", "LActTm", "EditSG", "CnfEdit"]);
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }],
            SettingGroupControls = [verified]
        };
        var values = new IedBrowserLiveValues();
        var session = values.BeginAssociation();
        var plan = IedBrowserReadPlan.BuildInitialPriority(model, values, 32);
        Assert.Equal(3, plan.Count);
        Assert.All(plan, target => Assert.Equal("SP", target.FunctionalConstraint));
        Assert.Contains(plan, target => target.Reference.EndsWith(".ActSG", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, target => target.Reference.Contains("EditSG", StringComparison.Ordinal) ||
            target.Reference.Contains("CnfEdit", StringComparison.Ordinal));

        Assert.True(values.PublishForAssociation(session, "LD/LLN0.SGCB.ActSG",
            "SP", "1", "", "", "MMS Read"));
        Assert.Equal(2, IedBrowserReadPlan.BuildInitialPriority(model, values, 32).Count);
        Assert.Single(IedBrowserReadPlan.BuildInitialPriority(model, values, 1));
        values.BeginAssociation();
        Assert.Equal(3, IedBrowserReadPlan.BuildInitialPriority(model, values, 32).Count);
    }

    [Fact]
    public void InitialSgcbPlanRejectsSclOnlyAndAmbiguousInventory()
    {
        var valid = Block(fields: ["NumOfSG", "ActSG"]);
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }],
            SettingGroupControls = [valid, valid,
                Block(status: "SCL", fields: ["LActTm"])]
        };
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32));
    }

}
