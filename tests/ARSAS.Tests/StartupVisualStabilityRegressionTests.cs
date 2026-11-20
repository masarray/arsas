using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Source-level ownership checks for the first-paint startup path. These tests
/// complement, not replace, a physical 100/125/150% DPI visual screenshot check.
/// </summary>
public sealed class StartupVisualStabilityRegressionTests
{
    [Fact]
    public void Startup_ThemeAndIndustrialTemplatesPrecedeFirstWindow()
    {
        var app = Read("App.xaml.cs");
        var theme = app.IndexOf("InstallP2BlueSteelGreigeTheme();", StringComparison.Ordinal);
        var fascia = app.IndexOf("InstallArvrelMiniIedFascia();", StringComparison.Ordinal);
        var controls = app.IndexOf("P2IndustrialWorkstationUx.PreloadResources();", StringComparison.Ordinal);
        var registration = app.IndexOf("EventManager.RegisterClassHandler(typeof(Window)", StringComparison.Ordinal);
        Assert.True(theme >= 0 && fascia > theme && controls > fascia);
        Assert.True(registration > controls);
        Assert.Contains("private void AppWindow_Loaded", app, StringComparison.Ordinal);
        Assert.Contains("_styledWindows.TryGetValue(window", app, StringComparison.Ordinal);

        // Every focus return must not redo the entire WPF visual tree.
        Assert.DoesNotContain("protected override void OnActivated(", app, StringComparison.Ordinal);
    }

    [Fact]
    public void WindowAdapter_IsOnceOnlyAndNeverOverridesCanonicalInterFont()
    {
        var adapter = Read("P2BlueSteelGreigeUx.cs");
        var controls = Read("P2IndustrialWorkstationUx.cs");
        var app = Read("App.xaml");

        Assert.Contains("ConditionalWeakTable<Window, object>", adapter, StringComparison.Ordinal);
        Assert.Contains("AppliedWindows.TryGetValue", adapter, StringComparison.Ordinal);
        Assert.Contains("AppliedWindows.Add", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("window.FontFamily =", adapter, StringComparison.Ordinal);
        Assert.DoesNotContain("Plus Jakarta Sans", adapter, StringComparison.Ordinal);
        Assert.Contains("./Assets/Fonts/#Inter", app, StringComparison.Ordinal);
        Assert.Contains("PreloadResources() => EnsureResources()", controls, StringComparison.Ordinal);
    }

    [Fact]
    public void ResponsiveNavigation_OwnsShellHeight_WithoutLateClobber()
    {
        var app = Read("App.xaml.cs");
        var main = XDocument.Parse(Read("MainWindow.xaml"));
        Assert.Equal("Normal", (string?)main.Root?.Attribute("WindowState"));
        Assert.Equal("CenterScreen", (string?)main.Root?.Attribute("WindowStartupLocation"));
        Assert.Contains("WorkstationWindowStartupBounds.Apply(window);", app, StringComparison.Ordinal);

        var responsive = Read("MainWindow.NavigationLayoutFix.cs");
        var p2 = Read("P2IndustrialWorkstationUx.cs");
        var guard = Read("MainWindow.TopBarContainerHardening.cs");

        Assert.Contains("shell.Height = 60;", responsive, StringComparison.Ordinal);
        Assert.DoesNotContain("navShell.Height = 52;", p2, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.Height = 64d;", guard, StringComparison.Ordinal);
        Assert.DoesNotContain("shell.Padding = new Thickness(5, 7, 5, 7);", guard,
            StringComparison.Ordinal);
        Assert.Contains("header.MinHeight = 68d;", guard, StringComparison.Ordinal);
    }

    [Fact]
    public void IpDialog_UsesSingleInputSurfaceAndLowNoiseAdvancedDisclosure()
    {
        var source = Read("IpConnectWizardWindow.xaml");
        var doc = XDocument.Parse(source);
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xaml = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.InRange(int.Parse(doc.Root!.Attribute("Width")!.Value), 390, 430);
        Assert.Equal("410", (string?)doc.Root.Attribute("MinWidth"));
        Assert.Equal("410", (string?)doc.Root.Attribute("MaxWidth"));

        var host = doc.Descendants(presentation + "Border")
            .Single(node => node.Attribute(xaml + "Name")?.Value == "IpEditorHost");
        Assert.Equal("44", (string?)host.Attribute("Height"));
        Assert.Null(host.Attribute("Width"));
        Assert.Equal("Stretch", (string?)host.Attribute("HorizontalAlignment"));
        Assert.Equal("Manual", (string?)doc.Root.Attribute("SizeToContent"));
        Assert.Equal("348", (string?)doc.Root.Attribute("Height"));
        Assert.DoesNotContain("ManualAddressCheck", source, StringComparison.Ordinal);

        var editor = doc.Descendants(presentation + "StackPanel")
            .Single(node => node.Attribute(xaml + "Name")?.Value == "OctetEditor");
        Assert.Equal("Center", (string?)editor.Attribute("HorizontalAlignment"));

        var popup = doc.Descendants(presentation + "Popup")
            .Single(node => node.Attribute(xaml + "Name")?.Value == "HistoryPopup");
        Assert.Contains("ActualWidth", popup.Descendants(presentation + "Border")
            .First().Attribute("Width")!.Value, StringComparison.Ordinal);

        var disclosure = doc.Descendants(presentation + "Style")
            .Single(node => node.Attribute(xaml + "Key")?.Value == "SmartIpOptionsExpander");
        Assert.Contains("ToggleButton", disclosure.ToString(), StringComparison.Ordinal);
        Assert.Contains("IsExpanded", disclosure.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Smart discovery", source, StringComparison.Ordinal);
        Assert.DoesNotContain("TaskDialogCardStyle", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IPv6", source, StringComparison.Ordinal);
        Assert.Contains("Content=\"Connect\"", source, StringComparison.Ordinal);

        var behavior = Read("IpConnectWizardWindow.xaml.cs");
        Assert.Contains("if (selection.MmsPort != 102)", behavior, StringComparison.Ordinal);
        Assert.Contains("if (selectedPort != 102)", behavior, StringComparison.Ordinal);
    }

    private static string Read(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relative);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
