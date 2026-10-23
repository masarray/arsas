using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class IedBrowserBatchWriteEditorTests
{
    [Fact]
    public void BatchRows_StartUnselected_AndRequireExplicitNewValueEdit()
    {
        var row = new BrowserBatchWriteRow(
            MmsObjectReference.Parse("LD0/LLN0.Mod.sboTimeout", "CF"),
            "LD0/LLN0.Mod.sboTimeout", "CF", true, "Inspect first");
        Assert.False(row.Selected);
        Assert.False(row.HasEdited);
        Assert.True(row.Eligible);
        var changed = new List<string?>();
        row.PropertyChanged += (_, args) => changed.Add(args.PropertyName);

        row.Selected = true;
        Assert.False(row.HasEdited);
        row.NewValue = "250";
        Assert.True(row.HasEdited);
        Assert.Contains(nameof(BrowserBatchWriteRow.Selected), changed);
        Assert.Contains(nameof(BrowserBatchWriteRow.NewValue), changed);
    }

    [Fact]
    public void BatchWindow_RealXamlKeepsScrolledRowsAndPinnedFooter()
    {
        var root = XDocument.Parse(File.ReadAllText(Find("BrowserBatchWriteWindow.xaml")));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var grid = root.Descendants(p + "DataGrid").Single(d =>
            (string?)d.Attribute(x + "Name") == "RowsGrid");
        Assert.Equal("2", grid.Attribute("Grid.Row")?.Value);
        Assert.Equal("Auto", grid.Attribute("ScrollViewer.VerticalScrollBarVisibility")?.Value);
        Assert.Equal("True", grid.Attribute("VirtualizingPanel.IsVirtualizing")?.Value);

        var footer = root.Descendants(p + "Grid").Single(d =>
            d.Attribute("Grid.Row")?.Value == "3" && d.Descendants(p + "Button")
                .Any(b => (string?)b.Attribute(x + "Name") == "WriteButton"));
        Assert.Contains(footer.Descendants(p + "Button"), b =>
            (string?)b.Attribute(x + "Name") == "InspectButton");
        Assert.Contains(footer.Descendants(p + "Button"), b =>
            (string?)b.Attribute(x + "Name") == "WriteButton" &&
            (string?)b.Attribute("IsEnabled") == "False");

        var selection = root.Descendants(p + "CheckBox").Single();
        Assert.Equal("{Binding Eligible}", selection.Attribute("IsEnabled")?.Value);
        Assert.Contains(root.Descendants(p + "TextBox"), input =>
            (string?)input.Attribute("Text") ==
            "{Binding NewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}");
    }

    [Fact]
    public void DoEditor_UsesSameSessionRuntimeAndNeverDispatchesOnFocusOrInspect()
    {
        var source = File.ReadAllText(Find("BrowserBatchWriteWindow.xaml.cs"));
        var click = File.ReadAllText(Find("MainWindow.IedBrowser.TypedDataWrite.cs"));
        Assert.Contains("BrowserWritableSelectedDo()", click);
        Assert.Contains("new BrowserBatchWriteWindow(_runtime, device, model, owner)", click);
        Assert.Contains("IedBrowserTypedWriteBatch.Freeze(pending)", source);
        Assert.Contains("WriteBrowserDataAttributesAsync(", source);
        Assert.Contains("PreviewBrowserDataAttributeWriteAsync(", source);
        Assert.DoesNotContain("MessageBox.Show(", source);
        Assert.Contains("TryFreezePlan(selected,", source);
        Assert.Contains("baseline.GvaSignature, preview.GvaSignature", source);
        Assert.Contains("ReferenceEquals(_device.LiveDiscoveryModel, _model)", source);
        Assert.DoesNotContain("WriteSingleVariableAsync(", source);
        Assert.DoesNotContain("OperateAsync(", source);

        var inspect = source.Split("private async void Inspect_Click(")[1]
            .Split("private static bool TryFreezePlan(")[0];
        Assert.DoesNotContain("WriteBrowserDataAttributesAsync(", inspect);
    }

    [Fact]
    public void StatusDaSelectionRevokesParentWriteAndControlAuthority()
    {
        var write = File.ReadAllText(Find("MainWindow.IedBrowser.TypedDataWrite.cs"));
        var control = File.ReadAllText(Find("MainWindow.IedBrowser.ContextControl.cs"));
        const string dataLeaf = "Kind: \"Data\", IsDataObject: false";
        Assert.Contains(dataLeaf, write);
        Assert.Contains(dataLeaf, control);
        Assert.Contains("RefreshIedBrowserControlButton();", write);
        Assert.Contains("var ctlModel = BrowserSelectedControlModelSignal()", write);
        Assert.DoesNotContain("IedBrowserWriteButton.Content =", write);
    }

    private static string Find(string relative)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Source file not found: " + relative);
    }
}
