using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class IntegratedFatWorkspaceRegressionTests
{
    [Fact]
    public void MainWindow_ExposesFatAsSeventhTabWithoutRemovingEngineeringTabs()
    {
        var document = XDocument.Load(FindRepoFile("MainWindow.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var tabs = document.Descendants(presentation + "TabItem").ToArray();
        Assert.Equal(7, tabs.Length);
        Assert.Equal("FAT", (string?)tabs[^1].Attribute("Header"));
        Assert.Contains(document.Descendants(), element =>
            (string?)element.Attribute(x + "Name") == "NavFatButton" &&
            (string?)element.Attribute("Tag") == "6");
    }

    [Fact]
    public void SclOnlyLaunch_UsesIntegratedTabWhileLegacySourcesRemainFallback()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.cs"));

        Assert.Contains("launch.Project.Sources.All", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSourceKinds.Scl", source, StringComparison.Ordinal);
        Assert.Contains("ShowIntegratedFatWorkspace(launch)", source, StringComparison.Ordinal);
        Assert.Contains("new IoListTestingWindow", source, StringComparison.Ordinal);
    }

    [Fact]
    public void IntegratedFat_ReusesEngineeringIedsLiveValuesAndInPlacePreview()
    {
        var xaml = File.ReadAllText(FindRepoFile("IntegratedFatWorkspaceControl.xaml"));
        var code = File.ReadAllText(FindRepoFile("IntegratedFatWorkspaceControl.xaml.cs"));

        Assert.Contains("ItemsSource=\"{Binding EngineeringIeds}\"", xaml, StringComparison.Ordinal);
        Assert.Contains("SelectedEngineeringIed", xaml, StringComparison.Ordinal);
        Assert.Contains("Runtime.CurrentValue", xaml, StringComparison.Ordinal);
        Assert.Contains("DisplaySignalName", xaml, StringComparison.Ordinal);
        Assert.Contains("VALUE 1", xaml, StringComparison.Ordinal);
        Assert.Contains("VALUE 2", xaml, StringComparison.Ordinal);
        Assert.Contains("RESULT", xaml, StringComparison.Ordinal);
        Assert.Contains("WorkspaceView.Visibility = Visibility.Collapsed", code, StringComparison.Ordinal);
        Assert.Contains("PreviewView.Visibility = Visibility.Visible", code, StringComparison.Ordinal);
        Assert.Contains("IoFatReportPreviewDocumentBuilder.Build", code, StringComparison.Ordinal);
        Assert.DoesNotContain("new Window", code, StringComparison.Ordinal);

        var navigation = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));
        Assert.Contains("\"NavFatButton\"", navigation, StringComparison.Ordinal);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
