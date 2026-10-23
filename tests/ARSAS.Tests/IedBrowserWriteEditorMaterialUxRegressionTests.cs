using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class IedBrowserWriteEditorMaterialUxRegressionTests
{
    private static readonly XNamespace P =
        "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X =
        "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void SingleDa_HasVisibleBooleanPickerAndNoBlankUnqualifiedFreeText()
    {
        var doc = XDocument.Parse(Read("BrowserDataWriteWindow.xaml"));
        Assert.Contains(doc.Descendants(P + "ComboBox"), element =>
            (string?)element.Attribute(X + "Name") == "BooleanPicker");
        Assert.Contains(doc.Descendants(P + "TextBox"), element =>
            (string?)element.Attribute(X + "Name") == "NewValueBox" &&
            (string?)element.Attribute("IsEnabled") == "False");
        Assert.Contains(doc.Descendants(P + "Button"), element =>
            (string?)element.Attribute(X + "Name") == "ReadButton");
        var source = Read("BrowserDataWriteWindow.xaml.cs");
        Assert.Contains("BooleanPicker.ItemsSource = new[] { \"false\", \"true\" }", source);
        Assert.Contains("SelectedInput()", source);
        Assert.Contains("p.OldValue.Value", source);
        Assert.Contains("MessageBoxResult.Yes", source);
        Assert.Contains("WriteBrowserDataAttributesAsync(", source);
        Assert.DoesNotContain("OperateAsync(", source);
    }

    [Fact]
    public void BatchDa_KeepsPreciseReferenceAsTooltip_ButUsesShortNamesAndTypedEditors()
    {
        var doc = XDocument.Parse(Read("BrowserBatchWriteWindow.xaml"));
        var rows = doc.Descendants(P + "DataGrid").Single(x =>
            (string?)x.Attribute(X + "Name") == "RowsGrid");
        Assert.Equal("34", (string?)rows.Attribute("RowHeight"));
        Assert.Contains(doc.Descendants(P + "DataGridTextColumn"), col =>
            (string?)col.Attribute("Header") == "Data Attribute" &&
            (string?)col.Attribute("Binding") == "{Binding ShortName}");
        Assert.Contains(doc.Descendants(P + "ComboBox"), el =>
            (string?)el.Attribute("SelectedItem") ==
            "{Binding NewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" &&
            (string?)el.Attribute("IsEnabled") == "{Binding CanEdit}");
        Assert.Contains(doc.Descendants(P + "TextBox"), el =>
            (string?)el.Attribute("Text") ==
            "{Binding NewValue, Mode=TwoWay, UpdateSourceTrigger=PropertyChanged}" &&
            (string?)el.Attribute("IsEnabled") == "{Binding CanEdit}");
        Assert.Contains(doc.Descendants(P + "TextBlock"), el =>
            (string?)el.Attribute("Text") == "Read to edit");
        var code = Read("BrowserBatchWriteWindow.xaml.cs");
        Assert.Contains("Take(24)", code); // bounded initial read-only previews
        Assert.Contains("ApplyLivePreview(preview, preserveExplicitEdit: false)", code);
        Assert.Contains("IedBrowserTypedWriteBatch.Freeze(pending)", code);
        Assert.Contains("MessageBoxResult.Yes", code);
        Assert.Contains("CurrentAssociation", code);
        Assert.DoesNotContain("OperateAsync(", code);
    }

    [Fact]
    public void AllWriteButtons_StableCaptionAndMaterialPencil_WithoutSelectionTextRewrites()
    {
        foreach (var file in new[]
        {
            "BrowserDataWriteWindow.xaml", "BrowserBatchWriteWindow.xaml",
            "MainWindow.xaml", "ControlModelWriteWindow.xaml", "IedBrowserWriteWindow.xaml"
        })
        {
            var doc = XDocument.Parse(Read(file));
            var buttons = doc.Descendants(P + "Button").Where(x =>
                (string?)x.Attribute(X + "Name") is "WriteButton" or "IedBrowserWriteButton").ToArray();
            Assert.NotEmpty(buttons);
            foreach (var button in buttons)
            {
                Assert.True((string?)button.Attribute("Content") == "Write" ||
                    button.Descendants(P + "TextBlock").Any(t => (string?)t.Attribute("Text") == "Write"),
                    file + ": Write caption must never be changed by selection.");
            }
        }
        foreach (var file in new[] { "MainWindow.xaml", "BrowserDataWriteWindow.xaml", "BrowserBatchWriteWindow.xaml", "IedBrowserWriteWindow.xaml" })
            Assert.Contains("M167-120q-21 5-36.5-10.5T120-167l40-191", Read(file));
        Assert.DoesNotContain("IedBrowserWriteButton.Content =", Read("MainWindow.IedBrowser.TypedDataWrite.cs"));
    }

    [Fact]
    public void Browser_HasOneVisibleStableWrite_AndNativeFallbackForAdditionalConfigurationFCs()
    {
        var main = XDocument.Parse(Read("MainWindow.xaml"));
        var primary = main.Descendants(P + "Button").Single(b =>
            (string?)b.Attribute(X + "Name") == "IedBrowserWriteButton");
        var duplicate = main.Descendants(P + "Button").Single(b =>
            (string?)b.Attribute(X + "Name") == "IedBrowserConfigWriteButton");
        Assert.Null(primary.Attribute("Visibility"));
        Assert.Equal("Collapsed", (string?)duplicate.Attribute("Visibility"));
        var router = Read("MainWindow.IedBrowser.TypedDataWrite.cs");
        Assert.Contains("IedBrowserWriteValues_Click(sender, e)", router);
        Assert.Contains("new ControlModelWriteWindow(_runtime, device, modelSignal)", router);
        Assert.Contains("new BrowserDataWriteWindow(_runtime, device, attribute)", router);
        Assert.Contains("new BrowserBatchWriteWindow(_runtime, device, model, owner)", router);
        Assert.DoesNotContain("IedBrowserWriteButton.Content =", router);
    }

    private static string Read(string relative)
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
