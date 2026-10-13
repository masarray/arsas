namespace ARSAS.Tests;
public sealed class P109ScopedReleaseNoBrowserRegressionTests
{
    [Fact]
    public void SevenTabShellAndOriginalRcbExport_AreRetained()
    {
        var xaml = Read("MainWindow.xaml");
        var nav = Read("MainWindow.NavigationLayoutFix.cs");
        var exporter = Read("RcbMultiExportWindow.cs");
        Assert.DoesNotContain("IedBrowserTab", xaml);
        Assert.DoesNotContain("NavIedBrowserButton", xaml);
        Assert.Contains("Grid.ColumnSpan=\"7\"", xaml);
        Assert.Contains("var cellWidth = contentWidth / 7d", nav);
        Assert.Contains("Math.Clamp(tabs.SelectedIndex, 0, 6)", nav);
        Assert.DoesNotContain("_inspectorPanel", exporter);
        Assert.DoesNotContain("Report ID", exporter);
        Assert.Contains("IedEditRcb_Click", Read("MainWindow.RcbCapability.cs"));
        Assert.Contains("IedEditRcbMulti_Click(button, e)", Read("MainWindow.RcbExportClickAuthority.cs"));
        Assert.False(File.Exists(Find("MainWindow.IedBrowser.cs")));
        Assert.False(File.Exists(Find("Models/RcbInspectionEvidence.cs")));
    }
    [Fact]
    public void EventPropertiesDrawerAndFullLabels_Ship()
    {
        var xaml = Read("MainWindow.xaml");
        var details = Read("MainWindow.EventDetails.cs");
        var policy = Read("Services/Iec61850EventOriginPolicy.cs");
        Assert.Contains("x:Name=\"EventDetailsDrawer\"", xaml);
        Assert.Contains("EventLogGrid_SelectionChanged", xaml);
        Assert.Contains("ReasonForInclusion", details);
        Assert.Contains("EntryID", details);
        Assert.Contains("TimeSpan.FromSeconds(18)", details);
        Assert.Contains("HideEventDetails();", Read("MainWindow.xaml.cs"));
        Assert.Contains("Spontaneous", policy);
        Assert.Contains("Integrity", policy);
        Assert.Contains("State change", policy);
    }
    private static string Read(string p) => File.ReadAllText(Find(p));
    private static string Find(string p)
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var path = Path.Combine(d.FullName,p);
            if (File.Exists(path)) return path;
        }
        return Path.Combine(AppContext.BaseDirectory,p);
    }
}