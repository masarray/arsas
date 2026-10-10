using System.Xml.Linq;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class MultiGridNaturalSortRegressionTests
{
    [Fact]
    public void ThreeRealGridsUseTheSharedViewOnlyNaturalSorter()
    {
        var xaml=Read("MainWindow.xaml");
        var xml=XDocument.Parse(xaml);
        XNamespace wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grids=xml.Descendants(wpf+"DataGrid").ToArray();
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="ExplorerLiveGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("SelectedDevice.Points")==true);
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="CommandGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("SelectedDevice.CommandSignals")==true);
        Assert.Contains(grids, g=>(string?)g.Attribute("Loaded")=="GlobalLiveGrid_Loaded" &&
            ((string?)g.Attribute("ItemsSource"))?.Contains("GlobalPoints")==true);
        var sort=Read("MainWindow.IecNaturalSort.cs");
        Assert.Contains("view.CustomSort = comparer;",sort);
        Assert.Contains("IecNaturalCommandSort.Instance",sort);
        Assert.Contains("IecNaturalGlobalMonitorSort.Instance",sort);
    }

    [Fact]
    public void CommandDockSortingOrdersInstanceNumbersWithoutMutatingSource()
    {
        var one =new SignalDefinition { ObjectReference="BCUGEF650/CSWI1.Pos" };
        var two =new SignalDefinition { ObjectReference="BCUGEF650/CSWI2.Pos" };
        var ten =new SignalDefinition { ObjectReference="BCUGEF650/CSWI10.Pos" };
        var unordered =new[]{ten,two,one};
        var sorted=unordered.OrderBy(x=>x,
            Comparer<SignalDefinition>.Create((a,b)=>IecNaturalCommandSort.Instance.Compare(a,b)))
            .Select(x=>x.ObjectReference).ToArray();
        Assert.Equal(new[]{one.ObjectReference,two.ObjectReference,ten.ObjectReference},sorted);
        Assert.Same(ten,unordered[0]);
    }

    [Fact]
    public void GlobalMonitorGroupsIEDThenNaturalInstance()
    {
        static Iec61850MonitorPoint Point(string device,string ln) =>
            new(){DeviceName=device,IecReference=$"{device}F650/{ln}.Pos.stVal"};
        var points=new[]{Point("GE","CSWI10"),Point("GE","CSWI2"),Point("ABB","CSWI10"),
            Point("GE","CSWI1"),Point("ABB","CSWI2")};
        var sorted=points.OrderBy(x=>x,
            Comparer<Iec61850MonitorPoint>.Create((a,b)=>IecNaturalGlobalMonitorSort.Instance.Compare(a,b))).ToArray();
        Assert.Equal(new[]{"ABB","ABB","GE","GE","GE"},sorted.Select(x=>x.DeviceName));
        Assert.Equal(new[]{"CSWI2","CSWI10"},sorted.Take(2).Select(x=>x.IecTelegram.Split('/').Last().Split('.')[0]));
        Assert.Equal(new[]{"CSWI1","CSWI2","CSWI10"},sorted.Skip(2).Select(x=>x.IecTelegram.Split('/').Last().Split('.')[0]));
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir=new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var file=Path.Combine(dir.FullName,path);
            if(File.Exists(file))return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
