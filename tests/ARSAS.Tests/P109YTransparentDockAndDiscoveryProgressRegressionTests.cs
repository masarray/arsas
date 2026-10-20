using System.Xml.Linq;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

/// <summary>
/// Source+state regression for one fixed, nonblocking IED card progress line.
/// No additional MMS reads, command calls or global progress authority.
/// </summary>
public sealed class P109YTransparentDockAndDiscoveryProgressRegressionTests
{
    private static readonly XNamespace P = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string Source(string path)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            var candidate = Path.Combine(current.FullName, path);
            if (File.Exists(candidate)) return File.ReadAllText(candidate);
            current = current.Parent;
        }
        throw new FileNotFoundException(path);
    }

    [Fact]
    public void CommandDockFocusDoesNotDrawPersistentWhiteRectangle_AndKeepsKeyboardToggle()
    {
        var app = XDocument.Parse(Source("App.xaml"));
        var style = app.Descendants(P + "Style").Single(n =>
            (string?)n.Attribute(X + "Key") == "CommandDockTransparentHeaderExpander");
        var toggle = style.Descendants(P + "ToggleButton").Single();
        Assert.Equal("{x:Null}", (string?)toggle.Attribute("FocusVisualStyle"));
        Assert.Equal("True", (string?)toggle.Attribute("Focusable"));
        Assert.Contains("IsExpanded", (string?)toggle.Attribute("IsChecked"));
        Assert.DoesNotContain("HeaderFocusRing", style.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("#AAFFFFFF", style.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(style.Descendants(P + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsKeyboardFocused");
        Assert.Contains(style.Descendants(P + "Trigger"),
            trigger => (string?)trigger.Attribute("Property") == "IsChecked");
    }

    [Fact]
    public void DiscoveryAndReadUseTheSameFixedUnderIpSlot_NoSpinnerOrBusyCard()
    {
        var doc = XDocument.Parse(Source("MainWindow.xaml"));
        var line = doc.Descendants(P + "ProgressBar").Single(n =>
            (string?)n.Attribute(X + "Name") == "BrowserReadProgressLine");
        Assert.Equal("3", (string?)line.Attribute("Height"));
        Assert.Equal("0", (string?)line.Attribute("Opacity"));
        Assert.Equal("{Binding IedCardProgressPercent, Mode=OneWay}", (string?)line.Attribute("Value"));
        Assert.Equal("{StaticResource DiscoveryProgressBar}", (string?)line.Attribute("Style"));
        Assert.Equal("7", (string?)line.Parent?.Attribute("Height"));
        Assert.Contains(line.Parent!.Parent!.Elements(P + "TextBlock"), n =>
            (string?)n.Attribute("Text") == "{Binding EndpointText}");
        var cardTemplate = line.Ancestors(P + "DataTemplate").First();
        Assert.DoesNotContain("BusyOverlay", cardTemplate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("CardSpinnerRotate", cardTemplate.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("Storyboard RepeatBehavior=\"Forever\"", cardTemplate.ToString(), StringComparison.Ordinal);

        var triggers = cardTemplate.Descendants(P + "DataTrigger").ToArray();
        foreach (var binding in new[] { "{Binding IsBrowserReading}", "{Binding IsDiscoveryProgressActive}" })
        {
            var trigger = Assert.Single(triggers, t =>
                (string?)t.Attribute("Binding") == binding);
            Assert.Contains(trigger.Elements(P + "Setter"), n =>
                (string?)n.Attribute("TargetName") == "BrowserReadProgressLine" &&
                (string?)n.Attribute("Property") == "Opacity" &&
                (string?)n.Attribute("Value") == "1");
        }
        // Stop remains pre-emptive while discovering. Never block all card
        // clicks with an opaque overlay or set IsHitTestVisible=False globally.
        Assert.DoesNotContain(triggers, t =>
            (string?)t.Attribute("Binding") == "{Binding IsBusy}");
        Assert.DoesNotContain(cardTemplate.Descendants(P + "Setter"), n =>
            (string?)n.Attribute("TargetName") == "CardContent" &&
            (string?)n.Attribute("Property") == "IsHitTestVisible");
    }

    [Fact]
    public void DiscoveryUsesExistingModelProgress_BrowserReadResumes_StaleBusyDoesNotRearm()
    {
        var device = new Iec61850MonitorDevice();
        var published = new List<string?>();
        device.PropertyChanged += (_, e) => published.Add(e.PropertyName);

        device.IsBrowserReading = true;
        device.ApplyBrowserReadProgress(2, 4);
        Assert.Equal(50d, device.BrowserReadProgressPercent);
        Assert.Equal(50d, device.IedCardProgressPercent);

        device.ResetDiscoveryProgress("Associating MMS...", "Discovering IED");
        Assert.False(device.IsDiscoveryProgressActive);
        device.IsBusy = true;
        Assert.True(device.IsDiscoveryProgressActive);
        Assert.Equal(0d, device.IedCardProgressPercent);

        device.ApplyDiscoveryProgress(new IedDiscoveryProgress(
            IedDiscoveryStage.DiscoveringDirectory, "Reading directory", 40d, 4, 15));
        Assert.True(device.AdvanceDiscoveryProgressAnimation());
        Assert.InRange(device.IedCardProgressPercent, 0.01d, 40d);
        Assert.Equal(50d, device.BrowserReadProgressPercent);

        device.CompleteDiscoveryProgressAnimation();
        Assert.Equal(100d, device.IedCardProgressPercent);
        device.IsBusy = false;
        Assert.False(device.IsDiscoveryProgressActive);
        Assert.Equal(50d, device.IedCardProgressPercent);
        device.IsBrowserReading = false;
        device.IsBusy = true; // e.g. Stop or unrelated busy phase
        Assert.False(device.IsDiscoveryProgressActive);
        device.IsBusy = false;

        device.IsBrowserReading = true;
        Assert.Equal(0d, device.IedCardProgressPercent);
        device.ApplyBrowserReadProgress(1, 4);
        Assert.Equal(25d, device.IedCardProgressPercent);
        Assert.Equal(100d, device.DiscoveryProgressPercent);
        Assert.Contains(nameof(Iec61850MonitorDevice.IedCardProgressPercent), published);
        Assert.Contains(nameof(Iec61850MonitorDevice.IsDiscoveryProgressActive), published);
    }
}
