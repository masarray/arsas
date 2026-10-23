using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// SIEBCU FAT: regress permanent Command Dock geometry, virtualized DataSet
/// provenance, and exact-identity Read All scheduling beyond 4096 attributes.
/// No IEC client, IO, report or control action is exercised in this suite.
/// </summary>
public sealed class P0SiebcuReadAllAndLayoutRegressionTests
{
    private static string Read(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }

    [Fact]
    public void RecycledDataSetRowsKeepBoundedSourceColumnIndependentOfScrollExtent()
    {
        var xml = XDocument.Parse(Read("MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = Assert.Single(xml.Descendants().Where(n =>
            n.Name.LocalName == "ListView" &&
            (string?)n.Attribute(x + "Name") == "IedBrowserDetailList"));
        Assert.Equal("Disabled", (string?)list.Attribute(
            "ScrollViewer.HorizontalScrollBarVisibility"));
        Assert.Equal("True", (string?)list.Attribute("ClipToBounds"));

        var row = Assert.Single(xml.Descendants().Where(n =>
            n.Name.LocalName == "DataTemplate" &&
            (string?)n.Attribute(x + "Key") == "BrowserDataRow"));
        var valuePanel = Assert.Single(row.Descendants().Where(n =>
            n.Name.LocalName == "Grid" &&
            (string?)n.Attribute("ClipToBounds") == "True"));
        var columns = valuePanel.Descendants()
            .Where(n => n.Name.LocalName == "ColumnDefinition").ToArray();
        Assert.Equal(new[] { "*", "176" },
            columns.Select(n => (string?)n.Attribute("Width")));
        var source = Assert.Single(valuePanel.Descendants().Where(n =>
            n.Name.LocalName == "TextBlock" &&
            (string?)n.Attribute("Text") == "{Binding ObservationSource}"));
        Assert.Equal("1", (string?)source.Attribute("Grid.Column"));
        Assert.Equal("CharacterEllipsis", (string?)source.Attribute("TextTrimming"));
        var valueSlot = Assert.Single(valuePanel.Descendants().Where(n =>
            n.Name.LocalName == "StackPanel" &&
            (string?)n.Attribute("Grid.Column") == "0"));
        var value = Assert.Single(valueSlot.Descendants().Where(n =>
            n.Name.LocalName == "TextBlock" &&
            (string?)n.Attribute("Text") == "{Binding Value}"));
        Assert.Equal("{Binding Value}", (string?)value.Attribute("ToolTip"));
        var qualityWarning = Assert.Single(valueSlot.Elements().Where(n =>
            n.Name.LocalName == "Path" &&
            (string?)n.Attribute("Data") == "{StaticResource InvalidQualityWarningGeometry}"));
        Assert.Equal("{StaticResource InvalidQualityWarningIconStyle}",
            (string?)qualityWarning.Attribute("Style"));
    }

    [Fact]
    public void OpenCloseAndConfirmKeepSameFootprintThroughRepeatedCommandCycles()
    {
        var xml = XDocument.Parse(Read("MainWindow.xaml"));
        var controls = xml.Descendants().Where(n => n.Name.LocalName == "Button")
            .Where(n => (string?)n.Attribute("Click") is
                "ControlStageAction_Click" or "ControlConfirmAction_Click")
            .ToArray();
        foreach (var action in new[] { "Open [01]", "Closed [10]" })
        {
            var button = Assert.Single(controls.Where(n =>
                (string?)n.Attribute("CommandParameter") == action));
            Assert.Equal("72", (string?)button.Attribute("Width"));
            Assert.Equal("72", (string?)button.Attribute("MinWidth"));
            Assert.Equal("Left", (string?)button.Attribute("HorizontalAlignment"));
            Assert.Equal("{Binding ControlCanInspectPosition}",
                (string?)button.Attribute("IsEnabled"));
        }
        var confirm = Assert.Single(controls.Where(n =>
            (string?)n.Attribute("Click") == "ControlConfirmAction_Click" &&
            (string?)n.Attribute("Content") ==
                "{Binding ControlPendingConfirmationLabel}"));
        Assert.Equal("108", (string?)confirm.Attribute("Width"));
        Assert.Equal("108", (string?)confirm.Attribute("MinWidth"));
        Assert.Equal("{Binding ControlCanConfirm}",
            (string?)confirm.Attribute("IsEnabled"));
        Assert.Contains("VirtualizationMode=\"Recycling\"",
            Read("MainWindow.xaml"));
    }

    private static IedBrowserReadTarget Target(string fc, int i, string ld = "LD1")
        => new($"{ld}/LN1.{fc}{i}.stVal", fc, "Int32");

    [Fact]
    public void ReadAllAboveFourThousandRetainsEveryExactFcAndPrioritizesSafeRoots()
    {
        var targets = Enumerable.Range(0, 4560)
            .Select(i => Target("ST", i))
            .Concat(Enumerable.Range(0, 20).Select(i => Target("SG", i)))
            .Concat(Enumerable.Range(0, 20).Select(i => Target("CF", i)))
            .Concat(Enumerable.Range(0, 20).Select(i => Target("MX", i)))
            .ToArray();
        Assert.True(targets.Length > 4096);
        var ordered = IedBrowserReadPlan.PrioritizeExplicitFullRead(targets);
        Assert.Equal(targets.Length, ordered.Count);
        Assert.Equal(targets.Select(t => t.Reference + "|" + t.FunctionalConstraint)
            .OrderBy(x => x, StringComparer.Ordinal),
            ordered.Select(t => t.Reference + "|" + t.FunctionalConstraint)
                .OrderBy(x => x, StringComparer.Ordinal));
        Assert.Contains(ordered.Take(24), t => t.FunctionalConstraint == "SG");
        Assert.Contains(ordered.Take(24), t => t.FunctionalConstraint == "CF");
        Assert.Contains(ordered.Take(24), t => t.FunctionalConstraint == "ST");
        Assert.Contains(ordered.Take(24), t => t.FunctionalConstraint == "MX");
        var shortPlan = targets[..16];
        Assert.Same(shortPlan, IedBrowserReadPlan.PrioritizeExplicitFullRead(shortPlan));
    }

    [Fact]
    public void TypedRootAdmissionRemainsExactBoundedAndDoesNotIncludeControlOrProcess()
    {
        Assert.Equal("LD1/LN1$SG0",
            IedBrowserSgRootProjection.RootFor(Target("SG", 1)));
        Assert.Equal("LD1/LN1$CF0",
            IedBrowserSgRootProjection.RootFor(Target("CF", 2)));
        Assert.Equal("", IedBrowserSgRootProjection.RootFor(Target("ST", 1)));
        Assert.Equal("", IedBrowserSgRootProjection.RootFor(Target("CO", 1)));
        Assert.True(IedBrowserSgRootProjection.CanReadRoot(512));
        Assert.False(IedBrowserSgRootProjection.CanReadRoot(513));
        Assert.False(IedBrowserSgRootProjection.CanReadRoot(0));
        var service = Read("Services/IedBrowserSgRootReadFlight.cs");
        Assert.DoesNotContain("_targets.Count > 4096", service);
        Assert.Contains("_rootTargets", service);
        Assert.Contains("ReadVerifiedSgRootAsync", service);
        Assert.Contains("MaxRootReadsPerPage", service);
        Assert.Contains("CanReadRoot", service);
        var browser = Read("MainWindow.IedBrowser.cs");
        Assert.Contains("PrioritizeExplicitFullRead(targets)", browser);
    }
}
