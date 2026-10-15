using System.Xml.Linq;
using System.Windows.Media;

namespace ARSAS.Tests;

/// <summary>
/// Exact operator SVGs rendered as WPF Geometry. Guards responsive nav
/// lifecycle against replacing icons during resize or tab selection.
/// Presentation-only: no IEC read, RCB, FAT, alarm, or NTP state changes.
/// </summary>
public sealed class OperatorHeaderIconRegressionTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static readonly string[] AllIcons =
    [
        "OperatorIedBrowserIcon", "OperatorFatIcon", "OperatorAlarmIcon",
        "OperatorEventLogIcon", "OperatorNtpServerIcon", "OperatorAddIedIcon"
    ];

    [Fact]
    public void SixOperatorSvgGeometries_AreDeclaredOnce_WithUsableContours()
    {
        var app = XDocument.Load(Find("App.xaml"));
        var geometries = app.Descendants(P + "Geometry").ToArray();
        foreach (var key in AllIcons)
        {
            var source = Assert.Single(geometries,
                element => (string?)element.Attribute(X + "Key") == key).Value.Trim();
            Assert.True(source.Length > 100, key);
            if (!OperatingSystem.IsWindows()) continue;
            var parsed = Geometry.Parse(source);
            Assert.False(parsed.Bounds.IsEmpty, key);
            Assert.True(parsed.Bounds.Width > 0 && parsed.Bounds.Height > 0, key);
        }
    }

    [Theory]
    [InlineData("NavIedBrowserButton", "7", "IED Browser", "OperatorIedBrowserIcon")]
    [InlineData("NavNativeFatButton", "6", "FAT", "OperatorFatIcon")]
    [InlineData("NavAlarmButton", "3", "Alarm", "OperatorAlarmIcon")]
    [InlineData("NavEventsButton", "2", "Event Log", "OperatorEventLogIcon")]
    public void HeaderTabs_PreserveClickIndices_AndTrackButtonForeground(
        string buttonName, string expectedIndex, string caption, string icon)
    {
        var view = XDocument.Load(Find("MainWindow.xaml"));
        var button = Assert.Single(view.Descendants(P + "Button"),
            candidate => (string?)candidate.Attribute(X + "Name") == buttonName);
        Assert.Equal(expectedIndex, (string?)button.Attribute("Tag"));
        Assert.Equal("NavButton_Click", (string?)button.Attribute("Click"));
        Assert.Equal("{StaticResource SegmentedNavButton}", (string?)button.Attribute("Style"));
        Assert.Equal(caption, (string?)button.Attribute("ToolTip"));
        var label = Assert.Single(button.Descendants(P + "TextBlock"));
        Assert.Equal(caption, (string?)label.Attribute("Text"));
        var path = Assert.Single(button.Descendants(P + "Path"));
        Assert.Equal($"{{StaticResource {icon}}}", (string?)path.Attribute("Data"));
        Assert.Equal("{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)path.Attribute("Fill"));
    }

    [Fact]
    public void ResponsiveLayout_UpdatesCaptionsWithoutOverwritingSvgContents()
    {
        var source = File.ReadAllText(Find("MainWindow.NavigationLayoutFix.cs"));
        Assert.Contains("button.Content is StackPanel iconContent", source, StringComparison.Ordinal);
        Assert.Contains("caption.Text = labels[index];", source, StringComparison.Ordinal);
        Assert.Matches(@"else\s+button\.Content = labels\[index\];", source);
        Assert.Contains("CompactLabels", source, StringComparison.Ordinal);
        Assert.Contains("FullLabels", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NtpAndAddIed_ChangeGlyphOnly_NotOperatorActionSemantics()
    {
        var view = XDocument.Load(Find("MainWindow.xaml"));
        var add = Assert.Single(view.Descendants(P + "Button"),
            candidate => (string?)candidate.Attribute("Click") == "AddRelay_Click");
        Assert.Contains(add.Descendants(P + "Path"),
            path => (string?)path.Attribute("Data") == "{StaticResource OperatorAddIedIcon}" &&
                    (string?)path.Attribute("Fill") == "White");
        Assert.Contains(add.Descendants(P + "TextBlock"),
            block => (string?)block.Attribute("Text") == "Add IED");
        var clock = File.ReadAllText(Find("MainWindow.ClockSyncToggle.cs"));
        Assert.Contains("FindResource(\"OperatorNtpServerIcon\")", clock, StringComparison.Ordinal);
        Assert.Contains("contents.Children.Add(clockIcon);", clock, StringComparison.Ordinal);
        Assert.Contains("contents.Children.Add(toggle);", clock, StringComparison.Ordinal);
        Assert.Contains("toggle.Checked += GlobalSntpToggle_Changed;", clock, StringComparison.Ordinal);
        Assert.Contains("toggle.Unchecked += GlobalSntpToggle_Changed;", clock, StringComparison.Ordinal);
    }

    private static string Find(string relative)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
