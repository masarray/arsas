namespace ARSAS.Tests;

public sealed class ProductionFatM2PermanentHostRegressionTests
{
    [Fact]
    public void FatTab_IsPermanentHost_NotLauncherOrAlternateOpenSclWorkflow()
    {
        var source = Read("MainWindow.ProductionFatTab.cs");

        Assert.Contains("BuildProductionFatPermanentHost()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("BuildProductionFatLauncher", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open SCL for FAT", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Open ARSAS Project", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenSclFatTesting_Click", source, StringComparison.Ordinal);
        Assert.DoesNotContain("OpenIoListPackage_Click", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringStaticDataSet_PrewarmIsIndependentOfFatTabSelection()
    {
        var bootstrap = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");
        var shared = Read("MainWindow.SharedSclWorkspace.cs");

        Assert.Contains("e.PropertyName != nameof(SelectedDevice)", bootstrap, StringComparison.Ordinal);
        Assert.Contains("QueueProductionFatEngineeringBootstrap();", shared, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionFatEngineeringBootstrapBusy ||\n            MainTabs.SelectedIndex != NativeFatWorkspaceIndex", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("_productionFatEngineeringBootstrapInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex", bootstrap, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedDonor_IsPreparedBeforeShow_AndMainWindowStaysVisible()
    {
        var launch = Read("MainWindow.IoTesting.cs");
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");
        var tab = Read("MainWindow.ProductionFatTab.cs");

        var prepare = launch.IndexOf("window.PrepareForEmbeddedEngineeringHost();", StringComparison.Ordinal);
        var show = launch.IndexOf("window.Show();", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && show > prepare, "Donor visibility must be prepared before Window.Show().");
        Assert.Contains("if (!ProductionFatTabReady)\n            Hide();", launch, StringComparison.Ordinal);
        Assert.Contains("Left = -32000d", host, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0d", host, StringComparison.Ordinal);
        Assert.Contains("ShowActivated = false", host, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", host, StringComparison.Ordinal);

        Assert.DoesNotContain("MainTabs.SelectedIndex = NativeFatWorkspaceIndex", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Activate();", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsVisible)\n            Show();", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedSurface_RemainsExactProductionGridAndPreviewSource()
    {
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");

        Assert.Contains("InstallFatV2WorkspaceUx();", host, StringComparison.Ordinal);
        Assert.Contains("InstallPerIedPrintPreview();", host, StringComparison.Ordinal);
        Assert.Contains("DetachProductionFatCentralWorkspace()", host, StringComparison.Ordinal);
        Assert.Contains("owner.MountProductionFatWorkspace(this, surface)", host, StringComparison.Ordinal);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
