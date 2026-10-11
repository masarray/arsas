namespace ARSAS.Tests;

public sealed class WindowsPackagingReproducibilityContractTests
{
    [Fact]
    public void NativeBridge_RequestsReproducibleMsvcLinkOutput()
    {
        var bridge = File.ReadAllText(
            FindRepositoryFile("scripts/build-ardirec-bridge.ps1"));

        Assert.Contains(
            "-DCMAKE_SHARED_LINKER_FLAGS_RELEASE=/Brepro",
            bridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "-DCMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded",
            bridge,
            StringComparison.Ordinal);
        Assert.Contains(
            "ctest --test-dir",
            bridge,
            StringComparison.Ordinal);
    }

    [Fact]
    public void PortablePublish_IsDeterministicAndEmitsHashBoundBuildIdentity()
    {
        var publish = File.ReadAllText(
            FindRepositoryFile("scripts/publish-windows-portable.ps1"));

        Assert.Contains("-p:Deterministic=true", publish, StringComparison.Ordinal);
        Assert.Contains("-p:ContinuousIntegrationBuild=true", publish, StringComparison.Ordinal);
        Assert.Contains("arsas-portable-build-identity", publish, StringComparison.Ordinal);
        Assert.Contains("ardIrecBridgeSha256", publish, StringComparison.Ordinal);
        Assert.Contains("portableSha256", publish, StringComparison.Ordinal);
        Assert.Contains("sourceCommit", publish, StringComparison.Ordinal);
        Assert.Contains("engineCommit", publish, StringComparison.Ordinal);
        Assert.Contains("ardIrecLockCommit", publish, StringComparison.Ordinal);
        Assert.Contains("reproducibleNativeLinkRequested = $true", publish, StringComparison.Ordinal);
    }

    [Fact]
    public void CanonicalAndFieldCaptureArtifacts_BothCarryBuildIdentity()
    {
        var canonical = File.ReadAllText(
            FindRepositoryFile(".github/workflows/build.yml"));
        var capture = File.ReadAllText(
            FindRepositoryFile(".github/workflows/smart-discovery-capture-build.yml"));

        const string identity =
            "ArIED61850Tester\\dist\\ARSAS-*-win-x64-portable-build-identity.json";

        Assert.Contains(identity, canonical, StringComparison.Ordinal);
        Assert.Contains(identity, capture, StringComparison.Ordinal);
        Assert.Contains(
            "--portable-identity",
            canonical,
            StringComparison.Ordinal);
        Assert.Contains(
            "Promote exact sealed canonical portable",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "verify-ci-package-reuse.py",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "--allow-in-progress-artifact",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "proof.portablePath",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "proof.portableIdentityPath",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "SMART_CAPTURE_PACKAGING_AUTHORITY=sealed-build-arsas:",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "SMART_CAPTURE_PACKAGING_AUTHORITY=independent-manual-field-capture-publish",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "if: github.event_name == 'workflow_dispatch'",
            capture,
            StringComparison.Ordinal);
        Assert.Contains(
            "Packaging authority: $env:SMART_CAPTURE_PACKAGING_AUTHORITY",
            capture,
            StringComparison.Ordinal);
    }

    private static string FindRepositoryFile(string relativePath)
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, relativePath);
            if (File.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Repository file not found: {relativePath}");
    }
}
