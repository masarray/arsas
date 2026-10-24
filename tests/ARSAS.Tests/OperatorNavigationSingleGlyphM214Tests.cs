using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Visual regression for the legacy P2 industrial navigation decorator:
/// an operator-supplied WPF SVG must not be wrapped in a second old icon.
/// Exercise the real runtime decorator on STA, not just XAML path counts.
/// </summary>
[Collection("WpfStaIsolation")]
public sealed class OperatorNavigationSingleGlyphM214Tests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void EventButtonHasOneOperatorGlyphInXamlAndNoLegacyTemplateAtRuntime()
    {
        var doc = XDocument.Load(Find("MainWindow.xaml"));
        var events = Assert.Single(doc.Descendants(P + "Button"),
            button => (string?)button.Attribute(X + "Name") == "NavEventsButton");
        Assert.Equal("2", (string?)events.Attribute("Tag"));
        Assert.Equal("NavButton_Click", (string?)events.Attribute("Click"));
        Assert.Equal("{StaticResource SegmentedNavButton}",
            (string?)events.Attribute("Style"));
        var glyph = Assert.Single(events.Descendants(P + "Path"));
        Assert.Equal("{StaticResource OperatorEventLogIcon}",
            (string?)glyph.Attribute("Data"));

        RunSta(() =>
        {
            var owner = new Border();
            var oldTemplate = new DataTemplate();
            owner.Resources.Add("IconEventsNavContent", oldTemplate);
            var icon = new System.Windows.Shapes.Path();
            var svg = new Viewbox { Width = 15, Height = 15, Child = icon };
            var content = new StackPanel { Orientation = Orientation.Horizontal };
            content.Children.Add(svg);
            content.Children.Add(new TextBlock { Text = "Events" });
            var button = new Button { Content = content, ContentTemplate = oldTemplate };

            InvokeNavigationDecorator(button, owner, "IconEventsNavContent");
            Assert.Same(content, button.Content);
            Assert.Null(button.ContentTemplate); // no second LucideListChecks
            Assert.Same(icon, svg.Child);
            InvokeNavigationDecorator(button, owner, "IconEventsNavContent");
            Assert.Null(button.ContentTemplate); // Loaded/resize remains idempotent
            Assert.Equal(2, content.Children.Count);
        });
    }

    [Fact]
    public void LegacyTextOnlyAndDiagnosticsAlertStillReceiveTheirIcons()
    {
        RunSta(() =>
        {
            var owner = new Border();
            var template = new DataTemplate();
            owner.Resources.Add("IconExplorerNavContent", template);
            owner.Resources.Add("IconDiagnosticsNavContent", template);

            var explorer = new Button { Content = "Explorer" };
            InvokeNavigationDecorator(explorer, owner, "IconExplorerNavContent");
            Assert.Same(template, explorer.ContentTemplate);

            // Diagnostics' content contains an alert badge but no SVG Path;
            // it must retain the P2 industrial diagnostics triangle.
            var alert = new StackPanel();
            alert.Children.Add(new TextBlock { Text = "Diagnostics" });
            alert.Children.Add(new Border());
            var diagnostics = new Button { Content = alert };
            InvokeNavigationDecorator(diagnostics, owner, "IconDiagnosticsNavContent");
            Assert.Same(template, diagnostics.ContentTemplate);
            Assert.Same(alert, diagnostics.Content);
        });
    }

    [Fact]
    public void ResponsiveCaptionsStillPreserveTheSingleOperatorGlyph()
    {
        var source = File.ReadAllText(Find("MainWindow.NavigationLayoutFix.cs"));
        Assert.Contains("button.Content is StackPanel iconContent", source,
            StringComparison.Ordinal);
        Assert.Contains("caption.Text = labels[index];", source,
            StringComparison.Ordinal);

        var decorator = File.ReadAllText(Find("P2IndustrialWorkstationUx.cs"));
        Assert.Contains("ApplyNavigationTemplate(window.FindName(\"NavEventsButton\")",
            decorator, StringComparison.Ordinal);
        Assert.Contains("button.ClearValue(ContentControl.ContentTemplateProperty);",
            decorator, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "ApplyTemplate(window.FindName(\"NavEventsButton\")",
            decorator, StringComparison.Ordinal);
    }

    private static void InvokeNavigationDecorator(
        Button button, FrameworkElement owner, string key)
    {
        var type = typeof(ArIED61850Tester.Models.IedBrowserLiveValues)
            .Assembly.GetType("ArIED61850Tester.P2IndustrialWorkstationUx", true)!;
        var method = type.GetMethod("ApplyNavigationTemplate",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(null, [button, owner, key]);
    }

    private static void RunSta(Action action)
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception exception) { failure = exception; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(10)),
            "STA visual-regression test did not terminate");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static string Find(string path)
    {
        DirectoryInfo? current = new(AppContext.BaseDirectory);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, path);
            if (File.Exists(candidate)) return candidate;
            current = current.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
