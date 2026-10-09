using System.Globalization;
using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Read-only XAML contract checks: the field screenshots exposed dark text on dark
/// backgrounds and a fixed-height connect wizard clipping its final panel.
/// This suite does not simulate IEC 61850 sessions or alter action authority.
/// </summary>
public sealed class IedTaskDialogVisualRegressionTests
{
    private static readonly XNamespace Presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace Xaml = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void BothDialogs_UseIsolatedHighContrastSurfaceAndBoundedNaturalHeight()
    {
        foreach (var file in new[] { "SclSignalSelectionModeWindow.xaml", "IpConnectWizardWindow.xaml" })
        {
            var document = XDocument.Load(Find(file));
            var root = Assert.IsType<XElement>(document.Root);
            Assert.Equal("Window", root.Name.LocalName);
            Assert.Equal("Height", (string?)root.Attribute("SizeToContent"));
            Assert.Equal("TaskDialogCanvas", ResourceRef((string?)root.Attribute("Background"), "DynamicResource"));
            Assert.True(double.TryParse((string?)root.Attribute("MaxHeight"),
                NumberStyles.Float, CultureInfo.InvariantCulture, out var max) && max <= 700);
            Assert.Null(root.Attribute("Height"));
            Assert.Contains(root.Descendants(Presentation + "ResourceDictionary"),
                element => element.Attribute("Source")?.Value == "Styles/IedTaskDialogStyles.xaml");
            Assert.Contains(root.Descendants(Presentation + "ScrollViewer"),
                element => element.Attribute("VerticalScrollBarVisibility")?.Value == "Auto");
            Assert.DoesNotContain(root.Descendants(Presentation + "RowDefinition"),
                element => element.Attribute("Height")?.Value == "*");
            Assert.DoesNotContain(root.Descendants(Presentation + "TextBlock"),
                element => element.Attribute("Foreground")?.Value == "{StaticResource Muted}" ||
                           element.Attribute("Foreground")?.Value == "{StaticResource Ink}");
        }
    }

