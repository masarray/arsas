using System.Xml.Linq;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class IedBrowserLiveControlWriteFatRegressionTests
{
    [Fact]
    public void ControlPopup_ReadOnlyDisplayBindings_AreOneWayNotWpfTwoWay()
    {
        var root = XDocument.Parse(Source("ControlCommandWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        foreach (var binding in new[] { "ControlNumberText", "TerminationText" })
        {
            var textBox = root.Descendants(p + "TextBox").Single(x =>
                ((string?)x.Attribute("Text"))?.Contains(binding, StringComparison.Ordinal) == true);
            Assert.Contains("Mode=OneWay", textBox.Attribute("Text")!.Value);
            Assert.Equal("True", (string?)textBox.Attribute("IsReadOnly"));
        }
    }

    [Fact]
    public void ControlLeaf_OnlyExactLiveCoCtlValMayBorrowOwningDoControl()
    {
        var src = Source("MainWindow.IedBrowser.ContextControl.cs");
        Assert.Contains("FunctionalConstraint: \"CO\"", src);
        Assert.Contains("LastOrDefault() == \"ctlVal\"", src);
        Assert.Contains("source.Attributes.Any", src);
        Assert.Contains("BrowserSelectedLiveControl()", src);
        Assert.Contains("new ControlCommandWindow(_runtime, device, control.Value.Signal)", src);
        Assert.Contains("signal.ControlSemanticVerified", src);
        Assert.Contains("IedBrowserControlEligibility.Check", src);
    }

    [Fact]
    public void CtlModelCfLeaf_UsesExistingGuardedWriteEditor_NotGenericMmsOrOperate()
    {
        var ctx = Source("MainWindow.IedBrowser.ContextControl.cs");
        var write = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        var safety = Source("Models/IedBrowserTypedWriteAdmission.cs");
        Assert.Contains("BrowserSelectedControlModelSignal()", ctx);
        Assert.Contains("FunctionalConstraint: \"CF\"", ctx);
        Assert.Contains("source.Attributes.Any", ctx);
        Assert.Contains("new ControlModelWriteWindow(_runtime, device, modelSignal)", write);
        Assert.Contains("ctlModel requires the existing guarded ConfigureControlModel workflow", safety);
        Assert.Contains("selectedDo is not null ? \"Write DAs…\" : \"Write DA…\"", write);
    }

    [Fact]
    public void Monitoring_StillAllowsExplicitTypedWriteOnSerializedLiveAssociation()
    {
        var ui = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        var runtime = Source("Services/Iec61850MonitorRuntime.cs");
        var batch = Source("BrowserBatchWriteWindow.xaml.cs");
        Assert.DoesNotContain("IsMonitoring: false, IsBrowserReading: false", ui);
        Assert.DoesNotContain("device.IsDemo || device.IsMonitoring || device.IsBusy", runtime);
        Assert.DoesNotContain("device.IsMonitoring || model is null", runtime);
        Assert.Contains("ReferenceEquals(session.Device, device)", runtime);
        Assert.Contains("ControlFlightQuarantined", runtime);
        Assert.Contains("IedBrowserTypedWriteBatch.Freeze(confirmedPlan)", runtime);
        Assert.DoesNotContain("!_device.IsMonitoring", batch);
    }

    [Fact]
    public void StatusStrip_FixedHeight_NoWrapAndNoHistoryExpansion()
    {
        var root = XDocument.Parse(Source("MainWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var status = root.Descendants(p + "Border").Single(x =>
            ((string?)x.Attribute("Grid.Row")) == "2" &&
            x.Descendants(p + "TextBlock").Any(t =>
                ((string?)t.Attribute("Text")) == "{Binding LastStatusText}"));
        Assert.Equal("34", (string?)status.Attribute("Height"));
        Assert.Equal("34", (string?)status.Attribute("MaxHeight"));
        var text = status.Descendants(p + "TextBlock").Single(x =>
            ((string?)x.Attribute("Text")) == "{Binding LastStatusText}");
        Assert.Equal("NoWrap", (string?)text.Attribute("TextWrapping"));
        Assert.Equal("CharacterEllipsis", (string?)text.Attribute("TextTrimming"));
        Assert.Equal("19", (string?)text.Attribute("MaxHeight"));
        Assert.Equal("ERROR • UI • System.InvalidOperationException: read-only",
            MainWindow.FormatSingleLineStatus(
                "ERROR • UI • System.InvalidOperationException: read-only\n   at WPF stack"));
        Assert.True(MainWindow.FormatSingleLineStatus(new string('x', 400)).Length <= 180);
    }

    private static string Source(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var path = Path.Combine(dir.FullName, relative);
            if (File.Exists(path)) return File.ReadAllText(path);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
