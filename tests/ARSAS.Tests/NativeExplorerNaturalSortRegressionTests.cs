using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class NativeExplorerNaturalSortRegressionTests
{
    [Fact]
    public void RealSevenColumnExplorerGrid_AttachesTheNaturalSortHandler()
    {
        var xaml=Read("MainWindow.xaml");
        Assert.Contains("ItemsSource=\"{Binding SelectedDevice.Points}\" IsReadOnly=\"True\"",xaml);
        Assert.Contains("Loaded=\"ExplorerLiveGrid_Loaded\"",xaml);
        var source=Read("MainWindow.IecNaturalSort.cs");
        Assert.Contains("AttachIecNaturalGridSort(sender as DataGrid, IecNaturalLiveMonitorSort.Instance)",source);
        Assert.Contains("view.CustomSort = comparer;",source);
        Assert.Contains("view.SortDescriptions.Clear()",source);
    }

    [Fact]
    public void CorrectOrderingIncludesDoubleDigitInstances()
    {
        var source=new[]{"F650/CSWI11.Pos.stVal","F650/CSWI2.Pos.stVal",
            "F650/CSWI1.Pos.stVal","F650/CSWI10.Pos.stVal"};
        var sorted=source.OrderBy(x=>x,Comparer<string>.Create(IecNaturalReferenceOrder.Compare)).ToArray();
        Assert.Equal(new[]{"F650/CSWI1.Pos.stVal","F650/CSWI2.Pos.stVal",
            "F650/CSWI10.Pos.stVal","F650/CSWI11.Pos.stVal"},sorted);
    }

    private static string Read(string path)
    {
        DirectoryInfo? d=new(AppContext.BaseDirectory);
        while(d!=null)
        {
            var f=Path.Combine(d.FullName,path);
            if(File.Exists(f))return File.ReadAllText(f);
            d=d.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
