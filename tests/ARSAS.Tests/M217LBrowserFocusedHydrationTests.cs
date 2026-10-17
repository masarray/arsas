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
