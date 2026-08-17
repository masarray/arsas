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
        var section = ExtractAnnunciatorSection(source);

        Assert.Contains("Header=\"Alarm\"", source, StringComparison.Ordinal);
        Assert.Contains("IsAnnunciatorSelected", source, StringComparison.Ordinal);
        Assert.Contains("CanUseAsAnnunciator", source, StringComparison.Ordinal);
        Assert.Contains("Click=\"AnnunciatorSelection_Click\"", source, StringComparison.Ordinal);
        Assert.Contains("Header=\"Alarm Annunciator\"", section, StringComparison.Ordinal);
        Assert.Contains("Unacknowledged flashes • acknowledged steady • returned awaits ACK", section, StringComparison.Ordinal);
        Assert.DoesNotContain("FLASH = UNACK", section, StringComparison.Ordinal);
        Assert.Contains("Text=\"{Binding CurrentValue}\" FontSize=\"18\"", section, StringComparison.Ordinal);
        Assert.Contains("Click=\"AcknowledgeAlarm_Click\"", section, StringComparison.Ordinal);
        Assert.Contains("Click=\"AcknowledgeAllAlarms_Click\"", section, StringComparison.Ordinal);
        Assert.Contains("ActiveUnacknowledged", section, StringComparison.Ordinal);
        Assert.Contains("ActiveAcknowledged", section, StringComparison.Ordinal);
        Assert.Contains("ReturnedUnacknowledged", section, StringComparison.Ordinal);
        Assert.DoesNotContain("StateDetail", section, StringComparison.Ordinal);
        Assert.Contains("<Border.ToolTip>", section, StringComparison.Ordinal);
        Assert.Contains("StringFormat=Last SOE: {0}", section, StringComparison.Ordinal);
        Assert.DoesNotContain("StaticResource LucideBell", section, StringComparison.Ordinal);
    }

    [Fact]
    public void Annunciator_UsesVirtualizedIedRail_AndVerticalColumnMajorFascia()
    {
        var section = ExtractAnnunciatorSection(File.ReadAllText(FindRepoFile("MainWindow.xaml")));

        Assert.Contains("ItemsSource=\"{Binding AnnunciatorDevices}\"", section, StringComparison.Ordinal);
        Assert.Contains("SelectedItem=\"{Binding SelectedAnnunciatorDevice, Mode=TwoWay}\"", section, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.IsVirtualizing=\"True\"", section, StringComparison.Ordinal);
        Assert.Contains("VirtualizingPanel.VirtualizationMode=\"Recycling\"", section, StringComparison.Ordinal);
        Assert.Contains("ItemsSource=\"{Binding SelectedAnnunciatorDevice.Alarms}\"", section, StringComparison.Ordinal);
        Assert.Contains("<WrapPanel Orientation=\"Vertical\"/>", section, StringComparison.Ordinal);
        Assert.DoesNotContain("<WrapPanel Orientation=\"Horizontal\"/>", section, StringComparison.Ordinal);
        Assert.Contains("Width=\"250\" Height=\"64\"", section, StringComparison.Ordinal);
        Assert.Contains("Width=\"22\" Height=\"22\"", section, StringComparison.Ordinal);
        Assert.Contains("Content=\"ACK IED\"", section, StringComparison.Ordinal);
        Assert.DoesNotContain("Content=\"ACK ALL\"", section, StringComparison.Ordinal);
        Assert.Contains("SelectedAnnunciatorDevice.DeviceName", section, StringComparison.Ordinal);
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
    public void Annunciator_FlashAndAckWorkAreBoundedToVisibleIedOrIedRail()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.AlarmAnnunciator.cs"));

        Assert.Contains("SelectedAnnunciatorDevice.Alarms.Where(item => item.IsFlashing)", source, StringComparison.Ordinal);
        Assert.Contains("AnnunciatorDevices.Where(group => group.HasUnacknowledged)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("foreach (var alarm in AnnunciatorAlarms)", source, StringComparison.Ordinal);
        Assert.Contains("foreach (var item in group.Alarms.Where(item => item.CanAcknowledge).ToArray())", source, StringComparison.Ordinal);
        Assert.Contains("select an IED first", source, StringComparison.Ordinal);
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

    private static string ExtractAnnunciatorSection(string source)
    {
        const string start = "<!-- EVENT-LATCHED ALARM ANNUNCIATOR -->";
        const string end = "<!-- SCL / DISCOVERY-AWARE GOOSE SUBSCRIBER -->";
        var startIndex = source.IndexOf(start, StringComparison.Ordinal);
        var endIndex = source.IndexOf(end, StringComparison.Ordinal);
        Assert.True(startIndex >= 0 && endIndex > startIndex, "Alarm Annunciator XAML section was not found.");
        return source[startIndex..endIndex];
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
