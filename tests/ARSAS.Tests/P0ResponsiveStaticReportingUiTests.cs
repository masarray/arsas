using System.Xml.Linq;
using ArIED61850Tester.Models;
using Xunit;

namespace ARSAS.Tests;

public sealed class P0ResponsiveStaticReportingUiTests
{
    [Fact]
    public void ReportingUsesSameIedCardProgressAuthorityWithNoFalseReportSuccess()
    {
        var device = new Iec61850MonitorDevice();
        device.BeginReportingProgress("Preparing exact DataSet authority…");
        Assert.True(device.IsReportingProgressActive);
        Assert.Equal(0d, device.IedCardProgressPercent);
        Assert.Contains("DataSet", device.BusyStage);

        device.UpdateReportingProgress(63d, "Selecting static FCDA rows");
        Assert.Equal(63d, device.IedCardProgressPercent);
        device.UpdateReportingProgress(20d, "Stale stage");
        Assert.Equal(63d, device.IedCardProgressPercent); // monotonic
        device.UpdateReportingProgress(100d, "Arming RCB");
        Assert.Equal(99d, device.IedCardProgressPercent); // never claim complete pre-finish

        device.CompleteReportingProgress();
        Assert.False(device.IsReportingProgressActive);
        // Green progress is an arming-workflow indicator, not a live RCB
        // traffic gate, SCD parity verdict, or SOE completeness claim.
        Assert.False(device.HasReportStream);
    }

    [Fact]
    public void ExistingIedCardProgressLineIsReusedAndTurnsGreenDuringReporting()
    {
        var xaml = Read("MainWindow.xaml");
        var xml = XDocument.Parse(xaml);
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var bar = xml.Descendants(wpf + "ProgressBar").Single(e =>
            (string?)e.Attribute(x + "Name") == "BrowserReadProgressLine");
        Assert.Equal("{Binding IedCardProgressPercent, Mode=OneWay}",
            (string?)bar.Attribute("Value"));
        var template = bar.Ancestors(wpf + "DataTemplate").First();
        var trigger = template.Descendants(wpf + "DataTrigger").Single(t =>
            (string?)t.Attribute("Binding") == "{Binding IsReportingProgressActive}");
        Assert.Contains(trigger.Elements(wpf + "Setter"), setter =>
            (string?)setter.Attribute("TargetName") == "BrowserReadProgressLine" &&
            (string?)setter.Attribute("Property") == "Foreground" &&
            (string?)setter.Attribute("Value") == "#16A34A");
    }

    [Fact]
    public void ResponsiveSelectionKeepsWpfBoundObjectsOnDispatcherAndDoesNotPoll()
    {
        var workflow = Read("MainWindow.SharedSclWorkspace.cs");
        Assert.Contains("ToLookup(", Read("Services/Iec61850StaticDataSetAuthoritySelection.cs"));
        Assert.Contains("Dispatcher.Yield", workflow);
        Assert.Contains("device.BeginBulkSignalSelection()", workflow);
        Assert.Contains("device.EndBulkSignalSelection()", workflow);
        Assert.Contains("P0_STATIC_UI_WORK", workflow);
        Assert.DoesNotContain("Task.Run(", workflow);
        Assert.Contains("await ApplyStaticDataSetSelection(device);",
            Read("MainWindow.SclQuickActions.cs"));
        Assert.Contains("device.CompleteReportingProgress();",
            Read("MainWindow.SclQuickActions.cs"));
        Assert.Contains("device.UpdateReportingProgress(84",
            Read("MainWindow.xaml.cs"));
    }

    private static string Read(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
