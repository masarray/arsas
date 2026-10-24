using System.IO;
namespace ARSAS.Tests;
public sealed class SmvMeasurementCoolBlueCheckpointTests
{
    [Fact]
    public void Measurements_UseCoolBluePaletteWithoutBeigeSystemCells()
    {
        var x = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("x:Key=\"SmvMeasurementGrid\"", x);
        Assert.Contains("BasedOn=\"{StaticResource ModernDataGrid}\"", x);
        Assert.Contains("x:Key=\"SmvMeasurementHeader\"", x);
        Assert.Contains("x:Key=\"SmvMeasurementCell\"", x);
        Assert.Contains("x:Key=\"SmvMeasurementRow\"", x);
        Assert.Contains("Value=\"#F8FBFF\"", x);
        Assert.Contains("Value=\"#EDF5FF\"", x);
        Assert.Contains("Value=\"#E8F2FF\"", x);
        Assert.Contains("Value=\"#E4F0FF\"", x);
        Assert.Contains("RowStyle=\"{StaticResource SmvMeasurementRow}\"", x);
        Assert.Contains("CellStyle=\"{StaticResource SmvMeasurementCell}\"", x);
        Assert.Contains("ColumnHeaderStyle=\"{StaticResource SmvMeasurementHeader}\"", x);
        Assert.Contains("GridLinesVisibility=\"None\"", x);
        Assert.DoesNotContain("Background=\"#F3EEE", x);
    }

    [Fact]
    public void PaletteChanges_DoNotTouchRawSmvOrWaveformProcessing()
    {
        var x = Read("Views/SmvInlineViewer.xaml");
        Assert.Contains("x:Name=\"WaveformView\"", x);
        Assert.Contains("x:Name=\"PhasorView\"", x);
        Assert.Contains("ItemsSource=\"{Binding VoltageMeasurements}\"", x);
        Assert.Contains("ItemsSource=\"{Binding CurrentMeasurements}\"", x);
        var code = Read("Views/SmvInlineViewer.xaml.cs");
        Assert.Contains("ObservableCollection<SmvMeasurementLiveRow> Measurements", code);
        Assert.Equal(2, code.Split("new ListCollectionView(Measurements)").Length - 1);
        Assert.Contains("Content=\"Raw counts\" Tag=\"Raw\"", x);
    }

    private static string Read(string relative)
    {
        for (DirectoryInfo? d = new(AppContext.BaseDirectory); d != null; d = d.Parent)
        {
            var p = Path.Combine(d.FullName, relative);
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException(relative);
    }
}
