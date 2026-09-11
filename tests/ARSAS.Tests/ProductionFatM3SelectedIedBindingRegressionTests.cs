namespace ARSAS.Tests;

public sealed class ProductionFatM3SelectedIedBindingRegressionTests
{
    [Fact]
    public void Start_LatchesCaptureTarget_WithoutTakingOverViewedContext()
    {
        var window = Read("IoListTestingWindow.xaml.cs");
        var coordinator = Read("Services/IoTesting/IoTestMultiSessionCoordinator.cs");

        var requested = window.IndexOf("var requestedIed = SelectedIed;", StringComparison.Ordinal);
        var latch = window.IndexOf("IoFatProductionControllerAdapter.LatchStartTarget(Project, requestedIed)", requested, StringComparison.Ordinal);
        var selected = window.IndexOf("var selectedIed = captureLease.Ied;", latch, StringComparison.Ordinal);
        var prepare = window.IndexOf("await engineeringWindow.PrepareIoTestIedForFatAsync(", selected, StringComparison.Ordinal);
        var start = window.IndexOf("IoFatProductionControllerAdapter.StartLatched(Project, Session, captureLease)", prepare, StringComparison.Ordinal);

        Assert.True(requested >= 0, "Start FAT must read the currently viewed IED before asynchronous preparation.");
        Assert.True(latch > requested, "Production FAT must freeze the IED and exact capture scope before asynchronous preparation.");
        Assert.True(selected > latch, "The preparation target must come from the frozen production capture lease.");
        Assert.True(prepare > selected, "Engineering acquisition must prepare the latched target.");
        Assert.True(start > prepare, "The production session must start from the same frozen lease after preparation.");

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
            "private void ProductionFat_MainWindowPropertyChanged",
            "private void SynchronizeProductionFatSelectedIed()");
        Assert.Contains("e.PropertyName == nameof(SelectedDevice)", propertyHandler, StringComparison.Ordinal);
        Assert.Contains("SynchronizeProductionFatSelectedIed();", propertyHandler, StringComparison.Ordinal);
        Assert.DoesNotContain("HasRunningSession", propertyHandler, StringComparison.Ordinal);

        var selectDevice = MethodBody(
            embedded,
            "internal void SelectEngineeringDeviceForEmbeddedFat",
            "internal void NotifyEmbeddedHostActivated");
        Assert.Contains("SelectedIed = null;", selectDevice, StringComparison.Ordinal);
        Assert.Contains("SelectedIed = match;", selectDevice, StringComparison.Ordinal);
        Assert.Contains("Session.SelectContext", window, StringComparison.Ordinal);
        Assert.Contains("public bool CanSelectIed => true;", window, StringComparison.Ordinal);

        var deviceId = selectDevice.IndexOf("device.DeviceId", StringComparison.Ordinal);
        var sclIedName = selectDevice.IndexOf("device.SclIedName", StringComparison.Ordinal);
        var displayName = selectDevice.IndexOf("device.Name", StringComparison.Ordinal);
        var ipAddress = selectDevice.IndexOf("device.IpAddress", StringComparison.Ordinal);
        Assert.True(deviceId >= 0, "Engineering DeviceId must be the strongest view identity.");
        Assert.True(sclIedName > deviceId, "SCL IED identity must follow DeviceId fallback.");
        Assert.True(displayName > sclIedName, "Engineering display name must be weaker than SCL identity.");
        Assert.True(ipAddress > displayName, "IP address must be the final view-binding fallback.");
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
