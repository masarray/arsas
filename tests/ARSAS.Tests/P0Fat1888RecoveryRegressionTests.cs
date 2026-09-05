namespace ARSAS.Tests;

public sealed class P0Fat1888RecoveryRegressionTests
{
    [Fact]
    public void FatLiveProjection_IsEventDrivenAndNeverChangesVirtualizationMode()
    {
        var runtime = File.ReadAllText(FindRepoFile("MainWindow.IoFatRuntimeAuthority.cs"));
        var presentation = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0Presentation.cs"));

        Assert.Contains("_runtime.PointUpdated += Runtime_IoFatRuntimeAuthorityPointUpdated", runtime, StringComparison.Ordinal);
        Assert.Contains("IoFatValuePresentation.Canonicalize(snapshot.Value)", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("SetVirtualizationMode", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("SetVirtualizationMode", presentation, StringComparison.Ordinal);
        Assert.DoesNotContain("VirtualizationMode", presentation, StringComparison.Ordinal);
    }

    [Fact]
    public void BooleanPresentation_IsCanonicalTrueFalseFromFatBoundary()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoFatValuePresentation.cs"));

        Assert.Contains("return boolean ? \"True\" : \"False\";", source, StringComparison.Ordinal);
        Assert.Contains("bool.TryParse", source, StringComparison.Ordinal);
        Assert.Contains("return text;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void EngineeringCommandChecks_AreInitializedOnceWithoutTimerPolling()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0CommandDefaults.cs"));

        Assert.Contains("_p0CommandDefaultsInitialized.Add(signal)", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlInterlockCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true;", source, StringComparison.Ordinal);
        Assert.Contains("CommandSignals.CollectionChanged", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_uiFlushTimer", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FatFirstStaticDataSetSelection_CannotBeDemotedToHybridByGenericMarker()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));

        Assert.Contains("_pendingSharedSclSelectionMode == SclSignalSelectionMode.StaticDataSet", source, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device);", source, StringComparison.Ordinal);
        Assert.Contains("if (IsSharedStaticDataSetAuthority(device))", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseHybrid(device);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportBackedAnalog_AutoCapturesWhilePollingRetainsSettlingGate()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/FatAutoCaptureCoordinator.cs"));

        Assert.Contains("IsAuthoritativeReportObservation(observation)", source, StringComparison.Ordinal);
        Assert.Contains("Report-backed analog Value 1 captured automatically", source, StringComparison.Ordinal);
        Assert.Contains("Report-backed analog Value 2 captured automatically", source, StringComparison.Ordinal);
        Assert.Contains("AnalogStableSampleCount = 3", source, StringComparison.Ordinal);
        Assert.Contains("next.Count < AnalogStableSampleCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0Patch_DoesNotTouchGoldenAriecPinOrIntroduceLegacyScrollHack()
    {
        var lockFile = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));
        var p0Files = new[]
        {
            "MainWindow.IoFatRuntimeAuthority.cs",
            "IoListTestingWindow.P0Presentation.cs",
            "MainWindow.P0CommandDefaults.cs"
        };

        Assert.Contains("11ab2304482600c19ba979f4fc9021ddb46b9af9", lockFile, StringComparison.OrdinalIgnoreCase);
        foreach (var path in p0Files)
        {
            var source = File.ReadAllText(FindRepoFile(path));
            Assert.DoesNotContain("VirtualizingPanel.SetVirtualizationMode", source, StringComparison.Ordinal);
            Assert.DoesNotContain("VirtualizationMode.Standard", source, StringComparison.Ordinal);
        }
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
