using System.Xml.Linq;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class P0GBrowserSubstitutionReadAndIedCardProgressTests
{
    private const string LnRef = "SIEBCUCTRL/XCBR1";

    private static LiveIedDataAttributeModel Attribute(
        string doName, string leaf, string fc, bool wireKnown = true) => new()
    {
        ObjectReference = LnRef + "." + doName,
        AttributePath = doName + "." + leaf,
        FunctionalConstraint = fc,
        MmsReference = wireKnown
            ? LnRef + "$" + fc + "$" + doName + "$" + leaf : "",
        SclBType = leaf == "subVal" ? "Dbpos" :
            leaf == "subQ" ? "Quality" :
            leaf == "tOpOk" ? "Timestamp" : "Boolean",
        TypeDiscoveryStatus = "Exact"
    };

    private static (LiveIedModelDiscoveryDocument Model, IedBrowserTreeRow Page)
        XcbrPage()
    {
        var ln = new LiveIedLogicalNodeModel
        {
            Name = "XCBR1",
            LnClass = "XCBR",
            DataObjects =
            [
                new LiveIedDataObjectModel
                {
                    Name = "Pos", Reference = LnRef + ".Pos",
                    Attributes =
                    [
                        Attribute("Pos", "stVal", "ST"),
                        Attribute("Pos", "subEna", "SV"),
                        Attribute("Pos", "subVal", "SV"),
                        Attribute("Pos", "subQ", "SV"),
                        Attribute("Pos", "subID", "SV"),
                        Attribute("Pos", "opRcvd", "OR"),
                        Attribute("Pos", "opOk", "OR"),
                        Attribute("Pos", "tOpOk", "OR"),
                        Attribute("Pos", "blkEna", "BL"),
                        Attribute("Pos", "Oper", "CO"),
                        Attribute("Pos", "notVerified", "SV", wireKnown: false)
                    ]
                }
            ]
        };
        var model = new LiveIedModelDiscoveryDocument
        {
            LogicalDevices =
            [
                new LiveIedLogicalDeviceModel
                {
                    MmsDomain = "SIEBCUCTRL",
                    LogicalNodes = [ln]
                }
            ]
        };
        return (model, new IedBrowserTreeRow(
            "SIEBCUCTRL/XCBR1", "XCBR1", "LN", 2, ln));
    }

    [Fact]
    public void ExplicitReadLnIncludesModelVerifiedSubstitutionOperationAndBlocking_WithoutControl()
    {
        var (model, page) = XcbrPage();
        var selected = IedBrowserReadPlan.Build(model, page, readAll: false);
        foreach (var (leaf, fc) in new[]
        {
            ("subEna", "SV"), ("subVal", "SV"), ("subQ", "SV"),
            ("subID", "SV"), ("opRcvd", "OR"), ("opOk", "OR"),
            ("tOpOk", "OR"), ("blkEna", "BL")
        })
        {
            Assert.Contains(selected, target =>
                target.Reference == LnRef + "$" + fc + "$Pos$" + leaf &&
                target.FunctionalConstraint == fc);
        }
        Assert.Contains(selected, target => target.FunctionalConstraint == "ST");
        Assert.DoesNotContain(selected, target =>
            target.FunctionalConstraint == "CO" ||
            target.Reference.Contains("notVerified", StringComparison.Ordinal));
        var ordered = IedBrowserReadPlan.PrioritizeExplicitLn(selected);
        Assert.Equal(selected.Count, ordered.Count);
    }

    [Fact]
    public void ExplicitReadAllKeepsModelTypesAndDoesNotAdmitSclOnlyOrControl()
    {
        var (model, page) = XcbrPage();
        var all = IedBrowserReadPlan.Build(model, page, readAll: true);
        Assert.Contains(all, target =>
            target.Reference == LnRef + "$SV$Pos$subVal" &&
            target.FunctionalConstraint == "SV" &&
            target.DataType == "Dbpos");
        Assert.Contains(all, target =>
            target.Reference == LnRef + "$SV$Pos$subQ" &&
            target.DataType == "Quality");
        Assert.Contains(all, target => target.FunctionalConstraint == "OR");
        Assert.Contains(all, target => target.FunctionalConstraint == "BL");
        Assert.DoesNotContain(all, target =>
            target.FunctionalConstraint == "CO" ||
            target.Reference.Contains("notVerified", StringComparison.Ordinal));
    }

    [Fact]
    public void PassiveNavigationDoesNotAutomaticallyReadSubstitutionOrBlockingOrOperationState()
    {
        var (model, page) = XcbrPage();
        var ledger = new IedBrowserLiveValues();
        ledger.BeginAssociation();
        var passive = IedBrowserReadPlan.BuildFocused(model, page, ledger);
        Assert.Contains(passive, target => target.FunctionalConstraint == "ST");
        Assert.DoesNotContain(passive, target =>
            target.FunctionalConstraint is "SV" or "OR" or "BL" or "CO");
    }

    [Fact]
    public void ProgressLineHasFixedSpaceBelowIpAndReadNeverRevealsFullCardOverlay()
    {
        var xaml = XDocument.Load(FindFile("MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var progress = xaml.Descendants().Single(e =>
            (string?)e.Attribute(x + "Name") == "BrowserReadProgressLine");
        Assert.Equal("ProgressBar", progress.Name.LocalName);
        Assert.Equal("3", (string?)progress.Attribute("Height"));
        Assert.Equal("0", (string?)progress.Attribute("Opacity"));
        Assert.Contains("BrowserReadProgressPercent", (string?)progress.Attribute("Value"));
        var reserved = progress.Parent!;
        Assert.Equal("Grid", reserved.Name.LocalName);
        Assert.Equal("7", (string?)reserved.Attribute("Height"));
        var ip = reserved.Parent!.Elements().Single(e =>
            (string?)e.Attribute("Text") == "{Binding EndpointText}");
        Assert.Equal("TextBlock", ip.Name.LocalName);
        var readTrigger = xaml.Descendants().Single(e =>
            e.Name.LocalName == "DataTrigger" &&
            (string?)e.Attribute("Binding") == "{Binding IsBrowserReading}");
        var setter = Assert.Single(readTrigger.Elements());
        Assert.Equal("BrowserReadProgressLine", (string?)setter.Attribute("TargetName"));
        Assert.Equal("Opacity", (string?)setter.Attribute("Property"));
        Assert.Equal("1", (string?)setter.Attribute("Value"));
        Assert.DoesNotContain(readTrigger.Descendants(), e =>
            (string?)e.Attribute("TargetName") == "BusyOverlay");
    }

    [Fact]
    public void ProgressIsPerDeviceResetsBetweenSweepsAndDoesNotAffectDiscoveryState()
    {
        var device = new Iec61850MonitorDevice();
        device.IsBrowserReading = true;
        device.ApplyBrowserReadProgress(3, 4);
        Assert.Equal(75d, device.BrowserReadProgressPercent);
        device.IsBrowserReading = false;
        device.IsBrowserReading = true;
        Assert.Equal(0d, device.BrowserReadProgressPercent);
        device.ApplyBrowserReadProgress(4, 4);
        Assert.Equal(100d, device.BrowserReadProgressPercent);
        device.IsBrowserReading = false;
        device.ApplyBrowserReadProgress(1, 2);
        Assert.Equal(100d, device.BrowserReadProgressPercent);
    }

    [Fact]
    public void ReadProgressFollowsOwningAssociationWhenOperatorSelectsAnotherIed()
    {
        var source = File.ReadAllText(FindFile("MainWindow.IedBrowser.cs"));
        var callback = source.Split(
            "var readProgress = new Progress<IedBrowserReadProgress>(p =>",
            StringSplitOptions.None)[1].Split(
            "if (device.IsMonitoring)", StringSplitOptions.None)[0];
        Assert.Contains("sourceDevice.LiveValues.Association == readAssociation", callback);
        Assert.Contains("ReferenceEquals(_iedBrowserReadOperation, cts)", callback);
        Assert.DoesNotContain("ReferenceEquals(SelectedDevice, sourceDevice)", callback);
        Assert.Contains("sourceDevice.ApplyBrowserReadProgress", callback);
    }

    private static string FindFile(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName, name);
            if (File.Exists(path)) return path;
            dir = dir.Parent;
        }
        throw new FileNotFoundException(name);
    }
}
