using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace ArIED61850Tester.Services;

/// <summary>
/// Makes the pinned ArdIrec native bridge available to single-file ARSAS builds.
/// Folder/installer builds keep using Tools\ArdIrec\ardirec_bridge.dll directly;
/// portable single-file builds carry the same DLL as a managed embedded resource.
/// </summary>
internal static class ArdIrecEmbeddedBridgeBootstrap
{
    private const string BridgeEnvironmentVariable = "ARSAS_ARDIREC_BRIDGE_PATH";
    private const string BridgeResourceName = "ArIED61850Tester.Native.ardirec_bridge.dll";
    private const string BridgeFileName = "ardirec_bridge.dll";

    [ModuleInitializer]
    internal static void Initialize()
    {
        if (!OperatingSystem.IsWindows())
            return;

        // An explicit engineering/test override always wins.
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(BridgeEnvironmentVariable)))
            return;

        // Installer/folder builds already publish the bridge beside ARSAS. Let the normal
        // ArdIrecNativeBridge candidate order use that physical deployment directly.
        var stagedBridge = Path.Combine(AppContext.BaseDirectory, "Tools", "ArdIrec", BridgeFileName);
        if (File.Exists(stagedBridge))
            return;

        try
        {
            var assembly = typeof(ArdIrecEmbeddedBridgeBootstrap).Assembly;
            using var resource = assembly.GetManifestResourceStream(BridgeResourceName);
            if (resource is null)
                return;

            var hash = Convert.ToHexString(SHA256.HashData(resource)).ToLowerInvariant();
            resource.Position = 0;

            var cacheRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ARSAS",
                "Native",
                "ArdIrec",
                hash);
            Directory.CreateDirectory(cacheRoot);

            var destination = Path.Combine(cacheRoot, BridgeFileName);
            if (!File.Exists(destination) || new FileInfo(destination).Length != resource.Length)
            {
                var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    using (var output = new FileStream(
                               temporary,
                               FileMode.CreateNew,
                               FileAccess.Write,
                               FileShare.None,
                               64 * 1024,
                               FileOptions.WriteThrough))
                    {
                        resource.CopyTo(output);
                        output.Flush(flushToDisk: true);
                    }

                    File.Move(temporary, destination, overwrite: true);
                }
                finally
                {
                    if (File.Exists(temporary))
                        File.Delete(temporary);
                }
            }

            Environment.SetEnvironmentVariable(BridgeEnvironmentVariable, destination);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or CryptographicException)
        {
            // Fail closed. ArdIrecNativeBridge will surface its normal "bridge not installed"
            // message rather than making application startup depend on extraction permissions.
        }
    }
}
