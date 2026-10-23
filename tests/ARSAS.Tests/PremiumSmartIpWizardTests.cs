using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Xml.Linq;
using ArIED61850Tester;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class PremiumSmartIpWizardTests
{
    [Theory]
    [InlineData("192", "168", "1", "33", "192.168.1.33")]
    [InlineData("0", "0", "0", "0", "0.0.0.0")]
    [InlineData("255", "255", "255", "255", "255.255.255.255")]
    [InlineData("001", "002", "003", "004", "1.2.3.4")]
    public void SegmentedIpv4_ValidOctets_Canonicalize(
        string a, string b, string c, string d, string expected)
    {
        Assert.True(IpConnectWizardWindow.TryNormalizeIpv4Octets(
            [a, b, c, d], out var actual));
        Assert.Equal(expected, actual);
    }

    [Theory]
    [InlineData("256", "1", "2", "3")]
    [InlineData("25x", "1", "2", "3")]
    [InlineData("", "1", "2", "3")]
    [InlineData("-1", "1", "2", "3")]
    [InlineData("999", "1", "2", "3")]
    [InlineData(" 1", "1", "2", "3")]
    public void SegmentedIpv4_InvalidOctets_Reject(
        string a, string b, string c, string d)
        => Assert.False(IpConnectWizardWindow.TryNormalizeIpv4Octets(
            [a, b, c, d], out _));

    [Theory]
    [InlineData("192.168.0.12", true)]
    [InlineData("10.0.0.256", false)]
    [InlineData("10.0.0", false)]
    [InlineData("::1", false)]
    [InlineData("10.0.0.1:102", false)]
    [InlineData("10.0..1", false)]
    public void FullAddressPaste_StrictlyRequiresFourIpv4Octets(string address, bool accepted)
        => Assert.Equal(accepted, IpConnectWizardWindow.TrySplitIpv4(address, out _));

    [Fact]
    public void ExistingPreferences_SeparateSubmittedFromVerifiedHistory_WithNoMigrationLoss()
    {
        var path = Path.Combine(Path.GetTempPath(), "arsas-smart-ip-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, """
                {
                  "LastDiscoveryIpAddress":"192.0.2.14",
                  "LastDiscoveryMmsPort":102,
                  "SuccessfulRelays":[
                    {"IpAddress":"192.0.2.15","MmsPort":1102,"IedName":"SUB_BCU",
                     "LastConnectedUtc":"2026-10-22T07:00:00Z","SuccessCount":3}],
                  "SignalSelectionProfiles":[{"IedName":"SUB_BCU","IpAddress":"192.0.2.15",
                    "Port":1102,"SelectedObjectReferences":["IEDLD0/LLN0.Mod.stVal"]}],
                  "RecentRelayIps":["192.0.2.16"]
                }
                """);
            var before = File.ReadAllText(path);
            var entries = UserPreferenceStore.LoadRecentEndpointDetails(path);
            Assert.Equal("192.0.2.14", entries[0].IpAddress);
            Assert.False(entries[0].ConfirmedSuccessful);
            Assert.Equal("192.0.2.15", entries[1].IpAddress);
            Assert.True(entries[1].ConfirmedSuccessful);
            Assert.Equal(1102, entries[1].MmsPort);
            Assert.Equal("SUB_BCU", entries[1].IedName);
            Assert.Contains(entries, e => e.IpAddress == "192.0.2.16" && !e.ConfirmedSuccessful);
            Assert.Equal(before, File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }

    [Fact]
    public void DefaultDialog_IsCompactUsesAppTypography_AndKeepsExpertControlsCollapsed()
    {
        var doc = XDocument.Load(Find("IpConnectWizardWindow.xaml"));
        XNamespace w = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";
        var root = Assert.IsType<XElement>(doc.Root);

        Assert.InRange(int.Parse(root.Attribute("Width")!.Value), 460, 530);
        Assert.Equal("350", root.Attribute("Height")?.Value);
        Assert.Contains("AppFontFamily", root.Attribute("FontFamily")!.Value);
        Assert.Equal("Manual", root.Attribute("SizeToContent")?.Value);

        var octetStyle = doc.Descendants(w + "Style")
            .Single(style => style.Attribute(x + "Key")?.Value == "SmartIpOctet");
        var setters = octetStyle.Elements(w + "Setter")
            .ToDictionary(e => e.Attribute("Property")!.Value, e => e.Attribute("Value")!.Value);
        Assert.True(double.Parse(setters["Width"], System.Globalization.CultureInfo.InvariantCulture) <= 46);
        Assert.True(double.Parse(setters["FontSize"], System.Globalization.CultureInfo.InvariantCulture) <= 13);
        Assert.True(double.Parse(setters["Height"], System.Globalization.CultureInfo.InvariantCulture) <= 34);

        var options = doc.Descendants(w + "Expander")
            .Single(e => e.Attribute(x + "Name")?.Value == "MoreOptionsExpander");
        Assert.NotEqual("True", options.Attribute("IsExpanded")?.Value);
        Assert.Contains(options.Descendants(w + "TextBox"),
            box => box.Attribute(x + "Name")?.Value == "MmsPortBox");
        Assert.DoesNotContain(doc.Descendants(w + "CheckBox"),
            box => box.Attribute(x + "Name")?.Value == "ManualAddressCheck");
        Assert.Equal("265", doc.Descendants(w + "Border")
            .Single(e => e.Attribute(x + "Name")?.Value == "IpEditorHost")
            .Attribute("Width")?.Value);
        Assert.Contains("expand_circle_down", File.ReadAllText(Find("IpConnectWizardWindow.xaml")));

        var xaml = File.ReadAllText(Find("IpConnectWizardWindow.xaml"));
        Assert.DoesNotContain("Smart discovery", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("DataSets", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("RCBs", xaml, StringComparison.Ordinal);
        Assert.DoesNotContain("IPv6", xaml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"HistoryPopup\"", xaml, StringComparison.Ordinal);
        Assert.Contains("Content=\"Connect\"", xaml, StringComparison.Ordinal);
    }

    [Fact]
    public void WpfEditor_ConstructsOnSta_AndHasReachableKeyboardFocusAndIPv6Mode()
    {
        if (!OperatingSystem.IsWindows()) return;
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                var wizard = new IpConnectWizardWindow("192.168.10.20");
                try
                {
                    var boxes = new[]
                    {
                        Assert.IsType<TextBox>(wizard.FindName("Octet1Box")),
                        Assert.IsType<TextBox>(wizard.FindName("Octet2Box")),
                        Assert.IsType<TextBox>(wizard.FindName("Octet3Box")),
                        Assert.IsType<TextBox>(wizard.FindName("Octet4Box"))
                    };
                    Assert.Equal(new[] { "192", "168", "10", "20" },
                        boxes.Select(b => b.Text).ToArray());
                    Assert.All(boxes, b =>
                    {
                        var style = b.Style;
                        Assert.NotNull(style);
                        Assert.Contains(style.Setters, setter =>
                            setter is EventSetter evt && evt.Event == Keyboard.PreviewKeyDownEvent);
                        Assert.Contains(style.Setters, setter =>
                            setter is EventSetter evt && evt.Event == TextCompositionManager.PreviewTextInputEvent);
                    });

                    FocusManager.SetFocusedElement(wizard, boxes[0]);
                    Assert.Same(boxes[0], FocusManager.GetFocusedElement(wizard));

                    var options = Assert.IsType<Expander>(wizard.FindName("MoreOptionsExpander"));
                    Assert.False(options.IsExpanded);
                    var raw = Assert.IsType<TextBox>(wizard.FindName("RawIpBox"));
                    var host = Assert.IsType<Border>(wizard.FindName("IpEditorHost"));
                    Assert.Equal(Visibility.Collapsed, raw.Visibility);
                    Assert.Equal(Visibility.Visible, host.Visibility);
                    Assert.Null(wizard.FindName("ManualAddressCheck"));

                    // IPv6 has no visible toggle; it activates via the caller's
                    // initial address or a history recall, without losing data.
                    var ipv6Wizard = new IpConnectWizardWindow("2001:db8::2");
                    try
                    {
                        Assert.Equal(Visibility.Visible,
                            Assert.IsType<TextBox>(ipv6Wizard.FindName("RawIpBox")).Visibility);
                        Assert.Equal(Visibility.Collapsed,
                            Assert.IsType<Border>(ipv6Wizard.FindName("IpEditorHost")).Visibility);
                    }
                    finally { ipv6Wizard.Close(); }

                    Assert.NotNull(wizard.FindName("HistorySearchBox"));
                    Assert.NotNull(wizard.FindName("HistoryListBox"));
                }
                finally { wizard.Close(); }
            }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "Smart IP WPF STA test timed out.");
        Assert.Null(failure);
    }

    [Fact]
    public void HistorySelection_AndKeyboardHandlers_DoNotDirectlyInitiateConnections()
    {
        var source = File.ReadAllText(Find("IpConnectWizardWindow.xaml.cs"));
        var historyStart = source.IndexOf("private void CommitHistorySelection()", StringComparison.Ordinal);
        var connectStart = source.IndexOf("private void Connect_Click(", StringComparison.Ordinal);
        Assert.True(historyStart >= 0 && connectStart > historyStart);
        var history = source[historyStart..connectStart];
        Assert.Contains("ApplyAddress(selection.IpAddress)", history);
        Assert.DoesNotContain("RecordSuccessfulEndpoint", history);
        Assert.DoesNotContain("RecordLastDiscoveryEndpoint", history);
        Assert.DoesNotContain("DialogResult = true", history);
        Assert.Contains("Key.Back", source);
        Assert.Contains("Key.Left", source);
        Assert.Contains("Key.Right", source);
        Assert.Contains("Key.OemPeriod", source);
        Assert.Contains("Key.Enter", source);
        Assert.Contains("Key.Escape", source);
    }

    private static string Find(string relative)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var path = Path.Combine(directory.FullName, relative);
            if (File.Exists(path)) return path;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(relative);
    }
}
