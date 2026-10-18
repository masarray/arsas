using AR.Iec61850.Discovery;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Field regression: a status-heavy LN must still admit its live SP settings
/// and exact CF metadata; FC roots use the captured variable name and typed GVA.
/// </summary>
public sealed class P0ProtectionFocusedCoverageAndFcRootTests
{
    const string Ln = "BCUGEF650/gndPTOC2";
    static LiveIedDataObjectModel Obj(string name, params (string leaf, string fc)[] attrs)
        => new()
        {
            Name = name, Reference = Ln + "." + name,
            Attributes = attrs.Select(x => new LiveIedDataAttributeModel
            {
                ObjectReference = Ln + "." + name,
                AttributePath = name + "." + x.leaf,
                MmsReference = Ln + "$" + x.fc + "$" + name + "$" + x.leaf.Replace('.', '$'),
                FunctionalConstraint = x.fc, SclBType = "Int32",
                TypeDiscoveryStatus = "Exact"
            }).ToArray()
        };
    static (LiveIedModelDiscoveryDocument, IedBrowserTreeRow) Model()
    {
        var objects = new List<LiveIedDataObjectModel>();
        foreach (var name in new[] { "Beh", "Blk", "Health", "Mod", "NamPlt", "Op", "Str" })
        {
            objects.Add(Obj(name, ("general", "ST"), ("neut", "ST"),
                ("q", "ST"), ("t", "ST"), ("d", "DC")));
        }
        foreach (var (name, leaf) in new[]
        {
            ("PTOCEna", "setVal"), ("InMagTyp", "setVal"),
            ("RsMod", "setVal"), ("StrVal", "setMag"),
            ("StrMult", "setMag"), ("TmMult", "setMag"),
            ("TmACrv", "setCharact"), ("ExNumTr", "setMag")
        })
            objects.Add(Obj(name, (leaf, "SP"), ("units.SIUnit", "CF"),
                ("units.multiplier", "CF"), ("d", "DC")));
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "gndPTOC2", LnClass = "PTOC", DataObjects = objects
        };
        return (new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            { MmsDomain = "BCUGEF650", LogicalNodes = [ln] }]
        }, new IedBrowserTreeRow("model/ln/" + Ln, ln.Name, "LN", 2, ln));
    }

    [Fact]
    public void StatusHeavyLnCannotStarveTheEightSpSettingOwners()
    {
        var (model, page) = Model();
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var plan = IedBrowserReadPlan.BuildFocused(model, page, ledger);
        Assert.InRange(plan.Count, 1, 24);
        foreach (var name in new[] { "PTOCEna", "InMagTyp", "RsMod",
            "StrVal", "StrMult", "TmMult", "TmACrv", "ExNumTr" })
            Assert.Contains(plan, p =>
                p.FunctionalConstraint == "SP" &&
                IedBrowserLiveValues.CanonicalReference(p.Reference)
                    .StartsWith(Ln + "." + name + ".", StringComparison.Ordinal));
        Assert.Contains(plan, p => p.FunctionalConstraint == "ST");
        Assert.Contains(plan, p => p.FunctionalConstraint == "CF");
        Assert.DoesNotContain(plan, p => p.FunctionalConstraint is "CO" or "SE");
    }

    [Theory]
    [InlineData("SP", "SP0")]
    [InlineData("SG", "SG0")]
    [InlineData("CF", "CF0")]
    [InlineData("DC", "DC0")]
    [InlineData("EX", "EX0")]
    public void CapturedGEVariableNamingStillNeedsTypedWireVerification(
        string fc, string wireFc)
    {
        var t = new IedBrowserReadTarget(Ln + "$" + fc + "$StrVal$setMag",
            fc, "Int32");
        Assert.Equal(Ln + "$" + wireFc,
            IedBrowserSgRootProjection.RootFor(t));
    }

    [Fact]
    public void CfRootDoesNotProjectSpOrWrongAttributeEvenIfValuesCoincide()
    {
        var type = new MmsTypeSpecificationNode
        {
            MmsType = "structure", Children =
            [
                new MmsTypeSpecificationNode
                {
                    Name = "ExNumTr", MmsType = "structure",
                    Children = [new MmsTypeSpecificationNode
                    {
                        Name = "units", MmsType = "structure",
                        Children = [new MmsTypeSpecificationNode
                            { Name = "SIUnit", MmsType = "integer" }]
                    }]
                }
            ]
        };
        var data = MmsDataValue.Structure([
            MmsDataValue.Structure([
                MmsDataValue.Structure([MmsDataValue.Integer(4)])])]);
        var cf = new IedBrowserReadTarget(Ln + "$CF$ExNumTr$units$SIUnit", "CF", "Enum");
        var sp = new IedBrowserReadTarget(Ln + "$SP$ExNumTr$units$SIUnit", "SP", "Enum");
        var result = Assert.Single(IedBrowserSgRootProjection.Project(
            Ln + "$CF0", type, data, [cf, sp]));
        Assert.Equal(cf.Reference, result.Target.Reference);
        Assert.Equal("4", result.Value);
        Assert.Empty(IedBrowserSgRootProjection.Project(
            Ln + "$CF0", type, data, [sp]));
    }
}
