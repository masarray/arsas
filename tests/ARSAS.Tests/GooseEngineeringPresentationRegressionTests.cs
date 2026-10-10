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
        Assert.Contains("historical.Apply(value.Snapshot)", t);
        Assert.Contains("GooseEvents.Add(eventRow)", t);
        foreach(var name in new[]{"Time","Relative time","Source MAC","Destination MAC","GOOSE DataSet","Details"})
            Assert.Contains($"Header=\"{name}\"", ui);
        Assert.Contains("ItemsSource=\"{Binding SelectedGooseStream.EngineeringLeaves}\"", ui);
        Assert.Contains("Binding Quality", ui);
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
