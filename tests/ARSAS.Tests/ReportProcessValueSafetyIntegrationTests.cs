namespace ARSAS.Tests;

public sealed class ReportProcessValueSafetyIntegrationTests
{
    [Fact]
    public void ReportSafetyGate_RunsBefore_ReportValueMutation()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));

        var gate = source.IndexOf("ReportProcessValueSafety.IsSafe", StringComparison.Ordinal);
        var rejection = source.IndexOf("REPORT_VALUE_REJECTED", StringComparison.Ordinal);
        var apply = source.IndexOf("ApplyValueUpdate(", gate >= 0 ? gate : 0, StringComparison.Ordinal);

        Assert.True(gate >= 0, "Report process-value safety gate is missing from the runtime report ingestion path.");
        Assert.True(rejection > gate, "Rejected report values must emit explicit diagnostic evidence.");
        Assert.True(apply > rejection, "Report value safety must execute before ApplyValueUpdate can mutate state or raise SOE events.");
        Assert.Contains("state.ReportChangeVerified = false", source, StringComparison.Ordinal);
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
