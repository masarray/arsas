using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M214DataSetMultiLeafEvidenceTests
{
    private const string Owner = "LD/ATCC1.TapChg";

    private static LiveIedDataAttributeModel Attr(string fc, string path)
        => new()
        {
            ObjectReference = Owner, AttributePath = "TapChg." + path,
            FunctionalConstraint = fc,
            MmsReference = "LD/ATCC1$" + fc + "$TapChg$" + path.Replace('.', '$')
        };

    private static (LiveIedModelDiscoveryDocument Model, LiveIedDataSetMemberModel Member)
        Fixture(bool splitByFc = false)
    {
        var first = new LiveIedDataObjectModel
        {
            Name = "TapChg", Reference = Owner, InferredCdc = "BSC",
            Attributes = [Attr("ST", "valWTr"), Attr("ST", "posVal"), Attr("ST", "q"),
                          Attr("ST", "t")]
        };
        var others = splitByFc
            ? new LiveIedDataObjectModel
            {
                Name = "TapChg", Reference = Owner, InferredCdc = "BSC",
                Attributes = [Attr("CF", "ctlModel"), Attr("DC", "d")]
            }
            : first;
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "ATCC1", DataObjects =
                        splitByFc ? [first, others] : [first]
                }]
            }]
        };
        return (model, new LiveIedDataSetMemberModel
        {
            Reference = Owner, FunctionalConstraint = "ST"
        });
    }

    [Fact]
    public void TapChgTwoProcessFields_ResolveOnParentWithoutPickingOne()
    {
        var (model, member) = Fixture(splitByFc: true);
        var row = IedBrowserStructuredDetails.Member(member, model);
        Assert.Equal(Owner, IedBrowserLiveValues.CanonicalReference(row.ValueReference));
        Assert.Equal(2, row.ScalarComponents.Count);
        Assert.Equal(["valWTr", "posVal"], row.ScalarComponents.Select(x => x.Label).ToArray());
        Assert.All(row.ScalarComponents, x => Assert.Equal("ST", x.FunctionalConstraint));

        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        Assert.True(ledger.PublishForAssociation(token, "LD/ATCC1$ST$TapChg$valWTr",
            "ST", "3", "", "", "Report"));
        Assert.True(ledger.PublishForAssociation(token, "LD/ATCC1$ST$TapChg$posVal",
            "ST", "3", "", "", "MMS Read"));
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out var summary));
        Assert.Equal("valWTr: 3; posVal: 3", summary.Text);
        Assert.Contains("Independent ST fields", summary.Source);
        Assert.Contains("TapChg.posVal", summary.Provenance);
        Assert.False(summary.IsStale);

        var children = IedBrowserStructuredDetails.DataSetMemberChildren(member, model, 1);
        Assert.Contains(children, x => x.Name == "valWTr [ST]");
        Assert.Contains(children, x => x.Name == "posVal [ST]");
        Assert.DoesNotContain(children, x => x.Name == "ctlModel [CF]");
    }

    [Fact]
    public void PartialAndOfflineEvidence_AreExplicitAndAssociationFenced()
    {
        var (model, member) = Fixture();
        var row = IedBrowserStructuredDetails.Member(member, model);
        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        Assert.False(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out _));
        ledger.PublishForAssociation(token, Owner + ".valWTr", "ST", "7", "", "", "Report");
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out var partial));
        Assert.Equal("Partial · valWTr: 7", partial.Text);
        ledger.MarkOffline();
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out var offline));
        Assert.True(offline.IsStale);
        var newToken = ledger.BeginAssociation();
        Assert.NotEqual(token, newToken);
        Assert.False(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out _));
        Assert.False(ledger.PublishForAssociation(token,
            Owner + ".posVal", "ST", "8", "", "", "MMS Read"));
    }

    [Fact]
    public void DirectParentEvidenceWins_AndWrongFcOrSiblingIsIgnored()
    {
        var (model, member) = Fixture();
        var row = IedBrowserStructuredDetails.Member(member, model);
        var ledger = new IedBrowserLiveValues();
        var token = ledger.BeginAssociation();
        ledger.PublishForAssociation(token, Owner + ".posVal", "MX",
            "77", "", "", "Report");
        ledger.PublishForAssociation(token, "LD/ATCC1.TapChgx.valWTr", "ST",
            "88", "", "", "Report");
        Assert.False(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out _));
        ledger.PublishForAssociation(token, Owner + ".posVal", "ST",
            "4", "", "", "Report");
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out var leafSummary));
        Assert.Contains("posVal: 4", leafSummary.Text);
        ledger.PublishForAssociation(token, Owner, "ST", "Full object",
            "", "", "Report");
        Assert.False(IedBrowserMultiLeafPresentation.TryCompose(row, ledger,
            true, out _));
        Assert.True(ledger.TryGet(Owner, "ST", out var exact));
        Assert.Equal("Full object", exact.Value);
    }

    [Fact]
    public void FcdaAndSingleProcessValue_DoNotSynthesizeCompoundSummary()
    {
        var (model, _) = Fixture();
        var fcda = new LiveIedDataSetMemberModel
        {
            Reference = Owner + ".valWTr", FunctionalConstraint = "ST"
        };
        var row = IedBrowserStructuredDetails.Member(fcda, model);
        Assert.Empty(row.ScalarComponents);
        var onlyOne = IedBrowserMultiLeafPresentation.BuildPlan(
            [Attr("ST", "valWTr"), Attr("CF", "units.SIUnit")],
            Owner, "ST");
        Assert.Empty(onlyOne);
        var unrelated = IedBrowserMultiLeafPresentation.BuildPlan(
            [Attr("MX", "posVal"), Attr("ST", "q"), Attr("ST", "t")],
            Owner, "ST");
        Assert.Empty(unrelated);
        Assert.Empty(IedBrowserMultiLeafPresentation.BuildPlan(
            [Attr("ST", "valWTr"), Attr("ST", "posVal")], Owner, "SE"));
    }

    [Fact]
    public void DataModelAndDataSetUseSameDeclaredScalarSources()
    {
        var (model, member) = Fixture();
        var obj = model.LogicalDevices[0].LogicalNodes[0].DataObjects[0];
        var direct = IedBrowserStructuredDetails.DataObject(obj, "ST");
        var dataSet = IedBrowserStructuredDetails.Member(member, model);
        Assert.Equal(dataSet.ScalarComponents, direct.ScalarComponents);
        var values = new IedBrowserLiveValues();
        var token = values.BeginAssociation();
        values.PublishForAssociation(token, Owner + ".valWTr", "ST",
            "3", "", "", "MMS Read");
        Assert.True(IedBrowserMultiLeafPresentation.TryCompose(direct, values,
            false, out var result));
        Assert.Equal("Partial · valWTr: 3", result.Text);
    }
}
