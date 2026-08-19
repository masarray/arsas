using System.Text.Json;

namespace ARSAS.Tests;

public sealed class G1ControlCorrectnessRegressionTests
{
    [Fact]
    public void EngineLock_PinsExactG1ControlEngine()
    {
        var root = RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "engines", "ARIEC61850.lock.json")));
        var json = doc.RootElement;
        Assert.Equal("masarray/ARIEC61850", json.GetProperty("repository").GetString());
        Assert.Equal("main", json.GetProperty("ref").GetString());
        Assert.Equal("e2c26fc4c081b785c2fe12005ada26ba9580bd61", json.GetProperty("commit").GetString());
        Assert.Equal(90, json.GetProperty("sourcePullRequest").GetInt32());
        Assert.Contains("signed primitive constraints", json.GetProperty("purpose").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void LocalControlPreparationFailure_IsExplicitlyNotSent()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "NativeIec61850Client.cs"));
        Assert.Contains("wireRequestBeforeControl", source, StringComparison.Ordinal);
        Assert.Contains("ControlNotSentFailure", source, StringComparison.Ordinal);
        Assert.Contains("CompletionState = \"NotSent\"", source, StringComparison.Ordinal);
        Assert.Contains("Stage = \"NOT SENT TO IED\"", source, StringComparison.Ordinal);
        Assert.Contains("before any MMS control request was built or sent", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ControlWireUnknownFailure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Runtime_EmitsExactWireEvidenceOnlyWhenReturnedByControlStack()
    {
        var source = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("CONTROL_WIRE_REQUEST:", source, StringComparison.Ordinal);
        Assert.Contains("CONTROL_WIRE_RESPONSE:", source, StringComparison.Ordinal);
        Assert.Contains("MMS request encoded / no response captured", source, StringComparison.Ordinal);
        Assert.Contains("MMS response received", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MMS command submitted", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void G1_DoesNotReenableDynamicReportingOrChangeReconnectPolicy()
    {
        var engineLock = File.ReadAllText(Path.Combine(RepoRoot(), "engines", "ARIEC61850.lock.json"));
        Assert.Contains("PR #89 quarantines automatic full dynamic DataSet activation", engineLock, StringComparison.OrdinalIgnoreCase);

        var runtime = File.ReadAllText(Path.Combine(RepoRoot(), "Services", "Iec61850MonitorRuntime.cs"));
        Assert.Contains("SmartReconnectPolicy", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicBrcb = true", runtime, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicUrcb = true", runtime, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current != null)
        {
            if (File.Exists(Path.Combine(current.FullName, "ArIED61850Tester.csproj")))
                return current.FullName;
            current = current.Parent;
        }
        throw new DirectoryNotFoundException("ARSAS repository root not found.");
    }
}
