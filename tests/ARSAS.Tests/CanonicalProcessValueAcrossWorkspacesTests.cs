using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class CanonicalProcessValueAcrossWorkspacesTests
{
    [Theory]
    [InlineData(0, "Intermediate [00]", Iec61850ValueStatePresentation.PositionIntermediate)]
    [InlineData(1, "Open [01]", Iec61850ValueStatePresentation.PositionOpen)]
    [InlineData(2, "Close [10]", Iec61850ValueStatePresentation.PositionClose)]
    [InlineData(3, "Bad state [11]", Iec61850ValueStatePresentation.PositionBad)]
    public void TypedGooseAndReportUseIdenticalDbposVocabulary(int code, string expected, string tone)
    {
        var wire = MmsDataValue.BitString(6, new byte[] { (byte)(code << 6) });
        var path = "BCUGEF650/CSWI6.Pos.stVal";
        var goose = GooseTypedValueInterpreter.Render(wire, "DPC", "Dbpos", path);
        var report = Iec61850ValueFormatter.FormatReportProcessValue(
            code, "Dbpos", "", "Position", path);
        Assert.Equal(expected, report);
        Assert.Equal(report, goose);
        Assert.Equal(expected, Iec61850ValueFormatter.Format(code, "Dbpos", ""));
        Assert.Equal(expected, Iec61850ValueFormatter.FormatOperatorDbpos(code));

        var row = new GooseLeafValueRow();
        row.Apply(new GooseLeafValueSnapshot(
            1, 0, "CSWI6.Pos.stVal", path, "ST", "DPC", "Dbpos",
            goose, "", true, "SCL"));
        Assert.Equal(expected, row.DisplayValue);
        Assert.Equal("DP", row.ValueTypeToken);
        Assert.Equal(tone, row.ValueVisualKind);
        Assert.Equal(tone, Iec61850ValueStatePresentation.ClassifyVisualKind(
            report, "Dbpos", reference: path));
    }

    [Fact]
    public void TypedPreviousValueAndEventSummaryUseSameFourStateVocabulary()
    {
        foreach (var (bits, expected) in new[]
        {
            ("00", "Intermediate [00]"), ("40", "Open [01]"),
            ("80", "Close [10]"), ("C0", "Bad state [11]")
        })
        {
            Assert.Equal(expected, GooseTypedValueInterpreter.RenderPrevious(
                $"bits({bits}, unused=6)", "DPC", "Dbpos"));
        }
        Assert.Equal("Open [01]", GooseTypedValueInterpreter.RenderPrevious("Off", "DPC", "Dbpos"));
        Assert.Equal("Close [10]", GooseTypedValueInterpreter.RenderPrevious("On", "DPC", "Dbpos"));
        var timeline = Read("MainWindow.GooseTimeline.cs");
        Assert.Contains("GooseTypedValueInterpreter.RenderPrevious(leaf.PreviousValue", timeline);
        Assert.Contains("ShortenGooseText(leaf.Value", timeline);
    }

    [Fact]
    public void UnboundAndQualityMembersRemainIndependentOfPositionRendering()
    {
        var source = new GooseLeafValueRow();
        source.Apply(new GooseLeafValueSnapshot(
            1, 0, "GGIO1.Ind1.stVal", "", "ST", "", "", "On", "", false, "Unbound"));
        Assert.Equal("On", source.DisplayValue);
        Assert.Equal("", source.ValueTypeToken);
        Assert.Equal(Iec61850ValueStatePresentation.Neutral, source.ValueVisualKind);

        var quality = new GooseLeafValueRow();
        quality.Apply(new GooseLeafValueSnapshot(
            2, 1, "CSWI6.Pos.q", "BCUGEF650/CSWI6.Pos.q",
            "ST", "DPC", "Quality", "Good", "", false, "SCL"));
        Assert.Equal("Good", quality.DisplayValue);
        Assert.Equal("", quality.ValueTypeToken);
        Assert.Equal(Iec61850ValueStatePresentation.Neutral, quality.ValueVisualKind);
        Assert.Equal("On", GooseEngineeringValueFormatter.Format("On"));
        Assert.Equal("On", GooseEngineeringValueFormatter.Format("On", "Enum"));
        Assert.Equal("bits(80, unused=6)", GooseEngineeringValueFormatter.Format("bits(80, unused=6)"));
        Assert.Equal("Good", GooseTypedValueInterpreter.Render(
            MmsDataValue.BitString(3, new byte[] { 0, 0 }), "DPC", "Quality", "CSWI6.Pos.q"));
    }

    [Fact]
    public void CommandFeedbackIsCachedPresentationOnly_AndOperationAuthorityUnchanged()
    {
        var signal = new SignalDefinition
        {
            ObjectReference = "BCUGEF650/CSWI6.Pos",
            ControlCdc = "DPC",
            ControlCurrentValue = "On"
        };
        Assert.Equal("Closed", signal.ControlCurrentValue);
        var first = signal.ControlProcessValue;
        Assert.Equal("Close [10]", first.DisplayValue);
        Assert.Equal("DP", first.ValueTypeToken);
        Assert.Equal(Iec61850ValueStatePresentation.PositionClose, first.ValueVisualKind);
        Assert.Same(first, signal.ControlProcessValue);

        signal.ControlCurrentValue = "Off";
        Assert.Equal("Open", signal.ControlCurrentValue);
        Assert.NotSame(first, signal.ControlProcessValue);
        Assert.Equal("Open [01]", signal.ControlProcessValue.DisplayValue);
        Assert.Equal(Iec61850ValueStatePresentation.PositionOpen, signal.ControlProcessValue.ValueVisualKind);
    }

    [Fact]
    public void BooleanWireTypeIsExplicitAndUsesReportVocabulary()
    {
        var raw = MmsDataValue.BitString(7, new byte[] { 0x80 });
        Assert.Equal("True [1]", GooseTypedValueInterpreter.Render(raw,"SPS","Boolean","GGIO1.Ind1.stVal"));
        Assert.Equal("True [1]", GooseTypedValueInterpreter.RenderPrevious("bits(80, unused=7)", "SPS", "Boolean"));
        Assert.Contains("bits(", GooseTypedValueInterpreter.Render(raw,"","","unmapped"));
    }

    [Fact]
    public void OneApplicationBadgeTemplateIsWiredIntoAllActiveWorkspaceGrids()
    {
        var app = XDocument.Parse(Read("App.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var shared = app.Descendants(wpf + "DataTemplate")
            .Where(n => (string?)n.Attribute(x + "Key") == "ProcessValueBadgeTemplate").ToArray();
        Assert.Single(shared);
        var main = Read("MainWindow.xaml");
        Assert.DoesNotContain("x:Key=\"ProcessValueBadgeTemplate\"", main);
        Assert.True(main.Split("CellTemplate=\"{StaticResource ProcessValueBadgeTemplate}\"").Length >= 4);
        Assert.Contains("Content=\"{Binding ControlProcessValue}\"", main);
        Assert.Contains("ContentTemplate=\"{StaticResource ProcessValueBadgeTemplate}\"", main);

        var goose = XDocument.Parse(Read("Views/GooseSubscriberLiteView.xaml"));
        var dataSetGrid = goose.Descendants(wpf + "DataGrid")
            .Single(g => ((string?)g.Attribute("ItemsSource"))?.Contains("SelectedGooseStream.Leaves")==true);
        var headers = dataSetGrid.Descendants().Where(e =>
            e.Name==wpf+"DataGridTextColumn" || e.Name==wpf+"DataGridTemplateColumn")
            .Select(e=>(string?)e.Attribute("Header")).ToArray();
        Assert.Equal(new[]{"Signal","Value"},headers);
        Assert.Contains(dataSetGrid.Descendants(wpf+"DataGridTemplateColumn"),
            col => (string?)col.Attribute("CellTemplate")=="{StaticResource ProcessValueBadgeTemplate}");
        Assert.DoesNotContain("SelectedGooseStream.EngineeringLeaves",Read("Views/GooseSubscriberLiteView.xaml"));
        Assert.DoesNotContain("Header=\"Quality\"",Read("Views/GooseSubscriberLiteView.xaml"));
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while(dir is not null)
        {
            var file=Path.Combine(dir.FullName,path.Replace('/',Path.DirectorySeparatorChar));
            if(File.Exists(file)) return File.ReadAllText(file);
            dir=dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
