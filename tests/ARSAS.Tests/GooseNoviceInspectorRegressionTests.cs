using System.Globalization;
using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class GooseNoviceInspectorRegressionTests
{
    [Fact]
    public void ApplicationId_IsDerivedFromRealWireValue_NotHardcoded()
    {
        var row = new GooseStreamRow { AppIdText = "0x002C" };
        Assert.Equal("44", row.ApplicationIdDecimal);
        row.AppIdText = "0x0021";
        Assert.Equal("33", row.ApplicationIdDecimal);
    }

    [Fact]
    public void Inspector_UsesRealWireVlanAndDatasetEntryCount()
    {
        var row = new GooseStreamRow { StreamKey="G1" };
        var snapshot = new GooseStreamSnapshot(
            StreamKey:"G1",AppIdText:"0x002C",GoCbRef:"LD/LLN0$GO$gcb",
            GoId:"BCUGE03",DataSetReference:"LD/LLN0.GOOSE1",
            SourceMac:"00:11:22:33:44:55",DestinationMac:"01:0C:CD:01:00:33",
            VlanText:"VID 0 / PCP 4",StateNumberText:"2",SequenceNumberText:"185",
            SequenceStatus:"Retransmission",TimeAllowedToLiveText:"2000 ms",
            ConfigurationRevisionText:"1",ModelIedName:"BCUGE",BindingSource:"SCL",
            DiagnosticsSummary:"",LastSeenText:"16:00:00.000",PacketCount:14,
            ChangedValueCount:0,Test:true,NeedsCommissioning:false,
            Leaves:Array.Empty<GooseLeafValueSnapshot>(),VlanId:"0",VlanPriority:"4",
            WireDataSetEntryCount:4);
        row.Apply(snapshot);
        Assert.Equal("44",row.ApplicationIdDecimal);
        Assert.Equal("0",row.VlanId);
        Assert.Equal("4",row.VlanPriority);
        Assert.Equal("2",row.StateNumberText);
        Assert.Equal("185",row.SequenceNumberText);
        Assert.Equal("True",row.SimulationTestText);
        Assert.Equal("4",row.DataSetEntryCountText);
    }

    [Fact]
    public void ActualGooseView_UsesNoviceMetadataLabelsAndTimelineColumns()
    {
        var view=Read("Views/GooseSubscriberLiteView.xaml");
        var xml=XDocument.Parse(view);
        XNamespace wpf="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var headers=xml.Descendants(wpf+"DataGridTextColumn")
            .Select(x=>(string?)x.Attribute("Header")).ToArray();
        Assert.Contains("IED",headers);
        Assert.Contains("GOOSE ID",headers);
        var time=xml.Descendants(wpf+"DataGridTextColumn")
            .Single(x=>(string?)x.Attribute("Header")=="Time");
        Assert.True(int.Parse((string)time.Attribute("Width")!,CultureInfo.InvariantCulture)>=110);
        foreach(var label in new[]{"Application ID","VLAN ID","VLAN priority",
            "Time allowed to live","Status number","Sequence number",
            "Configuration revision","Simulation test","Number of DataSet entries"})
            Assert.Contains($"Text=\"{label}\"",view);
        Assert.Contains("SelectedGooseStream.ApplicationIdDecimal",view);
        Assert.Contains("WireDataSetEntryCount",Read("Models/GooseSubscriberModels.cs"));
        Assert.Contains("rawValueCount);",Read("MainWindow.GooseSubscriber.cs"));
        Assert.Contains("IedName = ",Read("MainWindow.GooseTimeline.cs"));
        Assert.Contains("GooseId = ",Read("MainWindow.GooseTimeline.cs"));
    }

    private static string Read(string path)
    {
        DirectoryInfo? d=new(AppContext.BaseDirectory);
        while(d!=null)
        {
            var file=Path.Combine(d.FullName,path);
            if(File.Exists(file))return File.ReadAllText(file);
            d=d.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
