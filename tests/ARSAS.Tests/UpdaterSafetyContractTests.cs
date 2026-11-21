using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using ArIED61850Tester;

namespace ARSAS.Tests;

public sealed class UpdaterSafetyContractTests
{
    [Fact]
    public void VerifiedStableReleaseMetadata_IsAccepted()
    {
        AppUpdateService.ValidateManifest(ValidManifest());
    }

    [Fact]
    public void WrongSchema_IsRejected()
    {
        var manifest = ValidManifest();
        manifest.SchemaVersion = 2;
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));
    }

    [Fact]
    public void MismatchedTagOrReleaseLocation_IsRejected()
    {
        var manifest = ValidManifest();
        manifest.Tag = "v1.6.99";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));

        manifest = ValidManifest();
        manifest.ReleaseUrl = "https://example.com/arsas/latest";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));
    }

    [Fact]
    public void UntrustedInstallerLocationOrDigest_IsRejected()
    {
        var manifest = ValidManifest();
        manifest.Installer.Url = "https://example.com/arsas-installer.exe";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));

        manifest = ValidManifest();
        manifest.Installer.Sha256 = "broken";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));
    }

    [Fact]
    public void MalformedProvenanceOrFuturePublication_IsRejected()
    {
        var manifest = ValidManifest();
        manifest.SourceCommit = "not-a-git-sha";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));

        manifest = ValidManifest();
        manifest.PublishedAtUtc = DateTimeOffset.UtcNow.AddDays(30);
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));
    }

    [Fact]
    public void MissingOrFourComponentVersion_IsRejected()
    {
        var manifest = ValidManifest();
        manifest.Version = "";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));

        manifest = ValidManifest();
        manifest.Version = "1.6.43.0";
        Assert.Throws<InvalidDataException>(() => AppUpdateService.ValidateManifest(manifest));
    }

    [Fact]
    public async Task FirstDownload_VerifiesClosedPartialFileAndReusesTrustedCache()
    {
        var payload = Enumerable.Repeat((byte)0xA5, 1_100_000).ToArray();
        var manifest = ValidInstallerPayloadManifest(payload);
        var root = Path.Combine(Path.GetTempPath(), "arsas-updater-test-" + Guid.NewGuid().ToString("N"));

        try
        {
            var handler = new InstallerResponseHandler(payload);
            using var service = new AppUpdateService(handler, root);

            var installedFile = await service.DownloadInstallerAsync(manifest, null, CancellationToken.None);
            Assert.Equal(Path.Combine(root, "Packages", "1.6.46", "ARSAS-Windows-x64-Setup.exe"), installedFile);
            Assert.Equal(payload, await File.ReadAllBytesAsync(installedFile));
            Assert.False(File.Exists(installedFile + ".part"));
            Assert.Equal(1, handler.RequestCount);

            // Cache hits must independently rehash without issuing another download.
            var cachedFile = await service.DownloadInstallerAsync(manifest, null, CancellationToken.None);
            Assert.Equal(installedFile, cachedFile);
            Assert.Equal(1, handler.RequestCount);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public async Task MismatchedDigest_DoesNotExposeInstallerOrLeavePartialFile()
    {
        var payload = Enumerable.Repeat((byte)0x5A, 1_100_000).ToArray();
        var manifest = ValidInstallerPayloadManifest(payload);
        manifest.Installer.Sha256 = new string('0', 64);
        var root = Path.Combine(Path.GetTempPath(), "arsas-updater-reject-" + Guid.NewGuid().ToString("N"));

        try
        {
            using var service = new AppUpdateService(new InstallerResponseHandler(payload), root);
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                service.DownloadInstallerAsync(manifest, null, CancellationToken.None));

            var expectedFile = Path.Combine(root, "Packages", "1.6.46", "ARSAS-Windows-x64-Setup.exe");
            Assert.False(File.Exists(expectedFile));
            Assert.False(File.Exists(expectedFile + ".part"));
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    private static UpdateManifest ValidInstallerPayloadManifest(byte[] payload)
    {
        var manifest = ValidManifest();
        manifest.Version = "1.6.46";
        manifest.Tag = "v1.6.46";
        manifest.ReleaseUrl = "https://github.com/masarray/arsas/releases/tag/v1.6.46";
        manifest.Installer.SizeBytes = payload.Length;
        manifest.Installer.Sha256 = Convert.ToHexString(SHA256.HashData(payload));
        return manifest;
    }

    private sealed class InstallerResponseHandler(byte[] payload) : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Assert.Equal(HttpMethod.Get, request.Method);
            Assert.Equal(
                "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe",
                request.RequestUri?.AbsoluteUri);
            RequestCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload)
            });
        }
    }

    private static UpdateManifest ValidManifest() => new()
    {
        SchemaVersion = 1,
        Product = "ARSAS",
        Version = "1.6.43",
        Tag = "v1.6.43",
        Channel = "stable",
        PublishedAtUtc = DateTimeOffset.UtcNow.AddHours(-1),
        SourceCommit = new string('a', 40),
        ReleaseUrl = "https://github.com/masarray/arsas/releases/tag/v1.6.43",
        Installer = new UpdateInstallerManifest
        {
            Name = "ARSAS-Windows-x64-Setup.exe",
            Url = "https://github.com/masarray/arsas/releases/latest/download/ARSAS-Windows-x64-Setup.exe",
            Sha256 = new string('a', 64),
            SizeBytes = 50_000_000
        }
    };
}
