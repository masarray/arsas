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
    public void P0_RuntimeObserver_IsEventDriven_FailIsolated_AndWpfFree()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0FatRecovery.cs"));
        var start = source.IndexOf("private void P0FatRuntimePointUpdated", StringComparison.Ordinal);
        var end = source.IndexOf("private void P0DrainFatRuntimeProjection", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start);
        var callback = source[start..end];

        Assert.Contains("_runtime.PointUpdated += P0FatRuntimePointUpdated", source, StringComparison.Ordinal);
        Assert.Contains("snapshot.Point.PointKey", callback, StringComparison.Ordinal);
        Assert.Contains("Volatile.Read(ref _p0FatProjectionActive)", callback, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.BeginInvoke", callback, StringComparison.Ordinal);
        Assert.Contains("catch", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("_loadedIoFatWindow", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("IsLoaded", callback, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", callback, StringComparison.Ordinal);
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
        var autoConnect = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("device.HasDiscoveryCache = true", recovery, StringComparison.Ordinal);
        Assert.Contains("_pendingSharedStaticSelectionAssignments", shared, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device);", shared, StringComparison.Ordinal);
        Assert.Contains("ApplyStaticDataSetSelection(device);", append, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", shared, StringComparison.Ordinal);

        // Direct SCL FAT import must remain one shared Engineering model: connect/associate
        // with the imported ARIEC workspace and do not silently fall back to discovery.
        Assert.Contains("AttachIoFatSclRuntimeAuthority(ied, device)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("ConnectIoFatUsingPreparedSclAsync(device)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("Full discovery was intentionally not started", autoConnect, StringComparison.Ordinal);
        Assert.Contains("sharedStaticDataSetAuthority", autoConnect, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", autoConnect, StringComparison.Ordinal);
        Assert.Contains("hasBlockingNonSclMiss", autoConnect, StringComparison.Ordinal);
        Assert.Contains("!IoTestSignalSelectionService.IsDirectSclAuthority(point)", autoConnect, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_EngineeringPlay_UsesPreparedSclModel_ThenStartsStaticDataSetMonitorWithoutDiscovery()
    {
        var main = File.ReadAllText(FindRepoFile("MainWindow.xaml.cs"));
        var playStart = main.IndexOf("private async void IedPlayAction_Click", StringComparison.Ordinal);
        var playEnd = main.IndexOf("private async void IedStopAction_Click", playStart, StringComparison.Ordinal);
        Assert.True(playStart >= 0 && playEnd > playStart);
        var play = main[playStart..playEnd];

        Assert.Contains("device.HasDiscoveryCache && device.Signals.Count > 0", play, StringComparison.Ordinal);
        Assert.Contains("await ConnectUsingSavedModelAsync(device)", play, StringComparison.Ordinal);
        Assert.Contains("await ConnectAndConfigureDeviceAsync(device, openWizard: discoveryWillOpenWizard)", play, StringComparison.Ordinal);
        Assert.Contains("await StartDeviceMonitorAsync(device)", play, StringComparison.Ordinal);
        Assert.True(
            play.IndexOf("ConnectUsingSavedModelAsync(device)", StringComparison.Ordinal) <
            play.IndexOf("ConnectAndConfigureDeviceAsync(device", StringComparison.Ordinal));

        var runtime = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        var monitorStart = runtime.IndexOf("public async Task<IReadOnlyList<Iec61850MonitorPoint>> StartMonitoringAsync", StringComparison.Ordinal);
        var monitorEnd = runtime.IndexOf("private ", monitorStart, StringComparison.Ordinal);
        Assert.True(monitorStart >= 0 && monitorEnd > monitorStart);
        var monitor = runtime[monitorStart..monitorEnd];

        Assert.Contains("Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device)", monitor, StringComparison.Ordinal);
        Assert.Contains("!staticDataSetReportOnly || !string.IsNullOrWhiteSpace(signal.DataSetReference)", monitor, StringComparison.Ordinal);
        Assert.DoesNotContain("ConnectAndDiscoverAsync", monitor, StringComparison.Ordinal);
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
    public void P0_StructuredStaticMember_BindsThroughExactDisplayIdentityToRuntimeLeaf()
    {
        var source = File.ReadAllText(FindRepoFile("Services/IoTesting/IoTestLiveBindingService.cs"));

        Assert.Contains("ExactSignalIdentityMatches", source, StringComparison.Ordinal);
        Assert.Contains("signal.DisplayReference", source, StringComparison.Ordinal);
        Assert.Contains("signal.ObjectReference", source, StringComparison.Ordinal);
        Assert.Contains("Exact static DataSet member resolved to one active scalar runtime point", source, StringComparison.Ordinal);
        Assert.Contains("CanonicalFatPresentationValue", source, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_AnalogCapture_IsAutomatic_AndNormalCellCaptureTemplateIsRemoved()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0BenchUx.cs"));
        var coordinator = File.ReadAllText(FindRepoFile("Services/IoTesting/FatAutoCaptureCoordinator.cs"));

        Assert.Contains("column.CellTemplate = BuildP0EvidenceValueTemplate", ux, StringComparison.Ordinal);
        Assert.Contains("Intentionally no normal Capture button", ux, StringComparison.Ordinal);
        Assert.Contains("P0FatCanonicalValueConverter", ux, StringComparison.Ordinal);
        Assert.Contains("AnalogStableSampleCount = 3", coordinator, StringComparison.Ordinal);
        Assert.Contains("AnalogRelativeSettlingFraction = 0.0005d", coordinator, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_FirstVisibleFatFrame_AlreadyUsesV2CanonicalNoCaptureTemplates()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0BenchUx.cs"));
        var install = ux.IndexOf("window.InstallFatV2WorkspaceUx();", StringComparison.Ordinal);
        var apply = ux.IndexOf("window.ApplyP0BenchUx();", StringComparison.Ordinal);

        Assert.True(install >= 0);
        Assert.True(apply > install);
        Assert.DoesNotContain("ContentRendered +=", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("DispatcherPriority.ContextIdle", ux, StringComparison.Ordinal);
        Assert.DoesNotContain("Dispatcher.BeginInvoke", ux, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_HeaderUsesPrimaryAndSecondaryActionRows()
    {
        var ux = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0BenchUx.cs"));

        Assert.Contains("_p0PrimaryHeaderActions", ux, StringComparison.Ordinal);
        Assert.Contains("_p0SecondaryHeaderActions", ux, StringComparison.Ordinal);
        Assert.Contains("ConfigureP0AdaptiveHeaderActions", ux, StringComparison.Ordinal);
        Assert.Contains("_clockSyncEvidenceText", ux, StringComparison.Ordinal);
    }

    [Fact]
    public void P0_CloseQuiescesLiveProjectionBeforeDurableSave()
    {
        var lifecycle = File.ReadAllText(FindRepoFile("IoListTestingWindow.P0Lifecycle.cs"));

        Assert.Contains("SuspendIoFatRuntimeProjection(this)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Dispatcher.Yield(DispatcherPriority.Render)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(Storage.SaveNow)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("ResumeIoFatRuntimeProjection(this)", lifecycle, StringComparison.Ordinal);
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
