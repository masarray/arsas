using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArIED61850Tester.Views;
using ProcessBus.App.Wpf.Controls;
using ProcessBus.Core.Models;

namespace ARSAS.Tests;

/// <summary>
/// P1 Windows FAT visual contracts: short RAW callouts, bounded polar side
/// rails, four-row content-sized measurement panel and aligned Power metrics.
/// No sample decoding, DSP or live-capture behavior is modified.
/// </summary>
public sealed class SmvP1VisualDensityTests
{
    [Theory]
    [InlineData("Ch1", 0, "Ch1 ∠0°")]
    [InlineData("Ch2", -155, "Ch2 ∠205°")]
    [InlineData("Ch5", -15, "Ch5 ∠345°")]
    [InlineData("Ch6", -175, "Ch6 ∠185°")]
    public void RawCallouts_ShowIdentityAndWrappedAngle_NotCountMagnitude(
        string channel, double angle, string expected)
    {
        var label = PhasorDiagramControl.FormatPhasorLabel(new PhasorDisplayItem
        {
            Name = channel, Unit = "counts", Family = PhasorFamily.Voltage,
            Magnitude = 999999.123, AngleDegrees = angle
        });
        Assert.Equal(expected, label);
        Assert.DoesNotContain("counts", label, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("999", label, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Ua", "V", 100, 0, "Ua 100V ∠0°")]
    [InlineData("Ub", "V", 49.999, -160, "Ub 50V ∠200°")]
    [InlineData("Ic", "A", 0.997, 150, "Ic 0.997A ∠150°")]
    [InlineData("Ia", "A", 1.002, 15, "Ia 1.002A ∠15°")]
    public void EngineeringCallouts_KeepMagnitudeWithCompactUnitAndPhase(
        string channel, string unit, double magnitude, double angle, string expected)
    {
        var label = PhasorDiagramControl.FormatPhasorLabel(new PhasorDisplayItem
        {
            Name = channel, Unit = unit, Family = unit == "V" ? PhasorFamily.Voltage : PhasorFamily.Current,
            Magnitude = magnitude, AngleDegrees = angle
        });
        Assert.Equal(expected, label);
    }

    [Fact]
    public void NarrowPhasorHost_RetainsAngleAndVectorWithoutOverflowingMagnitude()
    {
        var item = new PhasorDisplayItem
        {
            Name = "Ub", Unit = "V", Family = PhasorFamily.Voltage,
            Magnitude = 49.999, AngleDegrees = -160
        };
        Assert.Equal("Ub 50V ∠200°", PhasorDiagramControl.FormatPhasorLabel(item));
        Assert.Equal("Ub ∠200°", PhasorDiagramControl.FormatPhasorLabel(item, fitNarrow: true));
        var renderer = Source("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        Assert.Contains("var fitNarrow = width < 420;", renderer);
        Assert.Contains("engineeringLabels = !fitNarrow", renderer);
    }

    [Fact]
    public void PhasorPlot_MeasuresTextWidthBeforePlacingCircle_AndKeepsLeaderTip()
    {
        var renderer = Source("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        Assert.Contains("var labelCache = _visualItems.Where(IsRenderable)", renderer);
        Assert.Contains("leftRail = Math.Max(leftRail, pair.Value.Width + 10.0)", renderer);
        Assert.Contains("rightRail = Math.Max(rightRail, pair.Value.Width + 10.0)", renderer);
        Assert.Contains("center.X - leftRail, width - rightRail - center.X", renderer);
        Assert.Contains("labelCache[item]", renderer);
        Assert.Contains("PhasorCalloutRouting.Route(", renderer);
        Assert.Contains("vector.Tip.X, vector.Tip.Y", renderer);
        Assert.DoesNotContain("width / 2.0 - 69", renderer);
    }

    [Fact]
    public void CompactPowerAndMeasurementPanel_AreContentSized_WithoutBlankBottomArea()
    {
        var xaml = XDocument.Parse(Source("Views/SmvInlineViewer.xaml"));
        var panel = xaml.Descendants().Single(n => n.Name.LocalName == "TextBlock" &&
            (string?)n.Attribute("Text") == "Measurements").Parent!.Parent!;
        var rows = panel.Elements().Single(n => n.Name.LocalName == "Grid.RowDefinitions")
            .Elements().ToArray();
        Assert.Equal(7, rows.Length);
        Assert.Equal("Auto", (string?)rows[2].Attribute("Height"));

        var power = panel.Descendants().Single(n => n.Name.LocalName == "Border" &&
            (string?)n.Attribute("ToolTip") == "{Binding PowerDetails}");
        Assert.Equal("{Binding PowerVisibility}", (string?)power.Attribute("Visibility"));
        var powerLabels = power.Descendants().Where(n => n.Name.LocalName == "TextBlock")
            .Select(n => (string?)n.Attribute("Text")).ToArray();
        foreach (var field in new[] { "P", "Q₁", "S", "PF",
            "{Binding PowerP}", "{Binding PowerQ}", "{Binding PowerS}", "{Binding PowerPf}" })
            Assert.Contains(field, powerLabels);
        Assert.DoesNotContain(power.Descendants(), n => n.Name.LocalName == "UniformGrid");

        var source = Source("Views/SmvInlineViewer.xaml");
        Assert.DoesNotContain("Height=\"0.9*\" MinHeight=\"220\"", source);
        Assert.Contains("Height=\"*\" MinHeight=\"235\"", source);
        Assert.Contains("<!-- Measurement sizes to its actual four fixed channel rows.", source);
    }

    private static string Source(string path)
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var candidate = Path.Combine(folder.FullName, path);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate);
        }
        throw new FileNotFoundException(path);
    }
}
