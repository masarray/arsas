using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214EngineeringHierarchyUxTests
{
    private const string Owner = "LD/MMXU1.A";
    private static LiveIedDataAttributeModel Attr(string fc, string relative)
        => new()
        {
            ObjectReference = Owner, AttributePath = "A." + relative,
            FunctionalConstraint = fc,
            MmsReference = "LD/MMXU1$" + fc + "$A$" + relative.Replace('.', '$')
        };
    private static LiveIedDataObjectModel Part(string cdc,
        params LiveIedDataAttributeModel[] attributes)
        => new()
        {
            Name = "A", Reference = Owner, InferredCdc = cdc,
            Attributes = attributes
        };

    [Fact]
    public void LnPageAndNavigationShareOneEngineeringDoAcrossMxCfDc()
    {
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "MMXU1", LnClass = "MMXU",
            DataObjects =
            [
                Part("WYE", Attr("MX", "phsA.cVal.mag.f"),
                    Attr("MX", "phsA.cVal.ang.f")),
                Part("WYE", Attr("CF", "phsA.units.SIUnit"),
                    Attr("CF", "phsA.units.multiplier")),
                Part("WYE", Attr("DC", "phsA.d"))
            ]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [ln]
            }]
        };
        var page = new IedBrowserTreeRow("model/ln/LD/MMXU1",
            "MMXU1", "LN", 2, ln);
        var doRow = Assert.Single(IedBrowserPresentation.Details(
            page, model, connected: true).Where(r => r.IsDataObject));
        Assert.Equal("A", doRow.Name);
        Assert.True(doRow.HasChildren);
        var phase = Assert.Single(doRow.GetChildren());
        Assert.Equal("phsA", phase.Name);
        Assert.True(phase.HasChildren);
        Assert.Equal("MX", phase.FunctionalConstraint);
        var branchNames = phase.GetChildren().Select(x => x.Name).ToArray();
        Assert.Contains("cVal [MX]", branchNames);
        Assert.Contains("units [CF]", branchNames);
        Assert.Contains("d [DC]", branchNames);
        Assert.DoesNotContain(doRow.GetChildren(), x => x.Name.Contains("[CF]"));

        var treeModel = Assert.Single(IedBrowserPresentation.Roots(model,
            Array.Empty<RcbExportRow>()).Where(r => r.Key == "category/model"));
        var ld = Assert.Single(treeModel.GetChildren());
        var treeLn = Assert.Single(ld.GetChildren());
        Assert.Single(treeLn.GetChildren());
        Assert.Equal(Owner, ((LiveIedDataObjectModel)treeLn.GetChildren()[0].Payload!).Reference);
    }

    [Fact]
    public void DifferentExactDoReferencesNeverMergeAndConflictingCdcFailsClosed()
    {
        var same = IedBrowserEngineeringObjects.GroupExact(
        [
            Part("WYE", Attr("MX", "phsA.cVal.mag.f")),
            Part("CMV", Attr("CF", "phsA.units.SIUnit")),
            new LiveIedDataObjectModel
            {
                Name = "A", Reference = "LD/MMXU2.A",
                Attributes = [new LiveIedDataAttributeModel
                {
                    MmsReference = "LD/MMXU2$MX$A$phsA$cVal$mag$f",
                    FunctionalConstraint = "MX"
                }]
            }
        ]);
        Assert.Equal(2, same.Count);
        var first = Assert.Single(same.Where(x => x.Reference == Owner));
        Assert.Equal("", first.InferredCdc);
        Assert.Equal(2, first.Attributes.Count);
    }

    [Fact]
    public void ThreePhaseRootUsesCompactPreview_WithoutLosingNeutralEvidence()
    {
        var phases = new[] { "phsA", "phsB", "phsC", "neut" };
        var obj = Part("WYE", phases.SelectMany(p => new[]
        {
            Attr("MX", p + ".cVal.mag.f"),
            Attr("MX", p + ".cVal.ang.f")
        }).ToArray());
        var row = IedBrowserStructuredDetails.DataObject(obj, "MX");
        Assert.Equal(4, row.VectorComponents.Count);
        Assert.Equal(3, row.PhasePlans.Count);
        var ledger = new IedBrowserLiveValues();
        var session = ledger.BeginAssociation();
        foreach (var phase in phases)
        {
            ledger.PublishForAssociation(session,
                Owner + "." + phase + ".cVal.mag.f", "MX",
                "0", "", "t1", "Report");
            ledger.PublishForAssociation(session,
                Owner + "." + phase + ".cVal.ang.f", "MX",
                "0", "", phase == "neut" ? "t2" : "t1", "Report");
        }
        Assert.True(IedBrowserEngineeringComposite.TryCompose(row, ledger,
            new Dictionary<string, string?>(), true, out var preview));
        Assert.Contains("phsA: 0", preview.Text);
        Assert.Contains("phsC: 0", preview.Text);
        Assert.DoesNotContain("neut:", preview.Text);
        Assert.DoesNotContain("independent, not paired", preview.Text);
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row with { IsDataObject = false }, ledger,
            new Dictionary<string, string?>(), true, out var expanded));
        Assert.Contains("neut:", expanded.Text);
        Assert.Contains("angle 0°", expanded.Text);
        Assert.DoesNotContain("independent, not paired", expanded.Text);
        Assert.Contains("not paired with magnitude", expanded.Provenance);
    }

    [Fact]
    public void PlainScalarMvIsNeverPromotedToAnAngleByGrouping()
    {
        var model = Part("MV", Attr("MX", "mag.f"),
            Attr("CF", "units.SIUnit"));
        var row = IedBrowserStructuredDetails.DataObject(model, "MX");
        Assert.Empty(row.PhasePlans);
        Assert.Null(row.PolarPlan);
        Assert.Empty(row.VectorComponents);
        Assert.DoesNotContain(row.GetChildren(), x => x.PolarPlan is not null);
    }
}
