using AR.Iec61850.Discovery;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class P6FieldStabilityRegressionTests
{
    [Fact]
    public void MandatoryStaticDescriptor_IndexResolvesPrimaryLeafBeforeBroadCatalogFallback()
    {
        var descriptor = new Iec61850SignalDescriptor
        {
            DesignReference = "IEDLD0/GGIO2.ComFail.stVal",
            CanonicalMmsReference = "IEDLD0/GGIO2.ComFail.stVal",
            EffectiveMmsReference = "IEDLD0/GGIO2.ComFail.stVal",
            PrimaryValueReference = "IEDLD0/GGIO2.ComFail.stVal",
            PrimaryValueMmsReference = "IEDLD0/GGIO2.ComFail.stVal",
            SemanticRole = Iec61850DataAttributeSemanticRole.PrimaryValue,
            IsStaticDataSetMandatory = true,
            DataSetMemberships =
            [
                new Iec61850SignalDataSetMembership
                {
                    DataSetReference = "IEDLD0/LLN0$Digital",
                    OriginalMemberReference = "IEDLD0/GGIO2.ComFail",
                    CanonicalMemberReference = "IEDLD0/GGIO2.ComFail",
                    IsPrimaryValueForMember = true
                }
            ]
        };

        var index = NativeIec61850Client.BuildLiteralCatalogIndex([descriptor]);

        Assert.True(NativeIec61850Client.TryResolveLiteralCatalogSignal(
            index,
            "IEDLD0/GGIO2.ComFail.stVal",
            out var resolved));
        Assert.Same(descriptor, resolved);
        Assert.True(resolved.IsStaticDataSetMandatory);
        Assert.Single(resolved.DataSetMemberships);
        Assert.True(resolved.DataSetMemberships[0].IsPrimaryValueForMember);
    }

    [Fact]
    public void HybridPlannerSource_UsesMandatoryStaticInventoryBeforeBroadCatalog()
    {
        var source = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.cs");

        var projection = source.IndexOf(
            "Iec61850DataSetSignalInventoryProjection",
            StringComparison.Ordinal);
        var staticLookup = source.IndexOf(
            "TryResolveLiteralCatalogSignal(staticIndex, point.IecReference",
            StringComparison.Ordinal);
        var broadLookup = source.IndexOf(
            "TryResolveLiteralCatalogSignal(index, point.IecReference",
            StringComparison.Ordinal);

        Assert.True(projection >= 0, "P6 must use the ARIEC mandatory DataSet-member projection.");
        Assert.True(staticLookup > projection, "Static inventory lookup must happen after projection creation.");
        Assert.True(broadLookup > staticLookup, "Static inventory must win before broad catalog fallback.");
        Assert.Contains("staticInventoryMappedCount", source, StringComparison.Ordinal);
    }

    [Fact]
    public void FailedRealDynamicAttempt_OpensProcessLifetimeCircuitBreaker()
    {
        var source = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.cs");

        Assert.Contains("DynamicWriteCircuitByDevice", source, StringComparison.Ordinal);
        Assert.Contains("attempt.DynamicAttempted", source, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice[plan.RelayId] = reason", source, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicBrcb = allowDynamicWrites", source, StringComparison.Ordinal);
        Assert.Contains("AllowDynamicUrcb = allowDynamicWrites", source, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitOpen", source, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticFailure_IsBaselineIsolatedAndCannotOpenOrUseDynamicCircuit()
    {
        var source = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.P4.cs");

        Assert.Contains("baseline static-failure isolation", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("no dynamic DataSet/RCB write was attempted", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("UsedDynamicDataSet = false", source, StringComparison.Ordinal);
        Assert.Contains("DynamicAttempted = false", source, StringComparison.Ordinal);
        Assert.Contains("FailureReason = \"StaticActivationFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("PollingFallbackReason = \"StaticActivationFailed\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorWithAttemptEvidenceAsync", source, StringComparison.Ordinal);
        Assert.DoesNotContain("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicWriteCircuitByDevice.TryGetValue(appPlan.RelayId", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DynamicWriteCircuitByDevice[appPlan.RelayId]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void DiagnosticsJournal_HasNoAcquisitionRefreshTimerOrInjectedGrid()
    {
        var source = ReadRepoFile("MainWindow.HybridAcquisitionDiagnostics.cs");

        Assert.DoesNotContain("DispatcherTimer", source, StringComparison.Ordinal);
        Assert.DoesNotContain("DataGrid", source, StringComparison.Ordinal);
        Assert.DoesNotContain("InstallHybridAcquisitionDiagnosticsPanel", source, StringComparison.Ordinal);
        Assert.DoesNotContain("RefreshHybridAcquisitionTelemetry", source, StringComparison.Ordinal);
    }

    private static string ReadRepoFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return File.ReadAllText(candidate).Replace("\r\n", "\n", StringComparison.Ordinal);
            directory = directory.Parent;
        }

        throw new FileNotFoundException(relativePath);
    }
}