    [Fact]
    public void DialogStyles_AreSelfContained_AndBothWindowsInitializeOnSta()
    {
        var dictionary = XDocument.Load(Find("Styles/IedTaskDialogStyles.xaml"));
        var declared = dictionary.Descendants()
            .Attributes(Xaml + "Key")
            .Select(attribute => attribute.Value)
            .ToHashSet(StringComparer.Ordinal);
        var resourceReferences = dictionary.Descendants()
            .Attributes()
            .Select(attribute => ResourceRef(attribute.Value))
            .Where(value => value is not null)
            .Cast<string>()
            .ToArray();

        // ResourceDictionary.Source is loaded on its own; inheritance from an
        // application-level style can throw StaticResourceExtension at runtime
        // even when compiled XAML and text-based CI contracts all pass.
        Assert.All(resourceReferences, key => Assert.Contains(key, declared));
        Assert.Contains("TaskDialogButtonBase", declared);
        Assert.Contains("TaskDialogPrimaryAction", declared);
        Assert.Contains("TaskDialogSubmitAction", declared);

        if (!OperatingSystem.IsWindows())
            return;

        Exception? failure = null;
        var thread = new System.Threading.Thread(() =>
        {
            try
            {
                var choice = new ArIED61850Tester.SclSignalSelectionModeWindow(1);
                var connect = new ArIED61850Tester.IpConnectWizardWindow("127.0.0.1");
                Assert.NotNull(choice.TryFindResource("TaskDialogPrimaryAction"));
                Assert.NotNull(connect.TryFindResource("TaskDialogSubmitAction"));
                Assert.NotNull(choice.Background);
                Assert.NotNull(connect.Background);
                choice.Close();
                connect.Close();
            }
            catch (Exception error)
            {
                failure = error;
            }
        });
        thread.SetApartmentState(System.Threading.ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(20)), "WPF dialog initialization timed out.");
        Assert.True(failure is null, failure?.ToString());
    }

    [Fact]
    public void SharedDialogPalette_MeetsReadableForegroundContrast()
    {
        var document = XDocument.Load(Find("Styles/IedTaskDialogStyles.xaml"));
        string Color(string key) => document.Descendants(Presentation + "SolidColorBrush")
            .Single(element => element.Attribute(Xaml + "Key")?.Value == key)
            .Attribute("Color")!.Value;
        var background = Color("TaskDialogCanvas");
        var card = Color("TaskDialogCard");
        var ink = Color("TaskDialogInk");
        var muted = Color("TaskDialogMuted");
        Assert.True(Contrast(ink, background) >= 7.0);
        Assert.True(Contrast(muted, background) >= 4.5);
        Assert.True(Contrast(ink, card) >= 7.0);
        Assert.True(Contrast(muted, card) >= 4.5);
    }

    [Fact]
    public void IedActions_RemainsTaskScoped_AndTechnicalNotesAreInTooltips()
    {
        var xml = File.ReadAllText(Find("SclSignalSelectionModeWindow.xaml"));
        var code = File.ReadAllText(Find("SclSignalSelectionModeWindow.xaml.cs"));
        foreach (var action in new[]
        {
            "MonitorStaticDataSet_Click", "MonitorManual_Click",
            "RcbEngineering_Click", "DownloadComtrade_Click", "BrowseOffline_Click"
        })
            Assert.Contains($"Click=\"{action}\"", xml, StringComparison.Ordinal);

        Assert.Contains("IsEnabled=\"{Binding CanUseStaticDataSet}\"", xml, StringComparison.Ordinal);
        Assert.Contains("StaticDataSetAvailabilityText", xml, StringComparison.Ordinal);
        Assert.Contains("ContextHeading", xml, StringComparison.Ordinal);
        Assert.Contains("ImportScopeText", xml, StringComparison.Ordinal);
        Assert.Contains("reportBackedCount > 0", code, StringComparison.Ordinal);
        Assert.Contains("UseStaticDataSet => _useStaticDataSet", code, StringComparison.Ordinal);
        Assert.DoesNotContain("Signal selection is shared with FAT, but", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("task-scoped MMS file-transfer connection", xml, StringComparison.Ordinal);
    }

    [Fact]
    public void AddIed_PreservesSavedIpBindingValidationFeedbackAndKeyboardActions()
    {
        var xml = File.ReadAllText(Find("IpConnectWizardWindow.xaml"));
        var source = File.ReadAllText(Find("IpConnectWizardWindow.xaml.cs"));
        Assert.Contains("x:Name=\"RelayIpBox\"", xml, StringComparison.Ordinal);
        Assert.Contains("RecentRelayIps", xml, StringComparison.Ordinal);
        Assert.Contains("RelayIpAddress", xml, StringComparison.Ordinal);
        Assert.Contains("MmsPortText", xml, StringComparison.Ordinal);
        Assert.Contains("x:Name=\"WizardStatusText\"", xml, StringComparison.Ordinal);
        Assert.Contains("Style=\"{StaticResource TaskDialogError}\"", xml, StringComparison.Ordinal);
        Assert.Contains("IsDefault=\"True\"", xml, StringComparison.Ordinal);
        Assert.Contains("IsCancel=\"True\"", xml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Connect_Click\"", xml, StringComparison.Ordinal);
        Assert.Contains("Click=\"Cancel_Click\"", xml, StringComparison.Ordinal);
        Assert.DoesNotContain("Flow: TCP/TPKT/COTP", xml, StringComparison.Ordinal);
        Assert.Contains("UserPreferenceStore.LoadRecentEndpoints()", source, StringComparison.Ordinal);
        Assert.Contains("System.Net.IPAddress.TryParse", source, StringComparison.Ordinal);
        Assert.Contains("port is <= 0 or > 65535", source, StringComparison.Ordinal);
    }

    private static string? ResourceRef(string? value, string extension = "StaticResource")
    {
        var prefix = "{" + extension + " ";
        return value?.StartsWith(prefix, StringComparison.Ordinal) == true &&
               value.EndsWith("}", StringComparison.Ordinal)
            ? value[prefix.Length..^1] : null;
    }

    private static double Contrast(string first, string second)
    {
        static double Luminance(string value)
        {
            var c = value.TrimStart('#');
            Assert.Equal(6, c.Length);
            static double Channel(string hex)
            {
                var s = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture) / 255d;
                return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return Channel(c[..2]) * 0.2126 +
                   Channel(c[2..4]) * 0.7152 +
                   Channel(c[4..6]) * 0.0722;
        }
        var a = Luminance(first);
        var b = Luminance(second);
        return (Math.Max(a, b) + 0.05) / (Math.Min(a, b) + 0.05);
    }

    private static string Find(string path)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var full = Path.Combine(directory.FullName, path);
            if (File.Exists(full))
                return full;
            directory = directory.Parent;
        }
        throw new FileNotFoundException(path);
    }
}
