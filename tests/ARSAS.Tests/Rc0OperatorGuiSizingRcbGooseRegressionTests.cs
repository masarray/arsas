using System.Windows;
using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class Rc0OperatorGuiSizingRcbGooseRegressionTests
{
    [Fact]
    public void WideNormalStartup_UsesDesktopWorkAreaWithoutForcingMaximize()
    {
        var main = XDocument.Parse(Read("MainWindow.xaml")).Root!;
        Assert.Equal("Normal", (string?)main.Attribute("WindowState"));
        Assert.Equal("CenterScreen", (string?)main.Attribute("WindowStartupLocation"));
        Assert.True(double.Parse(main.Attribute("Width")!.Value) >= 1680d);
        Assert.True(double.Parse(main.Attribute("Height")!.Value) >= 920d);
        var bounds = Read("WorkstationWindowStartupBounds.cs");
        Assert.Contains("SystemParameters.WorkArea", bounds, StringComparison.Ordinal);
        Assert.Contains("const double inset = 8d", bounds, StringComparison.Ordinal);
        Assert.Contains("WindowStartupLocation.Manual", bounds, StringComparison.Ordinal);
        Assert.DoesNotContain("WindowState.Maximized", bounds, StringComparison.Ordinal);
    }

    [Fact]
    public void RcbActivityIcon_OnlyClaimsVerifiedReadOrOwnLiveAssociation()
    {
        var row = new RcbExportRow { Reference = "LD/LLN0.RP.urcb01", Name = "Urcb01" };
        var node = new IedBrowserTreeRow("rcb/1", "Urcb01", "RCB", 0, row);
        Assert.Equal(Visibility.Collapsed, node.RcbActivityIconVisibility);
        row.Availability = MmsRcbOperationalAvailability.InUse;
        node.RefreshRcbUsage();
        Assert.Equal(Visibility.Collapsed, node.RcbActivityIconVisibility);
        row.Availability = MmsRcbOperationalAvailability.UsedByCaller;
        node.RefreshRcbUsage();
        Assert.Equal(Visibility.Visible, node.RcbActivityIconVisibility);
        Assert.Contains("current ARSAS", node.RcbActivityIconTooltip, StringComparison.Ordinal);
        row.Availability = MmsRcbOperationalAvailability.Unknown;
        node.RefreshRcbUsage();
        Assert.Equal(Visibility.Collapsed, node.RcbActivityIconVisibility);
        Assert.True(node.SetRcbEnabled(true, "verified MMS Read"));
        Assert.Equal(Visibility.Visible, node.RcbActivityIconVisibility);
        Assert.Equal("verified MMS Read", node.RcbActivityIconTooltip);
        Assert.True(node.SetRcbEnabled(false, ""));
        Assert.Equal(Visibility.Collapsed, node.RcbActivityIconVisibility);

        var xaml = Read("MainWindow.xaml");
        Assert.Equal(2, xaml.Split("Visibility=\"{Binding RcbActivityIconVisibility}\"").Length - 1);
        Assert.Equal(2, xaml.Split("ToolTip=\"{Binding RcbActivityIconTooltip}\"").Length - 1);
    }

    [Fact]
    public void GooseTabNavigation_DoesNotInvokeBlockingNpcapOrSclCompilation()
    {
        var source = Read("MainWindow.GooseSubscriber.cs");
        var start = source.IndexOf("private void ActivateGooseSubscriberWorkspace()", StringComparison.Ordinal);
        var end = source.IndexOf("private async Task LoadGooseAdaptersForNavigationAsync()", StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var activation = source[start..end];
        Assert.DoesNotContain("RefreshGooseBindingPreview();", activation, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshGooseAdapters();", activation, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.Background", activation, StringComparison.Ordinal);
        Assert.Contains("LoadGooseAdaptersForNavigationAsync()", activation, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(", source, StringComparison.Ordinal);
        Assert.Contains("_gooseSubscriberRuntime.ListAdapters()", source, StringComparison.Ordinal);
        // Explicit Start and Refresh Models continue to build the model before capture.
        Assert.Contains("RefreshGooseBindingPreview();\n            ResetGooseView", source, StringComparison.Ordinal);
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var full = Path.Combine(dir.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
        }
        throw new FileNotFoundException(path);
    }
}
