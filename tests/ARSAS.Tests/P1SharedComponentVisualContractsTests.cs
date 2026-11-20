using System.Xml.Linq;

namespace ARSAS.Tests;

/// <summary>
/// Protect visual-only P1 component convergence without changing IEC 61850 bindings,
/// command editing, model scopes or the SMV measurement instrument templates.
/// </summary>
public sealed class P1SharedComponentVisualContractsTests
{
    private static readonly XNamespace Wpf = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
    private static readonly XNamespace X = "http://schemas.microsoft.com/winfx/2006/xaml";

    [Fact]
    public void P1_InputsAndDropdownsExposeOneSharedNormalFocusAndErrorContract()
    {
        var doc = XDocument.Parse(Read("Resources/P2BlueSteelGreige.xaml"));
        var styles = doc.Descendants(Wpf + "Style").ToArray();
        XElement Named(string key) => styles.Single(e => (string?)e.Attribute(X + "Key") == key);

        var text = Named("WorkstationTextBox");
        Assert.Contains(text.Descendants(Wpf + "ScrollViewer"),
            e => (string?)e.Attribute(X + "Name") == "PART_ContentHost");
        Assert.Contains(text.Descendants(Wpf + "Trigger"),
            e => (string?)e.Attribute("Property") == "IsKeyboardFocusWithin");
        Assert.Contains(text.Descendants(Wpf + "Trigger"),
            e => (string?)e.Attribute("Property") == "Validation.HasError");
        Assert.Contains(text.Descendants(Wpf + "Trigger"),
            e => (string?)e.Attribute("Property") == "IsReadOnly");

        var combo = Named("WorkstationComboBox");
        Assert.Contains(combo.Descendants(Wpf + "Popup"),
            e => (string?)e.Attribute(X + "Name") == "PART_Popup");
        Assert.Contains(combo.Descendants(Wpf + "ItemsPresenter"), _ => true);
        Assert.Contains(combo.Descendants(Wpf + "ToggleButton"),
            e => ((string?)e.Attribute("IsChecked"))?.Contains("IsDropDownOpen", StringComparison.Ordinal) == true);
        Assert.Contains(combo.Descendants(Wpf + "Trigger"),
            e => (string?)e.Attribute("Property") == "IsEnabled");

        Assert.Equal("{StaticResource WorkstationComboBox}",
            (string?)Named("ModernComboBox").Attribute("BasedOn"));
        Assert.Equal(2, styles.Count(e => e.Attribute(X + "Key") == null &&
            ((string?)e.Attribute("TargetType") == "TextBox" ||
             (string?)e.Attribute("TargetType") == "ComboBox")));
    }

    [Fact]
    public void P1_EditableCommandValuePreservesFreeTextEditor()
    {
        var command = XDocument.Parse(Read("ControlCommandWindow.xaml"));
        var editor = command.Descendants(Wpf + "ComboBox").Single(e =>
            (string?)e.Attribute(X + "Name") == "CommandValueBox");
        Assert.Equal("{StaticResource WorkstationEditableComboBox}", (string?)editor.Attribute("Style"));
        Assert.Equal("{Binding AllowCustomValue}", (string?)editor.Attribute("IsEditable"));
        Assert.Contains("UpdateSourceTrigger=PropertyChanged", (string?)editor.Attribute("Text"));
        Assert.Equal("{Binding ValueOptions}", (string?)editor.Attribute("ItemsSource"));

        var theme = XDocument.Parse(Read("Resources/P2BlueSteelGreige.xaml"));
        var fallback = theme.Descendants(Wpf + "Style").Single(e =>
            (string?)e.Attribute(X + "Key") == "WorkstationEditableComboBox");
        Assert.DoesNotContain(fallback.Descendants(Wpf + "Setter"),
            e => (string?)e.Attribute("Property") == "Template");
    }

    [Fact]
    public void P1_EmptyStateAndTaskDialogsReuseCanonicalCoolBlueTokens()
    {
        var goose = XDocument.Parse(Read("Views/GooseSubscriberView.xaml"));
        Assert.Equal(2, goose.Descendants(Wpf + "Border").Count(e =>
            (string?)e.Attribute("Style") == "{StaticResource WorkstationEmptyState}"));
        var inherited = goose.Descendants(Wpf + "Style").Single(e =>
            (string?)e.Attribute("BasedOn") == "{StaticResource WorkstationEmptyState}");
        Assert.Contains(inherited.Descendants(Wpf + "DataTrigger"), e =>
            ((string?)e.Attribute("Binding"))?.Contains("SelectedGooseStream", StringComparison.Ordinal) == true);

        var dialogs = XDocument.Parse(Read("Styles/IedTaskDialogStyles.xaml"));
        string Brush(string key) => (string?)dialogs.Descendants(Wpf + "SolidColorBrush")
            .Single(e => (string?)e.Attribute(X + "Key") == key).Attribute("Color") ?? "";
        Assert.Equal("{DynamicResource CoolCanvasColor}", Brush("TaskDialogCanvas"));
        Assert.Equal("{DynamicResource CoolBorderColor}", Brush("TaskDialogBorder"));
        Assert.Equal("{DynamicResource InkColor}", Brush("TaskDialogInk"));
    }

    private static string Read(string path)
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var file = Path.Combine(dir.FullName, path);
            if (File.Exists(file)) return File.ReadAllText(file);
        }
        throw new FileNotFoundException(path);
    }
}
