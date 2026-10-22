using System.IO;

namespace ARSAS.Tests;

public sealed class SmvLatestP0SharpRenderingTests
{
    [Fact]
    public void SmvToolbar_KeepsAdapterCompactAndCaptureActionsTogether()
    {
        var x = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("Grid.Column=\"2\" Width=\"246\" MaxWidth=\"246\"", x);
        Assert.Contains("x:Name=\"StartButton\" Grid.Column=\"6\"", x);
        Assert.Contains("x:Name=\"StopButton\" Grid.Column=\"8\"", x);
        Assert.Contains("x:Name=\"FrequencyCombo\" Grid.Column=\"10\"", x);
        Assert.Contains("SelectedItem.DisplayText, ElementName=AdapterCombo", x);
    }

    [Fact]
    public void Waveform_IsSingleSharpStroke_NotOriginalThreeLayerBlur()
    {
        var code = Read("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        Assert.Contains("dc.DrawGeometry(null, GetSeriesPen(series.Name), geometry);", code);
        Assert.Contains("CreateFrozenSeriesPen(GetSeriesBrush(name), 1.9)", code);
        Assert.DoesNotContain("pens.Glow", code);
        Assert.DoesNotContain("pens.Shadow", code);
        Assert.DoesNotContain("new SeriesPens(", code);
        Assert.Contains("ctx.LineTo(point, true, false)", code); // exact samples, no synthetic smoothing
    }

    [Fact]
    public void IedExplorer_IsInheritedFromCurrentP0NotReplacedByOldSmvWindow()
    {
        var main = Read("MainWindow.xaml");
        Assert.Contains("<views:SmvInlineViewer x:Name=\"MainSmvViewer\"/>", main);
        Assert.DoesNotContain("Select an IED in Device Explorer to launch", main);
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        Assert.Contains("TrustedSclIdentityAuthority.TryMatchAsync", runtime);
        Assert.Contains("IdentitySource = identity.Source", runtime);
        // Current P0 evidence stays present on the integration base.
        Assert.Contains("ProjectCurrentReadSignals", Read("MainWindow.xaml.cs"));
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
