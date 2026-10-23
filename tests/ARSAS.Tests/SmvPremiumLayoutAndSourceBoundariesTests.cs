using System.IO;

namespace ARSAS.Tests;

public sealed class SmvPremiumLayoutAndSourceBoundariesTests
{
    [Fact]
    public void MainHeaderAndSidePanels_UseSeparateColumns_NoOverlayDockHeaders()
    {
        var xml = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("Text=\"Phasors\" Style=\"{StaticResource SvHeading}\"", xml);
        Assert.Contains("Text=\"Measurements\" Style=\"{StaticResource SvHeading}\"", xml);
        Assert.Contains("Text=\"Waveforms\" Style=\"{StaticResource SvHeading}\"", xml);
        Assert.DoesNotContain("<DockPanel Margin=\"2,1,2,0\">", xml);
        Assert.Contains("Text=\"{Binding PhaseReferenceText}\"", xml);
        Assert.Contains("Text=\"{Binding UnitStatus}\"", xml);
        Assert.Contains("Text=\"{Binding DisplayName}\"", xml);
        Assert.Contains("VoltageMeasurements", xml);
        Assert.Contains("CurrentMeasurements", xml);
        Assert.DoesNotContain("Header=\"Fund.\"", xml);
        Assert.DoesNotContain("Header=\"Unit\"", xml);
    }

    [Fact]
    public void UserChoosesEngineeringMode_AndRawRemainsTheOnlyDefault()
    {
        var xml = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("x:Name=\"UnitsModeCombo\"", xml);
        Assert.Contains("Content=\"Raw counts\" Tag=\"Raw\"", xml);
        Assert.Contains("Content=\"9-2LE · V/A\" Tag=\"NineTwoLe\"", xml);
        Assert.Contains("SelectionChanged=\"UnitsMode_SelectionChanged\"", xml);
        var model = Read("Views/SmvEngineeringDisplay.cs");
        Assert.Contains("snapshot.Channels.Count == 8", model);
        Assert.Contains("SmvDisplayUnits.RawCounts", model);
        Assert.Contains("0.001", model);
        Assert.Contains("0.01", model);
        var component = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("SmvEngineeringDisplay.Effective(_visibleSnapshot, RequestedUnits)", component);
        Assert.Contains("SmvEngineeringDisplay.Present(raw, profile)", component);
        Assert.Contains("SmvDigSubPresentationAdapter.BuildPhasors(_visibleVectors, ActiveUnits)", component);
    }

    private static string Read(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var source = Path.Combine(dir.FullName, path);
            if (File.Exists(source)) return File.ReadAllText(source);
        }
        throw new FileNotFoundException(path);
    }
}
