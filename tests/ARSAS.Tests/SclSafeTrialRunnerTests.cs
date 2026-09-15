using AR.Iec61850.Mms;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclSafeTrialRunnerTests
{
    [Fact]
    public void CommandParser_UsesExplicitSclIdentityAndBoundedBatchDefault()
    {
        var source = Path.Combine(Path.GetTempPath(), "trial.cid");
        var args = new[]
        {
            SclSafeTrialCommand.Switch,
            source,
            "IED01",
            "AP1",
            "192.0.2.10"
        };

        Assert.True(SclSafeTrialCommand.TryParse(args, out var command, out var error), error);
        Assert.NotNull(command);
        Assert.Equal(Path.GetFullPath(source), command!.SclPath);
        Assert.Equal("IED01", command.IedName);
        Assert.Equal("AP1", command.AccessPointName);
        Assert.Equal("192.0.2.10", command.Host);
        Assert.Equal(102, command.Port);
        Assert.Equal(MmsReadBatchCodec.MaximumVariableReferencesPerRead, command.MaximumVariableReferencesPerRead);
        Assert.EndsWith(".json", command.EvidencePath, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CommandParser_SingleReferenceMode_UsesExactlyOneVariablePerRead()
    {
        var args = new[]
        {
            SclSafeTrialCommand.SingleReferenceSwitch,
            "trial.cid",
            "IED01",
            "AP1",
            "192.0.2.10"
        };

        Assert.True(SclSafeTrialCommand.TryParse(args, out var command, out var error), error);
        Assert.NotNull(command);
        Assert.Equal(1, command!.MaximumVariableReferencesPerRead);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("not-a-port")]
    public void CommandParser_RejectsInvalidMmsPort(string port)
    {
        var args = new[]
        {
            SclSafeTrialCommand.Switch,
            "trial.cid",
            "IED01",
            "AP1",
            "192.0.2.10",
            port
        };

        Assert.False(SclSafeTrialCommand.TryParse(args, out var command, out var error));
        Assert.Null(command);
        Assert.Contains("port", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void TrialRunner_SourceContract_HasNoDiscoveryWriteControlOrReportingEntryPoint()
    {
        var source = File.ReadAllText(FindRepoFile("Services/SclSafeTrialRunner.cs"));

        Assert.Contains("ConnectUsingSclAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverAsync(", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DiscoverSignalsAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("WriteAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Operate", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartReportMonitor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", source, StringComparison.Ordinal);
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
