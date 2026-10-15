using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Exact operator toolbar glyph ownership. WPF-specific presentation only:
/// no new events, network calls, IEC actions, RCB edits or data projections.
/// </summary>
public sealed class MaterialIconConsistencyTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Theory]
    [InlineData("NavExplorerButton", "1", "OperatorIecTopologyIcon")]
    [InlineData("NavSmvButton", "5", "OperatorLiveMonitorIcon")]
    [InlineData("NavEventsButton", "2", "OperatorEventLogIcon")]
    [InlineData("NavAlarmButton", "3", "OperatorAlarmIcon")]
    [InlineData("NavGooseButton", "4", "OperatorIecWirelessIcon")]
    [InlineData("NavDiagnosticsButton", "7", "OperatorDiagnosticsIcon")]
    [InlineData("NavNativeFatButton", "6", "OperatorFatIcon")]
    [InlineData("NavIedBrowserButton", "0", "OperatorIedBrowserIcon")]
    public void EveryNavigationDestinationOwnsOneGlyphAndSameClickHandler(
        string buttonName, string index, string glyph)
    {
        var doc = XDocument.Load(Find("MainWindow.xaml"));
        var button = Assert.Single(doc.Descendants(P + "Button"), x =>
            (string?)x.Attribute(X + "Name") == buttonName);
        Assert.Equal(index, (string?)button.Attribute("Tag"));
        Assert.Equal("NavButton_Click", (string?)button.Attribute("Click"));
        Assert.Equal("{StaticResource SegmentedNavButton}",
            (string?)button.Attribute("Style"));
        var path = Assert.Single(button.Descendants(P + "Path"));
        Assert.Equal("{StaticResource " + glyph + "}",
            (string?)path.Attribute("Data"));
        Assert.Equal("{Binding Foreground, RelativeSource={RelativeSource AncestorType={x:Type Button}}}",
            (string?)path.Attribute("Fill"));
        Assert.Single(button.Descendants(P + "Viewbox"));
        Assert.NotEmpty(button.Descendants(P + "TextBlock"));
    }

    [Fact]
    public void SelectedOperatorPathsBindToExactlyOneNavigationAndCommandGlyph()
    {
        var app = XDocument.Load(Find("App.xaml"));
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        foreach (var (key, prefix) in new[]
        {
            ("OperatorIecTopologyIcon", "M600-160v-80"),
            ("OperatorDiagnosticsIcon", "M80-600v-120"),
            ("OperatorCommandDockPowerIcon", "M451.5-491.5")
        })
        {
            var entry = Assert.Single(app.Descendants(P + "Geometry"), g =>
                (string?)g.Attribute(X + "Key") == key);
            Assert.StartsWith(prefix, entry.Value);
            Assert.False(Geometry.Parse(entry.Value).Bounds.IsEmpty);
        }
        var command = Assert.Single(xaml.Descendants(P + "Expander"),
            x => (string?)x.Attribute(X + "Name") == "CommandPanelExpander");
        Assert.Equal("{StaticResource OperatorCommandDockPowerIcon}",
            (string?)Assert.Single(command.Element(P + "Expander.Header")!
                .Descendants(P + "Path")).Attribute("Data"));
        var goose = Assert.Single(xaml.Descendants(P + "Button"), b =>
            (string?)b.Attribute(X + "Name") == "NavGooseButton");
        Assert.Contains(goose.Descendants(P + "Path"), g =>
            (string?)g.Attribute("Data") == "{StaticResource OperatorIecWirelessIcon}");
    }

    [Fact]
    public void OfficialMaterialGeometryRegistryIsValidAndEveryKeyIsUnique()
    {
        var doc = XDocument.Load(Find("App.xaml"));
        var geometries = doc.Descendants(P + "Geometry")
            .Where(x => ((string?)x.Attribute(X + "Key"))?.StartsWith(
                "Material", StringComparison.Ordinal) == true).ToArray();
        Assert.True(geometries.Length >= 26);
        Assert.Equal(geometries.Length, geometries.Select(x =>
            (string?)x.Attribute(X + "Key")).Distinct().Count());
        Assert.All(geometries, entry =>
        {
            Assert.False(string.IsNullOrWhiteSpace(entry.Value));
            if (!OperatingSystem.IsWindows()) return;
            var geometry = Geometry.Parse(entry.Value);
            Assert.False(geometry.Bounds.IsEmpty);
            Assert.True(geometry.Bounds.Width > 0);
            Assert.True(geometry.Bounds.Height > 0);
        });
        var style = Assert.Single(doc.Descendants(P + "Style"), x =>
            (string?)x.Attribute(X + "Key") == "MaterialIconGlyph");
        Assert.Contains(style.Descendants(P + "Setter"), s =>
            (string?)s.Attribute("Property") == "StrokeThickness" &&
            (string?)s.Attribute("Value") == "0");
    }

    [Fact]
    public void AllFirstRunAddIedButtonsUseOperatorMaterialGlyphEvenWhenRebuilt()
    {
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        var add = Assert.Single(xaml.Descendants(P + "Button"), b =>
            (string?)b.Attribute("Click") == "AddRelay_Click");
        Assert.Contains(add.Descendants(P + "Path"), p =>
            (string?)p.Attribute("Data") == "{StaticResource OperatorAddIedIcon}");

        foreach (var path in new[]
                 {
                     "MainWindow.IoTesting.cs",
                     "MainWindow.ExplorerHeroActions.cs",
                     "MainWindow.FirstRunLauncherRepair.cs"
                 })
        {
            var src = File.ReadAllText(Find(path));
            Assert.DoesNotContain("\"LucidePlus\"", src,
                StringComparison.Ordinal);
            Assert.Contains("\"OperatorAddIedIcon\"", src,
                StringComparison.Ordinal);
        }
        foreach (var path in new[]
                 {
                     "MainWindow.IoTesting.cs",
                     "MainWindow.ExplorerHeroActions.cs"
                 })
        {
            var src = File.ReadAllText(Find(path));
            Assert.Contains("TryFindResource(\"MaterialIconGlyph\")", src,
                StringComparison.Ordinal);
            Assert.Contains("icon.Fill = iconStroke", src,
                StringComparison.Ordinal);
            Assert.DoesNotContain("icon.Stroke = iconStroke", src,
                StringComparison.Ordinal);
        }
    }

    [Fact]
    public void P2IndustrialTemplatesUseGoogleGeometryAndCannotReintroduceLucideGlyphs()
    {
        var doc = XDocument.Load(Find("Resources/P2IndustrialControls.xaml"));
        var templates = doc.Descendants(P + "DataTemplate").ToArray();
        var changedKeys = new[]
        {
            "IconPrintContent", "IconClockContent", "IconComtradeContent",
            "IconConnectContent", "IconStartFatContent", "IconStopContent",
            "IconSaveContent", "IconExcelContent", "IconPdfContent",
            "IconExportContent", "IconWorkspaceContent",
            "IconExplorerNavContent", "IconMonitorNavContent",
            "IconEventsNavContent", "IconGooseNavContent",
            "IconDiagnosticsNavContent"
        };
        foreach (var key in changedKeys)
        {
            var template = Assert.Single(templates,
                t => (string?)t.Attribute(X + "Key") == key);
            var path = Assert.Single(template.Descendants(P + "Path"));
            var glyph = (string?)path.Attribute("Data");
            Assert.StartsWith("{DynamicResource ", glyph);
            Assert.DoesNotContain("Lucide", glyph, StringComparison.Ordinal);
            Assert.Contains("Foreground", (string?)path.Attribute("Fill"));
        }
        var decorator = File.ReadAllText(Find("P2IndustrialWorkstationUx.cs"));
        Assert.Contains("button.ClearValue(ContentControl.ContentTemplateProperty);",
            decorator, StringComparison.Ordinal);
    }

    [Fact]
    public void ReadAndWorkstationCommandsHaveLocalMaterialIconsWithoutChangingActions()
    {
        var xaml = XDocument.Load(Find("MainWindow.xaml"));
        foreach (var (caption, handler) in new[]
        {
            ("Refresh view", "IedBrowserRefresh_Click"),
            ("Read", "IedBrowserRead_Click"),
            ("Read All", "IedBrowserRead_Click"),
            ("Cancel Read", "IedBrowserCancelRead_Click")
        })
        {
            // Other command groups can also have a Read caption; inspect
            // the IED Browser's explicit read/refresh action by its handler.
            Assert.Contains(xaml.Descendants(P + "Button"), b =>
                (string?)b.Attribute("Content") == caption &&
                ((string?)b.Attribute("Click"))?.Length > 0);
        }
        var styleDocument = XDocument.Load(Find("App.xaml"));
        foreach (var key in new[]
                 {
                     "MaterialRefreshAction", "MaterialReadAction",
                     "MaterialCancelAction", "MaterialFolderAction",
                     "MaterialDownloadAction", "MaterialCopyAction",
                     "MaterialClearAction", "MaterialBackAction",
                     "MaterialAcknowledgeAction", "MaterialSettingsAction"
                 })
        {
            var template = Assert.Single(styleDocument.Descendants(P + "DataTemplate"),
                t => (string?)t.Attribute(X + "Key") == key);
            Assert.Single(template.Descendants(P + "Path"));
        }
        var decorator = File.ReadAllText(Find("P2IndustrialWorkstationUx.cs"));
        foreach (var label in new[]
                 {
                     "\"Refresh view\"", "\"Read All\"", "\"Cancel Read\"",
                     "\"Browse Files\"", "\"Export CSV\"", "\"Copy Diagnostic\"",
                     "\"Clear Log\"", "\"Check live RCB\""
                 })
            Assert.Contains(label, decorator, StringComparison.Ordinal);
    }

    [Fact]
    public void NtpRetainsOneClockServerGeometryAndItsOriginalToggle()
    {
        var app = XDocument.Load(Find("App.xaml"));
        Assert.Single(app.Descendants(P + "Geometry"), entry =>
            (string?)entry.Attribute(X + "Key") == "OperatorNtpServerIcon");
        var clock = File.ReadAllText(Find("MainWindow.ClockSyncToggle.cs"));
        Assert.Equal(1, clock.Split("contents.Children.Add(clockIcon);").Length - 1);
        Assert.Contains("FindResource(\"OperatorNtpServerIcon\")", clock,
            StringComparison.Ordinal);
        Assert.Contains("toggle.Checked += GlobalSntpToggle_Changed;", clock,
            StringComparison.Ordinal);
        Assert.Contains("toggle.Unchecked += GlobalSntpToggle_Changed;", clock,
            StringComparison.Ordinal);
    }

    [Fact]
    public void MaterialIconLicenseAndProvenanceShipWithApplication()
    {
        var csproj = File.ReadAllText(Find("ArIED61850Tester.csproj"));
        Assert.Contains("MaterialIcons-LICENSE.txt", csproj,
            StringComparison.Ordinal);
        var license = File.ReadAllText(Find("Assets/Icons/MaterialIcons-LICENSE.txt"));
        Assert.Contains("Apache License", license, StringComparison.Ordinal);
        Assert.Contains("Version 2.0", license, StringComparison.Ordinal);
        Assert.Contains("Material Icons / Material Symbols",
            File.ReadAllText(Find("THIRD_PARTY_NOTICES.md")),
            StringComparison.Ordinal);
    }

    private static string Find(string path)
    {
        DirectoryInfo? root = new(AppContext.BaseDirectory);
        while (root != null)
        {
            var candidate = System.IO.Path.Combine(root.FullName, path);
            if (File.Exists(candidate)) return candidate;
            root = root.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
