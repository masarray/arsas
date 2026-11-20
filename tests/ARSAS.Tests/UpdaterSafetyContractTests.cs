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
