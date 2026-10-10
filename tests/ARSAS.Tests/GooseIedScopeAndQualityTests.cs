using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class GooseIedScopeAndQualityTests
{
    [Fact]
    public void ScopeIncludesOnlyVerifiedSelectedIEDAndPreservesUnresolvedInAllMode()
    {
        Assert.True(GooseIedScopePolicy.Matches(0,"BCUGE","BCUGE","BCUGE"));
        Assert.True(GooseIedScopePolicy.Matches(0,"Engineering Alias","BCUGE","BCUGE"));
        Assert.False(GooseIedScopePolicy.Matches(0,"BCUGE","BCUGE","C264"));
        Assert.False(GooseIedScopePolicy.Matches(0,"BCUGE","BCUGE","Unresolved"));
        Assert.True(GooseIedScopePolicy.Matches(1,"BCUGE","BCUGE","Unresolved"));
        Assert.True(GooseIedScopePolicy.Matches(1,"BCUGE","BCUGE","C264"));
        Assert.True(GooseIedScopePolicy.Matches(0,null,null,"C264"));
    }

    [Fact]
    public void ActiveGooseViewFiltersEventsAndStreamsOnly()
    {
        var xaml=Read("Views/GooseSubscriberLiteView.xaml");
        Assert.Contains("SelectedIndex=\"{Binding GooseIedScopeIndex, Mode=TwoWay}\"",xaml);
        Assert.Contains("ItemsSource=\"{Binding GooseVisibleEvents}\"",xaml);
        Assert.Contains("ItemsSource=\"{Binding GooseVisibleStreams}\"",xaml);
        var main=Read("MainWindow.GooseTimeline.cs");
        Assert.Contains("GooseEvents.Add(eventRow)",main);
        Assert.Contains("IsGooseIedInScope(eventRow.IedName)",main);
        var scope=Read("MainWindow.GooseIedScope.cs");
        Assert.Contains("new ListCollectionView(GooseEvents)",scope);
        Assert.Contains("new ListCollectionView(GooseStreams)",scope);
        Assert.Contains("RefreshGooseScopeViews()",scope);
    }

    [Fact]
    public void RealBCUGEAdjacentFcdStatusAndQualityCollapseToTwoEngineeringRows()
    {
        static GooseLeafValueSnapshot F(int n,string signal,string reference,string btype,string value)
            => new(n,n-1,signal,reference,"ST","DPC",btype,value,"",false,"SCL");
        var raw=new[]{
            F(1,"CSWI6.Pos.stVal","BCUGEF650/CSWI6.Pos","Dbpos","Off"),
            F(2,"CSWI6.Pos.q","BCUGEF650/CSWI6.Pos","Quality","Good"),
            F(3,"CSWI7.Pos.stVal","BCUGEF650/CSWI7.Pos","Dbpos","Intermediate"),
            F(4,"CSWI7.Pos.q","BCUGEF650/CSWI7.Pos","Quality","Good")
        };
        var projected=GooseCanonicalLeafProjection.Project(raw);
        Assert.Equal(4,raw.Length);
        Assert.Equal(2,projected.Count);
        Assert.Equal("Good",projected[0].Quality);
        Assert.Equal("Good",projected[1].Quality);
        Assert.Equal(new[]{0,2},projected.Select(x=>x.DataSetIndex));
        Assert.EndsWith(".q",raw[1].SignalName);
    }

    [Fact]
    public void MissingSclReferenceStillPairsUniqueAdjacentTypedMembersButNeverUnbound()
    {
        static GooseLeafValueSnapshot F(int n,string name,string value,string binding)
            => new(n,n-1,name,"","ST","DPC",name.EndsWith(".q")?"Quality":"Dbpos",value,"",false,binding);
        var resolved=new[]{F(1,"CSWI6.Pos.stVal","Off","SCL"),F(2,"CSWI6.Pos.q","Good","SCL")};
        var raw=new[]{F(1,"CSWI6.Pos.stVal","Off","Unbound"),F(2,"CSWI6.Pos.q","Good","Unbound")};
        Assert.Single(GooseCanonicalLeafProjection.Project(resolved));
        Assert.Equal("Good",GooseCanonicalLeafProjection.Project(resolved)[0].Quality);
        Assert.Equal(2,GooseCanonicalLeafProjection.Project(raw).Count);
    }

    [Fact]
    public void NeverPairDifferentExplicitLogicalDevicesEvenWithSameDisplayNames()
    {
        static GooseLeafValueSnapshot F(int n,string label,string full,string t,string value)
            => new(n,n-1,label,full,"ST","DPC",t,value,"",false,"SCL");
        var raw=new[]{
            F(1,"CSWI6.Pos.stVal","LDA/CSWI6.Pos.stVal","Dbpos","Off"),
            F(2,"CSWI6.Pos.q","LDB/CSWI6.Pos.q","Quality","Good")
        };
        Assert.Equal(2,GooseCanonicalLeafProjection.Project(raw).Count);
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
