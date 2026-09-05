namespace ARSAS.Tests;

public sealed class P0LifecycleAndTotPfRegressionTests
{
    [Fact]
    public void MainWindow_RuntimeFacade_OffloadsLifecycleAndSerializesPerIedOnly()
    {
        var facade = Read("Services/UiResponsiveIec61850MonitorRuntimeFacade.cs");
        var main = Read("MainWindow.xaml.cs");

        Assert.Contains("namespace ArIED61850Tester;", facade, StringComparison.Ordinal);
        Assert.Contains("public sealed class Iec61850MonitorRuntime", facade, StringComparison.Ordinal);
        Assert.Contains("Services.Iec61850MonitorRuntime _inner", facade, StringComparison.Ordinal);
        Assert.Contains("ConcurrentDictionary<string, SemaphoreSlim> _deviceLifecycleGates", facade, StringComparison.Ordinal);
        Assert.Contains("Task.Run(operation, CancellationToken.None)", facade, StringComparison.Ordinal);
        Assert.Contains("RunDeviceLifecycleAsync", facade, StringComparison.Ordinal);
        Assert.Contains("DisposeBudget = TimeSpan.FromSeconds(3)", facade, StringComparison.Ordinal);

        // MainWindow intentionally uses the unqualified type from its own namespace, so the
        // responsive facade is the UI boundary without altering protocol/runtime ownership.
        Assert.Contains("private readonly Iec61850MonitorRuntime _runtime = new();", main, StringComparison.Ordinal);
    }

    [Fact]
    public void FatWindow_Close_DoesNotSealJournalsOrSaveOnDispatcher()
    {
        var lifecycle = Read("IoListTestingWindow.P0Lifecycle.cs");

        Assert.Contains("Closing -= Window_Closing", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Closing += P0Window_Closing", lifecycle, StringComparison.Ordinal);
        Assert.Contains("await Task.Run(() =>", lifecycle, StringComparison.Ordinal);
        Assert.Contains("Session.StopAll", lifecycle, StringComparison.Ordinal);
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
        Assert.Contains("69bfe70e2c779c7e8268af087bd1a3a38986c0fc", engineLock, StringComparison.Ordinal);
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
