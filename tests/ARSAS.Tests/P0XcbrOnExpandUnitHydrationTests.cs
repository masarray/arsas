using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0XcbrOnExpandUnitHydrationTests
{
    private const string Owner = "BCUGEF650/XCBR1.SumSwATmms";
    private static LiveIedDataAttributeModel A(string path, string fc)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "SumSwATmms." + path,
            MmsReference = "BCUGEF650/XCBR1$" + fc +
                "$SumSwATmms$" + path.Replace('.', '$'),
            FunctionalConstraint = fc, SclBType = "Enum"
        };

    [Fact]
    public void ExpandingExactCfUnitsOffersOnlyTwoMissingOwnerLeaves()
    {
        var obj = new LiveIedDataObjectModel
        {
            Name = "SumSwATmms", Reference = Owner,
            Attributes = [A("setMag", "SP"), A("units.SIUnit", "CF"),
                A("units.multiplier", "CF"), A("minVal", "CF")]
        };
        var root = IedBrowserStructuredDetails.DataObject(obj, null);
        var units = Assert.Single(root.GetChildren().Where(x =>
            x.Name == "units [CF]"));
        Assert.Equal(Owner + ".units", units.ReadObjectReference);
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "XCBR1", LnClass = "XCBR", DataObjects = [obj]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "BCUGEF650", LogicalNodes = [ln]
            }]
        };
        var page = new IedBrowserTreeRow("model/ln/BCUGEF650/XCBR1",
            "XCBR1", "LN", 2, ln);
        var values = new IedBrowserLiveValues();
        values.BeginAssociation();
        var targets = IedBrowserVisibleConfigurationReadRequest.Build(
            model, page, units.ReadObjectReference, values);
        Assert.Equal(2, targets.Count);
        Assert.All(targets, t => Assert.Equal("CF", t.FunctionalConstraint));
        Assert.All(targets, t => Assert.StartsWith(Owner + ".units.",
            IedBrowserLiveValues.CanonicalReference(t.Reference)));
        Assert.DoesNotContain(targets, t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference).EndsWith(".minVal"));
    }

    [Fact]
    public void PartialOrCrossOwnerCfCannotAuthorizeUnitsRead()
    {
        var obj = new LiveIedDataObjectModel
        {
            Name = "SumSwATmms", Reference = Owner,
            Attributes = [A("setMag", "SP"), A("units.SIUnit", "CF")]
        };
        var units = Assert.Single(
            IedBrowserStructuredDetails.DataObject(obj, null).GetChildren()
            .Where(x => x.Name == "units [CF]"));
        Assert.Equal("", units.ReadObjectReference);
    }
}
