using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class GooseEventHeaderLayoutTests
{
    [Fact]
    public void ActiveGooseEventToolbarUsesOneAlignedCompactRow()
    {
        var xaml = XDocument.Parse(Read("Views/GooseSubscriberLiteView.xaml"));
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var header = xaml.Descendants(w + "Grid")
            .Single(e => (string?)e.Attribute(x + "Name") == "GooseEventHeader");

        Assert.Equal("30", (string?)header.Attribute("MinHeight"));
        var columns = header.Element(w + "Grid.ColumnDefinitions")!.Elements(w + "ColumnDefinition").ToArray();
        Assert.Equal(new[] { "Auto", "*", "Auto" },
            columns.Select(c => (string?)c.Attribute("Width")).ToArray());
        var count = header.Descendants(w + "Border").Single(e =>
            (string?)e.Attribute(x + "Name") == "GooseEventCountBadge");
        Assert.Equal("21", (string?)count.Attribute("Height"));
        Assert.Equal("Center", (string?)count.Attribute("VerticalAlignment"));
        Assert.Equal("NoWrap", (string?)count.Descendants(w + "TextBlock").Single()
            .Attribute("TextWrapping"));
        Assert.Contains(header.Descendants(w + "TextBlock"),
            e => (string?)e.Attribute("Text") == "GOOSE Events");
    }

    [Fact]
    public void EventScopeFollowAndRetransmissionControlsRetainExistingContracts()
    {
        var xaml = XDocument.Parse(Read("Views/GooseSubscriberLiteView.xaml"));
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var header = xaml.Descendants(w + "Grid").Single(e =>
            (string?)e.Attribute(x + "Name") == "GooseEventHeader");
        var scope = header.Descendants(w + "ComboBox").Single();
        Assert.Equal("GooseEventScope", (string?)scope.Attribute(x + "Name"));
        Assert.Equal("{Binding GooseIedScopeIndex, Mode=TwoWay}",
            (string?)scope.Attribute("SelectedIndex"));
        Assert.Equal("{StaticResource ModernComboBox}", (string?)scope.Attribute("Style"));
        Assert.Equal(new[] { "Selected IED only", "All IED GOOSE signals" },
            scope.Elements(w + "ComboBoxItem").Select(i => (string?)i.Attribute("Content")).ToArray());
        var boxes = header.Descendants(w + "CheckBox").ToArray();
        Assert.Equal(2,boxes.Length);
        Assert.Equal(new[] {
            "{Binding FollowLatestGooseEvents, Mode=TwoWay}",
            "{Binding ShowGooseRetransmissions, Mode=TwoWay}"
        },boxes.Select(b => (string?)b.Attribute("IsChecked")).ToArray());
        Assert.All(boxes,b=>Assert.Equal("28",(string?)b.Attribute("Height")));
        var events = xaml.Descendants(w + "DataGrid").Single(g =>
            (string?)g.Attribute("ItemsSource") == "{Binding GooseVisibleEvents}");
        Assert.Equal("{Binding SelectedGooseEvent, Mode=TwoWay}",
            (string?)events.Attribute("SelectedItem"));
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
