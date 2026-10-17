using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Physical GR_X_7SX85 shape: read-on-open never turns a model enumeration
/// into process truth; CF units belong to the exact MX member ancestor, and
/// all wire targets require an actual live discovery declaration.
/// </summary>
public sealed class M217LBrowserContextValueParityTests
{
    private const string Owner = "LD/MMXU1.A";
    private const string Other = "LD/MMXU2.A";

    private static LiveIedDataAttributeModel Attr(string owner, string fc, string relative)
    {
        var slash = owner.LastIndexOf('/');
        var dot = owner.IndexOf('.', slash + 1);
        var ln = owner[..dot];
        var name = owner[(dot + 1)..];
        return new()
        {
            ObjectReference = owner,
            AttributePath = name + "." + relative,
            FunctionalConstraint = fc,
            SclBType = relative is "units.SIUnit" or "units.multiplier" ? "Enum" :
                relative.EndsWith(".f", StringComparison.Ordinal) ? "FLOAT32" : "BOOLEAN",
            MmsReference = ln + "$" + fc + "$" + name + "$" +
                relative.Replace('.', '$')
        };
    }

    private static LiveIedDataObjectModel Obj(string owner,
        params LiveIedDataAttributeModel[] attributes) => new()
        {
            Name = owner[(owner.LastIndexOf('.') + 1)..],
            Reference = owner,
            InferredCdc = "WYE",
            Attributes = attributes
        };

