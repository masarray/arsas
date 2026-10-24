using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214SclSettingsDirectoryFallbackTests
{
    private const string Owner = "LD/ATCC1.BndWid";
    private static LiveIedModelDiscoveryDocument Fixture(params LiveIedDataAttributeModel[] attrs)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "ATCC1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "BndWid", Reference = Owner, Attributes = attrs
                    }]
                }]
            }]
        };
    private static LiveIedDataAttributeModel Attr(string fc, string suffix)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "BndWid." + suffix,
            FunctionalConstraint = fc,
            MmsReference = "LD/ATCC1$" + fc + "$BndWid$" +
                suffix.Replace('.', '$')
        };
    private static readonly IedBrowserTreeRow Page = new("sg", "SGCB", "SGCB", 2,
        new LiveIedControlBlockModel
        {
            Domain = "LD", Name = "SGCB",
            Reference = "LD/LLN0.SGCB", Kind = "SGCB"
        });

    [Fact]
    public void ExactSclSgDirectoryRenders_WhenLiveDiscoveryOmitsSettings()
    {
        var live = Fixture(Attr("MX", "mag.f"));
        var scl = Fixture(Attr("SG", "setMag.f"), Attr("SE", "setMag.f"));
        var rows = IedBrowserPresentation.Details(Page, live, true, scl);
        var parameter = Assert.Single(rows.Where(r => r.Kind == "Data"));
        Assert.Equal("ATCC1.BndWid.setMag [SG]", parameter.Name);
        Assert.Equal("SG", parameter.FunctionalConstraint);
        Assert.Contains("SCL declaration", parameter.Source);
        Assert.Contains(rows, r => r.Kind == "Message" &&
            r.Name.Contains("live MMS SG inventory not verified"));
        // SCL metadata must not authorize a speculative live read.
        var targets = IedBrowserReadPlan.Build(live, Page, readAll: false);
        Assert.DoesNotContain(targets, r => r.FunctionalConstraint == "SG");
    }

    [Fact]
    public void LiveInventoryWinsOverSclAndNeverBorrowsSeEditValue()
    {
        var live = Fixture(Attr("SG", "setVal"));
        var scl = Fixture(Attr("SG", "setMag.f"), Attr("SE", "setMag.f"));
        var rows = IedBrowserPresentation.Details(Page, live, true, scl);
        var item = Assert.Single(rows.Where(r => r.Kind == "Data"));
        Assert.Equal("ATCC1.BndWid.setVal [SG]", item.Name);
        Assert.DoesNotContain("SCL declaration", item.Source);
        Assert.DoesNotContain(rows, r => r.Name.Contains("SCL settings directory"));
    }

    [Fact]
    public void EditOnlySclShowsHonestSeNotActiveSg()
    {
        var rows = IedBrowserPresentation.Details(Page,
            Fixture(Attr("ST", "stVal")),
            true, Fixture(Attr("SE", "setMag.f")));
        var item = Assert.Single(rows.Where(r => r.Kind == "Data"));
        Assert.EndsWith("[SE]", item.Name);
        Assert.Contains(rows, r => r.Kind == "Message" &&
            r.Name.Contains("not current active [SG] values"));
        Assert.DoesNotContain(rows, r => r.Kind == "Property" &&
            r.Name == "Active Setting Group" && r.Value != "Not read");
    }

    [Fact]
    public void ForeignSclDomainMustNeverLeakParameters()
    {
        var foreign = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "DIFFERENT",
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "ATCC1",
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "BndWid", Reference = Owner,
                        Attributes = [Attr("SG", "setVal")]
                    }]
                }]
            }]
        };
        var rows = IedBrowserPresentation.Details(Page,
            Fixture(Attr("ST", "stVal")), true, foreign);
        Assert.Empty(rows.Where(r => r.Kind == "Data"));
    }
}
