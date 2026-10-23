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
