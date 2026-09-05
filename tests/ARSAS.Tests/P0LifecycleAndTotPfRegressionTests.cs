namespace ARSAS.Tests;

public sealed class P0LifecycleAndTotPfRegressionTests
{
    [Fact]
    public void MainWindow_RuntimeFacade_OffloadsLifecycle_AndStopCanPreemptHungOperation()
    {
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        var main = Read("MainWindow.xaml.cs");

        Assert.Contains("namespace ArIED61850Tester;", facade, StringComparison.Ordinal);
        Assert.Contains("public sealed class Iec61850MonitorRuntime", facade, StringComparison.Ordinal);
        Assert.Contains("Services.Iec61850MonitorRuntime _inner", facade, StringComparison.Ordinal);
        Assert.Contains("ConcurrentDictionary<string, DeviceOperationSlot> _deviceSlots", facade, StringComparison.Ordinal);
        Assert.Contains("SemaphoreSlim OperationGate", facade, StringComparison.Ordinal);
        Assert.Contains("SemaphoreSlim StopGate", facade, StringComparison.Ordinal);
        Assert.Contains("RunDeviceOperationAsync", facade, StringComparison.Ordinal);
        Assert.Contains("RunPreemptiveStopAsync", facade, StringComparison.Ordinal);
        Assert.Contains("activeCancellation?.Cancel()", facade, StringComparison.Ordinal);
        Assert.Contains("without\n        // waiting for OperationGate", facade, StringComparison.Ordinal);
        Assert.Contains("linkedCancellation.Token.ThrowIfCancellationRequested()", facade, StringComparison.Ordinal);
        Assert.Contains("slot.Generation != generation", facade, StringComparison.Ordinal);
        Assert.Contains("DisposeBudget = TimeSpan.FromSeconds(3)", facade, StringComparison.Ordinal);
        Assert.Contains("Do not Dispose the per-device semaphores here", facade, StringComparison.Ordinal);

        // MainWindow intentionally uses the unqualified type from its own namespace, so the
        // responsive facade is the UI boundary without altering protocol/runtime ownership.
        Assert.Contains("private readonly Iec61850MonitorRuntime _runtime = new();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWindow_Close_KeepsUiBoundSessionMutationOnDispatcher_AndOffloadsPersistence()
    {
        var lifecycle = Read("IoListTestingWindow.P0Lifecycle.cs");

        Assert.Contains("Closing -= Window_Closing", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Closing += P0Window_Closing", lifecycle, StringComparison.Ordinal);
        Assert.Contains("var stopAll = Session.StopAll(", lifecycle, StringComparison.Ordinal);
        Assert.DoesNotContain("Task.Run(() =>\n                    Session.StopAll", lifecycle, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(Storage.SaveNow)", lifecycle, StringComparison.Ordinal);
        Assert.Contains("e.Cancel = true", lifecycle, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticReport_TotPf_UsesExactSemanticEngineAuthorityWithoutMmsFallback()
    {
        var engineLock = Read("engines/ARIEC61850.lock.json");
        var semanticBridge = Read("Services/NativeIec61850Client.SemanticReporting.cs");
        var runtime = Read("Services/Iec61850MonitorRuntime.cs");
        var projection = Read("Services/StaticDataSetReportProjectionAccumulator.cs");

        Assert.Contains("\"sourcePullRequest\": 111", engineLock, StringComparison.Ordinal);
        Assert.Contains("0d7525bd330900917fb9f6d15a46059dc3d7a70a", engineLock, StringComparison.Ordinal);
        Assert.Contains("P1 hardening", engineLock, StringComparison.Ordinal);
        Assert.Contains("report-value position", engineLock, StringComparison.Ordinal);
        Assert.Contains("omits MemberReference", engineLock, StringComparison.Ordinal);
        Assert.Contains("MmsSemanticReportValueProjector.Project", semanticBridge, StringComparison.Ordinal);
        Assert.Contains("session.StaticReportProjection.Project", runtime, StringComparison.Ordinal);
        Assert.Contains("MMS process fallback is disabled", runtime, StringComparison.Ordinal);
        Assert.Contains(".mag.f", projection, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(".instmag.f", projection, StringComparison.OrdinalIgnoreCase);
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

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

        throw new FileNotFoundException(relativePath);
    }
}
