using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// The trusted selected-IED SCL may complete *presentation schema* for a
/// live DataSet. It never supplies a process value, a new MMS Read target,
/// or authority for a differently spelled object/FC.
/// </summary>
public sealed class M217GDataSetSclSchemaFallbackTests
{
    private const string Owner = "LD/MMXU1.PhV";

    private static LiveIedDataAttributeModel Attr(string fc, string relative)
        => new()
        {
            ObjectReference = Owner,
            AttributePath = "PhV." + relative,
            MmsReference = "LD/MMXU1$" + fc + "$PhV$" +
                relative.Replace('.', '$'),
            FunctionalConstraint = fc,
            SclBType = relative.EndsWith(".f", StringComparison.Ordinal)
                ? "FLOAT32" : "Quality"
        };

    private static LiveIedDataObjectModel Obj(
        params LiveIedDataAttributeModel[] attrs)
        => new()
        {
            Name = "PhV", Reference = Owner, InferredCdc = "WYE",
            Attributes = attrs
        };

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataObjectModel[] objects)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", DataObjects = objects
                }]
            }]
        };

    private static LiveIedDataAttributeModel[] ThreePhase()
        => ["phsA", "phsB", "phsC"].SelectMany(phase =>
            new[] { Attr("MX", phase + ".cVal.mag.f"),
                Attr("MX", phase + ".cVal.ang.f"),
                Attr("MX", phase + ".q"),
                Attr("MX", phase + ".t") }).ToArray();

    private static LiveIedDataSetMemberModel Member(string fc = "MX",
        string reference = Owner)
        => new() { Reference = reference, FunctionalConstraint = fc };

    [Fact]
    public void ExactSelectedIedSclMxSchemaRendersOnlyActuallyObservedValues()
    {
        var scl = Model(Obj(ThreePhase()));
        var live = Model(); // the live DataSet member exists but DA hierarchy is sparse
        var member = Member();
        var row = IedBrowserStructuredDetails.Member(member, live, sclModel: scl);
        Assert.Equal(3, row.PhasePlans.Count);
        Assert.Contains("SCL structure only", row.Tooltip);

        var children = IedBrowserStructuredDetails.DataSetMemberChildren(
            member, live, 1, scl);
        Assert.Contains(children, x => x.Name == "phsA [MX]");
        Assert.DoesNotContain(children, x => x.FunctionalConstraint == "ST");

        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        Assert.False(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out _));
        Assert.True(ledger.PublishForAssociation(association,
            Owner + ".phsA.cVal.mag.f", "MX", "11", "", "", "Report"));
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var actual));
        Assert.Contains("phsA: 11", actual.Text);
        Assert.Contains("phsB: not observed", actual.Text);
        Assert.DoesNotContain("∠ 0", actual.Text);
        Assert.False(ledger.TryGet(Owner + ".phsB.cVal.mag.f", "MX", out _));
        ledger.MarkOffline();
        Assert.True(IedBrowserEngineeringComposite.TryCompose(
            row, ledger, new Dictionary<string, string?>(), true, out var stale));
        Assert.True(stale.IsStale);
    }

    [Fact]
    public void LiveMxSchemaWinsOverConflictingSclPresentation()
    {
        var live = Model(Obj(Attr("MX", "phsA.cVal.mag.f"),
            Attr("MX", "phsA.cVal.ang.f")));
        var scl = Model(Obj(ThreePhase()));
        var row = IedBrowserStructuredDetails.Member(Member(), live, sclModel: scl);
        // A single live phase is not a full three-phase vector, but it
        // must remain live-only: SCL cannot manufacture phsB/phsC leaves.
        Assert.Empty(row.PhasePlans);
        Assert.DoesNotContain("SCL structure only", row.Tooltip);
        var children = IedBrowserStructuredDetails.DataSetMemberChildren(
            Member(), live, 1, scl);
        Assert.Contains(children, x => x.Name == "phsA [MX]");
        Assert.DoesNotContain(children, x => x.Name == "phsB [MX]");
    }

    [Fact]
    public void OtherFcAndDifferentIedReferenceCannotSupplyMxDirectory()
    {
        var scl = Model(Obj(ThreePhase()));
        var noLive = Model();
        var st = IedBrowserStructuredDetails.Member(Member("ST"), noLive,
            sclModel: scl);
        Assert.Empty(st.PhasePlans);
        var otherLn = IedBrowserStructuredDetails.Member(
            Member(reference: "LD/MMXU2.PhV"), noLive, sclModel: scl);
        Assert.Empty(otherLn.PhasePlans);
        var otherDomain = IedBrowserStructuredDetails.Member(
            Member(reference: "OTHER/MMXU1.PhV"), noLive, sclModel: scl);
        Assert.Empty(otherDomain.PhasePlans);
        var children = IedBrowserStructuredDetails.DataSetMemberChildren(
            Member("ST"), noLive, 1, scl);
        Assert.Contains(children, x => x.Kind == "Message");
    }

    [Fact]
    public void SclPresentationCannotAuthorizeMmsReadTargets()
    {
        var scl = Model(Obj(ThreePhase()));
        var live = Model(); 
        var member = Member();
        var row = IedBrowserStructuredDetails.Member(member, live, sclModel: scl);
        Assert.Equal(3, row.PhasePlans.Count);
        // Existing acquisition is still strictly built from LIVE discovery.
        var dataSet = new LiveIedDataSetModel
        {
            Reference = "LD/LLN0.Measurements",
            Domain = "LD", LogicalNode = "LLN0",
            Name = "Measurements", MemberCount = 1,
            Members = [member]
        };
        var liveWithSet = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = live.LogicalDevices, DataSets = [dataSet]
        };
        var page = new IedBrowserTreeRow("datasets/set/" + dataSet.Reference,
            dataSet.Name, "DataSet", 3, dataSet);
        Assert.Empty(IedBrowserReadPlan.Build(liveWithSet, page, readAll: false));
    }    [Fact]
    public void ObservedQualityWithoutTypedBitsIsNotMislabeledNotExposed()
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder is not null &&
            !File.Exists(Path.Combine(folder.FullName, "MainWindow.IedBrowser.cs")))
            folder = folder.Parent;
        Assert.NotNull(folder);
        var code = File.ReadAllText(Path.Combine(folder!.FullName,
            "MainWindow.IedBrowser.cs"));
        Assert.Contains("display == \"Not exposed\"", code);
        Assert.Contains("display = \"Not decoded\";", code);
        Assert.Contains("raw typed bit evidence", code);
        Assert.Contains("row.EvidenceKind is \"Quality\" or \"Timestamp\"", code);
    }


}
