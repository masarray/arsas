using ArIED61850Tester.Services;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;
public sealed class GooseEngineeringPresentationRegressionTests
{
    [Fact]
    public void HistoricalFrameAndSixEngineeringColumns_AreRetainedOnSelection()
    {
        var t = Read("MainWindow.GooseTimeline.cs");
        var ui = Read("Views/GooseSubscriberLiteView.xaml");
        Assert.Contains("historical.Apply(snapshot)", t);
        Assert.Contains("GooseEvents.Add(eventRow)", t);
        foreach(var name in new[]{"Time","Relative time","Source MAC","Destination MAC","GOOSE DataSet","Details"})
            Assert.Contains($"Header=\"{name}\"", ui);
        Assert.Contains("ItemsSource=\"{Binding SelectedGooseStream.Leaves}\"", ui);
        Assert.Contains("Text=\"DataSet entries\"", ui);
        Assert.DoesNotContain("SelectedGooseStream.EngineeringLeaves", ui);
        Assert.Contains("<DataGrid ItemsSource=", ui, StringComparison.Ordinal);
        Assert.Contains("Header=\"Signal\"", ui, StringComparison.Ordinal);
        Assert.Contains("Header=\"Value\"", ui, StringComparison.Ordinal);
        Assert.DoesNotContain("Header=\"Quality\"", ui, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", ui, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null)
        {
            var f = Path.Combine(d.FullName, path.Replace('/', Path.DirectorySeparatorChar));
            if(File.Exists(f)) return File.ReadAllText(f);
            d = d.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
