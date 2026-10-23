using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Field-reported alignment regressions on the native WPF C264 workstation UI.
/// Assertions inspect scoped style contracts without launching a relay or Npcap.
/// </summary>
public sealed class C264FieldAlignmentRegressionTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";

    [Fact]
    public void IedIpAddress_HasFourAlignedOctets_SearchableHistory_AndNoDuplicateStorage()
    {
        var document = XDocument.Load(Find("IpConnectWizardWindow.xaml"));
        var boxes = document.Descendants(Wpf + "TextBox")
            .Where(e => ((string?)e.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")))?.StartsWith("Octet", StringComparison.Ordinal) == true)
            .ToArray();

        Assert.Equal(4, boxes.Length);
        Assert.All(boxes, box => Assert.Equal("{StaticResource SmartIpOctet}", (string?)box.Attribute("Style")));
        Assert.Contains(document.Descendants(Wpf + "Popup"),
            popup => (string?)popup.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml")) == "HistoryPopup");
        var source = File.ReadAllText(Find("IpConnectWizardWindow.xaml.cs"));
        Assert.Contains("UserPreferenceStore.LoadRecentEndpointDetails()", source);
        Assert.Contains("Octet_Pasting", source);
        Assert.Contains("Octet_PreviewKeyDown", source);
        Assert.Contains("CommitHistorySelection()", source);
        Assert.Contains("RelayIpAddress = submittedAddress;", source);
        Assert.DoesNotContain("File.WriteAllText", source);
    }

    [Fact]
    public void FaultRecordSelectAllAndRows_CenterInSameInsetFreeColumn_WithoutChangingOtherColumns()
    {
        var document = XDocument.Load(Find("FaultRecordWindow.xaml"));
        var checkboxColumn = document.Descendants(Wpf + "DataGridTemplateColumn")
            .Single(x => (string?)x.Attribute("Header") == "Get");

        Assert.Equal("48", (string?)checkboxColumn.Attribute("Width"));
        var header = checkboxColumn.Element(Wpf + "DataGridTemplateColumn.HeaderStyle")!
            .Element(Wpf + "Style")!;
        var cell = checkboxColumn.Element(Wpf + "DataGridTemplateColumn.CellStyle")!
            .Element(Wpf + "Style")!;
        Assert.Equal("{StaticResource {x:Type DataGridColumnHeader}}", (string?)header.Attribute("BasedOn"));
        Assert.Equal("{StaticResource {x:Type DataGridCell}}", (string?)cell.Attribute("BasedOn"));
        AssertSetter(header, "Padding", "0");
        AssertSetter(header, "HorizontalContentAlignment", "Center");
        AssertSetter(cell, "Padding", "0");
        AssertSetter(cell, "HorizontalContentAlignment", "Stretch");

        var rowCheckbox = checkboxColumn.Descendants(Wpf + "CheckBox").Single();
        Assert.Equal("Center", (string?)rowCheckbox.Attribute("HorizontalAlignment"));
        Assert.Equal("Center", (string?)rowCheckbox.Attribute("VerticalAlignment"));
        Assert.Contains("IsSelected", (string?)rowCheckbox.Attribute("IsChecked"));
        Assert.Contains("CanSelectForDownload", (string?)rowCheckbox.Attribute("IsEnabled"));

        // Text and status columns keep the existing 9px global typography inset.
        Assert.Contains(document.Descendants(Wpf + "Style"),
            s => (string?)s.Attribute("TargetType") == "DataGridColumnHeader"
                && s.Elements(Wpf + "Setter").Any(p =>
                    (string?)p.Attribute("Property") == "Padding" &&
                    (string?)p.Attribute("Value") == "9,0"));
        Assert.Contains(document.Descendants(Wpf + "Style"),
            s => (string?)s.Attribute("TargetType") == "DataGridCell"
                && s.Elements(Wpf + "Setter").Any(p =>
                    (string?)p.Attribute("Property") == "Padding" &&
                    (string?)p.Attribute("Value") == "9,0"));

        var code = File.ReadAllText(Find("FaultRecordWindow.HeaderSelection.cs"));
        Assert.Contains("IsThreeState = true", code);
        Assert.Contains("HorizontalAlignment = HorizontalAlignment.Center", code);
        Assert.Contains("column.Header = headerCheckBox", code);
        Assert.Contains("FaultRecordHeaderSelectionCheckBox_Click", code);
    }

    private static void AssertSetter(XElement style, string name, string value)
        => Assert.Contains(style.Elements(Wpf + "Setter"),
            setter => (string?)setter.Attribute("Property") == name &&
                      (string?)setter.Attribute("Value") == value);

    private static string Find(string path)
    {
        DirectoryInfo? d = new(AppContext.BaseDirectory);
        while (d is not null)
        {
            var file = Path.Combine(d.FullName, path);
            if (File.Exists(file)) return file;
            d = d.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
