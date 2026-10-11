using System.Security.Cryptography;
using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

/// <summary>IP-only Discovery identity MUST be proved by a live, exact-domain,
/// SHA-verified engineering IID previously accepted on a successful SCL/MMS association.</summary>
public sealed class P106RememberedTrustedDiscoveryIdentityTests
{
    [Fact]
    public async Task GE_F650_PriorSuccessfulSclAssociation_PromotesIpDiscoveryToBCUGE()
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, source.Sha,
            "BCUGE", "S1", "192.16.1.33", 102, source.Path);

        var device = Device("192.16.1.33");
        var original = Model("BCUGEF650");
        var result = await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            device, original, bindingStorePath: fixture.Store);

        Assert.NotNull(result);
        Assert.Equal("BCUGE", result.Identity.IedName);
        Assert.Equal("S1", result.AccessPointName);
        Assert.Equal(source.Sha, result.SourceSha256, ignoreCase: true);
        var consumer = TrustedSclIdentityAuthority.ToConsumerIdentity(
            result.Identity, ["BCUGEF650"]);
        Assert.Equal("BCUGE", consumer.IedName);
        Assert.Equal("F650", Assert.Single(consumer.LogicalDevices).Instance);
        Assert.Equal("BCUGEF650", Assert.Single(consumer.LogicalDevices).Domain);
        Assert.Equal("IED_192_16_1_33", original.IedName); // engine model not rewritten
        Assert.Null(device.SclWorkspace); // Discovery reporting remains MMS-owned
    }

    [Fact]
    public async Task NameIsNeverGuessedFromIpOrLoneMmsDomain()
    {
        using var fixture = new Fixture();
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device("192.16.1.33"), Model("BCUGEF650"), bindingStorePath: fixture.Store));
    }

    [Fact]
    public async Task LegacySuccessfulEndpointWithoutSourcePath_IsNotNameAuthority()
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, source.Sha,
            "BCUGE", "S1", "192.16.1.33", 102);
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device("192.16.1.33"), Model("BCUGEF650"), bindingStorePath: fixture.Store));
    }

    [Theory]
    [InlineData("192.16.1.34", "BCUGEF650")]
    [InlineData("192.16.1.33", "ANOTHERDOMAIN")]
    public async Task WrongEndpointOrDomain_FailsClosed(string ip, string domain)
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, source.Sha,
            "BCUGE", "S1", "192.16.1.33", 102, source.Path);
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device(ip), Model(domain), bindingStorePath: fixture.Store));
    }

    [Fact]
    public async Task ModifiedTrustedIid_RejectsSourceEvenIfXmlTopologyStillMatches()
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, source.Sha,
            "BCUGE", "S1", "192.16.1.33", 102, source.Path);
        File.AppendAllText(source.Path, " ");
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device("192.16.1.33"), Model("BCUGEF650"), bindingStorePath: fixture.Store));
    }

    [Fact]
    public async Task MultipleMatchingEngineeringSources_CannotSilentlySelectMostRecent()
    {
        using var fixture = new Fixture();
        var first = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        var other = fixture.NewIid("OTHER", "S1", "F650", "192.16.1.33",
            explicitDomain: "BCUGEF650");
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, first.Sha,
            "BCUGE", "S1", "192.16.1.33", 102, first.Path);
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, other.Sha,
            "OTHER", "S1", "192.16.1.33", 102, other.Path);
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device("192.16.1.33"), Model("BCUGEF650"), bindingStorePath: fixture.Store));
    }

    [Fact]
    public async Task MultiAccessPointIid_IsNotGuessedFromTheRememberedAp()
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33",
            extraAccessPoint: true);
        SclEndpointBindingStore.RecordVerifiedAtPath(fixture.Store, source.Sha,
            "BCUGE", "S1", "192.16.1.33", 102, source.Path);
        Assert.Null(await TrustedSclIdentityAuthority.TryMatchRememberedAsync(
            Device("192.16.1.33"), Model("BCUGEF650"), bindingStorePath: fixture.Store));
    }

    [Fact]
    public void VerifiedIdentityAllowsCanonicalParityWithoutMutatingIpFallbackModel()
    {
        using var fixture = new Fixture();
        var source = fixture.NewIid("BCUGE", "S1", "F650", "192.16.1.33");
        var device = Device("192.16.1.33");
        device.Name = "BCUGE";
        device.LiveDiscoveryModel = new LiveIedModelDiscoveryDocument
        {
            IedName = "IED_192_16_1_33",
            LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = "BCUGEF650" }],
            DataSets =
            [
                new LiveIedDataSetModel
                {
                    Reference = "BCUGEF650/LLN0.Events",
                    Domain = "BCUGEF650",
                    LogicalNode = "LLN0",
                    Name = "Events",
                    MemberCount = 1,
                    Members = [new LiveIedDataSetMemberModel {
                        Index = 0, Reference = "BCUGEF650/XCBR1.Pos.stVal", FunctionalConstraint = "ST" }]
                }
            ]
        };
        var plan = new NativeHybridReportPlanningResult
        {
            IsAuthoritative = true,
            RequestedPointCount = 1,
            StaticBrcbSignalCount = 1,
            ReportPlans = [new ReportControlPlan
            {
                IsEngineAuthoritative = true, EngineAcquisitionKind = "StaticBrcb",
                Buffered = true, DataSetReference = "BCUGEF650/LLN0.Events",
                ReportControlReference = "BCUGEF650/LLN0.BR.Events1",
                Bindings = [new Iec61850MonitorPoint {
                    DeviceId = "ge", IecReference = "BCUGEF650/XCBR1.Pos.stVal",
                    FunctionalConstraint = "ST" }]
            }]
        };

        var noProof = StaticAcquisitionParityTracker.BuildEvidence(
            device, plan, StaticAcquisitionIngressKind.LiveDiscovery);
        Assert.False(noProof.IsComparable);
        Assert.Contains("IED identity", noProof.IncomparableReason, StringComparison.Ordinal);

        // Identity evidence is set only by the trusted matcher at successful Discovery.
        var xml = System.Xml.Linq.XDocument.Load(source.Path);
        var proof = TrustedSclIedIdentityMatcher.TryMatch(
            xml, ["BCUGEF650"], "192.16.1.33", "BCUGE");
        Assert.NotNull(proof);
        device.TrustedLiveIdentity = proof;
        var verified = StaticAcquisitionParityTracker.BuildEvidence(
            device, plan, StaticAcquisitionIngressKind.LiveDiscovery);
        Assert.True(verified.IsComparable, verified.IncomparableReason);
        Assert.Equal("IED_192_16_1_33", device.LiveDiscoveryModel.IedName);
    }

    private static Iec61850MonitorDevice Device(string ip) => new()
    {
        Name = "IED_192_16_1_33", IpAddress = ip, Port = 102
    };

    private static LiveIedModelDiscoveryDocument Model(string domain) => new()
    {
        Host = "192.16.1.33",
        IedName = "IED_192_16_1_33",
        LogicalDevices = [new LiveIedLogicalDeviceModel { MmsDomain = domain, Inst = domain }]
    };

    private sealed class Fixture : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(),
            "arsas-p106-" + Guid.NewGuid().ToString("N"));
        public Fixture() => Directory.CreateDirectory(_directory);
        public string Store => Path.Combine(_directory, "bindings.json");

        public (string Path, string Sha) NewIid(
            string ied, string ap, string ld, string ip,
            string? explicitDomain = null, bool extraAccessPoint = false)
        {
            var path = Path.Combine(_directory, Guid.NewGuid().ToString("N") + ".iid");
            var ldName = explicitDomain is null ? "" : " ldName=\"" + explicitDomain + "\"";
            var extra = extraAccessPoint
                ? "<AccessPoint name=\"S2\"><Server><LDevice inst=\"OTHER\"/></Server></AccessPoint>"
                : "";
            var xml = $"""
                <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
                  <Communication><SubNetwork name="LAN"><ConnectedAP iedName="{ied}" apName="{ap}"><Address><P type="IP">{ip}</P></Address></ConnectedAP></SubNetwork></Communication>
                  <IED name="{ied}"><AccessPoint name="{ap}"><Server><LDevice inst="{ld}"{ldName}><LN0 lnClass="LLN0" lnType="LLN0_T"/></LDevice></Server></AccessPoint>{extra}</IED>
                </SCL>
                """;
            File.WriteAllText(path, xml);
            return (path, Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant());
        }
        public void Dispose() => Directory.Delete(_directory, recursive: true);
    }
}
