using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214SelectedDataObjectReadTests
{
    private static LiveIedDataAttributeModel Attribute(string owner, string fc, string leaf)
        => new()
        {
            ObjectReference = owner, FunctionalConstraint = fc,
            AttributePath = owner.Split('.')[1] + "." + leaf,
            MmsReference = owner.Replace(".", "$" + fc + "$")
                .Replace("$" + fc + "$", "$" + fc + "$") + "$" +
                leaf.Replace('.', '$')
        };

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page)
        Fixture()
    {
        const string first = "LD/CSWI1.Pos";
        const string other = "LD/CSWI1.Mod";
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "CSWI1",
            DataObjects =
            [
                new LiveIedDataObjectModel
                {
                    Reference = first, Name = "Pos",
                    Attributes =
                    [
                        Attribute(first, "ST", "stVal"),
                        Attribute(first, "ST", "q"),
                        Attribute(first, "ST", "t"),
                        Attribute(first, "CF", "ctlModel"),
                        Attribute(first, "DC", "d"),
                        Attribute(first, "CO", "Oper")
                    ]
                },
                new LiveIedDataObjectModel
                {
                    Reference = other, Name = "Mod",
                    Attributes = [Attribute(other, "ST", "stVal")]
                }
            ]
        };
        return (new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [ln]
            }]
        }, new IedBrowserTreeRow("model/ln/LD/CSWI1", "CSWI1", "LN", 2, ln));
    }

    [Fact]
    public void SelectedDoRead_ContainsAllReadOnlyFcAttributes_NotJustStVal()
    {
        var (model, page) = Fixture();
        var targets = IedBrowserReadPlan.Build(
            model, page, readAll: false, selectedDoReference: "LD/CSWI1.Pos");
        Assert.Equal(5, targets.Count);
        Assert.Contains(targets, x => x.Reference.EndsWith("$Pos$stVal") &&
            x.FunctionalConstraint == "ST");
        Assert.Contains(targets, x => x.Reference.EndsWith("$Pos$q"));
        Assert.Contains(targets, x => x.Reference.EndsWith("$Pos$t"));
        Assert.Contains(targets, x => x.Reference.EndsWith("$Pos$ctlModel") &&
            x.FunctionalConstraint == "CF");
        Assert.Contains(targets, x => x.Reference.EndsWith("$Pos$d") &&
            x.FunctionalConstraint == "DC");
        Assert.DoesNotContain(targets, x => x.Reference.Contains("$Mod$") ||
            x.FunctionalConstraint == "CO");
    }

    [Fact]
    public void DoFocusIsExact_AndCannotBorrowAnotherLogicalNodeOrBeReadAllShortcut()
    {
        var (model, page) = Fixture();
        Assert.Empty(IedBrowserReadPlan.Build(
            model, page, false, "LD/CSWI2.Pos"));
        Assert.Equal(6, IedBrowserReadPlan.Build(model, page, false).Count);
        // Read All remains a global explicit operation regardless of right-pane focus.
        Assert.Equal(6, IedBrowserReadPlan.Build(
            model, page, true, "LD/CSWI1.Pos").Count);
    }

    [Fact]
    public void DataObjectReadFocusNeverAliasesPreferredValueLeaf()
    {
        var (model, page) = Fixture();
        var ln = (LiveIedLogicalNodeModel)page.Payload!;
        var pos = ln.DataObjects[0];
        var detail = IedBrowserStructuredDetails.DataObject(pos, "ST");
        Assert.Equal(pos.Reference, detail.ReadObjectReference);
        Assert.True(detail.HasChildren);
        Assert.NotEmpty(IedBrowserReadPlan.Build(
            model, page, false, detail.ReadObjectReference));
    }
}
