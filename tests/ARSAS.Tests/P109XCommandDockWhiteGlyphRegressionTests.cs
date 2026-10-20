using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// P0 cosmetic-only checkpoint: replacing Command Dock chrome must not
/// change MMS command admission, DataGrid row lifetime or other expanders.
/// </summary>
public sealed class P109XCommandDockWhiteGlyphRegressionTests
{
    private static string Source(string path)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, path);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            dir = dir.Parent;
        }

        throw new FileNotFoundException(path);
    }

    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void OperatorContoursRemainExactAndWhiteWithTransparentChrome()
    {
        var app = XDocument.Parse(Source("App.xaml"));
        string Geometry(string key) => app.Descendants(Presentation + "Geometry")
            .Single(node => (string?)node.Attribute(Xaml + "Key") == key).Value.Trim();

        Assert.Equal(
            "m480-340 180-180-57-56-123 123-123-57 56 180 180Zm0 260".Replace("123-123-57", "123-123-123-57"),
            Geometry("OperatorCommandDockChevronDownIcon")[..("m480-340 180-180-57-56-123 123-123-123-57 56 180 180Zm0 260".Length)]);
        Assert.StartsWith("m357-384 123-123 123 123 57-56", Geometry("OperatorCommandDockChevronUpIcon"), StringComparison.Ordinal);
        Assert.StartsWith("M324-111.5Q251-143 197-197", Geometry("OperatorCommandDockPowerIcon"), StringComparison.Ordinal);
        var style = app.Descendants(Presentation + "Style")
            .Single(node => (string?)node.Attribute(Xaml + "Key") == "CommandDockTransparentHeaderExpander");
        Assert.Equal("{x:Type Expander}", (string?)style.Attribute("TargetType"));
        Assert.Contains(style.Descendants(Presentation + "Setter"),
            s => (string?)s.Attribute("Property") == "Background" &&
                 (string?)s.Attribute("Value") == "Transparent");

        var paths = style.Descendants(Presentation + "Path").ToArray();
        Assert.Equal(2, paths.Length);
        Assert.All(paths, path => Assert.Equal("White", (string?)path.Attribute("Fill")));
        Assert.Contains(paths, p => (string?)p.Attribute("Data") == "{StaticResource OperatorCommandDockChevronDownIcon}");
        Assert.Contains(paths, p => (string?)p.Attribute("Data") == "{StaticResource OperatorCommandDockChevronUpIcon}");
    }

    [Fact]
    public void OneExpanderKeepsKeyboardToggleAndContentVisibleOnlyWhenExpanded()
    {
        var app = XDocument.Parse(Source("App.xaml"));
        var style = app.Descendants(Presentation + "Style")
            .Single(node => (string?)node.Attribute(Xaml + "Key") == "CommandDockTransparentHeaderExpander");
        var toggle = style.Descendants(Presentation + "ToggleButton")
            .Single(node => (string?)node.Attribute(Xaml + "Name") == "CommandDockHeaderToggle");

        Assert.Equal("{TemplateBinding Header}", (string?)toggle.Attribute("Content"));
        Assert.Equal("{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}",
            (string?)toggle.Attribute("IsChecked"));
        Assert.Equal("True", (string?)toggle.Attribute("Focusable"));
        Assert.Equal("Transparent", (string?)toggle.Attribute("Background"));
        Assert.Contains(style.Descendants(Presentation + "Trigger"),
            t => (string?)t.Attribute("Property") == "IsChecked" &&
                 (string?)t.Attribute("Value") == "True");
        Assert.Contains(style.Descendants(Presentation + "Trigger"),
            t => (string?)t.Attribute("Property") == "IsExpanded" &&
                 (string?)t.Attribute("Value") == "True" &&
                 t.Descendants(Presentation + "Setter").Any(s =>
                     (string?)s.Attribute("TargetName") == "CommandDockContentHost" &&
                     (string?)s.Attribute("Value") == "Visible"));

        var host = style.Descendants(Presentation + "ContentPresenter")
            .Single(n => (string?)n.Attribute(Xaml + "Name") == "CommandDockContentHost");
        Assert.Equal("Collapsed", (string?)host.Attribute("Visibility"));
        Assert.Equal("Content", (string?)host.Attribute("ContentSource"));
    }

    [Fact]
    public void OnlyCommandDockGetsNewTemplate_NoPowerCardOrNewCommandHandler()
    {
        var xaml = XDocument.Parse(Source("MainWindow.xaml"));
        var expander = xaml.Descendants(Presentation + "Expander")
            .Single(node => (string?)node.Attribute(Xaml + "Name") == "CommandPanelExpander");

        Assert.Equal("{StaticResource CommandDockTransparentHeaderExpander}", (string?)expander.Attribute("Style"));
        Assert.Equal("CommandPanel_Expanded", (string?)expander.Attribute("Expanded"));
        Assert.Equal("CommandPanel_Collapsed", (string?)expander.Attribute("Collapsed"));
        Assert.Equal("False", (string?)expander.Attribute("IsExpanded"));
        var header = expander.Element(Presentation + "Expander.Header");
        Assert.NotNull(header);
        Assert.Empty(header!.Descendants(Presentation + "Border"));
        Assert.Empty(header.Descendants(Presentation + "Button"));
        var power = header.Descendants(Presentation + "Path").Single();
        Assert.Equal("{StaticResource OperatorCommandDockPowerIcon}", (string?)power.Attribute("Data"));
        Assert.Equal("White", (string?)power.Attribute("Fill"));
        Assert.Contains(header.Descendants(Presentation + "TextBlock"),
            text => (string?)text.Attribute("Text") == "IED Command Panel");
        Assert.NotNull(expander.Descendants(Presentation + "DataGrid").FirstOrDefault());

        Assert.All(xaml.Descendants(Presentation + "Expander")
            .Where(node => (string?)node.Attribute(Xaml + "Name") != "CommandPanelExpander"),
            other => Assert.NotEqual("{StaticResource CommandDockTransparentHeaderExpander}",
                (string?)other.Attribute("Style")));
    }
}
