using System.Xml.Linq;

namespace ARSAS.Tests;

public sealed class AlarmAnnunciatorIntegrationRegressionTests
{
    [Fact]
    public void Header_UsesSixWorkflowDestinations_WithoutDuplicateRuntimeStatusChips()
    {
        var document = XDocument.Parse(File.ReadAllText(FindRepoFile("MainWindow.xaml")));
        XNamespace p = "http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace x = "http://schemas.microsoft.com/winfx/2006/xaml";

        var rootGrid = document.Root?.Elements(p + "Grid").Single()
            ?? throw new Xunit.Sdk.XunitException("MainWindow root Grid is missing.");
        var header = rootGrid.Elements(p + "Grid")
            .Single(grid => (string?)grid.Attribute("Grid.Row") == "0");
        var navGrid = header.Descendants(p + "Grid")
            .Single(grid => (string?)grid.Attribute(x + "Name") == "WorkflowNavGrid");
        var navColumns = navGrid.Element(p + "Grid.ColumnDefinitions")?
            .Elements(p + "ColumnDefinition").Count() ?? 0;

        Assert.Equal(6, navColumns);
        Assert.Contains(navGrid.Descendants(p + "Button"), button =>
            (string?)button.Attribute(x + "Name") == "NavAlarmButton" &&
            (string?)button.Attribute("Tag") == "3" &&
            (string?)button.Attribute("Content") == "Alarm");
        Assert.Contains(navGrid.Descendants(p + "Button"), button =>
            (string?)button.Attribute(x + "Name") == "NavGooseButton" &&
            (string?)button.Attribute("Tag") == "4");
        Assert.Contains(navGrid.Descendants(p + "Button"), button =>
            (string?)button.Attribute(x + "Name") == "NavDiagnosticsButton" &&
            (string?)button.Attribute("Tag") == "5");

        var headerSource = header.ToString(SaveOptions.DisableFormatting);
        Assert.DoesNotContain("ConnectionInsightText", headerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("MonitoringInsightText", headerSource, StringComparison.Ordinal);
        Assert.DoesNotContain("EventInsightText", headerSource, StringComparison.Ordinal);

        var windowSource = File.ReadAllText(FindRepoFile("MainWindow.xaml"));
        Assert.Contains("RuntimeSummaryText", windowSource, StringComparison.Ordinal);
    }

    [Fact]
    public void BallisticNavbar_AppliesTheSameCapsuleContractToAlarm()
    {
        var source = File.ReadAllText(FindRepoFile("SasOperationalUiPolicy.cs"));

        Assert.Contains("\"NavExplorerButton\", \"NavLiveButton\", \"NavEventsButton\", \"NavAlarmButton\", \"NavGooseButton\", \"NavDiagnosticsButton\"", source, StringComparison.Ordinal);
        Assert.Contains("UpdateNavigation(buttons, tabs.SelectedIndex", source, StringComparison.Ordinal);
        Assert.Contains("buttons[index].Background = selected ? AccentGradient()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Explorer_OffersExplicitAlarmCheckbox_AndAnnunciatorWorkspaceUsesAckControls()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml"));

        Assert.Contains("Header=\"Alarm\"", source, StringComparison.Ordinal);
        Assert.Contains("IsAnnunciatorSelected", source, StringComparison.Ordinal);
        Assert.Contains("CanUseAsAnnunciator", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"AnnunciatorSelection_Click\"", source, StringComparison.Ordinal);
        Assert.Contains("Header=\"Alarm Annunciator\"", source, StringComparison.Ordinal);
        Assert.Contains("FLASH = UNACK", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"VALUE\"", source, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentValue}\" FontSize=\"24\"", source, StringComparison.Ordinal);
        Assert.Contains("AnnunciatorAlarms", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"AcknowledgeAlarm_Click\"", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"AcknowledgeAllAlarms_Click\"", source, StringComparison.Ordinal);
        Assert.Contains("ActiveUnacknowledged", source, StringComparison.Ordinal);
        Assert.Contains("ActiveAcknowledged", source, StringComparison.Ordinal);
        Assert.Contains("ReturnedUnacknowledged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StateDetail", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StringFormat=Last SOE {0}", source, StringComparison.Ordinal);
        Assert.Contains("<Border.ToolTip>", source, StringComparison.Ordinal);
        Assert.Contains("StringFormat=Last SOE: {0}", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticResource LucideBell", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Annunciator_ConsumesRuntimeEventRaised_WithoutCreatingSecondAcquisitionPath()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.AlarmAnnunciator.cs"));

        Assert.Contains("_runtime.EventRaised += AlarmRuntime_EventRaised", source, StringComparison.Ordinal);
        Assert.Contains("_pendingAnnunciatorEvents.Enqueue(entry)", source, StringComparison.Ordinal);
        Assert.Contains("IsAnnunciatorConfigured(entry.DeviceId, entry.IecReference)", source, StringComparison.Ordinal);
        Assert.Contains("item.ApplyEvent(entry)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartDeviceAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartMonitoring", source, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationAndDiagnosticsUseSixTabGeometry()
    {
        var navigation = File.ReadAllText(FindRepoFile("MainWindow.NavigationLayoutFix.cs"));
        var main = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));

        Assert.Contains("contentWidth / 6d", navigation, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(tabs.SelectedIndex, 0, 5)", navigation, StringComparison.Ordinal);
        Assert.Contains("NavAlarmButton", navigation, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex == 4", main, StringComparison.Ordinal);
        Assert.Contains("MainTabs.SelectedIndex == 5", main, StringComparison.Ordinal);
        Assert.Contains("Math.Clamp(index, 0, 5)", main, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectSaveAndRestorePersistAnnunciatorConfiguration()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var models = File.ReadAllText(FindRepoFile(Path.Combine("Models", "MonitorModels.cs")));

        Assert.Contains("AnnunciatorReferences = GetAnnunciatorReferencesForDevice(device)", source, StringComparison.Ordinal);
        Assert.Contains("RestoreAnnunciatorReferences(device, profile.AnnunciatorReferences)", source, StringComparison.Ordinal);
        Assert.Contains("public List<string> AnnunciatorReferences", models, StringComparison.Ordinal);
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
