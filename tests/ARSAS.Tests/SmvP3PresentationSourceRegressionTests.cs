using System.IO;

namespace ARSAS.Tests;

public sealed class SmvP3PresentationSourceRegressionTests
{
    [Fact]
    public void P3ReadoutsAreVisibleAndProfileUnverifiedPowerIsNotInvented()
    {
        var xaml = Source("Views/SmvInlineViewer.xaml");
        Assert.Contains("Text=\"{Binding SyncLabel}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding SyncDetail}\"", xaml);
        Assert.Contains("Text=\"{Binding TimeAxisCaption}\"", xaml);
        Assert.Contains("Text=\"{Binding PowerP}\"", xaml);
        Assert.Contains("Text=\"{Binding PowerQ}\"", xaml);
        Assert.Contains("Text=\"{Binding PowerS}\"", xaml);
        Assert.Contains("Text=\"{Binding PowerPf}\"", xaml);
        Assert.Contains("ToolTip=\"{Binding PowerDetails}\"", xaml);
        Assert.Contains("Grid.Row=\"6\" Text=\"{Binding UnitStatus}\"", xaml);
        Assert.DoesNotContain("Header=\"Per-phase P / Q₁ / S / PF\"", xaml);
        var power = Source("Views/SmvPowerMeasurements.cs");
        Assert.Contains("SmvEngineeringDisplay.Effective(snapshot, selected) != SmvDisplayUnits.NineTwoLe", power);
        Assert.Contains("!snapshot.IsCleanProof", power);
        Assert.Contains("var p = 0.0;", power);
        Assert.Contains("Math.Sin(angle)", power);
        Assert.Contains("s > 1e-12 ? Math.Clamp(p / s, -1, 1)", power);
    }

    [Fact]
    public void BundledInterIsSoleGeneralPurposeTextFace_OnP3Surfaces()
    {
        var app = Source("App.xaml");
        Assert.Contains("<FontFamily x:Key=\"AppFontFamily\">./Assets/Fonts/#Inter</FontFamily>", app);
        Assert.Contains("<FontFamily x:Key=\"AppCodeFontFamily\">./Assets/Fonts/#Inter</FontFamily>", app);
        Assert.DoesNotContain("Cascadia Mono, Consolas", app);

        var wave = Source("Ports/DigSubAnalyzer/Controls/WaveformPanelControl.cs");
        Assert.Contains("FontWeights.SemiBold, FontStretches.Normal", wave);
        Assert.Contains("12.0, color, CalloutTypeface", wave);
        Assert.Contains("DrawMillisecondsAxis(dc, innerRect, snapshot)", wave);
        Assert.Contains("var referenceSample = SmvWaveformTimeAxis.ReferenceSample", wave);

        var diagram = Source("Ports/DigSubAnalyzer/Controls/PhasorDiagramControl.cs");
        Assert.Contains("CreateText(FormatPhasorLabel(item)", diagram);
        Assert.Contains("SmvAnglePresentation.Format(angle)", diagram);

        foreach (var path in new[]
        {
            "IoListTestingWindow.FatV2Ux.cs",
            "IoListTestingWindow.P0BenchUx.cs",
            "IoListTestingWindow.PrintPreview.cs",
            "IoListTestingWindow.CommandPanel.cs"
        })
            Assert.DoesNotContain("Cascadia Mono, Consolas", Source(path));

        Assert.DoesNotContain("FontFamily=\"Segoe UI\"", Source("Resources/ArvrelMiniIedFascia.xaml"));
    }

    private static string Source(string name)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, name);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(name);
    }
}
