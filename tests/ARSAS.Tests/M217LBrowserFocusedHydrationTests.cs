using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217LBrowserFocusedHydrationTests
{
    private const string Owner = "Application/MMXU1.A";

    private static LiveIedDataAttributeModel Attr(string relative, string fc) => new()
    {
        ObjectReference = Owner,
        AttributePath = "A." + relative,
        FunctionalConstraint = fc,
        MmsReference = "Application/MMXU1$" + fc + "$A$" + relative.Replace('.', '$'),
        SclBType = relative.EndsWith("SIUnit", StringComparison.Ordinal) ? "Enum" : "Float32"
    };

    private static LiveIedModelDiscoveryDocument Model() => new()
    {
        LogicalDevices = [new LiveIedLogicalDeviceModel
        {
            MmsDomain = "Application",
            LogicalNodes = [new LiveIedLogicalNodeModel
            {
                Name = "MMXU1",
                DataObjects = [new LiveIedDataObjectModel
                {
                    Name = "A",
                    Reference = Owner,
                    Attributes =
                    [
                        Attr("phsA.cVal.mag.f", "MX"),
                        Attr("phsA.q", "MX"),
                        Attr("phsA.t", "MX"),
                        Attr("units.SIUnit", "CF"),
                        Attr("units.multiplier", "CF"),
                        Attr("phsB.units.SIUnit", "CF")
                    ]
                }]
            }]
        }],
        DataSets = [new LiveIedDataSetModel
        {
            Name = "Measurements",
            Reference = "Application/LLN0.Measurements",
            Members = [new LiveIedDataSetMemberModel
            {
                Reference = Owner + ".phsA",
                FunctionalConstraint = "MX"
            }]
        }]
    };

    [Fact]
    public void DataSetDoesNotInventCfUnitMembersAndDataModelReadsExactCf()
    {
        var model = Model();
        var set = Assert.Single(model.DataSets);
        var row = new IedBrowserTreeRow("dataset/measurement", "Measurements",
            "DataSet", 3, set);
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();

        var planned = IedBrowserReadPlan.BuildFocused(model, row, ledger);
        Assert.All(planned, x => Assert.Equal("MX", x.FunctionalConstraint));
        var member = Assert.Single(set.Members);
        var dataSetRows = IedBrowserStructuredDetails.DataSetMemberChildren(
            member, model, depth: 0);
        Assert.DoesNotContain(dataSetRows,
            x => x.Name is "units" or "SIUnit" or "multiplier");

        // Same exact live schema, but the Data Model owns the CF leaves.
        var ln = Assert.Single(Assert.Single(model.LogicalDevices).LogicalNodes);
        var lnPage = new IedBrowserTreeRow("model/ln/Application/MMXU1",
            "MMXU1", "LN", 4, ln);
        var focused = IedBrowserReadPlan.BuildFocused(
            model, lnPage, ledger, selectedDoReference: Owner);
        Assert.Contains(focused, x => x.Reference.EndsWith(
            "$CF$A$units$SIUnit", StringComparison.Ordinal));
        Assert.Contains(focused, x => x.Reference.EndsWith(
            "$CF$A$units$multiplier", StringComparison.Ordinal));

        Assert.True(ledger.PublishForAssociation(association, Owner + ".units.SIUnit",
            "CF", "3", "", "", "MMS Read"));
        var afterRead = IedBrowserReadPlan.BuildFocused(
            model, lnPage, ledger, selectedDoReference: Owner);
        Assert.DoesNotContain(afterRead, x => x.Reference.EndsWith(
            "$CF$A$units$SIUnit", StringComparison.Ordinal));
        ledger.BeginAssociation();
        Assert.Contains(IedBrowserReadPlan.BuildFocused(
            model, lnPage, ledger, selectedDoReference: Owner),
            x => x.Reference.EndsWith("$CF$A$units$SIUnit", StringComparison.Ordinal));
    }

    [Fact]
    public void FocusedModelLnIsBoundedOfflineSafeAndCannotHydrateOtherLns()
    {
        var model = Model();
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var ln = Assert.Single(Assert.Single(model.LogicalDevices).LogicalNodes);
        var row = new IedBrowserTreeRow("model/ln/Application/MMXU1",
            "MMXU1", "LN", 4, ln);
        var plan = IedBrowserReadPlan.BuildFocused(model, row, ledger, budget: 2);
        Assert.InRange(plan.Count, 1, 2);
        Assert.All(plan, target => Assert.Equal("MX", target.FunctionalConstraint));
        Assert.DoesNotContain(plan, target => target.Reference.Contains(
            "$CF$", StringComparison.Ordinal));
        ledger.MarkOffline();
        Assert.Empty(IedBrowserReadPlan.BuildFocused(model, row, ledger));
    }
}
