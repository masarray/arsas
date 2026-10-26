using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Retains the unique live-wire and initial-SGCB admission contracts of
/// historical #606 against the CURRENT RC API. No obsolete UI navigation
/// assertions, no second GOOSE decoder, no MMS mutation and no device I/O.
/// </summary>
public sealed class M217DCurrentTreeEvidenceContinuityTests
{
    [Fact]
    public void ExactWireGoCbCanBeReadUnboundWithoutAnMmsDataSetInventory()
    {
        var first = new GooseLeafValueSnapshot(1, 0, "raw-1", "", "", "", "",
            "Open [01]", "", false, "Unbound");
        var second = new GooseLeafValueSnapshot(2, 1, "raw-2", "", "", "", "",
            "Good", "", false, "Unbound");
        var frame = new GooseStreamSnapshot(
            "received-wire", "0x0021", "LD/LLN0$GO$BCU", "BCUGE03",
            "LD/LLN0$GOOSE1", "02:00:4C:4F:5B:50",
            "01:0C:CD:01:00:33", "VID 0 / PCP 4",
            "1", "42", "Healthy", "20000 ms", "1",
            "", "Unbound", "", "12:00:00.000", 1, 0,
            false, false, [first, second],
            "0", "4", 2);
        // There is no MMS/SCL DatSet inventory and publisher IED label is
        // absent. Only the full received GoCB reference authorizes a match.
        var expected = new IedBrowserGooseExpectedIdentity(
            "BCUGE", "LD/LLN0.GO.BCU", "", "BCUGE03",
            33, 1, "", "", null);
        Assert.Same(frame, IedBrowserGooseInspection.Select(expected, [frame]));
        var raw = IedBrowserGooseInspection.OrderedUnboundWireEntries(frame);
        Assert.NotNull(raw);
        Assert.Equal(2, raw.Count);
        Assert.Equal("Good", raw[1].Value);
        Assert.All(raw, slot => Assert.Empty(slot.SignalReference));
        Assert.All(raw, slot => Assert.Empty(slot.FunctionalConstraint));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected with { GoCbRef = "" }, [frame]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected, [frame with { GoCbRef = "LD/LLN0$GO$OTHER" }]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected, [frame, frame]));
        Assert.Null(IedBrowserGooseInspection.Select(
            expected, [frame with { AppIdText = "0x0022" }]));
    }

    [Fact]
    public void ExactLiveSgcbInitialReadIsBoundedReadOnlyAndSkipsObservedFields()
    {
        var block = new LiveIedControlBlockModel
        {
            Kind = "SettingGroupControl",
            Domain = "LD", LogicalNode = "LLN0", Name = "SGCB",
            FunctionalConstraint = "SP",
            DiscoveryStatus = "AttributeInventoryOnly",
            Reference = "LD/LLN0.SP.SGCB",
            Attributes = ["NumOfSG", "ActSG", "LActTm", "EditSG", "CnfEdit"]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }],
            SettingGroupControls = [block]
        };
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var original = IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32);
        Assert.Equal(3, original.Count);
        Assert.All(original, entry => Assert.Equal("SP", entry.FunctionalConstraint));
        Assert.Equal(new[] { "ActSG", "LActTm", "NumOfSG" },
            original.Select(target => target.Reference.Split('.')[^1])
                .OrderBy(name => name, StringComparer.Ordinal).ToArray());
        Assert.DoesNotContain(original, target =>
            target.Reference.Contains("EditSG", StringComparison.Ordinal) ||
            target.Reference.Contains("CnfEdit", StringComparison.Ordinal));

        Assert.True(ledger.PublishForAssociation(association,
            "LD/LLN0.SGCB.ActSG", "SP", "1", "", "", "MMS Read"));
        var pending = IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32);
        Assert.Equal(2, pending.Count);
        Assert.DoesNotContain(pending, target =>
            target.Reference == "LD/LLN0.SGCB.ActSG");
        Assert.Single(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 1));
        ledger.MarkOffline();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(model, ledger, 32));
    }

    [Fact]
    public void UnverifiedSclOrWrongFcNeverBecomesAnInitialSgcbRead()
    {
        LiveIedModelDiscoveryDocument Create(string status, string fc) => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "LD" }],
            SettingGroupControls =
            [
                new LiveIedControlBlockModel
                {
                    Kind = "SettingGroupControl", Domain = "LD",
                    LogicalNode = "LLN0", Name = "SGCB",
                    FunctionalConstraint = fc,
                    DiscoveryStatus = status,
                    Reference = $"LD/LLN0.{fc}.SGCB",
                    Attributes = ["NumOfSG", "ActSG", "EditSG", "CnfEdit"]
                }
            ]
        };
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Create("SCL", "SP"), ledger, 32));
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Create("AttributeInventoryOnly", "CF"), ledger, 32));
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Create("AttributeInventoryOnly", "SP"), ledger, 0));
    }
}
