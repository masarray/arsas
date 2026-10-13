using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P109IedBrowserAndEventDrawerContractTests
{
    [Fact]
    public void BrowserIsAnEighthPermanentNativeTab_WithoutRenumberingExistingWorkflows()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("x:Name=\"IedBrowserTab\" Header=\"IED Browser\"", xaml);
        Assert.Contains("x:Name=\"NavIedBrowserButton\" Grid.Column=\"7\"", xaml);
        Assert.Contains("x:Name=\"NativeFatTab\" Header=\"FAT\"", xaml);
        Assert.Contains("x:Name=\"IedBrowserTreeGrid\"", xaml);
        Assert.Contains("x:Name=\"IedBrowserDetailList\"", xaml);
        Assert.Contains("GridSplitter Grid.Column=\"1\"", xaml);
        Assert.DoesNotContain("x:Name=\"IedBrowserSections\"", xaml);
        Assert.Contains("MainTabs.SelectedIndex = IedBrowserWorkspaceIndex", code);
        Assert.Contains("BuildRcbExportRows(device, sourceInventory, _iedBrowserSnapshot)", code);
        Assert.Contains("IedBrowserPresentation.Roots(", code);
    }

    [Fact]
    public void ReadOnlyBrowserNeverInitiatesMmsOnOpening_AndDoesNotToggleExportSelection()
    {
        var code = Read("MainWindow.IedBrowser.cs");
        var bridge = Read("MainWindow.RcbCapability.cs");
        var card = Read("FaultRecordUxBehavior.cs");
        Assert.Contains("OpenIedBrowserRcb(device);", bridge);
        Assert.Contains("Browse Report Control Blocks and details", card);
        Assert.Contains("_rcbAvailabilityProbe.CheckAsync(device, cts.Token)", code);
        Assert.Contains("private async void IedBrowserReadRcb_Click", code);
        var opening = code.Split("internal void OpenIedBrowserRcb")[1].Split("private void IedBrowserRefresh_Click")[0];
        Assert.DoesNotContain("CheckAsync", opening);
        Assert.DoesNotContain("MmsWrite", code, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("ConfigureControlModelAsync(", code);
        Assert.DoesNotContain(".IsSelected =", code);
        Assert.Contains("CancelIedBrowserRead();", code);
    }

    [Fact]
    public void EventCategoriesAreHumanReadable_AndBufferedReplayIsNeverInferredFromBrcb()
    {
        var expected = new Dictionary<string,string>
        {
            ["dchg"] = "Spontaneous", ["gi"] = "GI",
            ["integrity"] = "Integrity", ["qchg"] = "Quality",
            ["dupd"] = "Data update"
        };
        foreach (var entry in expected)
            Assert.Equal(entry.Value, Iec61850EventOriginPolicy.Cause(entry.Key, "Static DataSet: StaticBrcb"));
        Assert.Equal("Poll", Iec61850EventOriginPolicy.Cause("dchg", "MMS Polling"));
        Assert.Equal("State change", Iec61850EventOriginPolicy.Cause("", "Static DataSet: StaticBrcb"));
        Assert.Equal("Spontaneous", Iec61850EventOriginPolicy.Cause("dchg", "BRCB", false));
        Assert.Equal("Buffer", Iec61850EventOriginPolicy.Cause("dchg", "BRCB", true));
    }

    [Fact]
    public void EventDetailsDrawerAutoHidesAndDoesNotClaimMissingWireFields()
    {
        var xaml = Read("MainWindow.xaml");
        var code = Read("MainWindow.EventDetails.cs");
        Assert.Contains("x:Name=\"EventDetailsDrawer\"", xaml);
        Assert.Contains("EventLogGrid_SelectionChanged", xaml);
        Assert.Contains("TimeSpan.FromSeconds(18)", code);
        Assert.Contains("HideEventDetails()", code);
        Assert.Contains("EntryID", code);
        Assert.Contains("— (not captured)", code);
        Assert.Contains("Raw Reason", code);
        Assert.Contains("ReasonForInclusion", code);
        Assert.DoesNotContain("Header=\"Raw Reason\"", xaml);
    }

    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }
}
