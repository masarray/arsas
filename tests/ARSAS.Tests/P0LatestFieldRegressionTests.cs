using ArIED61850Tester.Services.IoTesting;

namespace ARSAS.Tests;

public sealed class P0LatestFieldRegressionTests
{
    [Theory]
    [InlineData("A", "IEDLD/MMXU1.A.phsA.cVal.mag.f", "A PhsA")]
    [InlineData("A", "IEDLD/MMXU1.A.phsB.cVal.mag.f", "A PhsB")]
    [InlineData("A", "IEDLD/MMXU1.A.phsC.cVal.mag.f", "A PhsC")]
    [InlineData("ThdA", "IEDLD/MMXU1.ThdA.phsA.instMag.f", "ThdA PhsA")]
    [InlineData("ThdPPV", "IEDLD/MMXU1.ThdPPV.phsAB.instMag.f", "ThdPPV PhsAB")]
    public void FatSignalDisplayName_PreservesDoFamilyAndAddsCompactPhaseContext(
        string signalName,
        string reference,
        string expected)
    {
        Assert.Equal(expected, IoFatSignalDisplayNameFormatter.Format(signalName, reference));
    }

    [Fact]
    public void RemovedFatSignals_AreFilteredFromActiveGridAndTestIsFailClosed()
    {
        var source = File.ReadAllText(FindRepoFile("IoListTestingWindow.ActiveFatView.cs"));

        Assert.Contains("Filter = item => item is IoTestPointPlan point && point.IsIncludedInFat", source, StringComparison.Ordinal);
        Assert.Contains("if (!point.IsIncludedInFat && point.TestEnabled)", source, StringComparison.Ordinal);
        Assert.Contains("point.TestEnabled = false", source, StringComparison.Ordinal);
        Assert.Contains("_activeFatView?.Refresh()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DownloadedFaultRecords_HaveASelectColumnFallbackIntoSafeRedownloadState()
    {
        var source = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadSelectionAuthority.cs"));
        var transfer = File.ReadAllText(FindRepoFile("FaultRecordWindow.RedownloadUx.cs"));

        Assert.Contains("cell.Column.DisplayIndex != 0", source, StringComparison.Ordinal);
        Assert.Contains("candidate.LocalState != FaultRecordLocalState.Downloaded", source, StringComparison.Ordinal);
        Assert.Contains("_redownloadSelections.Add(recordId)", source, StringComparison.Ordinal);
        Assert.Contains(".arsas-redownload-", transfer, StringComparison.Ordinal);
        Assert.Contains("CommitFreshRecordDirectory", transfer, StringComparison.Ordinal);
    }

    [Fact]
    public void CommandDefaults_KeepInterlockAndSyncCheckedUntilCtlModelIsResolved()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0CommandDefaults.cs"));

        Assert.Contains("signal.ControlInterlockCheck = true", source, StringComparison.Ordinal);
        Assert.Contains("signal.ControlSynchroCheck = true", source, StringComparison.Ordinal);
        Assert.Contains("ControlModelResolved", source, StringComparison.Ordinal);
        Assert.Contains("FinalizeP0CommandDefaults", source, StringComparison.Ordinal);
        Assert.Contains("signal.PropertyChanged -= P0CommandSignal_PropertyChanged", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AssociationWatchdog_DetectsOfflineWithoutCyclicMmsProcessReadsAndArmsReconnect()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.P0AssociationLiveness.cs"));

        Assert.Contains("TryPingEndpointAsync", source, StringComparison.Ordinal);
        Assert.Contains("failures < 2", source, StringComparison.Ordinal);
        Assert.Contains("device.Status = \"Offline\"", source, StringComparison.Ordinal);
        Assert.Contains("_associationReconnectWanted.Add", source, StringComparison.Ordinal);
        Assert.Contains("IsMmsEndpointReachableAsync", source, StringComparison.Ordinal);
        Assert.Contains("ConnectUsingSavedModelAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ReadValueAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("IecSignalReadResolver", source, StringComparison.Ordinal);
    }

    [Fact]
    public void MultiRcbExport_MergesSelectedReportControlsAndNativeDataSetsOnly()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.MultiRcbExport.cs"));
        var window = File.ReadAllText(FindRepoFile("RcbMultiExportWindow.cs"));

        Assert.Contains("IReadOnlyList<RcbExportRow> selectedRows", source, StringComparison.Ordinal);
        Assert.Contains("MergeScopedLnChildren(merged, additional, \"DataSet\")", source, StringComparison.Ordinal);
        Assert.Contains("MergeScopedLnChildren(merged, additional, \"ReportControl\")", source, StringComparison.Ordinal);
        Assert.Contains("ValidateGenericMultiRcbDocument", source, StringComparison.Ordinal);
        Assert.Contains("ExportLegacySasRcbAsync", source, StringComparison.Ordinal);
        Assert.Contains("row.IsSelected", window, StringComparison.Ordinal);
        Assert.Contains("select one or more native RCBs", window, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("CreateDynamic", source, StringComparison.OrdinalIgnoreCase);
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
        throw new FileNotFoundException(relativePath);
    }
}
