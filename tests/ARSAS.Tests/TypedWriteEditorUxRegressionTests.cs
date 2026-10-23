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
    public void SingleDaBooleanRequiresRecognizedLiveState_AndNeverDefaultsAnUnknownValueToFalse()
    {
        var code = Source("BrowserDataWriteWindow.xaml.cs");
        Assert.Contains("current.Equals(\"true\", StringComparison.OrdinalIgnoreCase)", code);
        Assert.Contains("current.Equals(\"false\", StringComparison.OrdinalIgnoreCase)", code);
        Assert.Contains("BoolPicker.SelectedIndex = verifiedBoolean;", code);
        Assert.Contains("BoolPicker.IsEnabled = supported && verifiedBoolean >= 0;", code);
        Assert.Contains("if (verifiedBoolean < 0)", code);
        Assert.Contains("No verified true/false current value; no Write permitted.", code);
        Assert.DoesNotContain(
            "p.CurrentText.Equals(\"true\", StringComparison.OrdinalIgnoreCase) ? 0 : 1",
            code);
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
