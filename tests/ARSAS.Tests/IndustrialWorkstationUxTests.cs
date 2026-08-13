using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class IndustrialWorkstationUxTests
{
    [Fact]
    public void IndustrialControls_DefineSearchAndLucideActionSystem()
    {
        var document = XDocument.Load(FindRepoFile("Resources/P2IndustrialControls.xaml"));
        XNamespace presentation = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var keys = document.Descendants()
            .Select(node => (string?)node.Attribute(x + "Key"))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);

        Assert.Contains("LucideSearch", keys);
        Assert.Contains("LucidePrinter", keys);
        Assert.Contains("LucideClock3", keys);
        Assert.Contains("LucideFileWaveform", keys);
        Assert.Contains("LucidePlugZap", keys);
        Assert.Contains("IndustrialSearchTextBox", keys);
        Assert.Contains("IndustrialGridHeader", keys);
        Assert.Contains("IconPrintContent", keys);
        Assert.Contains("IconConnectContent", keys);
        Assert.Contains("IconStartFatContent", keys);
        Assert.Contains("IconExplorerNavContent", keys);
        Assert.Contains("IconDiagnosticsNavContent", keys);

        var searchStyle = document.Descendants(presentation + "Style")
            .Single(node => (string?)node.Attribute(x + "Key") == "IndustrialSearchTextBox");
        Assert.Contains("Search", document.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Contains("LucideSearch", searchStyle.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void WorkstationSearch_FiltersExistingCollectionsInsteadOfReplacingEngineeringState()
    {
        var source = File.ReadAllText(FindRepoFile("P2IndustrialWorkstationUx.cs"));

        Assert.Contains("CollectionViewSource.GetDefaultView(window.Devices)", source, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GetDefaultView(window.Project.Ieds)", source, StringComparison.Ordinal);
        Assert.Contains("CollectionViewSource.GetDefaultView(selectedIed.TestPoints)", source, StringComparison.Ordinal);
        Assert.Contains("MatchesDevice", source, StringComparison.Ordinal);
        Assert.Contains("MatchesFatIed", source, StringComparison.Ordinal);
        Assert.Contains("MatchesPoint", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.Devices =", source, StringComparison.Ordinal);
        Assert.DoesNotContain("window.Project.Ieds =", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatSignalSearch_IsInsertedImmediatelyBeforePrintPreview_AndLegacyPauseResumeAreHidden()
    {
        var source = File.ReadAllText(FindRepoFile("P2IndustrialWorkstationUx.cs"));

        var findPreview = source.IndexOf("WorkspacePreviewToggle", StringComparison.Ordinal);
        var insertSearch = source.IndexOf("actionBar.Children.Insert(Math.Max(0, previewIndex), signalSearch)", StringComparison.Ordinal);
        Assert.True(findPreview >= 0);
        Assert.True(insertSearch > findPreview);
        Assert.Contains("Search signal, IEC reference or test point", source, StringComparison.Ordinal);
        Assert.Contains("text.Equals(\"Pause\"", source, StringComparison.Ordinal);
        Assert.Contains("text.Equals(\"Resume\"", source, StringComparison.Ordinal);
        Assert.Contains("button.Visibility = Visibility.Collapsed", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MainExplorerSearch_IsOptimizedForLargeIedWorkspaces()
    {
        var source = File.ReadAllText(FindRepoFile("P2IndustrialWorkstationUx.cs"));

        Assert.Contains("Search IED name, SCL name or IP", source, StringComparison.Ordinal);
        Assert.Contains("device.Name", source, StringComparison.Ordinal);
        Assert.Contains("device.SclIedName", source, StringComparison.Ordinal);
        Assert.Contains("device.IpAddress", source, StringComparison.Ordinal);
        Assert.Contains("device.EndpointText", source, StringComparison.Ordinal);
        Assert.Contains("Keyboard.Modifiers == ModifierKeys.Control && e.Key == Key.F", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P2Theme_AlwaysAppliesIndustrialWorkstationLayerAfterBasePalette()
    {
        var source = File.ReadAllText(FindRepoFile("P2BlueSteelGreigeUx.cs"));
        var baseMain = source.IndexOf("ApplyMainWindow(mainWindow)", StringComparison.Ordinal);
        var industrial = source.IndexOf("P2IndustrialWorkstationUx.Apply(window)", StringComparison.Ordinal);

        Assert.True(baseMain >= 0);
        Assert.True(industrial > baseMain);
    }

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}