using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Bounded initial MX evidence seeds must use the existing MMS Read plan and
/// exact live DataSet schema. This is not another polling/read-all mechanism.
/// </summary>
public sealed class M217HInitialMxHydrationPlanTests
{
    private static LiveIedDataAttributeModel Leaf(
        string obj, string relative, string fc = "MX")
        => new()
        {
            ObjectReference = "LD/MMXU1." + obj,
            AttributePath = obj + "." + relative,
            MmsReference = "LD/MMXU1$" + fc + "$" + obj + "$" +
                relative.Replace('.', '$'),
            FunctionalConstraint = fc,
            SclBType = relative.EndsWith(".q", StringComparison.Ordinal) ? "Quality"
                : relative.EndsWith(".t", StringComparison.Ordinal) ? "UtcTime" : "FLOAT32"
        };

    private static LiveIedModelDiscoveryDocument Model(
        IReadOnlyList<LiveIedDataAttributeModel> attrs,
        string? datasetFc = "MX", bool duplicate = false)
    {
        var obj = new LiveIedDataObjectModel
        {
            Name = "PhV", Reference = "LD/MMXU1.PhV",
            InferredCdc = "WYE", Attributes = attrs
        };
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", DataObjects = duplicate ? [obj, obj] : [obj]
                }]
            }],
            DataSets = datasetFc is null ? [] : [new LiveIedDataSetModel
            {
                Reference = "LD/LLN0.Measurements",
                Domain = "LD", LogicalNode = "LLN0",
                Name = "Measurements", MemberCount = 1,
                Members = [new LiveIedDataSetMemberModel
                {
                    Reference = "LD/MMXU1.PhV",
                    FunctionalConstraint = datasetFc
                }]
            }]
        };
    }

    private static string[] Canonical(IReadOnlyList<IedBrowserReadTarget> plan)
        => plan.Select(t => IedBrowserLiveValues.CanonicalReference(t.Reference))
            .ToArray();

    [Fact]
    public void LiveFcdMxMagnitudesAndDeclaredCompanionsAreSeededWithoutGuessedValues()
    {
        var attrs = new[]
        {
            Leaf("PhV", "phsC.cVal.mag.f"), Leaf("PhV", "phsB.cVal.mag.f"),
            Leaf("PhV", "phsA.cVal.mag.f"), Leaf("PhV", "phsA.cVal.ang.f"),
            Leaf("PhV", "phsA.q"), Leaf("PhV", "phsA.t")
        };
        var live = Model(attrs);
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var plan = IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32);
        Assert.Equal(6, plan.Count);
        Assert.Equal(new[]
        {
            "LD/MMXU1.PhV.phsA.cVal.mag.f",
            "LD/MMXU1.PhV.phsB.cVal.mag.f",
            "LD/MMXU1.PhV.phsC.cVal.mag.f",
            "LD/MMXU1.PhV.phsA.q",
            "LD/MMXU1.PhV.phsA.t",
            "LD/MMXU1.PhV.phsA.cVal.ang.f"
        }, Canonical(plan));
        Assert.All(plan, t => Assert.Equal("MX", t.FunctionalConstraint));
        Assert.Equal("Quality", plan[3].DataType);
        Assert.Equal("UtcTime", plan[4].DataType);
        Assert.Equal("FLOAT32", plan[5].DataType);
        Assert.Single(IedBrowserReadPlan.BuildInitialPriority(live, ledger, 1));

        var association = ledger.Association;
        Assert.True(ledger.PublishForAssociation(association,
            plan[0].Reference, "MX", "0", "", "", "Report"));
        var afterReport = IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32);
        Assert.Equal(5, afterReport.Count);
        // Existing magnitude from actual Report does not disqualify its
        // independent live-declared q/t and angle from bounded initial reads.
        Assert.Contains(afterReport, t => IedBrowserLiveValues.CanonicalReference(t.Reference)
            == "LD/MMXU1.PhV.phsA.q");
        ledger.BeginAssociation();
        Assert.Equal(6, IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32).Count);
        ledger.MarkOffline();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32));
    }

    [Fact]
    public void ThreePhaseMxReadsAreFiniteAndIndependentOfReceivedCompanionValues()
    {
        var attrs = new[] { "phsA", "phsB", "phsC" }.SelectMany(phase =>
            new[] { Leaf("PhV", phase + ".cVal.mag.f"),
                Leaf("PhV", phase + ".cVal.ang.f"),
                Leaf("PhV", phase + ".q"),
                Leaf("PhV", phase + ".t") }).ToArray();
        var ledger = new IedBrowserLiveValues();
        var association = ledger.BeginAssociation();
        var live = Model(attrs);
        var plan = IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32);
        Assert.Equal(12, plan.Count);
        Assert.Equal(3, plan.Count(t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference).EndsWith(".cVal.mag.f", StringComparison.Ordinal)));
        Assert.Equal(3, plan.Count(t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference).EndsWith(".cVal.ang.f", StringComparison.Ordinal)));
        Assert.Equal(3, plan.Count(t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference).EndsWith(".q", StringComparison.Ordinal)));
        Assert.Equal(3, plan.Count(t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference).EndsWith(".t", StringComparison.Ordinal)));
        Assert.All(plan, t => Assert.Equal("MX", t.FunctionalConstraint));
        Assert.Equal(4, IedBrowserReadPlan.BuildInitialPriority(live, ledger, 4).Count);

        var quality = "LD/MMXU1.PhV.phsA.q";
        Assert.True(ledger.PublishForAssociation(association, quality, "MX",
            "0x0000", "", "", "Report"));
        var remaining = IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32);
        Assert.Equal(11, remaining.Count);
        Assert.DoesNotContain(remaining, t =>
            IedBrowserLiveValues.CanonicalReference(t.Reference) == quality);
    }

    [Fact]
    public void MxCompanionRequiresUnambiguousExactLiveFcdaAndIsNeverGuessed()
    {
        var live = Model(new[]
        {
            Leaf("PhV", "phsA.cVal.mag.f"),
            Leaf("PhV", "phsA.q", "ST"), // wrong FC
            Leaf("PhV", "phsA.t"), // right FC and exact member owner
            Leaf("PhV", "phsA.cVal.ang.f"),
            Leaf("PhV", "phsA.q2"), // vendor lookalike never eligible
            Leaf("PhV", "phsB.q"), // other phase never eligible
            Leaf("PhV", "phsA.t") // duplicate live declaration invalidates t
        });
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var plan = IedBrowserReadPlan.BuildInitialPriority(live, ledger, 32);
        Assert.Equal(new[] { "LD/MMXU1.PhV.phsA.cVal.mag.f",
            "LD/MMXU1.PhV.phsA.cVal.ang.f" }, Canonical(plan));

        // FCDA coverage must be live, not SCL-only or same-domain inference.
        var noDataset = Model(new[] { Leaf("PhV", "phsA.cVal.mag.f"),
            Leaf("PhV", "phsA.q"), Leaf("PhV", "phsA.t") }, datasetFc: null);
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(noDataset, ledger, 32));
    }

    [Fact]
    public void WithoutExactLiveMxDatasetAuthorityNoAutomaticAnalogTrafficIsPlanned()
    {
        var leaves = new[]
        {
            Leaf("PhV", "phsA.cVal.mag.f"),
            Leaf("PhV", "phsB.cVal.mag.f"),
            Leaf("PhV", "phsC.cVal.mag.f")
        };
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Model(leaves, datasetFc: null), ledger, 32));
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Model(leaves, datasetFc: "ST"), ledger, 32));
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(
            Model(leaves, duplicate: true), ledger, 32));
    }

    [Fact]
    public void StrictBoundDoesNotTurnFullModelIntoUnboundedReadAll()
    {
        var many = Enumerable.Range(0, 60).Select(i =>
            Leaf("PhV", "phsA.cVal.mag.f" + i)).ToArray();
        // Wrong leaf suffixes are rejected, including vendor lookalikes.
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        Assert.Empty(IedBrowserReadPlan.BuildInitialPriority(Model(many), ledger, 32));

        // Exact six seeds remain finite even when more model owners exist.
        // All 9 are separate exact live MX DataSet-covered leaves.
        var attrs = new[]
        {
            Leaf("PhV", "phsA.cVal.mag.f"), Leaf("PhV", "phsB.cVal.mag.f"),
            Leaf("PhV", "phsC.cVal.mag.f"), Leaf("PhV", "neut.cVal.mag.f"),
            Leaf("PhV", "net.cVal.mag.f"), Leaf("PhV", "res.cVal.mag.f"),
            Leaf("PhV", "phsAB.cVal.mag.f"), Leaf("PhV", "phsBC.cVal.mag.f"),
            Leaf("PhV", "phsCA.cVal.mag.f")
        };
        Assert.Equal(6, IedBrowserReadPlan.BuildInitialPriority(
            Model(attrs), ledger, 32).Count);
    }

    [Fact]
    public void ExistingRuntimeEnforcesSameGlobalAndTimeBudget()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null &&
               !File.Exists(Path.Combine(root.FullName, "Services",
                   "Iec61850MonitorRuntime.cs")))
            root = root.Parent;
        Assert.NotNull(root);
        var code = File.ReadAllText(Path.Combine(root!.FullName,
            "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("InitialHydrationMaxTargets = 32", code);
        Assert.Contains("InitialHydrationTotalBudget = TimeSpan.FromSeconds(10)", code);
        Assert.Contains("await ReadBrowserValuesAsync(device, plan, inbox,", code);
    }
}
