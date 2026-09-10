namespace ARSAS.Tests;

public sealed class ProductionFatM3SelectedIedBindingRegressionTests
{
    [Fact]
    public void Start_LatchesCaptureTarget_WithoutTakingOverViewedContext()
    {
        var window = Read("IoListTestingWindow.xaml.cs");
        var coordinator = Read("Services/IoTesting/IoTestMultiSessionCoordinator.cs");

        var latch = window.IndexOf("var selectedIed = SelectedIed;", StringComparison.Ordinal);
        var prepare = window.IndexOf("await engineeringWindow.PrepareIoTestIedForFatAsync(", latch, StringComparison.Ordinal);
        var start = window.IndexOf("Session.Start(selectedIed)", latch, StringComparison.Ordinal);
        Assert.True(latch >= 0, "Start FAT must latch the currently viewed IED before asynchronous preparation.");
        Assert.True(prepare > latch, "Engineering acquisition must use the latched target.");
        Assert.True(start > prepare, "The production session must start the same latched target after preparation.");

        var startBody = MethodBody(
            coordinator,
            "public IoTestSessionActionResult Start(\n        IoTestIedPlan? ied,\n        IReadOnlyCollection<IoTestPointPlan>? captureScope)",
            "public IoTestSessionActionResult Pause");
        Assert.DoesNotContain("SelectContext(ied)", startBody, StringComparison.Ordinal);
        Assert.Contains("GetOrCreateController(ied)", startBody, StringComparison.Ordinal);
        Assert.Contains("controller.Start(ied, captureScope)", startBody, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringExplorer_RemainsViewedIedAuthority_WhileSessionsMayRun()
    {
        var owner = Read("MainWindow.ProductionFatTab.cs");
        var embedded = Read("IoListTestingWindow.EmbeddedEngineeringHost.cs");
        var window = Read("IoListTestingWindow.xaml.cs");

        var propertyHandler = MethodBody(
            owner,
            "private void ProductionFat_PropertyChanged",
            "private void ProductionFat_MainTabsSelectionChanged");
        Assert.Contains("SynchronizeProductionFatSelectedIed(SelectedDevice);", propertyHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("HasRunningSession", propertyHandler, StringComparison.Ordinal);

        var selectDevice = MethodBody(
            embedded,
            "internal void SelectEngineeringDeviceForEmbeddedFat",
            "internal void NotifyEmbeddedHostActivated");
        Assert.Contains("SelectedIed = match;", selectDevice, StringComparison.Ordinal);
        Assert.Contains("LiveDeviceId", selectDevice, StringComparison.Ordinal);
        Assert.Contains("IpAddress", selectDevice, StringComparison.Ordinal);
        Assert.Contains("IedName", selectDevice, StringComparison.Ordinal);

        Assert.Contains("public bool CanSelectIed => true;", window, StringComparison.Ordinal);
        Assert.Contains("Session.SelectContext(_selectedIed);", window, StringComparison.Ordinal);
    }

    [Fact]
    public void EvidenceActions_ResolveOwningActiveController_NotCurrentView()
    {
        var coordinator = Read("Services/IoTesting/IoTestMultiSessionCoordinator.cs");

        Assert.Contains("ResolveOwningActiveController", coordinator, StringComparison.Ordinal);
        Assert.Contains("points.Any(ied.TestPoints.Contains)", coordinator, StringComparison.Ordinal);
        Assert.Contains("points.Any(point => !owners[0].TestPoints.Contains(point))", coordinator, StringComparison.Ordinal);
        Assert.Contains("_controllers.TryGetValue(owners[0], out var controller)", coordinator, StringComparison.Ordinal);
        Assert.Contains("!controller.IsSessionActive", coordinator, StringComparison.Ordinal);
    }

    private static string MethodBody(string source, string startMarker, string nextMarker)
    {
        var start = source.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Missing method marker: {startMarker}");
        var end = source.IndexOf(nextMarker, start + startMarker.Length, StringComparison.Ordinal);
        Assert.True(end > start, $"Missing following method marker: {nextMarker}");
        return source[start..end];
    }

    private static string Read(string relativePath)
        => File.ReadAllText(Path.Combine(FindRepoRoot(), relativePath))
            .Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "MainWindow.xaml")) &&
                Directory.Exists(Path.Combine(directory.FullName, "tests", "ARSAS.Tests")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}
