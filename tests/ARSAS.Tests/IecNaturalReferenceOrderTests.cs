using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class IecNaturalReferenceOrderTests
{
    [Theory]
    [InlineData("F650/CSWI1.Pos.stVal","F650/CSWI2.Pos.stVal")]
    [InlineData("F650/CSWI2.Pos.stVal","F650/CSWI10.Pos.stVal")]
    [InlineData("BCU/CSWI9.Pos","BCU/CSWI12.Pos")]
    [InlineData("IED/evt9","IED/evt100")]
    [InlineData("F650/CSWI2.Pos","F650/CSWI02.Pos")]
    public void SortsNumericLogicalNodeInstanceNaturally(string first,string second)
        => Assert.True(IecNaturalReferenceOrder.Compare(first,second)<0);

    [Fact]
    public void LongNumericSuffixesAreComparedWithoutOverflow()
    {
        Assert.True(IecNaturalReferenceOrder.Compare("CSWI999999999999999999","CSWI1000000000000000000")<0);
        Assert.Equal(0,IecNaturalReferenceOrder.Compare("cswi1","CSWI1"));
    }

    [Fact]
    public void PresenterSortsViewOnlyAndKeepsStaticDataSetWireOrder()
    {
        var source=Read("MainWindow.LiveSignalSearch.cs");
        Assert.Contains("listView.CustomSort = IecNaturalLiveMonitorSort.Instance;",source);
        Assert.Contains("CollectionViewSource.GetDefaultView(source)",source);
        Assert.DoesNotContain("LivePoints.Sort",source);
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var file=Path.Combine(dir.FullName,path);
            if(File.Exists(file)) return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
