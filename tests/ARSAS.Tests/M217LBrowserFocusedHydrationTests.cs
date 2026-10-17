using ArIED61850Tester.Models;
using AR.Iec61850.Discovery;

namespace ARSAS.Tests;

/// <summary>Focused Browser acquisition is finite, read-only and reuses the
/// already verified live FC inventory; no Read All or shadow value source.</summary>
public sealed class M217LBrowserFocusedHydrationTests
{
    [Fact]
    public void FocusedHydrationSourceUsesExistingIsolatedReadAndCurrentAssociation()
    {
        var source = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("QueueIedBrowserFocusedRead(row)", source);
        Assert.Contains("Tag = \"FocusedAuto\"", source);
        Assert.Contains("const int maxFocusedTargets = 12", source);
        Assert.Contains("cts.CancelAfter(TimeSpan.FromSeconds(8))", source);
        Assert.Contains("device.LiveDiscoveryModel is null", source);
        Assert.Contains("device.LiveValues.Association", source);
        Assert.Contains("IedBrowserReadPlan.Build(", source);
        Assert.Contains("IedBrowserIsolatedReadService.ReadAsync(", source);
        Assert.Contains("IedBrowserReadPlan.BuildDataSetUnitRead(", source);
        Assert.Contains("currentPage?.Payload is LiveIedDataSetModel unitSet", source);
        Assert.Contains("page?.Kind is not (\"SGCB\" or \"LN\" or \"DataSet\")", source);
        Assert.Contains("sourceDevice.LiveValues.PublishForAssociation(", source);
        Assert.DoesNotContain("Tag = \"All\" },", source);
    }

    [Fact]
    public void VerifiedSgControlStateRetainsStrictReadOnlyFcInventory()
    {
        var control = Read("Models/IedBrowserControlStateEvidence.cs");
        Assert.Contains("NumOfSG", control);
        Assert.Contains("ActSG", control);
        Assert.Contains("LActTm", control);
        var plan = Read("Models/IedBrowserReadPlan.cs");
        Assert.Contains("IedBrowserControlStateEvidence.ReadTargets", plan);
        var browser = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("currentPage?.Kind == \"SGCB\"", browser);
        Assert.Contains("t.FunctionalConstraint == \"SG\" ? 1 : 0", browser);
        Assert.Contains("targets.Where(t => t.FunctionalConstraint is \"ST\" or \"MX\")", browser);
    }

    [Fact]
    public void ExactLiveMeasurementDatasetPlansOnlyDeclaredPairedCfUnits()
    {
        const string owner = "LD/MMXU1.A";
        static LiveIedDataAttributeModel Attribute(string fc, string suffix) => new()
        {
            ObjectReference = owner,
            AttributePath = "A." + suffix,
            MmsReference = "LD/MMXU1$" + fc + "$A$" + suffix.Replace('.', '
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,
                   "engines", "ARIEC61850.lock.json")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName,
            path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
),
            FunctionalConstraint = fc,
            SclBType = "Enum"
        };
        var obj = new LiveIedDataObjectModel
        {
            Name = "A", Reference = owner, InferredCdc = "WYE",
            Attributes =
            [
                Attribute("MX", "phsA.cVal.mag.f"),
                Attribute("CF", "units.SIUnit"),
                Attribute("CF", "units.multiplier")
            ]
        };
        var set = new LiveIedDataSetModel
        {
            Name = "Measurements", Reference = "LD/LLN0.Measurements",
            Domain = "LD", LogicalNode = "LLN0",
            Members = [new LiveIedDataSetMemberModel
            {
                Reference = owner, FunctionalConstraint = "MX"
            }]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                MmsDomain = "LD", LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    Name = "MMXU1", DataObjects = [obj]
                }]
            }],
            DataSets = [set]
        };
        var values = new IedBrowserLiveValues();
        values.BeginAssociation();
        var targets = IedBrowserReadPlan.BuildDataSetUnitRead(model, set, values);
        Assert.Equal(2, targets.Count);
        Assert.All(targets, target => Assert.Equal("CF", target.FunctionalConstraint));
        Assert.Contains(targets, target => target.Reference.EndsWith("$units$SIUnit", StringComparison.Ordinal));
        Assert.Contains(targets, target => target.Reference.EndsWith("$units$multiplier", StringComparison.Ordinal));
        var association = values.Association;
        Assert.True(values.PublishForAssociation(association,
            owner + ".units.SIUnit", "CF", "5", "", "", "MMS Read"));
        var missing = IedBrowserReadPlan.BuildDataSetUnitRead(model, set, values);
        Assert.Single(missing);
        Assert.EndsWith("$units$multiplier", missing[0].Reference);

        // A mismatched object/FC is not authorized by the selected set.
        var another = new LiveIedDataSetModel
        {
            Name = "Wrong", Reference = "LD/LLN0.Wrong",
            Domain = "LD", LogicalNode = "LLN0",
            Members = [new LiveIedDataSetMemberModel
            {
                Reference = owner, FunctionalConstraint = "ST"
            }]
        };
        Assert.Empty(IedBrowserReadPlan.BuildDataSetUnitRead(model, another, values));
    }

    private static string Read(string path)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName,
                   "engines", "ARIEC61850.lock.json")))
            root = root.Parent;
        Assert.NotNull(root);
        return File.ReadAllText(Path.Combine(root!.FullName,
            path.Replace('/', Path.DirectorySeparatorChar)));
    }
}
