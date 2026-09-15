using System.Security.Cryptography;
using ArIED61850Tester.Models;
using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class SclConnectionAuthorityTests
{
    [Fact]
    public void ConnectionPolicy_PrefersSclAuthorityOverDiscoveryCache()
    {
        var device = new Iec61850MonitorDevice
        {
            SclSourceSha256 = new string('a', 64),
            HasDiscoveryCache = true
        };
        device.Signals.Add(new SignalDefinition { Name = "stVal" });

        Assert.Equal(
            Iec61850ConnectionPath.SclAssisted,
            Iec61850ConnectionPathPolicy.SelectForFastConnect(device));
    }

    [Fact]
    public void ConnectionPolicy_UsesCachedLiveModelOnlyWithoutSclAuthority()
    {
        var device = new Iec61850MonitorDevice { HasDiscoveryCache = true };
        device.Signals.Add(new SignalDefinition { Name = "stVal" });

        Assert.Equal(
            Iec61850ConnectionPath.CachedLiveModel,
            Iec61850ConnectionPathPolicy.SelectForFastConnect(device));
    }

    [Fact]
    public void ConnectionPolicy_RequiresFullDiscoveryWithoutTrustedModel()
    {
        var device = new Iec61850MonitorDevice();

        Assert.Equal(
            Iec61850ConnectionPath.FullDiscovery,
            Iec61850ConnectionPathPolicy.SelectForFastConnect(device));
    }

    [Fact]
    public async Task VerifiedLoader_AcceptsExactImportedSourceBytes()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsas-scl-{Guid.NewGuid():N}.cid");
        const string xml = "<SCL xmlns=\"http://www.iec.ch/61850/2003/SCL\"><IED name=\"IED01\"/></SCL>";
        try
        {
            await File.WriteAllTextAsync(path, xml);
            var bytes = await File.ReadAllBytesAsync(path);
            var sha = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

            var verified = await VerifiedSclSourceLoader.LoadAsync(path, sha);

            Assert.Equal(sha, verified.Sha256);
            Assert.Equal(Path.GetFullPath(path), verified.FullPath);
            Assert.Contains("IED01", verified.Xml, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task VerifiedLoader_RejectsSourceChangedAfterImport()
    {
        var path = Path.Combine(Path.GetTempPath(), $"arsas-scl-{Guid.NewGuid():N}.cid");
        try
        {
            await File.WriteAllTextAsync(path, "<SCL><IED name=\"A\"/></SCL>");
            var original = await File.ReadAllBytesAsync(path);
            var expectedSha = Convert.ToHexString(SHA256.HashData(original)).ToLowerInvariant();
            await File.WriteAllTextAsync(path, "<SCL><IED name=\"B\"/></SCL>");

            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                VerifiedSclSourceLoader.LoadAsync(path, expectedSha));

            Assert.Contains("changed after import", error.Message, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
