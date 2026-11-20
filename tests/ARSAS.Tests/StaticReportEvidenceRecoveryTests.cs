using ArIED61850Tester.Services;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class StaticReportEvidenceRecoveryTests
{
    [Fact]
    public void PartialLiveMmsInventoryCannotProveTheIedHasNoConfiguredRcb()
    {
        var value = StaticReportEvidencePolicy.ClassifyMissingRcb(false);
        Assert.Contains("NO VERIFIED LIVE RCB→DATASET BINDING", value.Label);
        Assert.Contains("Unverified", value.PointStatus);
        Assert.DoesNotContain("Unavailable", value.PointStatus);
        Assert.Contains("may be partial", value.Explanation);
        Assert.Contains("does not prove", value.Explanation);
    }

    [Fact]
    public void OpenSclAbsentRcbRemainsAClaimAboutDesignNotLiveMms()
    {
        var value = StaticReportEvidencePolicy.ClassifyMissingRcb(true);
        Assert.Contains("OPEN SCL", value.Label);
        Assert.Contains("opened SCL", value.Explanation);
        Assert.Contains("Unavailable", value.PointStatus);
        Assert.DoesNotContain("relay lacks an RCB.", value.PointStatus);
    }

    [Theory]
    [InlineData(0, 0, false, "No static DataSets")]
    [InlineData(39, 0, false, "39 DataSet(s) · live RCB/DataSet binding unverified")]
    [InlineData(39, 0, true, "39 DataSet(s) · no RCB in open SCL")]
    [InlineData(39, 3, false, "3/39 DataSet(s) with live-observed RCB/DataSet binding · Report ingress unverified")]
    [InlineData(39, 35, true, "35/39 DataSet(s) with SCL-configured RCB/DataSet binding · Report ingress unverified")]
    public void QuickStartCoverage_DoesNotClaimIncompleteInventoryIsReportReady(
        int total, int reportBound, bool openedScl, string expected)
        => Assert.Equal(expected,
            StaticReportEvidencePolicy.DescribeDataSetCoverage(total, reportBound, openedScl));

    [Theory]
    [InlineData(true, true, 1, true, true)]
    [InlineData(false, true, 1, true, false)]
    [InlineData(true, false, 1, true, false)]
    [InlineData(true, true, 0, true, false)]
    [InlineData(true, true, 1, false, false)]
    public void TargetedRetryRunsOnlyForUnresolved64PageTruncation(
        bool association, bool partial, int unresolved, bool pageLimit, bool expected)
    {
        var warnings = pageLimit
            ? new[] { "Target domain 'LD0' NamedVariable enumeration is partial: Smart GetNameList NamedVariable/LD0: names=6400, pages=64, complete=False. Stopped safely: page limit 64 reached." }
            : new[] { "Target domain 'LD0' NamedVariable enumeration is partial: name limit 20000 reached." };
        Assert.Equal(expected, StaticReportEvidencePolicy.ShouldExpandTargetedNameList(
            association, partial, unresolved, warnings));
    }

    [Fact]
    public void DiagnosticLog_DistinguishesObservedInstancesFromVerifiedDataSetBindings()
    {
        var source = File.ReadAllText(FindSource("MainWindow.StaticReportEvidence.cs"));
        Assert.Contains("verifiedBindingCount", source);
        Assert.Contains("nonempty DataSet reference", source);
        Assert.Contains("unverified, not necessarily unconfigured", source);
        Assert.Contains("indexed candidate(s)", source);
        Assert.DoesNotContain("NO CONFIGURED RCB.", source);
    }

    [Fact]
    public void ReportProofWindow_BeginsAfterLastCompletedRcbActivation()
    {
        var started = DateTimeOffset.Parse("2026-11-20T11:01:37Z");
        var now = started.AddSeconds(3);
        var evidence = Enumerable.Range(1, 33).Select(index =>
            new Iec61850RcbActivationEvidence
            {
                Rcb = "LD0/LLN0.BR.rpt" + index,
                SetupSucceeded = true,
                ActivationReturnedAtUtc = started.AddMilliseconds(index * 95)
            }).ToArray();

        var deadline = StaticReportEvidencePolicy.ReportProofDeadline(
            started, now.AddMilliseconds(300), TimeSpan.FromSeconds(3),
            evidence);

        Assert.Equal(started.AddMilliseconds(33 * 95).AddSeconds(3), deadline);
    }

    [Fact]
    public void ReportProofWindow_FutureAndStaleActivationCannotExtendDeadline()
    {
        var started = DateTimeOffset.Parse("2026-11-20T11:01:37Z");
        var now = started.AddSeconds(3);
        var evidence = new[]
        {
            new Iec61850RcbActivationEvidence { ActivationReturnedAtUtc = started.AddMinutes(-1) },
            new Iec61850RcbActivationEvidence { ActivationReturnedAtUtc = now.AddMinutes(1) }
        };
        var deadline = StaticReportEvidencePolicy.ReportProofDeadline(
            started, now, TimeSpan.FromSeconds(3), evidence);
        Assert.Equal(started.AddSeconds(3), deadline);
        Assert.Equal(deadline, StaticReportEvidencePolicy.ReportProofDeadline(
            started, now, TimeSpan.FromSeconds(3), []));
    }

    [Fact]
    public void P0StructuralFreezeAndBoundedOneShotRecoveryAreSeparate()
    {
        var source = File.ReadAllText(FindSource("Services/NativeIec61850Client.StaticDataSetReporting.cs"));
        var capture = File.ReadAllText(FindSource("Services/NativeIec61850Client.SmartDiscoveryCapture.cs"));
        Assert.Contains("var smart = await PrepareAsync(pageBudget: 64)", source);
        Assert.Contains("smart = await PrepareAsync(pageBudget: 192)", source);
        Assert.Contains("ShouldExpandTargetedNameList(", source);
        Assert.Contains("MaxNameListPages = 64", capture);
    }

    private static string FindSource(string relative)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && directory is not null; i++, directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, relative.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(candidate))
                return candidate;
        }
        throw new FileNotFoundException("Could not locate checked out source for structural guard.", relative);
    }
}
