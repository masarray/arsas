using System.Xml.Linq;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class ControlPopupCompactUxTests
{
    private static string Source(string name)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var file = Path.Combine(directory.FullName, name);
            if (File.Exists(file)) return File.ReadAllText(file);
            directory = directory.Parent;
        }
        throw new FileNotFoundException(name);
    }

    [Fact]
    public void CompactPopup_UsesArsasTypographyBoundedScrollableContentAndPinnedActionRow()
    {
        var xml = XDocument.Parse(Source("ControlCommandWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        Assert.Equal("565", xml.Root!.Attribute("Width")?.Value);
        Assert.Equal("{StaticResource AppFontFamily}", xml.Root.Attribute("FontFamily")?.Value);
        var scroll = xml.Descendants(p + "ScrollViewer").Single();
        Assert.Equal("2", scroll.Attribute("Grid.Row")?.Value);
        Assert.Equal("Auto", scroll.Attribute("VerticalScrollBarVisibility")?.Value);
        var send = xml.Descendants(p + "Button").Single(b => (string?)b.Attribute(x + "Name") == "SendCommandButton");
        Assert.Equal("4", send.Attribute("Grid.Column")?.Value is null
            ? send.Parent?.Attribute("Grid.Row")?.Value : send.Parent?.Attribute("Grid.Row")?.Value);
        Assert.Equal("{Binding CanSend}", send.Attribute("IsEnabled")?.Value);
        var footer = send.Parent!;
        Assert.Equal("4", footer.Attribute("Grid.Row")?.Value);
        Assert.Contains(footer.Elements(p + "Button"), b => b.Attribute("Content")?.Value == "Close" &&
            b.Attribute("IsEnabled")?.Value == "{Binding CanClose}");
        Assert.Contains(xml.Descendants(p + "Border"), n => (string?)n.Attribute(x + "Name") == "LiveCommandWarning");
        Assert.Contains(xml.Descendants(p + "Border"), n => (string?)n.Attribute(x + "Name") == "ResultPanel" &&
            n.Attribute("Grid.Row")?.Value == "3");
    }

    [Fact]
    public void AdvancedParameters_AreCollapsedButBoundToNativeRequestAndResponse()
    {
        var xml = XDocument.Parse(Source("ControlCommandWindow.xaml"));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var advanced = xml.Descendants(p + "Expander").Single(e =>
            ((string?)e.Attribute("Header"))?.StartsWith("Advanced", StringComparison.Ordinal) == true);
        var markup = advanced.ToString();
        Assert.Contains("SelectedOriginCategory", markup);
        Assert.Contains("OriginatorId", markup);
        Assert.Contains("ControlNumberText", markup);
        Assert.Contains("TerminationText", markup);
        Assert.Contains("ControlEvidenceText", markup);
        var code = Source("ControlCommandWindow.xaml.cs");
        Assert.Contains("Originator = OriginatorId.Trim()", code);
        Assert.Contains("OriginCategory = SelectedOriginCategory", code);
        Assert.Contains("ControlNumberText = result.ControlNumber", code);
        Assert.Contains("result.CommandTerminationReceived", code);
        Assert.Contains("BuildCommandResultText(result)", code);
    }

    [Fact]
    public void SinglePrimaryAction_PreservesNativeSboSequenceAndDoesNotFakeSelectCancel()
    {
        var markup = Source("ControlCommandWindow.xaml");
        var source = Source("ControlCommandWindow.xaml.cs");
        Assert.Contains("Native: Select → Operate", source);
        Assert.Contains("Select → Operate", source);
        Assert.Contains("new Iec61850ControlCommandRequest", source);
        Assert.Contains("_runtime.ExecuteControlAsync(", source);
        Assert.Contains("MessageBoxResult.Yes", source);
        Assert.DoesNotContain("SelectAsync(", source);
        Assert.DoesNotContain("CancelAsync(", source);
        Assert.Contains("IsBusy", source);
        Assert.Contains("e.Cancel = true;", source);
        Assert.Contains("IsEnabled=\"{Binding CanClose}\"", markup);
    }

    [Theory]
    [InlineData("ARSAS", true)]
    [InlineData("Station-Engineer_1", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("é", false)]
    [InlineData("line\nfeed", false)]
    public void OriginIdentifier_IsPrintableAsciiOnly(string value, bool expected)
        => Assert.Equal(expected, ControlCommandWindow.IsValidOriginator(value));
}
