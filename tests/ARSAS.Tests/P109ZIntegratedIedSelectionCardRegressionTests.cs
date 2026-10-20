using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// P0 Phase 3: an IED's selected-state accent is part of the card silhouette,
/// not a detached rail that steals horizontal space or obstructs operator buttons.
/// No IEC session, point/report or control state is changed.
/// </summary>
public sealed class P109ZIntegratedIedSelectionCardRegressionTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(string path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var full = Path.Combine(current.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
            current = current.Parent;
        }
        throw new FileNotFoundException(path);
    }

    [Theory]
    [InlineData("P2MainIedListItemStyle", "Card")]
    [InlineData("P2FatIedListItemStyle", "IedCard")]
    public void SelectionRailIsFlushInsideCardAndNeverCreatesADetachedGutter(
        string resourceKey, string cardName)
    {
        var doc = XDocument.Parse(Source("Resources/P2BlueSteelGreige.xaml"));
        var style = doc.Descendants(P + "Style").Single(x =>
            (string?)x.Attribute(X + "Key") == resourceKey);
        var template = style.Descendants(P + "ControlTemplate").Single();
        var root = template.Elements(P + "Grid").Single();
        var layers = root.Elements(P + "Border").ToArray();

        Assert.Equal(2, layers.Length);
        var card = layers[0];
        var accent = layers[1];
        Assert.Equal(cardName, (string?)card.Attribute(X + "Name"));
        Assert.Null(card.Attribute("Margin")); // no longer pushed 5px away from rail
        Assert.Equal("10", (string?)card.Attribute("CornerRadius"));
        Assert.Equal("1", (string?)card.Attribute("BorderThickness"));
        Assert.NotNull(card.Element(P + "ContentPresenter"));

        Assert.Equal("SelectionRail", (string?)accent.Attribute(X + "Name"));
        Assert.Equal("3", (string?)accent.Attribute("Width"));
        Assert.Equal("Left", (string?)accent.Attribute("HorizontalAlignment"));
        Assert.Equal("0,8,0,8", (string?)accent.Attribute("Margin"));
        Assert.Equal("0,2,2,0", (string?)accent.Attribute("CornerRadius"));
        Assert.Equal("False", (string?)accent.Attribute("IsHitTestVisible"));
        Assert.Equal("1", (string?)accent.Attribute("Panel.ZIndex"));
        Assert.Equal("Transparent", (string?)accent.Attribute("Background"));
        Assert.DoesNotContain("Margin=\"5,0,0,0\"", template.ToString(), StringComparison.Ordinal);

        var selected = template.Descendants(P + "Trigger").Single(t =>
            (string?)t.Attribute("Property") == "IsSelected" &&
            (string?)t.Attribute("Value") == "True");
        Assert.Contains(selected.Elements(P + "Setter"), s =>
            (string?)s.Attribute("TargetName") == "SelectionRail" &&
            (string?)s.Attribute("Property") == "Background" &&
            (string?)s.Attribute("Value") == "{StaticResource Accent}");
        Assert.Contains(selected.Elements(P + "Setter"), s =>
            (string?)s.Attribute("TargetName") == cardName &&
            (string?)s.Attribute("Property") == "BorderBrush");
    }

    [Fact]
    public void MainCardMonitoringAndSelectionKeepsExistingStateBrushesAndBindings()
    {
        var doc = XDocument.Parse(Source("Resources/P2BlueSteelGreige.xaml"));
        var style = doc.Descendants(P + "Style").Single(x =>
            (string?)x.Attribute(X + "Key") == "P2MainIedListItemStyle");
        var template = style.Descendants(P + "ControlTemplate").Single();
        Assert.Contains(template.Descendants(P + "DataTrigger"), t =>
            (string?)t.Attribute("Binding") == "{Binding IsMonitoring}");
        var combined = Assert.Single(template.Descendants(P + "MultiDataTrigger"));
        Assert.Contains(combined.Descendants(P + "Setter"), s =>
            (string?)s.Attribute("TargetName") == "SelectionRail" &&
            (string?)s.Attribute("Value") == "{StaticResource Accent}");
        Assert.Contains(combined.Descendants(P + "Setter"), s =>
            (string?)s.Attribute("TargetName") == "Card" &&
            (string?)s.Attribute("Value") == "#6F9D84");

        var adapter = Source("P2BlueSteelGreigeUx.cs");
        Assert.Contains("deviceList.ItemContainerStyle = itemStyle;", adapter, StringComparison.Ordinal);
        Assert.Contains("fatIedList.ItemContainerStyle = itemStyle;", adapter, StringComparison.Ordinal);
    }

    [Fact]
    public void CardActionsAndFixedUnderIpLineRemainOwnedByExistingWpfTemplate()
    {
        var doc = XDocument.Parse(Source("MainWindow.xaml"));
        var list = doc.Descendants(P + "ListBox").Single(x =>
            (string?)x.Attribute("ItemsSource") == "{Binding Devices}");
        var template = list.Element(P + "ListBox.ItemTemplate")!
            .Element(P + "DataTemplate")!;
        var line = template.Descendants(P + "ProgressBar").Single(x =>
            (string?)x.Attribute(X + "Name") == "BrowserReadProgressLine");
        Assert.Equal("3", (string?)line.Attribute("Height"));
        Assert.Equal("7", (string?)line.Parent?.Attribute("Height"));
        Assert.Equal("{Binding IedCardProgressPercent, Mode=OneWay}",
            (string?)line.Attribute("Value"));
        Assert.Contains(template.Descendants(P + "Button"), x =>
            (string?)x.Attribute("Click") == "IedStopAction_Click" &&
            (string?)x.Attribute("IsEnabled") == "{Binding CanStopAction}");
        Assert.Contains(template.Descendants(P + "Button"), x =>
            (string?)x.Attribute("Click") == "IedPlayAction_Click");
        Assert.Contains(template.Descendants(P + "Button"), x =>
            (string?)x.Attribute("Click") == "IedRemove_Click");
    }
}
