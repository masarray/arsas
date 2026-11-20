using System.IO;
using System.Linq;
using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Protect the operator-accepted IED Browser section order and selection-only
/// IED identity emphasis. Pure presentation tests; no MMS I/O or control.
/// </summary>
public sealed class IedBrowserOperatorNavigationAndIdentityTests
{
    [Fact]
    public void AllSixBrowserCategories_PreserveExactOperatorOrderAndStableKeys()
    {
        var roots = IedBrowserPresentation.Roots(null, []);
        Assert.Equal(
            ["Reports", "DataSets", "Data Model", "Files", "Setting Groups", "GOOSE"],
            roots.Select(row => row.Title).ToArray());
        Assert.Equal(
            ["category/reports", "category/datasets", "category/model",
             "category/files", "category/settings", "category/goose"],
            roots.Select(row => row.Key).ToArray());
        Assert.All(roots, row => Assert.Equal(0, row.Depth));
        Assert.All(roots, row => Assert.Equal("Category", row.Kind));
    }

    [Fact]
    public void SelectedIedCard_BoldsNameAndEndpoint_WithoutBoldingOtherCards()
    {
        var xml = XDocument.Parse(Read("MainWindow.xaml"));
        XNamespace ns = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var list = Assert.Single(xml.Descendants(ns + "ListBox")
            .Where(e => (string?)e.Attribute("ItemsSource") == "{Binding Devices}"));
        Assert.Equal("{Binding SelectedDevice, Mode=TwoWay}", (string?)list.Attribute("SelectedItem"));
        var template = Assert.Single(list.Descendants(ns + "DataTemplate"));
        var resources = Assert.Single(template.Elements(ns + "DataTemplate.Resources"));
        foreach (var (key, initial) in new[]
        {
            ("IedCardSelectedNameTypography", "SemiBold"),
            ("IedCardSelectedEndpointTypography", "Normal")
        })
        {
            var style = Assert.Single(resources.Elements(ns + "Style")
                .Where(e => (string?)e.Attribute(x + "Key") == key));
            Assert.Contains(style.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "FontWeight" &&
                (string?)setter.Attribute("Value") == initial);
            var selected = Assert.Single(style.Descendants(ns + "DataTrigger"));
            Assert.Equal("{Binding IsActive}", (string?)selected.Attribute("Binding"));
            Assert.Equal("True", (string?)selected.Attribute("Value"));
            Assert.Contains(selected.Elements(ns + "Setter"), setter =>
                (string?)setter.Attribute("Property") == "FontWeight" &&
                (string?)setter.Attribute("Value") == "Bold");
        }
        foreach (var (binding, styleKey) in new[]
        {
            ("{Binding Name}", "IedCardSelectedNameTypography"),
            ("{Binding EndpointText}", "IedCardSelectedEndpointTypography")
        })
        {
            var identity = Assert.Single(template.Descendants(ns + "TextBlock")
                .Where(e => (string?)e.Attribute("Text") == binding));
            Assert.Equal("{StaticResource " + styleKey + "}", (string?)identity.Attribute("Style"));
            // A local value would override the dynamic trigger when selected.
            Assert.Null(identity.Attribute("FontWeight"));
        }
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? folder = new(AppContext.BaseDirectory); folder is not null; folder = folder.Parent)
        {
            var target = Path.Combine(folder.FullName, path);
            if (File.Exists(target)) return File.ReadAllText(target);
        }
        throw new FileNotFoundException(path);
    }
}
