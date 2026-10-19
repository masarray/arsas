using AR.Iec61850.Discovery;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;
using System.Security.Cryptography;

namespace ARSAS.Tests;

public sealed class P0GlobalIdentityConsumerRegressionTests
{
    private static readonly string[] Domains =
    [
        "SIEBCUCTRL", "SIEBCUMEAS", "SIEBCUPROT", "SIEBCUUD1", "SIEBCUUD2"
    ];

    [Fact]
    public void UnverifiedLiveDomains_PreserveIdentityCandidateWithoutWireConfidence()
    {
        var inferred = LiveIedIdentityResolver.Resolve(Domains, "192.0.2.20");

        Assert.Equal("SIEBCU", inferred.IedName);
        Assert.Equal(LiveIedDiscoveryConfidenceLevel.Medium, inferred.Confidence);
        Assert.Equal("CTRL", inferred.LogicalDeviceAliases["SIEBCUCTRL"]);
        Assert.Equal(5, inferred.LogicalDeviceAliases.Count);
    }

    [Fact]
    public async Task TrustedPinnedScl_ProjectsExactIdentityThroughConsumerWithoutChangingRawDomains()
    {
        var folder = Path.Combine(Path.GetTempPath(), "arsas-p0-identity-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, "synthetic.iid");
        try
        {
            var ldXml = string.Join("", new[] { "CTRL", "MEAS", "PROT", "UD1", "UD2" }
                .Select(inst => $"<LDevice inst=\"{inst}\"/>"));
            var scl = $"""
                <SCL xmlns="http://www.iec.ch/61850/2003/SCL">
                  <Communication><SubNetwork name="Test"><ConnectedAP iedName="SIEBCU" apName="P1"><Address><P type="IP">192.0.2.20</P></Address></ConnectedAP></SubNetwork></Communication>
                  <IED name="SIEBCU"><AccessPoint name="P1"><Server>{ldXml}</Server></AccessPoint></IED>
                </SCL>
                """;
            await File.WriteAllTextAsync(path, scl);
            var device = new Iec61850MonitorDevice
            {
                IpAddress = "192.0.2.20",
                SclSourcePath = path,
                SclSourceSha256 = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path))),
                SclIedName = "SIEBCU",
                SclAccessPointName = "P1"
            };
            var live = new LiveIedModelDiscoveryDocument
            {
                Host = "192.0.2.20",
                IedName = "192.0.2.20",
                LogicalDevices = Domains.Select(domain => new LiveIedLogicalDeviceModel
                {
                    MmsDomain = domain,
                    Inst = domain
                }).ToArray()
            };

            var verified = await TrustedSclIdentityAuthority.TryMatchAsync(device, live);
            Assert.NotNull(verified);
            Assert.Equal("SIEBCU", verified.IedName);
            Assert.Equal(LiveIedDiscoveryConfidenceLevel.High, verified.Confidence);
            var projection = TrustedSclIdentityAuthority.ToConsumerIdentity(verified, Domains);
            Assert.Equal("SIEBCU", projection.IedName);
            Assert.Equal(5, projection.LogicalDevices.Count);
            Assert.Contains(projection.LogicalDevices,
                ld => ld.Domain == "SIEBCUPROT" && ld.Instance == "PROT");

            device.IpAddress = "192.0.2.21";
            Assert.Null(await TrustedSclIdentityAuthority.TryMatchAsync(device, live));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
