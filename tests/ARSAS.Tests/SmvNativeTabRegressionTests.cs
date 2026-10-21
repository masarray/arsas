using System.IO;

namespace ARSAS.Tests;

public sealed class SmvNativeTabRegressionTests
{
    [Fact]
    public void MainSmvTab_HostsFullViewerWithoutIEDSelectionGate()
    {
        var host = Read("MainWindow.xaml");
        var inline = Read("Views/SmvInlineViewer.xaml");
        var code = Read("Views/SmvInlineViewer.xaml.cs");
        var quick = Read("MainWindow.IedSmvQuickStart.cs");
        Assert.Contains("<views:SmvInlineViewer x:Name=\"MainSmvViewer\"/>", host);
        Assert.DoesNotContain("Select an IED in Device Explorer to launch", host);
        Assert.DoesNotContain("Open SMV Capture", host);
        foreach (var control in new[] { "AdapterCombo", "StartButton", "StopButton", "StreamList",
                                        "WaveformCanvas", "PhasorCanvas", "Measurements" })
            Assert.Contains(control, inline);
        Assert.Contains("new SmvLiveCaptureService()", code);
        Assert.Contains("new SmvSnapshotCaptureService().ListAdapters()", code);
        Assert.Contains("Volatile.Read(ref _selectedKey)", code);
        Assert.Contains("MainTabs.SelectedIndex = EngineeringNavigationRoute.SmvWorkspaceIndex", quick);
    }

    [Fact]
    public void GooseStart_IsInteractiveEvenBeforeUserChoosesNIC_AndDoesNotNeedSCL()
    {
        var code = Read("MainWindow.GooseSubscriber.cs");
        Assert.Contains("CanStartGooseSubscriber => !IsGooseCapturing && !GooseActionBusy;", code);
        Assert.Contains("Select the Ethernet adapter in the dropdown", code);
        Assert.Contains("_gooseBindingCatalog.SclDocument", code); // optional, not gate
        Assert.DoesNotContain("CanStartGooseSubscriber => !IsGooseCapturing && !GooseActionBusy && SelectedGooseAdapter is not null", code);
    }

    private static string Read(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
