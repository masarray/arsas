using ArIED61850Tester.Services;
using System.Text.Json;

namespace ARSAS.Tests;

public sealed class OfflineDataSetSignalSelectionRegressionTests
{
    [Fact]
    public void DeviceInventoryMerge_FallsBackToOfflineSclDesignModel()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850DataSetSignalInventoryService.cs"));

        Assert.Contains(
            "device.LiveDiscoveryModel ?? device.SclWorkspace?.DesignModel",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "if (device.LiveDiscoveryModel is null)\n            return EmptyResult();",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SignalSelectionConstructor_PreservesExistingStaticFcdaDisplayIdentity()
    {
        var source = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.xaml.cs"));

        Assert.Contains(
            "if (string.IsNullOrWhiteSpace(signal.DisplayReference))",
            source,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "signal.DisplayReference = Iec61850MonitorPoint.StripIedNamePrefix(signal.ObjectReference, device.Name);\n            signal.PropertyChanged",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "Static DataSet inventory owns DisplayReference",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SignalSelectionRecovery_DoesNotOverwriteStaticFcdaDisplayIdentity()
    {
        var source = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.DataSetAuthority.cs"));

        Assert.Contains(
            "if (string.IsNullOrWhiteSpace(signal.DisplayReference))",
            source,
            StringComparison.Ordinal);
        Assert.Contains(
            "DisplayReference is the engine-authoritative static FCDA/FCD identity",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void SignalSelectionRecovery_RunsAgainAfterLegacyConstructorDisplayPreparation()
    {
        var source = File.ReadAllText(FindRepoFile("SignalSelectionWizardWindow.DataSetAuthority.cs"));

        Assert.Contains("protected override void OnContentRendered(EventArgs e)", source, StringComparison.Ordinal);
        Assert.Contains("RestoreAuthoritativeDataSetInventory();", source, StringComparison.Ordinal);
        Assert.Contains("SignalsView.Refresh();", source, StringComparison.Ordinal);
        Assert.Contains(
            "runtime primary leaf can survive as a replacement for a static FCDA/FCD",
            source,
            StringComparison.Ordinal);
    }

    [Fact]
    public void EngineLock_PreservesReportProjectionAndP62BHistoryAcrossLaterEnginePins()
    {
        var source = File.ReadAllText(FindRepoFile("engines/ARIEC61850.lock.json"));
        using var document = JsonDocument.Parse(source);
        var root = document.RootElement;

        Assert.Equal("masarray/ARIEC61850", root.GetProperty("repository").GetString());
        Assert.Equal("main", root.GetProperty("ref").GetString());
        Assert.Matches("^[0-9a-f]{40}$", root.GetProperty("commit").GetString() ?? string.Empty);
        Assert.True(root.GetProperty("sourcePullRequest").GetInt32() >= 89);
        Assert.Contains("one descriptor per static DataSet member", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("generic Boolean status structures", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DataRef-enabled InformationReport ordering", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("zero OptFlds", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quarantining unmapped canonical report metadata", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dynamic reporting before MMS polling", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("dynamic-attempt", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("rollback", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #87", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("baseline-safe static precedence", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #88", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DefineNamedVariableList", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("GetNamedVariableListAttributes", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("DeleteNamedVariableList", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("single-member", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cleanup evidence", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("PR #89", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("quarantines automatic full dynamic DataSet activation", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instMag/mag", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("instCVal/cVal", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ambiguous structures remain raw", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompletenessReport_SamplesEveryFailingDataSetAndSeparatesSemanticDescriptors()
    {
        var snapshot = new Iec61850DataSetCompletenessSnapshot(
            DataSetCount: 2,
            StaticMemberCount: 4,
            MandatoryInventoryCount: 6,
            RepresentedCount: 0,
            PrimaryLeafUnresolvedCount: 2,
            MissingReferences: new[]
            {
                "IEDApplication/LLN0$Analog[1] -> IEDLD0/MMXU1.A.phsA",
                "IEDApplication/LLN0$Analog[2] -> IEDLD0/MMXU1.A.phsB",
                "IEDApplication/LLN0$Digital[1] -> IEDADD/GGIO6.CBOpnd",
                "IEDApplication/LLN0$Digital[2] -> IEDADD/GGIO6.CBClsd"
            })
        {
            DataSets = new[]
            {
                new Iec61850DataSetCompletenessDataSetSnapshot(
                    "IEDApplication/LLN0$Analog",
                    2,
                    0,
                    new[]
                    {
                        "IEDApplication/LLN0$Analog[1] -> IEDLD0/MMXU1.A.phsA",
                        "IEDApplication/LLN0$Analog[2] -> IEDLD0/MMXU1.A.phsB"
                    }),
                new Iec61850DataSetCompletenessDataSetSnapshot(
                    "IEDApplication/LLN0$Digital",
                    2,
                    0,
                    new[]
                    {
                        "IEDApplication/LLN0$Digital[1] -> IEDADD/GGIO6.CBOpnd",
                        "IEDApplication/LLN0$Digital[2] -> IEDADD/GGIO6.CBClsd"
                    })
            }
        };

        var lines = Iec61850DataSetCompletenessDiagnostic.FormatReportLines(snapshot, maxMissing: 4).ToArray();

        Assert.Contains(lines, line => line.Contains("Semantic descriptors : 6", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("LLN0$Analog", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("LLN0$Digital", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("MMXU1.A.phsA", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("GGIO6.CBOpnd", StringComparison.Ordinal));
    }

    [Fact]
    public void UiDispatcherError_RoutesFullExceptionEvidence()
    {
        var appSource = File.ReadAllText(FindRepoFile("App.xaml.cs"));
        var detailSource = File.ReadAllText(FindRepoFile("MainWindow.UiExceptionDiagnostics.cs"));

        Assert.Contains("ReportUnexpectedUiErrorWithStackTrace(exception)", appSource, StringComparison.Ordinal);
        Assert.Contains("Message = exception.ToString()", detailSource, StringComparison.Ordinal);
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
