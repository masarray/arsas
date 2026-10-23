using System.Xml.Linq;
using AR.Iec61850.Mms;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserUnifiedWriteUxTests
{
    [Fact]
    public void WriteRow_RequiresLiveReadAndExplicitUserChange()
    {
        var row = new IedBrowserWriteRow("LD0/LLN0.Pos.stVal", "CF", "BOOLEAN");
        Assert.Equal("stVal", row.ShortName);
        Assert.True(row.IsBoolean);
        Assert.False(row.HasVerifiedRead);
        Assert.False(row.HasEdited);
        row.ApplyLiveRead(MmsDataValue.Boolean(false));
        Assert.Equal("false", row.RequestedValue);
        Assert.Equal("false", row.Current);
        Assert.False(row.HasEdited);
        Assert.True(row.HasVerifiedRead);
        row.RequestedValue = "true";
        Assert.True(row.HasEdited);
        row.ExpireLiveRead();
        Assert.False(row.HasVerifiedRead);
        Assert.Null(row.LiveBeforeValue);
    }

    [Fact]
    public void CtlModel_IsStillEnumeratedNotFreeText()
    {
        var row = new IedBrowserWriteRow("LD0/CSWI1.Pos.ctlModel", "CF", "ENUM");
        Assert.True(row.IsControlModel);
        row.ApplyLiveRead(MmsDataValue.Integer(2));
        Assert.Equal("2", row.RequestedValue);
        Assert.Equal(System.Windows.Visibility.Visible, row.CtlModelEditorVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, row.TextEditorVisibility);
    }

    [Fact]
    public void UnverifiedDiscreteEnumRemainsVisibleButCannotBecomeEditableOrReachWire()
    {
        var row = new IedBrowserWriteRow("LD0/ATCC1.TapChg.oprVal", "CF", "ENUMERATED");
        row.ApplyLiveRead(MmsDataValue.Integer(2));
        Assert.True(row.HasVerifiedRead);
        Assert.True(row.IsUnverifiedEnum);
        Assert.Equal(System.Windows.Visibility.Collapsed, row.TextEditorVisibility);
        Assert.Equal(System.Windows.Visibility.Collapsed, row.BooleanEditorVisibility);
        Assert.Equal(System.Windows.Visibility.Visible, row.PlaceholderVisibility);
        Assert.Contains("unverified", row.PlaceholderText);
        Assert.Throws<NotSupportedException>(() =>
            IedBrowserWritePlan.EncodeValue(row.DataType, "2", row.Reference));

        // Native final boundary also rejects forged Control/ctlModel paths.
        var native = Read("Services/NativeIec61850Client.BrowserWrite.cs");
        Assert.Contains("generic MMS Write forbidden.", native);
        Assert.Contains("segment.Equals(\"ctlModel\"", native);
        var source = Read("Models/IedBrowserWritePlan.cs");
        Assert.Contains(".Where(target => !target.Reference.EndsWith(\".ctlModel\"", source);
    }

    [Fact]
    public void UnifiedWindow_DoesNotRequestWriteDuringReadOrOpen()
    {
        var src = Read("IedBrowserWriteWindow.xaml.cs");
        var inspect = src.Split("private async Task ReadRowsAsync(")[1]
            .Split("private async void Write_Click(")[0];
        Assert.Contains("ReadBrowserConfigurationAsync(", inspect);
        Assert.DoesNotContain("WriteBrowserConfigurationAsync(", inspect);
        Assert.Contains("row.ApplyLiveRead(observation.Value)", inspect);
        Assert.Contains("Take(24)", src);
        Assert.Contains("!row.HasVerifiedRead || !row.HasEdited", src);
        Assert.Contains("MmsDataCodec.Encode(row.LiveBeforeValue!)", src);
        Assert.Contains("MessageBoxResult.Yes", src);
        Assert.Contains("row.ExpireLiveRead()", src);
    }

    [Fact]
    public void UnifiedWindow_HasStableMaterialWriteAndTypeAwareGrid()
    {
        var x = XDocument.Parse(Read("IedBrowserWriteWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace xNs = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Contains(x.Descendants(p + "DataGridTextColumn"), t =>
            (string?)t.Attribute("Binding") == "{Binding ShortName}");
        Assert.Contains(x.Descendants(p + "ComboBox"), t =>
            (string?)t.Attribute(xNs + "Name") == "BooleanPicker");
        Assert.Contains(x.Descendants(p + "ComboBox"), t =>
            (string?)t.Attribute(xNs + "Name") == "CtlPicker");
        var write = x.Descendants(p + "Button").Single(t =>
            (string?)t.Attribute(xNs + "Name") == "WriteButton");
        Assert.True(write.Descendants(p + "TextBlock").Any(t =>
            (string?)t.Attribute("Text") == "Write"));
        Assert.Contains("Data=\"{StaticResource MaterialEditIcon}\"", Read("IedBrowserWriteWindow.xaml"));
        Assert.Equal("34", (string?)x.Descendants(p + "DataGrid").Single().Attribute("RowHeight"));
    }

    private static string Read(string file)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, file);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            dir = dir.Parent;
        }
        throw new FileNotFoundException(file);
    }
}
