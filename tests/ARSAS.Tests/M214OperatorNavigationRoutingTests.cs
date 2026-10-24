using System.Xml.Linq;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class M214OperatorNavigationRoutingTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData(0, 7)]
    [InlineData(1, 0)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 8)]
    [InlineData(6, 6)]
    [InlineData(7, 5)]
    public void EightOperatorDestinations_KeepStableWorkspaceRoutes(int button, int tab)
    {
        Assert.Equal(tab, EngineeringNavigationRoute.TabForButton(button));
        Assert.Equal(button, EngineeringNavigationRoute.ButtonForTab(tab));
    }

    [Fact]
    public void LegacyLiveMonitor_IsNotAnOperatorDestination()
    {
        Assert.Equal(1, EngineeringNavigationRoute.ButtonForTab(1));
        Assert.DoesNotContain(Enumerable.Range(0, 8)
            .Select(EngineeringNavigationRoute.TabForButton), tab => tab == 1);
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EngineeringNavigationRoute.TabForButton(8));
    }

    [Fact]
    public void HeaderTabs_ExactlyMatchOperatorOrder_WithoutLosingLegacyWpfBindings()
    {
        var doc = XDocument.Load(Find("MainWindow.xaml"));
        var header = doc.Descendants(P + "Grid").Single(g =>
            (string?)g.Attribute(X + "Name") == "WorkflowNavGrid");
        var buttons = header.Elements(P + "Button")
            .OrderBy(b => (int?)b.Attribute("Grid.Column")).ToArray();
        var actual = buttons.Select(b => b.Descendants(P + "TextBlock")
            .First().Attribute("Text")?.Value).ToArray();
        Assert.Equal(new[]
        {
            "IED Browser", "Device Explorer", "Event Logs", "Annunciator",
            "GOOSE Sniff", "SMV", "Signal Testing", "Diagnostics"
        }, actual);
        Assert.Equal(8, buttons.Length);
        for (var i = 0; i < buttons.Length; i++)
        {
            Assert.Equal(i.ToString(), (string?)buttons[i].Attribute("Tag"));
            Assert.Equal(i.ToString(), (string?)buttons[i].Attribute("TabIndex"));
        }
        var tabs = doc.Descendants(P + "TabControl").Single(g =>
            (string?)g.Attribute(X + "Name") == "MainTabs")
            .Elements(P + "TabItem").ToArray();
        Assert.Equal(9, tabs.Length); // 8 destinations + internal legacy view
        Assert.Equal("Internal Live Monitor", (string?)tabs[1].Attribute("Header"));
        Assert.Equal("Collapsed", (string?)tabs[1].Attribute("Visibility"));
        Assert.Equal("SMV", (string?)tabs[8].Attribute("Header"));
        Assert.Equal("IED Browser", (string?)tabs[7].Attribute("Header"));
        Assert.DoesNotContain(buttons, b =>
            ((string?)b.Attribute(X + "Name")) == "NavLiveButton");
    }

    [Fact]
    public void SmvNav_UsesEmbeddedWireFirstViewer_AndPreservesIEDQuickWorkflow()
    {
        var source = File.ReadAllText(Find("MainWindow.IedSmvQuickStart.cs"));
        var main = File.ReadAllText(Find("MainWindow.xaml"));
        Assert.Contains("ActivateSelectedSmvWorkspace();", source);
        Assert.Contains("<views:SmvInlineViewer x:Name=\"MainSmvViewer\"/>", main);
        Assert.Contains("SmvViewerWindow(device)", source); // Legacy explicit IED snapshot path remains available.
        Assert.Contains("EnableFastWorkflow(adapter, routeDetail)", source);
        Assert.DoesNotContain("new MmsClientSession", source);
    }

    [Fact]
    public void SelectingSmvTab_DoesNotAutoOpenWindowOrStartNpcapCapture()
    {
        var main = File.ReadAllText(Find("MainWindow.xaml.cs"));
        const string tabRoute =
            "MainTabs.SelectedIndex == EngineeringNavigationRoute.SmvWorkspaceIndex";
        var index = main.IndexOf(tabRoute, StringComparison.Ordinal);
        Assert.True(index >= 0);
        var branch = main[index..Math.Min(main.Length, index + 380)];
        Assert.Contains("PrepareSelectedSmvWorkspace();", branch);
        Assert.DoesNotContain("ActivateSelectedSmvWorkspace();", branch);

        var viewer = File.ReadAllText(Find("MainWindow.IedSmvQuickStart.cs"));
        var prepareStart = viewer.IndexOf("private void PrepareSelectedSmvWorkspace()",
            StringComparison.Ordinal);
        var explicitStart = viewer.IndexOf("private void ActivateSelectedSmvWorkspace()",
            prepareStart, StringComparison.Ordinal);
        Assert.True(prepareStart > 0 && explicitStart > prepareStart);
        var passive = viewer[prepareStart..explicitStart];
        Assert.DoesNotContain("OpenSmvWorkspaceForDevice", passive);
        Assert.DoesNotContain("ListAdapters", passive);
        Assert.DoesNotContain("new SmvViewerWindow", passive);
        Assert.Contains("=> ActivateSelectedSmvWorkspace();", viewer);
        Assert.Contains("window.EnableFastWorkflow(adapter, routeDetail);", viewer);
    }

    private static string Find(string path)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var file = Path.Combine(d.FullName, path);
            if (File.Exists(file)) return file;
        }
        throw new FileNotFoundException(path);
    }
}