    private static LiveIedModelDiscoveryDocument Model(
        params LiveIedDataObjectModel[] objects)
        => new()
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes =
                [
                    new LiveIedLogicalNodeModel
                    {
                        Name = "MMXU1",
                        DataObjects = objects.Where(o =>
                            o.Reference.StartsWith("LD/MMXU1.", StringComparison.Ordinal))
                            .ToArray()
                    },
                    new LiveIedLogicalNodeModel
                    {
                        Name = "MMXU2",
                        DataObjects = objects.Where(o =>
                            o.Reference.StartsWith("LD/MMXU2.", StringComparison.Ordinal))
                            .ToArray()
                    },
                    new LiveIedLogicalNodeModel
                    {
                        Name = "LTIM0",
                        DataObjects = objects.Where(o =>
                            o.Reference.StartsWith("LD/LTIM0.", StringComparison.Ordinal))
                            .ToArray()
                    }
                ]
            }]
        };

    private static (LiveIedDataSetModel Set, IedBrowserTreeRow Page)
        SetPage(string member, string fc = "MX")
    {
        var ds = new LiveIedDataSetModel
        {
            Name = "Measurements", Domain = "LD", LogicalNode = "LLN0",
            Reference = "LD/LLN0.Measurements", MemberCount = 1,
            Members = [new LiveIedDataSetMemberModel
            {
                Reference = member, FunctionalConstraint = fc
            }]
        };
        return (ds, new IedBrowserTreeRow("datasets/set/" + ds.Reference,
            ds.Name, "DataSet", 3, ds));
    }

    [Fact]
    public void ExactPhaseCfIsRenderedOutsideMxReportAndNeverBorrowedFromOtherPhase()
    {
        var model = Model(Obj(Owner,
            Attr(Owner, "MX", "phsA.cVal.mag.f"),
            Attr(Owner, "MX", "phsA.q"),
            Attr(Owner, "CF", "phsA.units.SIUnit"),
            Attr(Owner, "CF", "phsA.units.multiplier"),
            Attr(Owner, "CF", "phsB.units.SIUnit"),
            Attr(Owner, "CF", "phsB.units.multiplier"),
            Attr(Owner, "CO", "Oper.ctlVal")));
        var (ds, page) = SetPage(Owner + ".phsA");
        var shown = IedBrowserMeasurementConfiguration.ForDataSet(ds, model);
        var config = Assert.Single(shown);
        Assert.Equal(Owner + ".phsA.units", config.ValueReference);
        Assert.Equal("CF", config.FunctionalConstraint);
        Assert.Equal(new[] { "SIUnit [CF]", "multiplier [CF]" },
            config.GetChildren().Select(x => x.Name).ToArray());
        Assert.DoesNotContain(shown, x => x.ValueReference.Contains("phsB", StringComparison.Ordinal));

        var ledger = new IedBrowserLiveValues();
        var current = ledger.BeginAssociation();
        var plan = IedBrowserContextHydrationPlan.Build(model, page, ledger, 12);
        Assert.Equal(2, plan.Count);
        Assert.All(plan, x => Assert.Equal("CF", x.FunctionalConstraint));
        Assert.DoesNotContain(plan, x => x.Reference.Contains("phsB", StringComparison.Ordinal));
        Assert.DoesNotContain(plan, x => x.FunctionalConstraint == "CO");
        var unit = plan.Single(x => x.Reference.EndsWith("$SIUnit", StringComparison.Ordinal));
        var multiplier = plan.Single(x => x.Reference.EndsWith("$multiplier", StringComparison.Ordinal));

        // Metadata is not process truth: unit appears only after exact CF reads.
        var evidence = new IedBrowserCfEvidencePresentation(ledger, model, null);
        Assert.False(evidence.TryFormatUnitsParent(config.ValueReference,
            "CF", out _, out _));
        Assert.True(ledger.PublishForAssociation(current, unit.Reference, "CF",
            "29", "", "", "MMS Read"));
        Assert.False(evidence.TryFormatUnitsParent(config.ValueReference,
            "CF", out _, out _));
        Assert.True(ledger.PublishForAssociation(current, multiplier.Reference, "CF",
            "3", "", "", "MMS Read"));
        Assert.True(evidence.TryFormatUnitsParent(config.ValueReference,
            "CF", out _, out _));

        // Same exact CF values must not be acquired twice in one association.
        Assert.Empty(IedBrowserContextHydrationPlan.Build(model, page, ledger, 12));
        ledger.BeginAssociation();
        Assert.Equal(2, IedBrowserContextHydrationPlan.Build(model, page,
            ledger, 12).Count);
    }

    [Fact]
    public void FocusedLnAutoReadNeverScansOtherDoOrCommandAndEditFcs()
    {
        var one = Obj(Owner, Attr(Owner, "MX", "phsA.cVal.mag.f"),
            Attr(Owner, "MX", "phsA.q"), Attr(Owner, "CF", "units.SIUnit"),
            Attr(Owner, "CF", "units.multiplier"),
            Attr(Owner, "CO", "Oper.ctlVal"), Attr(Owner, "SE", "setVal"));
        var two = Obj("LD/MMXU1.B", Attr("LD/MMXU1.B", "ST", "stVal"));
        var model = Model(one, two, Obj(Other, Attr(Other, "ST", "stVal")));
        var page = new IedBrowserTreeRow("model/ln/LD/MMXU1",
            "MMXU1", "LN", 2, model.LogicalDevices[0].LogicalNodes[0]);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();

        var focused = IedBrowserContextHydrationPlan.Build(model, page,
            ledger, 12, Owner);
        Assert.Equal(4, focused.Count);
        Assert.All(focused, x => Assert.StartsWith("LD/MMXU1$", x.Reference));
        Assert.DoesNotContain(focused, x => x.Reference.Contains("$B$", StringComparison.Ordinal));
        Assert.DoesNotContain(focused, x => x.FunctionalConstraint is "CO" or "SE");

        var allLn = IedBrowserContextHydrationPlan.Build(model, page, ledger, 12);
        Assert.Contains(allLn, x => x.Reference == "LD/MMXU1$ST$B$stVal");
        Assert.DoesNotContain(allLn, x => x.Reference.StartsWith("LD/MMXU2$", StringComparison.Ordinal));
        Assert.Empty(IedBrowserContextHydrationPlan.Build(model, page, ledger, 0));
        ledger.MarkOffline();
        Assert.Empty(IedBrowserContextHydrationPlan.Build(model, page, ledger, 12));
    }

    [Fact]
    public void SgcbUsesOnlyMmsVerifiedSpFieldsAndActiveSgNeverSe()
    {
        var sg = Obj("LD/LTIM0.TmChgST", Attr("LD/LTIM0.TmChgST", "SG", "setVal"),
            Attr("LD/LTIM0.TmChgST", "SE", "setVal"));
        var model = Model(sg);
        var verified = new LiveIedControlBlockModel
        {
            Kind = "SettingGroupControl", Domain = "LD", LogicalNode = "LLN0",
            Name = "SGCB", Reference = "LD/LLN0.SP.SGCB",
            FunctionalConstraint = "SP", DiscoveryStatus = "AttributeInventoryOnly",
            Attributes = ["NumOfSG", "ActSG", "LActTm"]
        };
        var page = new IedBrowserTreeRow("sg/LD", "SGCB", "SGCB", 3, verified);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var plan = IedBrowserContextHydrationPlan.Build(model, page, ledger, 12);
        Assert.Contains(plan, x => x.Reference == "LD/LLN0.SGCB.ActSG" &&
            x.FunctionalConstraint == "SP");
        Assert.Contains(plan, x => x.Reference == "LD/LTIM0$SG$TmChgST$setVal");
        Assert.DoesNotContain(plan, x => x.FunctionalConstraint == "SE");

        var unverified = new LiveIedControlBlockModel
        {
            Kind = verified.Kind, Domain = verified.Domain, LogicalNode = verified.LogicalNode,
            Name = verified.Name, Reference = verified.Reference,
            FunctionalConstraint = verified.FunctionalConstraint,
            Attributes = ["NumOfSG", "ActSG"]
            // Missing AttributeInventoryOnly -> no speculative SGCB MMS read.
        };
        var offlinePage = new IedBrowserTreeRow("sg/offline",
            "SGCB", "SGCB", 3, unverified);
        var offlineTargets = IedBrowserContextHydrationPlan.Build(model, offlinePage,
            ledger, 12);
        Assert.DoesNotContain(offlineTargets, x =>
            x.Reference.StartsWith("LD/LLN0.SGCB", StringComparison.Ordinal));
    }

    [Fact]
    public void DataSetPageKeepsMxMembersAndCfContextStrictlySeparate()
    {
        var model = Model(Obj(Owner,
            Attr(Owner, "MX", "phsA.cVal.mag.f"),
            Attr(Owner, "MX", "phsA.q"),
            Attr(Owner, "CF", "units.SIUnit"),
            Attr(Owner, "CF", "units.multiplier")));
        var (set, page) = SetPage(Owner + ".phsA");
        var withSet = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = model.LogicalDevices, DataSets = [set]
        };
        var rows = IedBrowserPresentation.Details(page, withSet, true);
        Assert.Contains(rows, x => x.Name.Contains(
            "Engineering configuration", StringComparison.Ordinal));
        var context = Assert.Single(rows.Where(r => r.ValueReference == Owner + ".units"));
        Assert.Equal("CF", context.FunctionalConstraint);
        Assert.All(context.GetChildren(), row => Assert.Equal("CF", row.FunctionalConstraint));
        var dataSetMember = Assert.Single(rows.Where(r =>
            r.Name.Contains("phsA", StringComparison.Ordinal) &&
            r.FunctionalConstraint == "MX"));
        Assert.NotSame(context, dataSetMember);
    }
}
