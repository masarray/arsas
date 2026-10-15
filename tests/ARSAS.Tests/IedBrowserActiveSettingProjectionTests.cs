using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserActiveSettingProjectionTests
{
    private const string Owner = "LD/ATCC1.BndWid";
    private static LiveIedDataAttributeModel Attr(string fc, string path) => new()
    {
        ObjectReference = Owner,
        AttributePath = "BndWid." + path,
        FunctionalConstraint = fc,
        MmsReference = "LD/ATCC1$" + fc + "$BndWid$" + path.Replace('.', '$')
    };
    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page)
        Fixture(params LiveIedDataAttributeModel[] attrs)
    {
        var model = new LiveIedModelDiscoveryDocument
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
        return (model, new IedBrowserTreeRow("sg/LD", "SGCB", "SGCB", 3,
            new LiveIedControlBlockModel { Kind = "SettingGroupControl",
                Domain = "LD", LogicalNode = "LLN0", Name = "SGCB",
                Reference = "LD/LLN0.SP.SGCB" }));
    }

    [Fact]
    public void MultiLeafSgExposesAlreadyReadScalarAtParentAndChild()
    {
        var (model, page) = Fixture(Attr("SG", "setMag.f"),
            Attr("SG", "setVal"), Attr("SE", "setMag.f"), Attr("CF", "units.SIUnit"));
        var rows = IedBrowserPresentation.Details(page, model, true)
            .Where(r => r.Kind == "Data").ToArray();
        Assert.Equal(2, rows.Length);
        var mag = Assert.Single(rows.Where(r => r.Name == "ATCC1.BndWid.setMag [SG]"));
        var scalar = Assert.Single(rows.Where(r => r.Name == "ATCC1.BndWid.setVal [SG]"));
        Assert.Equal("LD/ATCC1.BndWid.setMag.f",
            IedBrowserLiveValues.CanonicalReference(mag.ValueReference));
        Assert.Equal("LD/ATCC1$SG$BndWid$setVal", scalar.ValueReference);
        Assert.Equal("SG", mag.FunctionalConstraint);
        var child = Assert.Single(mag.GetChildren());
        Assert.Equal("f [SG]", child.Name);
        Assert.Equal(IedBrowserLiveValues.CanonicalReference(mag.ValueReference),
            IedBrowserLiveValues.CanonicalReference(child.ValueReference));
        var values = new IedBrowserLiveValues();
        var association = values.BeginAssociation();
        Assert.True(values.PublishForAssociation(association,
            mag.ValueReference, "SG", "3", "", "", "MMS Read"));
        Assert.True(values.TryGet(mag.ValueReference, "SG", out var parent));
        Assert.True(values.TryGet(child.ValueReference, "SG", out var leaf));
        Assert.Equal("3", parent.Value);
        Assert.Equal(parent.Value, leaf.Value);
    }

    [Fact]
    public void ExplicitSgReadOmitsSeEditGroupAndCfTargets()
    {
        var (model, page) = Fixture(Attr("SG", "setMag.f"),
            Attr("SG", "setVal"), Attr("SE", "setMag.f"),
            Attr("CF", "units.SIUnit"));
        var targets = IedBrowserReadPlan.Build(model, page, readAll: false);
        Assert.Equal(2, targets.Count);
        Assert.All(targets, t => Assert.Equal("SG", t.FunctionalConstraint));
        Assert.Contains(targets, t => t.Reference == "LD/ATCC1$SG$BndWid$setMag$f");
        Assert.Contains(targets, t => t.Reference == "LD/ATCC1$SG$BndWid$setVal");
    }

    [Fact]
    public void SgParentWithDirectAndPrimitiveDeclarationsPrefersVerifiedF()
    {
        var (model, page) = Fixture(Attr("SG", "setMag"),
            Attr("SG", "setMag.f"), Attr("SE", "setMag.f"));
        var root = Assert.Single(IedBrowserPresentation.Details(page, model, true)
            .Where(r => r.Kind == "Data"));
        Assert.Equal("LD/ATCC1.BndWid.setMag.f",
            IedBrowserLiveValues.CanonicalReference(root.ValueReference));
        Assert.Equal("LD/ATCC1$SG$BndWid$setMag", root.FallbackValueReference);
    }

    [Fact]
    public void EditOnlyDeclarationIsNotTreatedAsActiveSettingValue()
    {
        var (model, page) = Fixture(Attr("SE", "setMag.f"));
        var rows = IedBrowserPresentation.Details(page, model, true);
        Assert.Empty(rows.Where(r => r.Kind == "Data"));
        Assert.Contains(rows, r => r.Kind == "Message" &&
            r.Name.Contains("No model-proven SG or SCL SE", StringComparison.Ordinal));
    }
}
