using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class StaticReportEvidenceRecoveryTests
{
    [Fact]
    public void PartialLiveMmsInventoryCannotProveTheIedHasNoConfiguredRcb()
    {
        var value = StaticReportEvidencePolicy.ClassifyMissingRcb(false);
        Assert.Contains("UNVERIFIED", value.Label);
        Assert.Contains("Unverified", value.PointStatus);
        Assert.DoesNotContain("Unavailable", value.PointStatus);
        Assert.Contains("may be partial", value.Explanation);
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
