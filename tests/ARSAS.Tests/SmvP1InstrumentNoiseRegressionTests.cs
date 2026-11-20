using System.IO;
using System.Linq;
using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Windows FAT P1 lock: instrument-first surface remains quiet while protocol
/// and provenance evidence stays reachable through bounded hover details.
/// This is presentation-only; do not change sync, timebase or measured values.
/// </summary>
public sealed class SmvP1InstrumentNoiseRegressionTests
{
    [Fact]
    public void SyncHover_IsBoundedAndWrapped_WhileLiveBadgeRemains()
    {
        var xaml = XDocument.Parse(Read("Views/SmvInlineViewer.xaml"));
        var tooltip = xaml.Descendants().Single(x => x.Name.LocalName == "ToolTip" &&
            (string?)x.Attribute("MaxWidth") == "330");
        Assert.Contains(tooltip.Descendants(), x => x.Name.LocalName == "TextBlock" &&
            (string?)x.Attribute("TextWrapping") == "Wrap" &&
            ((string?)x.Attribute("Text"))!.Contains("SyncDetail", StringComparison.Ordinal));
        Assert.Contains("Text=\"{Binding SyncLabel}\"", Read("Views/SmvInlineViewer.xaml"));
        var vm = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("Previous publisher sync shown.", vm);
        Assert.Contains("New publisher sync not yet known.", vm);
        Assert.Contains("SmvSyncPresentation.Detail(_visibleSnapshot)", vm);
    }

    [Fact]
    public void MeasurementSurface_FavorsLiveElectricalValues_AndRetainsOnDemandMetadata()
    {
        var xaml = Read("Views/SmvInlineViewer.xaml");
        var vm = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.DoesNotContain("Text=\"RMS · phase\"", xaml);
        Assert.DoesNotContain("ACTIVE · P", xaml);
        Assert.DoesNotContain("REACTIVE · Q₁", xaml);
        Assert.DoesNotContain("APPARENT · S", xaml);
        Assert.DoesNotContain("POWER FACTOR", xaml);
        Assert.Contains("Text=\"3φ Power\"", xaml);
        Assert.Contains("ToolTip=\"{Binding PowerDetails}\"", xaml);
        Assert.Contains("Text=\"{Binding UnitStatus}\" Visibility=\"Collapsed\"", xaml);
        Assert.Contains("ToolTip=\"{Binding UnitStatus}\"", xaml);
        Assert.Contains("\"Voltage (V)\"", vm);
        Assert.Contains("\"Current (A)\"", vm);
        Assert.Contains("\"Ref: Ua\"", vm);
        Assert.Contains("\"Ua = 0 ms\"", vm);
        Assert.Contains("SmvEngineeringDisplay.Effective(_visibleSnapshot, RequestedUnits)", vm);
    }

    [Fact]
    public void PublisherAndCapture_StayConciseButFullEvidenceIsStillAvailable()
    {
        var xaml = Read("Views/SmvInlineViewer.xaml");
        var vm = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("Text=\"{Binding StreamDetail}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding StreamTechnicalDetail}\"", xaml);
        Assert.Contains("SourceMac", vm);
        Assert.Contains("DestinationMac", vm);
        Assert.Contains("summary.Gaps", vm);
        Assert.Contains("summary.Restarts", vm);
        Assert.Contains("Text=\"{Binding CaptureBrief}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding CaptureStatus}\"", xaml);
        Assert.Contains("\"SV capture error\"", vm);
        Assert.Contains("\"HOLD · stream interrupted\"", vm);
    }

    [Fact]
    public void WaveformAndPhasor_AvoidRepeatedPendingAndScaleExplanations()
    {
        var wave = Read("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        var phasor = Read("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        Assert.DoesNotContain("Signal frequency pending", wave);
        Assert.DoesNotContain("Sample rate pending", wave);
        Assert.Contains("if (laneIndex == 0)", wave);
        Assert.Contains("DrawTimebaseLabel(dc, innerRect, snapshot);", wave);
        Assert.Contains("\"VOLTAGE\"", wave);
        Assert.Contains("\"CURRENT\"", wave);
        Assert.Contains("U solid · I dashed · separate scales", phasor);
        Assert.DoesNotContain("9-2LE · U solid / I dashed · independent scales", phasor);
        var xaml = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("Visibility=\"Collapsed\" ToolTip=\"{Binding WaveformStatus}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding WaveformStatus}\"", xaml);
        Assert.Contains("Full measurement precision is shown below.", xaml);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d is not null; d = d.Parent)
        {
            var resolved = Path.Combine(d.FullName, path);
            if (File.Exists(resolved)) return File.ReadAllText(resolved);
        }
        throw new FileNotFoundException(path);
    }
}
