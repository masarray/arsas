using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
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
    public void MixedAuthoritativePlans_ProbationDynamicBeforeStaticWithoutChangingCoverageIdentity()
    {
        var staticPlan = new ReportControlPlan
        {
            PlanId = "static",
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = "StaticBrcb",
            AllowDynamicDataSetWrites = false,
            ReportControlReference = "IEDLD0/LLN0.BR.Buffer02"
        };
        var dynamicPlan = new ReportControlPlan
        {
            PlanId = "dynamic",
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = "DynamicUrcb",
            AllowDynamicDataSetWrites = true,
            ReportControlReference = "IEDLD0/LLN0.RP.A_URCB01"
        };

        var ordered = NativeIec61850Client.OrderHybridActivationPlans([staticPlan, dynamicPlan]);

        Assert.Equal(2, ordered.Count);
        Assert.Same(dynamicPlan, ordered[0]);
        Assert.Same(staticPlan, ordered[1]);
        Assert.Equal("StaticBrcb", staticPlan.EngineAcquisitionKind);
        Assert.Equal("IEDLD0/LLN0.BR.Buffer02", staticPlan.ReportControlReference);
    }

    [Fact]
    public void StaticOnlyActivationOrder_RemainsBaselineStable()
    {
        var first = new ReportControlPlan
        {
            PlanId = "s1",
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = "StaticBrcb",
            AllowDynamicDataSetWrites = false
        };
        var second = new ReportControlPlan
        {
            PlanId = "s2",
            IsEngineAuthoritative = true,
            EngineAcquisitionKind = "StaticUrcb",
            AllowDynamicDataSetWrites = false
        };

        var ordered = NativeIec61850Client.OrderHybridActivationPlans([first, second]);

        Assert.Same(first, ordered[0]);
        Assert.Same(second, ordered[1]);
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
        Assert.Contains("mixed-plan safety", source, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StaticFailure_RecoveryPreservesP6FieldSafety()
    {
        var bridge = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.cs");
        var recovery = ReadRepoFile("Services/NativeIec61850Client.HybridReporting.P4.cs");

        // Recovery cannot reuse the failed static RCB and cannot write directly from the
        // compatibility layer. ARIEC must plan an alternate dynamic target from fresh data.
        Assert.Contains("alternateSnapshots", recovery, StringComparison.Ordinal);
        Assert.Contains("!SameLiteralReference(snapshot.Reference, authoritative.ReportControlReference)", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowStaticBrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("AllowStaticUrcb = false", recovery, StringComparison.Ordinal);
        Assert.Contains("RequireExactAvailabilityEvidence = true", recovery, StringComparison.Ordinal);
        Assert.Contains("MmsCapabilityAwareHybridReportAcquisitionPlanner.Build", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("StartPersistentReportMonitorWithAttemptEvidenceAsync", recovery, StringComparison.Ordinal);
        Assert.DoesNotContain("DefineNamedVariableList", recovery, StringComparison.OrdinalIgnoreCase);

        // A static activation that already touched RCB state needs positive rollback evidence
        // before Smart Auto is allowed to attempt the alternate dynamic RCB.
        Assert.Contains("StaticCleanupUnproven", recovery, StringComparison.Ordinal);
        Assert.Contains("staticCleanupProven: attempt.CleanupSucceeded", bridge, StringComparison.Ordinal);
        Assert.Contains("DynamicWriteCircuitByDevice.TryGetValue(appPlan.RelayId", recovery, StringComparison.Ordinal);
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
