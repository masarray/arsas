using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class M217DGooseRcbFilesUxRegressionTests
{
    [Fact]
    public void ReportFlagsStayInsideControlBlockExpandableSection()
    {
        var raw = new List<IedBrowserDetailRow>
        {
            new("Section", "Control Block attributes"),
            new("Property", "RptEna", "true"),
            new("Section", "Trigger options"),
            new("FlagGroup", "Trigger options"),
            new("Section", "Optional fields"),
            new("FlagGroup", "Optional fields"),
            new("Section", "Information received in last Report"),
            new("Property", "Last Report status", "—"),
            new("Section", "Data"),
            new("DataHeader", "Name", "Value")
        };
        var sections = IedBrowserDetailSectionLayout.Attach(raw);
        var root = Assert.Single(sections.Where(x => x.Name == "Control Block attributes"));
        Assert.True(root.HasChildren);
        Assert.True(root.ExpandByDefault);
        var children = root.GetChildren();
        Assert.Contains(children, x => x.Kind == "FlagGroup" && x.Name == "Trigger options");
        Assert.Contains(children, x => x.Kind == "FlagGroup" && x.Name == "Optional fields");
        Assert.DoesNotContain(sections, x => x.Kind == "Section" && x.Name == "Trigger options");
        Assert.DoesNotContain(sections, x => x.Kind == "Section" && x.Name == "Optional fields");
        Assert.Contains(sections, x => x.Name == "Information received in last Report");
        Assert.Contains(sections, x => x.Name == "Data");
    }

    [Fact]
    public void BrowserGooseButtonUsesExistingSubscriberAndDoesNotEnableControlBlock()
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "MainWindow.xaml")))
            root = root.Parent;
        Assert.NotNull(root);
        var xaml = File.ReadAllText(Path.Combine(root!.FullName, "MainWindow.xaml"));
        var source = File.ReadAllText(Path.Combine(root.FullName, "MainWindow.IedBrowser.cs"));
        Assert.Contains("Margin=\"10,0,5,0\"", xaml);
        Assert.Contains("IedBrowserGooseSubscribeButton", xaml);
        Assert.Contains("Click=\"IedBrowserGooseSubscribe_Click\"", xaml);
        Assert.Contains("OpenIedGooseSubscriber(device);", source);
        Assert.Contains("View GOOSE Capture", source);
        Assert.DoesNotContain("SubscribeAsync(", source);
    }
}
