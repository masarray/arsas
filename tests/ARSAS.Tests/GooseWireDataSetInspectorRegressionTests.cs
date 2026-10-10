using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class GooseWireDataSetInspectorRegressionTests
{
    [Fact]
    public void ActiveLiteInspectorShowsRawFourOrderedMembersWithOnlySignalAndValueColumns()
    {
        var ui=Read("Views/GooseSubscriberLiteView.xaml");
        var xaml=XDocument.Parse(ui);
        XNamespace ns="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grid=xaml.Descendants(ns+"DataGrid").Single(x =>
            ((string?)x.Attribute("ItemsSource"))?.Contains("SelectedGooseStream.Leaves")==true);
        // Value is now a DataGridTemplateColumn, so verify both column kinds
        // and its canonical process badge rather than assuming TextColumn.
        var headers=grid.Descendants()
            .Where(c=>c.Name==ns+"DataGridTextColumn" || c.Name==ns+"DataGridTemplateColumn")
            .Select(c=>(string?)c.Attribute("Header")).ToArray();
        Assert.Equal(new[]{"Signal","Value"},headers);
        Assert.Contains(grid.Descendants(ns+"DataGridTemplateColumn"),
            c=>(string?)c.Attribute("CellTemplate")=="{StaticResource ProcessValueBadgeTemplate}");
        Assert.Contains("Text=\"DataSet entries\"",ui);
        Assert.DoesNotContain("SelectedGooseStream.EngineeringLeaves",ui);
        Assert.DoesNotContain("Header=\"Quality\"",ui);
    }

    [Fact]
    public void SelectedGooseStreamRendersQualityAsOriginalQValueNotAsInventedStatusField()
    {
        static GooseLeafValueSnapshot Leaf(int index,string name,string value,string type)
            => new(index+1,index,name,"BCUGEF650/"+name,"ST","DPC",type,value,"",false,"SCL");
        var raw=new[]{
            Leaf(0,"CSWI6.Pos.stVal","Off","Dbpos"),
            Leaf(1,"CSWI6.Pos.q","Good","Quality"),
            Leaf(2,"CSWI7.Pos.stVal","On","Dbpos"),
            Leaf(3,"CSWI7.Pos.q","Good","Quality")
        };
        var row=new GooseStreamRow{StreamKey="BCUGE/GOOSE1"};
        var snapshot=new GooseStreamSnapshot(
            StreamKey:"BCUGE/GOOSE1",AppIdText:"0x0021",
            GoCbRef:"BCUGEF650/LLN0$GO$gcb",GoId:"BCUGE03",
            DataSetReference:"BCUGEF650/LLN0.GOOSE1",
            SourceMac:"02:00:4C:4F:4F:50",DestinationMac:"01:0C:CD:01:00:33",
            VlanText:"VID 0 / PCP 4",StateNumberText:"7",SequenceNumberText:"0",
            SequenceStatus:"StateChange",TimeAllowedToLiveText:"2 ms",
            ConfigurationRevisionText:"1",ModelIedName:"BCUGE",BindingSource:"SCL",
            DiagnosticsSummary:"",LastSeenText:"19:54:52.097",PacketCount:66,
            ChangedValueCount:1,Test:true,NeedsCommissioning:false,Leaves:raw,
            VlanId:"0",VlanPriority:"4",WireDataSetEntryCount:4);
        row.Apply(snapshot);
        Assert.Equal(new[]{"CSWI6.Pos.stVal","CSWI6.Pos.q","CSWI7.Pos.stVal","CSWI7.Pos.q"},
            row.Leaves.Select(v=>v.SignalName).ToArray());
        Assert.Equal(new[]{"Off","Good","On","Good"},row.Leaves.Select(v=>v.Value).ToArray());
        Assert.Equal(4,row.Leaves.Count);
        Assert.Equal(4,row.WireDataSetEntryCount);
    }

    [Fact]
    public void RuntimeNoLongerInvokesQualityProjectionInPresentation()
    {
        var model=Read("Models/GooseSubscriberModels.cs");
        Assert.DoesNotContain("GooseCanonicalLeafProjection.Project",model);
        Assert.DoesNotContain("EngineeringLeaves",model);
        var counter=Read("MainWindow.GooseTimeline.cs");
        Assert.Contains("SelectedGooseStream.Leaves.Count",counter);
        Assert.DoesNotContain("EngineeringLeaves",counter);
    }

    private static string Read(string path)
    {
        DirectoryInfo? current=new(AppContext.BaseDirectory);
        while(current!=null)
        {
            var file=Path.Combine(current.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(file))return File.ReadAllText(file);
            current=current.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
