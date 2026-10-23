using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class TypedWriteEditorUxRegressionTests
{
    [Fact]
    public void ToolbarWrite_IsStableAndHasRequestedMaterialPencilVector()
    {
        var xaml = Source("MainWindow.xaml");
        Assert.Contains("x:Name=\"IedBrowserWriteButton\"", xaml);
        Assert.Contains("Text=\"Write\"", xaml);
        Assert.Contains("M167,840", xaml);
        var route = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        Assert.DoesNotContain("IedBrowserWriteButton.Content =", route);
        Assert.DoesNotContain("Write ctlModel…\" :", route);
    }

    [Theory]
    [InlineData("BrowserDataWriteWindow.xaml")]
    [InlineData("BrowserBatchWriteWindow.xaml")]
    public void MaterialPencilInWriteFooter_RetainsFixedLabelAndWidth(string file)
    {
        var doc = XDocument.Parse(Source(file));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var button = Assert.Single(doc.Descendants(presentation + "Button").Where(
            element => (string?)element.Attribute(x + "Name") == "WriteButton"));
        Assert.Equal("Write", (string?)button.Attribute("Content"));
        Assert.Equal("94", (string?)button.Attribute("Width"));
        Assert.Equal("{StaticResource MaterialEditIcon}",
            (string?)Assert.Single(button.Descendants(presentation + "Path")).Attribute("Data"));
        Assert.Contains(button.Descendants(presentation + "TextBlock"),
            child => (string?)child.Attribute("Text") == "{Binding}");
        var footer = Assert.Single(doc.Descendants(presentation + "Grid").Where(grid =>
            grid.Descendants(presentation + "Button").Contains(button) &&
            (string?)grid.Attribute("Grid.Row") is "2" or "3"));
        Assert.NotNull(footer);
    }

    [Fact]
    public void SingleDaEditor_HasInlineTypedDropdownAndNoEmptyBooleanInput()
    {
        var xaml = Source("BrowserDataWriteWindow.xaml");
        XDocument.Parse(xaml);
        Assert.Contains("x:Name=\"BoolPicker\"", xaml);
        Assert.Contains("Content=\"true\"", xaml);
        Assert.Contains("Content=\"false\"", xaml);
        Assert.Contains("x:Name=\"NewValueBox\"", xaml);
        Assert.Contains("Text=\"Name\"", xaml);
        Assert.Contains("Text=\"Value\"", xaml);
        Assert.Contains("Content=\"Write\"", xaml);
        Assert.DoesNotContain("Wire preview", xaml);
        var code = Source("BrowserDataWriteWindow.xaml.cs");
        Assert.Contains("NewValueBox.Text = p.CurrentText", code);
        Assert.Contains("BoolPicker.SelectedIndex", code);
        Assert.Contains("SelectedInput()", code);
        Assert.Contains("IedBrowserTypedWriteAdmission.RejectReason", code);
        Assert.Contains("WriteBrowserDataAttributesAsync", code);
    }

    [Fact]
    public void BatchEditor_BooleanPickerIsLiveGvaDriven_AndWriteHasNoAutomaticRetry()
    {
        var xaml = Source("BrowserBatchWriteWindow.xaml");
        XDocument.Parse(xaml);
        Assert.Contains("BooleanEditorVisibility", xaml);
        Assert.Contains("IsEnabled=\"{Binding CanEdit}\"", xaml);
        Assert.Contains("TextEditorVisibility", xaml);
        Assert.Contains("DisplayName", xaml);
        Assert.Contains("Content=\"Write\"", xaml);
        Assert.DoesNotContain("Write selected…", xaml);
        var code = Source("BrowserBatchWriteWindow.xaml.cs");
        Assert.Contains("ApplyLiveEditor(preview.Type?.MmsType == \"boolean\"", code);
        Assert.Contains("if (!HasEdited", code);
        Assert.Contains("IedBrowserTypedWriteBatch.Freeze", code);
        Assert.Contains("WriteBrowserDataAttributesAsync", code);
    }

    [Fact]
    public void BrowserWrite_ProvidesSingleAndMultiLeafWithoutSecondModalConfirmation()
    {
        var main = Source("MainWindow.xaml");
        XDocument.Parse(main);
        Assert.Contains("x:Name=\"IedBrowserWriteDoButton\"", main);
        Assert.Contains("Click=\"IedBrowserWriteDo_Click\"", main);
        var route = Source("MainWindow.IedBrowser.TypedDataWrite.cs");
        Assert.Contains("BrowserWritableSelectedDo(allowLeafSelection: true)", route);
        Assert.Contains("dialog.VerifiedWriteOccurred", route);
        Assert.Contains("new BrowserBatchWriteWindow(", route);
        foreach (var file in new[]
        {
            "ControlModelWriteWindow.xaml.cs",
            "BrowserDataWriteWindow.xaml.cs",
            "BrowserBatchWriteWindow.xaml.cs"
        })
        {
            var code = Source(file);
            Assert.DoesNotContain("MessageBox.Show(", code);
            Assert.Contains("Write_Click(", code);
        }
        Assert.Contains("await ReadModelAsync();", Source("ControlModelWriteWindow.xaml.cs"));
        Assert.Contains("PreviewBrowserDataAttributeWriteAsync(", Source("BrowserDataWriteWindow.xaml.cs"));
        var batch = Source("BrowserBatchWriteWindow.xaml.cs");
        Assert.Contains("await PrimeLiveEditorsAsync();", batch);
        Assert.Contains("row.ResetToReadback(actual.CurrentText);", batch);
        Assert.Contains("var readbackMismatch = false;", batch);
        Assert.Contains("MmsDataCodec.Encode(steps[i].ProposedValue)", batch);
        Assert.Contains("baseline.GvaSignature, preview.GvaSignature", batch);
        Assert.Contains("MmsDataCodec.Encode(baseline.OldValue)", batch);
        Assert.Contains("WriteBrowserDataAttributesAsync(", batch);
        Assert.Contains("TryFreezePlan(selected,", batch);
        Assert.Contains("No automatic retry", batch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EmbeddedInter_AlsoUsedForReferenceAndCodeTypography()
    {
        var xaml = Source("App.xaml");
        Assert.Contains("<FontFamily x:Key=\"AppFontFamily\">./Assets/Fonts/#Inter</FontFamily>", xaml);
        Assert.Contains("<FontFamily x:Key=\"AppCodeFontFamily\">./Assets/Fonts/#Inter</FontFamily>", xaml);
        Assert.DoesNotContain("Cascadia Mono, Consolas</FontFamily>", xaml);
        foreach (var file in new[]
        {
            "ControlModelWriteWindow.xaml",
            "BrowserDataWriteWindow.xaml",
            "BrowserBatchWriteWindow.xaml"
        })
            XDocument.Parse(Source(file));
    }

    private static string Source(string filename)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var p = Path.Combine(dir.FullName, filename);
            if (File.Exists(p)) return File.ReadAllText(p);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(filename);
    }
}
