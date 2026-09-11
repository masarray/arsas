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
    public void EngineeringStaticDataSet_FatBootstrapIsNavigationGated()
    {
        var bootstrap = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");

        Assert.DoesNotContain(
            "window.QueueProductionFatEngineeringBootstrap();\n    }\n\n    private void ProductionFatEngineeringBootstrap_SelectionChanged",
            bootstrap,
            StringComparison.Ordinal);
        Assert.Contains(
            "e.PropertyName != nameof(SelectedDevice) || MainTabs.SelectedIndex != NativeFatWorkspaceIndex",
            bootstrap,
            StringComparison.Ordinal);
        Assert.Contains(
            "!_productionFatEngineeringBootstrapInstalled || MainTabs.SelectedIndex != NativeFatWorkspaceIndex",
            bootstrap,
            StringComparison.Ordinal);
        Assert.Contains(
            "_productionFatEngineeringBootstrapBusy ||\n            MainTabs.SelectedIndex != NativeFatWorkspaceIndex",
            bootstrap,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AutomaticFatBootstrap_PreservesStaticReportOnlyAuthority()
    {
        var bootstrap = Read("MainWindow.ProductionFatEngineeringBootstrap.cs");
        var authority = Read("MainWindow.SharedStaticDataSetAuthority.cs");

        Assert.Contains("PreserveSharedStaticDataSetAuthority(device);", bootstrap, StringComparison.Ordinal);
        Assert.DoesNotContain("MarkSharedSelectionAuthority(device);", bootstrap, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("UseHybrid", authority, StringComparison.Ordinal);
        Assert.Contains("_sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId);", authority, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddedDonor_IsPreparedBeforeShow_AndNormalizesIllegalMaximizedState()
    {
        var launch = Read("MainWindow.IoTesting.cs");
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");
        var tab = Read("MainWindow.ProductionFatTab.cs");

        var prepare = launch.IndexOf("window.PrepareForEmbeddedEngineeringHost();", StringComparison.Ordinal);
        var show = launch.IndexOf("window.Show();", StringComparison.Ordinal);
        Assert.True(prepare >= 0 && show > prepare, "Donor visibility must be prepared before Window.Show().");
        Assert.Contains("if (!ProductionFatTabReady)\n            Hide();", launch, StringComparison.Ordinal);

        var normal = host.IndexOf("WindowState = WindowState.Normal;", StringComparison.Ordinal);
        var nonActivating = host.IndexOf("ShowActivated = false;", StringComparison.Ordinal);
        Assert.True(normal >= 0 && nonActivating > normal,
            "The hidden donor must leave Maximized state before ShowActivated=false is used.");
        Assert.Contains("Left = -32000d", host, StringComparison.Ordinal);
        Assert.Contains("Opacity = 0d", host, StringComparison.Ordinal);
        Assert.Contains("ShowInTaskbar = false", host, StringComparison.Ordinal);

        Assert.DoesNotContain("MainTabs.SelectedIndex = NativeFatWorkspaceIndex", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("Activate();", tab, StringComparison.Ordinal);
        Assert.DoesNotContain("if (!IsVisible)\n            Show();", tab, StringComparison.Ordinal);
    }

    [Fact]
    public void HiddenNonActivatingWpfDonor_CanShowAfterStateNormalization()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Exception? failure = null;
        var completed = false;
        var thread = new Thread(() =>
        {
            try
            {
                var window = new System.Windows.Window
                {
                    WindowState = System.Windows.WindowState.Normal,
                    ShowActivated = false,
                    ShowInTaskbar = false,
                    Opacity = 0d,
                    WindowStartupLocation = System.Windows.WindowStartupLocation.Manual,
                    Left = -32000d,
                    Top = -32000d
                };

                window.Show();
                window.Close();
                completed = true;
            }
            catch (Exception ex)
            {
                failure = ex;
            }
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(8)), "WPF donor Show/Close timed out.");
        Assert.Null(failure);
        Assert.True(completed);
    }

    [Fact]
    public void EmbeddedSurface_RemainsExactProductionGridAndPreviewSource()
    {
        var host = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");

        Assert.Contains("InstallFatV2WorkspaceUx();", host, StringComparison.Ordinal);
        Assert.Contains("InstallPerIedPrintPreview();", host, StringComparison.Ordinal);
        Assert.Contains("DetachProductionFatCentralWorkspace()", host, StringComparison.Ordinal);
        Assert.Contains("owner.MountProductionFatWorkspace(this, surface)", host, StringComparison.Ordinal);
        Assert.Contains("throw new InvalidOperationException(\"Production FAT center could not be detached", host, StringComparison.Ordinal);
        Assert.Contains("WindowState = WindowState.Maximized;", host, StringComparison.Ordinal);
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
