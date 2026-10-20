using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0FSubstitutionOperateBlockingReadPlanTests
{
    private static LiveIedModelDiscoveryDocument BuildModel()
    {
        var root = "SIEBCUCTRL/XCBR1.Pos";
        var attributes = new[]
        {
            ("subEna", "SV"), ("subVal", "SV"), ("subQ", "SV"), ("subID", "SV"),
            ("opRcvd", "OR"), ("opOk", "OR"), ("tOpOk", "OR"),
            ("blkEna", "BL"), ("stVal", "ST"), ("ctlVal", "CO")
        }.Select(x => new LiveIedDataAttributeModel
        {
            ObjectReference = root,
            AttributePath = "Pos." + x.Item1,
            MmsReference = "SIEBCUCTRL/XCBR1$" + x.Item2 + "$Pos$" + x.Item1,
            FunctionalConstraint = x.Item2,
            MmsType = "Boolean",
            TypeSource = "LiveTypeSpecification"
        }).ToArray();
        return new LiveIedModelDiscoveryDocument
        {
            LogicalDevices = [new LiveIedLogicalDeviceModel
            {
                LogicalNodes = [new LiveIedLogicalNodeModel
                {
                    DataObjects = [new LiveIedDataObjectModel
                    {
                        Name = "Pos", Reference = root, InferredCdc = "DPC",
                        Attributes = attributes
                    }]
                }]
            }]
        };
    }

    [Fact]
    public void ExplicitReadAllIncludesVerifiedSvOrBlButNeverControl()
    {
        var targets = IedBrowserReadPlan.Build(BuildModel(), null, readAll: true);
        Assert.Equal(9, targets.Count);
        foreach (var fc in new[] { "SV", "OR", "BL", "ST" })
            Assert.Contains(targets, t => t.FunctionalConstraint == fc);
        Assert.DoesNotContain(targets, t => t.FunctionalConstraint == "CO");
        Assert.Contains(targets, t => t.Reference.EndsWith("$SV$Pos$subEna", StringComparison.Ordinal));
        Assert.Contains(targets, t => t.Reference.EndsWith("$SV$Pos$subVal", StringComparison.Ordinal));
        Assert.Contains(targets, t => t.Reference.EndsWith("$OR$Pos$opRcvd", StringComparison.Ordinal));
        Assert.Contains(targets, t => t.Reference.EndsWith("$BL$Pos$blkEna", StringComparison.Ordinal));
    }

    [Fact]
    public void NoLiveModelMeansNoReadTargets()
    {
        Assert.Empty(IedBrowserReadPlan.Build(null, null, readAll: true));
        Assert.Equal("Control only", IedBrowserReadPlan.UnobservedValue("CO"));
        Assert.Equal("Not read", IedBrowserReadPlan.UnobservedValue("SV"));
    }

    [Fact]
    public void InlineReadProgressHasFixedCardSlotAndDoesNotInvokeBusyOverlay()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        string xaml;
        while (true)
        {
            var path = Path.Combine(directory.FullName, "MainWindow.xaml");
            if (File.Exists(path)) { xaml = File.ReadAllText(path); break; }
            directory = directory.Parent ?? throw new FileNotFoundException("MainWindow.xaml");
        }
        Assert.Contains("<RowDefinition Height=\"5\"/>", xaml);
        Assert.Contains("x:Name=\"BrowserReadCardLine\" Grid.Row=\"1\" Grid.Column=\"1\"", xaml);
        Assert.Contains("<Setter TargetName=\"BrowserReadCardLine\" Property=\"Opacity\" Value=\"1\"/>", xaml);
        Assert.DoesNotContain("<Setter TargetName=\"BusyOverlay\" Property=\"Visibility\" Value=\"Visible\"/>\n                                            </DataTrigger>\n                                        </DataTemplate.Triggers>", xaml);
    }
}
