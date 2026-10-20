using System.Xml.Linq;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0FBrowserReadCoverageAndCardTests
{
    [Fact]
    public void ExplicitReadLnIncludesVerifiedSubstitutionOriginAndBlockingWithoutControlOperations()
    {
        var attributes = new[] { ("subEna", "SV"), ("subVal", "SV"), ("subQ", "SV"),
            ("subID", "SV"), ("opRcvd", "OR"), ("opOk", "OR"),
            ("blkEna", "BL"), ("stVal", "ST"), ("ctlVal", "CO") };
        var dataObject = new LiveIedDataObjectModel
        {
            Name = "Pos", Reference = "LD/XCBR1.Pos",
            Attributes = attributes.Select(a => new LiveIedDataAttributeModel
            {
                ObjectReference = "LD/XCBR1.Pos",
                AttributePath = "Pos." + a.Item1,
                MmsReference = "LD/XCBR1$" + a.Item2 + "$Pos$" + a.Item1,
                FunctionalConstraint = a.Item2,
                MmsType = "Boolean",
                TypeSource = "LiveTypeSpecification"
            }).ToArray()
        };
        var ln = new LiveIedLogicalNodeModel { DataObjects = [dataObject] };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [ln]
            }]
        };
        var page = new IedBrowserTreeRow
        {
            Kind = "LN",
            Payload = ln
        };
        var plan = IedBrowserReadPlan.Build(model, page, readAll: false);
        Assert.Equal(8, plan.Count);
        Assert.All(new[] { "SV", "OR", "BL" },
            fc => Assert.Contains(plan, target => target.FunctionalConstraint == fc));
        Assert.DoesNotContain(plan, target => target.FunctionalConstraint == "CO");
        Assert.Contains(plan, target => target.Reference.EndsWith("$subEna", StringComparison.Ordinal));
        Assert.Contains(plan, target => target.Reference.EndsWith("$blkEna", StringComparison.Ordinal));
    }

    [Fact]
    public void BrowserCardReadUsesPermanentLaneAndNeverInvokesBusyOverlay()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        string? file = null;
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "MainWindow.xaml");
            if (File.Exists(candidate)) { file = candidate; break; }
            dir = dir.Parent;
        }
        Assert.NotNull(file);
        var xaml = File.ReadAllText(file!);
        Assert.Contains("Height=\"6\" Margin=\"8,5,8,0\"", xaml);
        Assert.Contains("x:Name=\"IedCardBrowserReadProgress\"", xaml);
        Assert.Contains("TargetName=\"IedCardBrowserReadProgress\" Property=\"Opacity\" Value=\"1\"", xaml);
        Assert.DoesNotContain(
            "<DataTrigger Binding=\"{Binding IsBrowserReading}\" Value=\"True\">\n                                                <Setter TargetName=\"BusyOverlay\"",
            xaml);
        _ = XDocument.Parse(xaml);
    }
}
