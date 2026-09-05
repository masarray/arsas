using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticDataSetReportOnlyModeRegressionTests
{
    [Fact]
    public void StaticMode_DisablesDynamicWrites_AndManualModeRestoresPriorValue()
    {
        var device = new Iec61850MonitorDevice { AllowDynamicDataSetWrites = true };
        Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device);
        Assert.True(Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device));
        Assert.False(device.AllowDynamicDataSetWrites);
        Iec61850MonitoringModeRegistry.UseHybrid(device);
        Assert.False(Iec61850MonitoringModeRegistry.IsStaticDataSetReportOnly(device));
        Assert.True(device.AllowDynamicDataSetWrites);
    }

    [Fact]
    public void StaticSelectionPolicy_StillRejectsBrowsedAndControlSignals()
    {
        var dataSetLeaf = Signal("IEDLD/MMXU1.TotW.mag.f", "IEDLD/LLN0.Analog");
        var browsedLeaf = Signal("IEDLD/MMXU1.Hz.mag.f", string.Empty);
        var control = Signal("IEDLD/CSWI1.Pos", "IEDLD/LLN0.Digital");
        control.IsControlSignal = true;
        Assert.True(Iec61850StaticDataSetSelectionPolicy.IsEligible(dataSetLeaf));
        Assert.False(Iec61850StaticDataSetSelectionPolicy.IsEligible(browsedLeaf));
        Assert.False(Iec61850StaticDataSetSelectionPolicy.IsEligible(control));
    }

    [Fact]
    public void RuntimeContract_StaticDataSetMode_DoesNotScheduleCyclicMmsProcessPolling()
    {
        var source = File.ReadAllText(FindRepoFile("Services/Iec61850MonitorRuntime.cs"));
        Assert.Contains("Static DataSet acquisition ready", source, StringComparison.Ordinal);
        Assert.Contains("cyclic MMS process polling=0", source, StringComparison.Ordinal);
        Assert.Contains("state.NextPollUtc = DateTime.MaxValue", source, StringComparison.Ordinal);
        Assert.Contains("process leaves are never repurposed as cyclic MMS heartbeat reads", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSclStaticSelection_UsesOnlyExactRcbBackedAriecMembershipRows()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var authority = File.ReadAllText(FindRepoFile("Services/Iec61850StaticDataSetAuthoritySelection.cs"));

        Assert.Contains("Iec61850DataSetSignalInventoryService.EnsureMandatorySignals(device)", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850StaticDataSetAuthoritySelection.Build(device)", source, StringComparison.Ordinal);
        Assert.Contains("signal.IsSelected = authoritativeSignals.Contains(signal)", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850StaticDataSetSelectionPolicy.IsEligible(signal)", source, StringComparison.Ordinal);
        Assert.DoesNotContain("UseStaticDataSetWithMmsFallback", source, StringComparison.Ordinal);
        Assert.DoesNotContain("fallback remains available", source, StringComparison.OrdinalIgnoreCase);

        Assert.Contains("var authorityModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", authority, StringComparison.Ordinal);
        Assert.Contains("Iec61850DataSetSignalInventoryProjection.GetMandatorySignals(authorityModel)", authority, StringComparison.Ordinal);
        Assert.Contains("BuildReportBackedDataSetReferences(device)", authority, StringComparison.Ordinal);
        Assert.Contains("reportBackedDataSets.Contains", authority, StringComparison.Ordinal);
        Assert.Contains("var configurationModel = device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", authority, StringComparison.Ordinal);
        Assert.Contains("AddReportBackedDataSets(configurationModel, result)", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("AddReportBackedDataSets(device.SclWorkspace?.DesignModel, result)", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("AddReportBackedDataSets(device.LiveDiscoveryModel, result)", authority, StringComparison.Ordinal);
        Assert.Contains("LiteralEquals(signal.DataSetReference, membership.DataSetReference)", authority, StringComparison.Ordinal);
        Assert.Contains("LiteralEquals(signal.DisplayReference, memberReference)", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("StartsWith(memberReference", authority, StringComparison.Ordinal);
        Assert.DoesNotContain("Contains(memberReference", authority, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("IEDApplication/LLN0$BR$Buffer", "IEDApplication/LLN0$BR$Buffer01", true)]
    [InlineData("IEDApplication/LLN0$BR$Buffer", "IEDApplication/LLN0$BR$Buffer02", true)]
    [InlineData("IEDApplication/LLN0$BR$Buffer02", "IEDApplication/LLN0$BR$Buffer02", true)]
    [InlineData("IEDApplication/LLN0$BR$Buffer02", "IEDApplication/LLN0$BR$Buffer01", false)]
    [InlineData("IEDApplication/LLN0$BR$Buffer", "IEDApplication/LLN0$BR$Other01", false)]
    [InlineData("IEDApplication/LLN0$BR$Buffer0", "IEDApplication/LLN0$BR$Buffer01", false)]
    public void StaticRcbMatcher_AcceptsOnlyExactOrDecimalIndexedFamilyInstances(
        string configured,
        string live,
        bool expected)
    {
        Assert.Equal(expected, Iec61850StaticRcbReferenceMatcher.IsConfiguredOrIndexedInstance(configured, live));
    }

    [Fact]
    public void StaticRcbMatcher_ExactIdentityAlwaysRanksBeforeIndexedFamily()
    {
        const string configured = "IEDApplication/LLN0$BR$Buffer";
        Assert.Equal(0, Iec61850StaticRcbReferenceMatcher.MatchRank(configured, configured));
        Assert.Equal(1, Iec61850StaticRcbReferenceMatcher.MatchRank(configured, "IEDApplication/LLN0$BR$Buffer01"));
        Assert.Equal(int.MaxValue, Iec61850StaticRcbReferenceMatcher.MatchRank(configured, "IEDApplication/LLN0$BR$Other01"));
    }

    [Fact]
    public void DeterministicStaticPlanner_PreservesSclConfigurationAndConcreteLiveRcbAuthority()
    {
        var source = File.ReadAllText(FindRepoFile("Services/NativeIec61850Client.StaticDataSetReporting.cs"));

        Assert.Contains("device.SclWorkspace?.DesignModel ?? device.LiveDiscoveryModel", source, StringComparison.Ordinal);
        Assert.Contains("var configurationModel = projectionModel", source, StringComparison.Ordinal);
        Assert.Contains("Iec61850StaticRcbReferenceMatcher.MatchRank", source, StringComparison.Ordinal);
        Assert.Contains("SelectMany(configured => discovery.ReportInventory.ReportControls", source, StringComparison.Ordinal);
        Assert.Contains("ReportControlReference = concreteReportReference", source, StringComparison.Ordinal);
        Assert.Contains("Install InformationReport receiver before enabling the RCB", source, StringComparison.Ordinal);
        Assert.Contains("enable RptEna, then request GI after receiver registration", source, StringComparison.Ordinal);
        Assert.DoesNotContain("configurationModels", source, StringComparison.Ordinal);
        Assert.DoesNotContain("var configured = configuredReports[0]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("AllowDynamicDataSetWrites = true", source, StringComparison.Ordinal);
        Assert.DoesNotContain("PollingPointKeys = points.Select", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticDataSetRegressionGuard_PreservesFa16ReportOnlyDirection()
    {
        var shared = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var registry = File.ReadAllText(FindRepoFile("Services/Iec61850MonitoringModeRegistry.cs"));

        Assert.Contains("UseStaticDataSetReportOnly(device)", shared, StringComparison.Ordinal);
        Assert.Contains("Static DataSet report-only authority selected", shared, StringComparison.Ordinal);
        Assert.Contains("cyclic MMS process polling and dynamic DataSet writes remain disabled", shared, StringComparison.Ordinal);
        Assert.Contains("state.StaticDataSetReportOnly = true", registry, StringComparison.Ordinal);
        Assert.DoesNotContain("UseStaticDataSetWithMmsFallback", registry, StringComparison.Ordinal);
    }

    [Fact]
    public void FatMode_ReusesSharedStaticDataSetAuthority_InsteadOfDemotingToMms()
    {
        var shared = File.ReadAllText(FindRepoFile("MainWindow.SharedSclWorkspace.cs"));
        var fatMonitor = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.MultiIedMonitor.cs"));

        Assert.Contains("_sharedSclStaticDataSetAuthorityDeviceIds", shared, StringComparison.Ordinal);
        Assert.Contains("_sharedSclStaticDataSetAuthorityDeviceIds.Add(device.DeviceId)", shared, StringComparison.Ordinal);
        Assert.Contains("_sharedSclStaticDataSetAuthorityDeviceIds.Remove(device.DeviceId)", shared, StringComparison.Ordinal);
        Assert.Contains("IsSharedStaticDataSetAuthority(device)", fatMonitor, StringComparison.Ordinal);
        Assert.Contains("Iec61850MonitoringModeRegistry.UseStaticDataSetReportOnly(device)", fatMonitor, StringComparison.Ordinal);
        Assert.Contains("FAT reuses the shared Static DataSet report-only authority", fatMonitor, StringComparison.Ordinal);
        Assert.DoesNotContain("Iec61850MonitoringModeRegistry.UseHybrid(device)", fatMonitor, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedSclFat_IsConsumerOfExistingAcquisitionSession_NotASecondMonitorOwner()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("var reuseSharedSclAcquisition", source, StringComparison.Ordinal);
        Assert.Contains("FAT attached to the existing shared acquisition session · no monitor restart", source, StringComparison.Ordinal);
        Assert.Contains("StartDeviceMonitorAsync(device, navigateToExplorer: false)", source, StringComparison.Ordinal);
        Assert.Contains("one IED owns one acquisition session", source, StringComparison.Ordinal);
        Assert.Contains("FAT is only a", source, StringComparison.Ordinal);
        Assert.Contains("consumer", source, StringComparison.Ordinal);
        Assert.Contains("if (reuseSharedSclAcquisition)", source, StringComparison.Ordinal);
        Assert.Contains("if (!reuseSharedSclAcquisition)", source, StringComparison.Ordinal);
        Assert.Contains("Compatibility path for non-shared/legacy FAT only", source, StringComparison.Ordinal);

        var sharedBranchStart = source.IndexOf("if (reuseSharedSclAcquisition)", StringComparison.Ordinal);
        var legacyBranchStart = source.IndexOf("Compatibility path for non-shared/legacy FAT only", StringComparison.Ordinal);
        Assert.True(sharedBranchStart >= 0 && legacyBranchStart > sharedBranchStart);

        var sharedBranch = source[sharedBranchStart..legacyBranchStart];
        Assert.DoesNotContain("StopDeviceMonitorAsync(device)", sharedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("StartIoFatDeviceMonitorAsync(device", sharedBranch, StringComparison.Ordinal);
        Assert.DoesNotContain("initial MMS", sharedBranch, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SharedStaticFat_DoesNotAddSupplementalSignalOrMisreportPendingAsReportBacked()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("if (!reuseSharedSclAcquisition)", source, StringComparison.Ordinal);
        Assert.Contains("IoFatSupplementalEvidenceService.EnsureTimeSyncSignalSelected(device)", source, StringComparison.Ordinal);
        Assert.Contains("source.Contains(\"pending\"", source, StringComparison.Ordinal);
        Assert.Contains("source.Contains(\"unavailable\"", source, StringComparison.Ordinal);
        Assert.Contains("static report live", source, StringComparison.Ordinal);
        Assert.Contains("pending/unavailable", source, StringComparison.Ordinal);
        Assert.Contains("MMS polling", source, StringComparison.Ordinal);
    }

    [Fact]
    public void SharedStaticFat_EmitsCausalReportEvidenceWithoutMmsFallback()
    {
        var source = File.ReadAllText(FindRepoFile("MainWindow.IoTesting.AutoConnect.cs"));

        Assert.Contains("ObserveSharedStaticReportEvidenceAsync", source, StringComparison.Ordinal);
        Assert.Contains("waiting for actual InformationReport traffic", source, StringComparison.Ordinal);
        Assert.Contains("actual InformationReport traffic observed", source, StringComparison.Ordinal);
        Assert.Contains("no InformationReport traffic was observed", source, StringComparison.Ordinal);
        Assert.Contains("will NOT switch process values to cyclic MMS polling", source, StringComparison.Ordinal);
    }

    private static SignalDefinition Signal(string reference, string dataSetReference)
        => new()
        {
            Name = "signal",
            ObjectReference = reference,
            DisplayReference = reference,
            FunctionalConstraint = "MX",
            DataType = "Float32",
            Category = "Measurement",
            DataSetReference = dataSetReference,
            Confidence = "High"
        };

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
        throw new FileNotFoundException($"Could not locate repository file '{relativePath}' from '{AppContext.BaseDirectory}'.");
    }
}
