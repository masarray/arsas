using System.Text.Json;

namespace ARSAS.Tests;

public sealed class P0Build1888RecoveryRegressionTests
{
    [Fact]
    public void P0_KeepsExactArIec61850GoldenPin()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json")));
        Assert.Equal(
            "11ab2304482600c19ba979f4fc9021ddb46b9af9",
            document.RootElement.GetProperty("commit").GetString());
    }

    [Fact]
    public void P0_LiveProjection_IsEventDriven_Canonical_AndNeverMutatesVirtualization()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatRecovery.cs"));

        Assert.Contains("_runtime.PointUpdated += P0FatRuntimePointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("DispatcherPriority.DataBind", source, StringComparison.Ordinal);
        Assert.Contains("? boolean ? \"True\" : \"False\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("SetVirtualizationMode", source, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizationMode.Standard", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_SclFromFat_ReusesEngineeringModel_AndPreservesStaticDataSetAuthority()
    {
        var recovery = File.ReadAllText(FindRepoFile("MainWindow.P0FatRecovery.cs"));
        var shared = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var append = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.SclAppend.cs"));

        Assert.Contains("device.HasDiscoveryCache = true", recovery, StringComparison.Ordinal);
        Assert.Contains("_pendingSharedStaticSelectionAssignments", shared, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device);", shared, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device);", append, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", shared, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_CommandDefaults_EnableInterlockAndSynchronismOnce()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0CommandDefaults.cs"));

        Assert.Contains("signal.ControlInterlockCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("_p0CommandDefaultsInitialized.Add(signal)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_AnalogCapture_RemainsAutomatic_AndNormalManualCaptureIsHidden()
    {
        var recovery = File.ReadAllText(FindRepoFile("MainWindow.P0FatRecovery.cs"));
        var coordinator = File.ReadAllText(FindRepoFile("Services/IoTesting/FatAutoCaptureCoordinator.cs"));

        Assert.Contains("FatAutoCaptureCoordinator", recovery, StringComparison.Ordinal);
        Assert.Contains("\"✓ Capture\"", recovery, StringComparison.Ordinal);
        Assert.Contains("button.Visibility = Visibility.Collapsed", recovery, StringComparison.Ordinal);
        Assert.Contains("AnalogStableSampleCount = 3", coordinator, StringComparison.Ordinal);
        Assert.Contains("AnalogRelativeSettlingFraction = 0.0005d", coordinator, StringComparison.Ordinal);
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
