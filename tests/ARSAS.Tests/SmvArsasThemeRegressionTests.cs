using System.IO;

namespace ARSAS.Tests;

/// <summary>Protect the ARSAS design-system boundary against a pasted dark-theme viewer.</summary>
public sealed class SmvArsasThemeRegressionTests
{
    [Fact]
    public void SmvWorkspace_UsesMainWorkstationResourcesForEveryOperatorControl()
    {
        var x = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("BasedOn=\"{StaticResource WorkspaceCard}\"", x);
        Assert.Contains("Style=\"{StaticResource ModernDataGrid}\"", x);
        Assert.Contains("Style=\"{StaticResource PrimaryButton}\"", x);
        Assert.Contains("Style=\"{StaticResource SoftButton}\"", x);
        Assert.Contains("Style=\"{StaticResource IedIconButton}\"", x);
        Assert.Contains("Style=\"{StaticResource AppFontFamily}\"", x.Replace("FontFamily=", "Style="));
        Assert.Equal(5, Count(x, "Style=\"{StaticResource ModernComboBox}\""));
        Assert.Contains("<ListBox x:Name=\"StreamList\"", x);
        Assert.Contains("ItemContainerStyle=\"{StaticResource SmvStreamItem}\"", x);
        Assert.Contains("VirtualizationMode=\"Recycling\"", x);
        Assert.Contains("SmvChannelDot", x);
        Assert.DoesNotContain("GridLinesVisibility=\"Horizontal\"", x);
        Assert.DoesNotContain("Background=\"#071625\"", x);
        Assert.DoesNotContain("Background=\"#0C1B2B\"", x);
    }

    [Fact]
    public void PlotSource_IsStillDigSubRenderer_ButOnArsasLightSurfaces()
    {
        var wave = Read("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        var phasor = Read("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        foreach (var source in new[] { wave, phasor })
        {
            Assert.Contains("CreateBrush(\"#FFFFFF\")", source);
            Assert.DoesNotContain("CreateBrush(\"#0C1B2B\")", source);
            Assert.Contains("AppFontFamily", source);
            Assert.Contains("protected override void OnRender(DrawingContext dc)", source);
        }
        Assert.Contains("BuildDisplaySamples", wave);
        Assert.Contains("ResolvePeakHoldScale", wave);
        Assert.Contains("PhasorLabelLayout.Arrange", phasor);
    }

    private static int Count(string s, string fragment)
    {
        var count = 0;
        var offset = 0;
        while ((offset = s.IndexOf(fragment, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += fragment.Length;
        }
        return count;
    }

    private static string Read(string relative)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, relative);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(relative);
    }
}
