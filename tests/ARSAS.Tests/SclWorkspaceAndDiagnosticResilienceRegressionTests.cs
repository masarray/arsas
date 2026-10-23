using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclWorkspaceAndDiagnosticResilienceRegressionTests
{
    [Fact]
    public async Task DiagnosticReport_AllowsMultipleOfflineCardsWithSameEndpointText()
    {
        var first = new Iec61850MonitorDevice
        {
            Name = "IED-A",
            IpAddress = string.Empty,
            Port = 102,
            Status = "SCL model ready — bind endpoint"
        };
        var second = new Iec61850MonitorDevice
        {
            Name = "IED-A",
            IpAddress = string.Empty,
            Port = 102,
            Status = "SCL model ready — bind endpoint"
        };

        var report = await DiagnosticReportBuilder.BuildAsync(
            new[] { first, second },
            Array.Empty<DiagnosticEntry>(),
            first,
            CancellationToken.None);

        Assert.Contains($"IED count       : 2", report, StringComparison.Ordinal);
        Assert.Equal(2, Count(report, "Endpoint         : No endpoint"));
    }

    [Fact]
    public void EmergencyDiagnostic_RemainsCopyableWithoutNetworkOrAggregation()
    {
        var device = new Iec61850MonitorDevice
        {
            Name = "IED-A",
            IpAddress = string.Empty,
            Status = "Broken workspace state"
        };

        var report = DiagnosticReportBuilder.BuildEmergency(
            new[] { device },
            Array.Empty<DiagnosticEntry>(),
            device,
            new ArgumentException("duplicate endpoint"));

        Assert.Contains("ARSAS Emergency Diagnostic Report", report, StringComparison.Ordinal);
        Assert.Contains("ArgumentException: duplicate endpoint", report, StringComparison.Ordinal);
        Assert.Contains(device.DeviceId, report, StringComparison.Ordinal);
        Assert.Contains("endpoint=No endpoint", report, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenScl_ProjectsOneEngineeringCardPerIed_AndRestoresKnownEndpoint()
    {
        var source = Read("MainWindow.xaml.cs");

        Assert.Contains("SelectPrimarySclWorkspaces(document)", source, StringComparison.Ordinal);
        Assert.Contains(".GroupBy(workspace => workspace.IedName", source, StringComparison.Ordinal);
        Assert.Contains("one Engineering card is owned per IEDName", source, StringComparison.Ordinal);
        Assert.Contains("FindReusableSclDevice(document, workspace)", source, StringComparison.Ordinal);
        Assert.Contains("RestoreKnownSclEndpointIfAvailable(device, workspace)", source, StringComparison.Ordinal);
        Assert.Contains("TryLoadSuccessfulEndpointForIed", source, StringComparison.Ordinal);
        Assert.Contains("RemoveRedundantOfflineSclCards", source, StringComparison.Ordinal);

        Assert.DoesNotContain(
            "foreach (var workspace in document.Ieds)",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CopyDiagnostic_HasEmergencyFallbackInsteadOfSecondFailure()
    {
        var source = Read("MainWindow.xaml.cs");
        var builder = Read("Services/DiagnosticReportBuilder.cs");

        Assert.Contains("DiagnosticReportBuilder.BuildEmergency", source, StringComparison.Ordinal);
        Assert.Contains("Emergency diagnostic report copied", source, StringComparison.Ordinal);
        Assert.Contains("probe-free report", source, StringComparison.Ordinal);
        Assert.Contains("GroupBy(result => result.DeviceId", builder, StringComparison.Ordinal);
        Assert.DoesNotContain(
            "probes.ToDictionary(result => result.Endpoint",
            builder,
            StringComparison.Ordinal);
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var offset = 0;
        while ((offset = text.IndexOf(value, offset, StringComparison.Ordinal)) >= 0)
        {
            count++;
            offset += value.Length;
        }
        return count;
    }

    private static string Read(string relativePath)
        => File.ReadAllText(FindRepoFile(relativePath)).Replace("\r\n", "\n", StringComparison.Ordinal);

    private static string FindRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
