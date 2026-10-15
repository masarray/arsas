using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;

namespace ARSAS.Tests;

public sealed class IedBrowserGooseInspectionTests
{
    private static IedBrowserGooseExpectedIdentity Expected()
        => new("BCUGE", "LD/LLN0.GO.BCU", "LD/LLN0.GOOSE1", "BCUGE03",
            33, 1, "", "", null);

    private static GooseStreamSnapshot Frame(string ied = "BCUGE",
        string gocb = "LD/LLN0$GO$BCU", string dataset = "LD/LLN0$GOOSE1",
        string appid = "0x0021", string revision = "1",
        IReadOnlyList<GooseLeafValueSnapshot>? leaves = null)
        => new("wire", appid, gocb, "BCUGE03", dataset,
            "02:00:4C:4F:5B:50", "01:0C:CD:01:00:33", "VID 0 / PCP 4",
            "1", "143", "Healthy", "20000 ms", revision, ied, "SCL",
            "", "12:00:00.000", 2, 0, false, false,
            leaves ?? Array.Empty<GooseLeafValueSnapshot>(),
            "0", "4", leaves?.Count ?? 0);

    [Fact]
    public void ExactCanonicalControlIdentityAndOnePublisherRequired()
    {
        var right = Frame();
        Assert.Same(right, IedBrowserGooseInspection.Select(Expected(), [right]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(ied: "OTHER")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(gocb: "LD/LLN0$GO$BCU3")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(dataset: "LD/LLN0$OTHER")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(appid: "0x0022")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [Frame(revision: "2")]));
        Assert.Null(IedBrowserGooseInspection.Select(Expected(), [right, Frame()]));
    }

    [Fact]
    public void ExactSclGoCbMetadata_ProjectsOnlyConfiguredVlanAndTimingFields()
    {
        var expected = Expected() with
        {
            SclVlanId = 0, SclVlanPriority = 4,
            SclMinTimeMs = 0, SclMaxTimeMs = 2000
        };
        var fields = IedBrowserGooseInspection.SclNetworkProperties(expected)
            .ToDictionary(field => field.Name, field => field.Value,
                StringComparer.Ordinal);
        Assert.Equal(4, fields.Count);
        Assert.Equal("0", fields["VLAN ID"]);
        Assert.Equal("4", fields["VLAN priority"]);
        Assert.Equal("0", fields["Minimum time (ms)"]);
        Assert.Equal("2000", fields["Maximum time (ms)"]);
        Assert.DoesNotContain("Enabled", fields.Keys);
        Assert.DoesNotContain("Capture status", fields.Keys);
    }

    [Fact]
    public void MissingSclGseAddressCannotFabricateNetworkState()
    {
        Assert.Empty(IedBrowserGooseInspection.SclNetworkProperties(Expected()));
    }

    [Fact]
    public void WireDataSetValuesOnlyIfWholeOrderedMemberIdentityMatches()
    {
        var first = new LiveIedDataSetMemberModel { Index = 0,
            Reference = "LD/CSWI6.Pos.stVal", FunctionalConstraint = "ST" };
        var second = new LiveIedDataSetMemberModel { Index = 1,
            Reference = "LD/CSWI6.Pos.q", FunctionalConstraint = "ST" };
        var set = new LiveIedDataSetModel { Reference = "LD/LLN0.GOOSE1",
            Members = [first, second] };
        var a = new GooseLeafValueSnapshot(1, 0, "Pos", first.Reference,
            "ST", "DPC", "Dbpos", "intermediate-state", "", false, "SCL");
        var b = new GooseLeafValueSnapshot(2, 1, "q", second.Reference,
            "ST", "DPC", "Quality", "good", "", false, "SCL");
        var frame = Frame(leaves: [a, b]);
        Assert.Equal("good", IedBrowserGooseInspection.VerifiedMembers(frame, set)?[1].Value);
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { Leaves = [b, a] }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { WireDataSetEntryCount = 1 }, set));
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame with
            { Leaves = [a, b with { FunctionalConstraint = "MX" }] }, set));
        var ambiguous = new LiveIedDataSetModel { Reference = set.Reference,
            Members = [first, first] };
        Assert.Null(IedBrowserGooseInspection.VerifiedMembers(frame, ambiguous));
    }
}
