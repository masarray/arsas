using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Regression contracts for the dedicated CF ctlModel editor. The native
/// semantic gate is tested separately by the control service suite; these
/// tests defend operator affordances and reconnect safety at the WPF seam.
/// </summary>
public sealed class ControlModelWriteAcceptanceTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    private static string Source(string path)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }

    [Fact]
    public void SingleCfEditorHasOneInlineDropdownAndPersistentMaterialActionFooter()
    {
        var window = XDocument.Parse(Source("ControlModelWriteWindow.xaml"));
        var root = Assert.IsType<XElement>(window.Root);
        Assert.Equal(P + "Window", root.Name);
        Assert.Equal("Write Data to IED", (string?)root.Attribute("Title"));
        Assert.Equal("CanResize", (string?)root.Attribute("ResizeMode"));
        Assert.Equal("{StaticResource AppFontFamily}", (string?)root.Attribute("FontFamily"));
        Assert.Equal("450", (string?)root.Attribute("MaxHeight"));

        var picker = Assert.Single(window.Descendants(P + "ComboBox"));
        Assert.Equal("ModelPicker", (string?)picker.Attribute(XName.Get("Name",
            "http://schemas.microsoft.com/winfx/2006/xaml")));
        Assert.Equal("{Binding Choices}", (string?)picker.Attribute("ItemsSource"));
        Assert.Equal("False", (string?)picker.Attribute("IsEnabled"));
        Assert.Contains(window.Descendants(P + "TextBlock"), x => (string?)x.Attribute("Text") == "Name");
        Assert.Contains(window.Descendants(P + "TextBlock"), x => (string?)x.Attribute("Text") == "Value");
        Assert.Contains(window.Descendants(P + "TextBlock"), x => (string?)x.Attribute("Text") == "ctlModel [CF]");
        Assert.All(window.Descendants(P + "TextBox"), x =>
            Assert.Equal("True", (string?)x.Attribute("IsReadOnly")));

        var button = Assert.Single(window.Descendants(P + "Button").Where(x =>
            (string?)x.Attribute("Click") == "Write_Click"));
        Assert.Equal("94", (string?)button.Attribute("Width"));
        Assert.Equal("3", (string?)button.Parent?.Attribute("Grid.Row"));
        Assert.Contains(button.Descendants(P + "TextBlock"), x =>
            (string?)x.Attribute("Text") == "Write");
        Assert.Contains(button.Descendants(P + "Path"), x =>
            (string?)x.Attribute("Data") == "{StaticResource MaterialEditIcon}");
        Assert.Contains(window.Descendants(P + "Path"), x =>
            (string?)x.Attribute("Data") == "{StaticResource MaterialRefreshIcon}");
        Assert.Contains(window.Descendants(P + "Path"), x =>
            (string?)x.Attribute("Data") == "{StaticResource MaterialCloseIcon}");

        var scrolling = Assert.Single(window.Descendants(P + "ScrollViewer"));
        Assert.Equal("2", (string?)scrolling.Attribute("Grid.Row"));
        Assert.Equal("Auto", (string?)scrolling.Attribute("VerticalScrollBarVisibility"));
        var resources = Source("App.xaml");
        Assert.Contains("x:Key=\"MaterialEditIcon\"", resources);
        Assert.Contains("x:Key=\"MaterialRefreshIcon\"", resources);
        Assert.Contains("x:Key=\"MaterialCloseIcon\"", resources);
    }

    [Fact]
    public void ControlModelWriteUsesOneExplicitClick_LiveAssociationAndReadbackToast()
    {
        var source = Source("ControlModelWriteWindow.xaml.cs");
        var requireRead = source.IndexOf("_inspectedAssociation = association", StringComparison.Ordinal);
        var requireModel = source.IndexOf("_inspectedLiveModel = model", StringComparison.Ordinal);
        var dispatchGuard = source.IndexOf("if (!InspectionIsCurrent", StringComparison.Ordinal);
        var dispatch = source.IndexOf("ConfigureExactModelAsync(expected, selected.Model", StringComparison.Ordinal);
        Assert.True(requireRead >= 0 && requireModel >= 0);
        Assert.True(dispatchGuard > requireModel);
        Assert.True(dispatch > dispatchGuard);
        Assert.Contains("ReferenceEquals(_device.LiveDiscoveryModel, _inspectedLiveModel)", source);
        Assert.Contains("_device.LiveValues.Association == _inspectedAssociation", source);
        Assert.DoesNotContain("MessageBox.Show(", source);
        Assert.Contains("await ReadModelAsync();", source);
        Assert.Contains("VerifiedWriteOccurred = true;", source);
        Assert.Contains("ResultBanner.Background", source);
        Assert.DoesNotContain("OperateAsync(", source);
        Assert.DoesNotContain("WriteDataAttributeAsync(", source);

        var xaml = Source("ControlModelWriteWindow.xaml");
        Assert.Contains("x:Name=\"ResultBanner\"", xaml);
        Assert.DoesNotContain("x:Name=\"FailureDetails\"", xaml);
        var bridge = Source("Services/Iec61850ControlModelWriteDispatcher.cs");
        Assert.Contains("runtime.ConfigureControlModelAsync", bridge);
        Assert.DoesNotContain("WriteDataAttributeAsync(", bridge);
    }

}
