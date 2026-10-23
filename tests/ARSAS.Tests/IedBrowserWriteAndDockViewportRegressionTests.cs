using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserWriteAndDockViewportRegressionTests
{
    [Fact]
    public void CommandDockExpanderUsesFiniteStarViewportRatherThanAutoOverflow()
    {
        var app = XDocument.Parse(Read("App.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var style = Assert.Single(app.Descendants(wpf + "Style")
            .Where(s => (string?)s.Attribute(x + "Key") == "CommandDockTransparentHeaderExpander"));
        var rows = style.Descendants(wpf + "RowDefinition").ToArray();
        Assert.True(rows.Length >= 2);
        Assert.Equal("Auto", (string?)rows[0].Attribute("Height"));
        Assert.Equal("*", (string?)rows[1].Attribute("Height"));
        var host = Assert.Single(style.Descendants(wpf + "ContentPresenter")
            .Where(n => (string?)n.Attribute(x + "Name") == "CommandDockContentHost"));
        Assert.Equal("0", (string?)host.Attribute("MinHeight"));
        Assert.Equal("Stretch", (string?)host.Attribute("VerticalAlignment"));
    }

    [Fact]
    public void ActualCommandDataGridHasVisiblePixelScrollAndBoundedParent()
    {
        var xaml = XDocument.Parse(Read("MainWindow.xaml"));
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var grid = Assert.Single(xaml.Descendants(wpf + "DataGrid")
            .Where(n => (string?)n.Attribute(x + "Name") == "EquipmentCommandGrid"));
        Assert.Equal("Visible",
            (string?)grid.Attribute("ScrollViewer.VerticalScrollBarVisibility"));
        Assert.Equal("Pixel",
            (string?)grid.Attribute("VirtualizingPanel.ScrollUnit"));
        Assert.Equal("True",
            (string?)grid.Attribute("VirtualizingPanel.IsVirtualizing"));
        Assert.Equal("False",
            (string?)grid.Attribute("ScrollViewer.IsDeferredScrollingEnabled"));
        Assert.Equal("0", (string?)grid.Parent?.Attribute("MinHeight"));
        Assert.Equal("True", (string?)grid.Parent?.Attribute("ClipToBounds"));
    }

    [Fact]
    public void DockGeometryRecomputesAgainstActualShellNotInventedMinimum()
    {
        var source = Read("MainWindow.WorkbenchP1PanelGeometry.cs");
        Assert.Contains("shell.SizeChanged += P1DockHost_SizeChanged;", source);
        Assert.Contains("var measured = _persistentWorkbench?.Shell.ActualHeight ?? 0d;", source);
        Assert.Contains("availableHeight - 160d", source);
        Assert.Contains("state.DockRow.MaxHeight = upper;", source);
        Assert.DoesNotContain("Math.Max(520, _persistentWorkbench", source);
    }

    [Fact]
    public void WriteDialogIsAccessibleOnlyFromBrowserAndRequiresExplicitStaging()
    {
        var xaml = Read("MainWindow.xaml");
        Assert.Contains("x:Name=\"IedBrowserWriteButton\"", xaml);
        Assert.Contains("Click=\"IedBrowserWriteValues_Click\"", xaml);
        var editor = XDocument.Parse(Read("IedBrowserWriteWindow.xaml"));
        XNamespace wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        Assert.Contains(editor.Descendants(wpf + "DataGridCheckBoxColumn"),
            column => (string?)column.Attribute("Binding") == "{Binding Selected, Mode=TwoWay}");
        Assert.Contains(editor.Descendants(wpf + "ComboBoxItem"),
            item => (string?)item.Attribute("Tag") == "4");
        var source = Read("IedBrowserWriteWindow.xaml.cs");
        Assert.Contains("MessageBoxButton.YesNo", source);
        Assert.Contains("selected.Length > 16", source);
        Assert.Contains("if (!result.Succeeded)", source);
    }

    [Fact]
    public void TypedEncoderSupportsCtlModelAndRefusesInvalidEnumsAndFloats()
    {
        Assert.Equal(MmsDataKind.Integer,
            IedBrowserWritePlan.EncodeValue("Enum", "4", "ZIVAVRLD1/ATCC1.TapChg.ctlModel").Kind);
        Assert.Equal(4L,
            IedBrowserWritePlan.EncodeValue("Enum", "4", "ZIVAVRLD1/ATCC1.TapChg.ctlModel").Value);
        Assert.Throws<FormatException>(() =>
            IedBrowserWritePlan.EncodeValue("Enum", "7", "ZIVAVRLD1/ATCC1.TapChg.ctlModel"));
        Assert.Throws<FormatException>(() =>
            IedBrowserWritePlan.EncodeValue("FLOAT32", "NaN", "LD1/ATCC1.f"));
        Assert.Equal(MmsDataKind.Boolean,
            IedBrowserWritePlan.EncodeValue("BOOLEAN", "true", "LD1/ATCC1.b").Kind);
        Assert.False(IedBrowserWritePlan.IsSupported("Quality"));
        Assert.Empty(IedBrowserWritePlan.Build(null, null));
    }

    [Fact]
    public void WriteCannotBypassLiveTypeOrControlFlightAdmission()
    {
        var native = Read("Services/NativeIec61850Client.BrowserWrite.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.BrowserWrite.cs");
        Assert.Contains("MmsClientSession", Read("Services/NativeIec61850Client.cs"));
        Assert.Contains("_session.WriteSingleVariableAsync(target, desired", native);
        Assert.Contains("ReadSingleVariableAsync(target, cancellationToken)", native);
        Assert.Contains("No automatic retry", native);
        Assert.Contains("ReferenceEquals(session.Device.LiveDiscoveryModel, model)", runtime);
        Assert.Contains("Iec61850ControlFlightAdmission.TryEnter(", runtime);
        Assert.Contains("Iec61850ControlFlightAdmission.Quarantine(", runtime);
    }

    private static string Read(string path)
    {
        DirectoryInfo? folder = new(AppContext.BaseDirectory);
        while (folder != null)
        {
            var full = Path.Combine(folder.FullName, path);
            if (File.Exists(full)) return File.ReadAllText(full);
            folder = folder.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
