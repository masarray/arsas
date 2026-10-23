using ArIED61850Tester.Services;

namespace ARSAS.Tests;

public sealed class ArdIrecViewerLauncherTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(),
        "ARSAS COMTRADE viewer tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveComtradeCfg_AcceptsUppercaseExtensionsAndPathWithSpaces()
    {
        var directory = CreateDirectory("Downloaded relay record with spaces");
        var cfg = Path.Combine(directory, "Fault 001.CFG");
        var dat = Path.Combine(directory, "Fault 001.DAT");
        File.WriteAllText(cfg, "cfg");
        File.WriteAllBytes(dat, [0x00]);

        var success = ArdIrecViewerLauncher.TryResolveComtradeCfg(
            directory,
            "Fault 001",
            out var resolved,
            out var error);

        Assert.True(success, error);
        Assert.Equal(Path.GetFullPath(cfg), resolved, ignoreCase: true);
    }

    [Fact]
    public void ResolveComtradeCfg_RejectsCfgWithoutMatchingDat()
    {
        var directory = CreateDirectory("Missing DAT");
        File.WriteAllText(Path.Combine(directory, "Fault002.cfg"), "cfg");
        File.WriteAllBytes(Path.Combine(directory, "DifferentRecord.dat"), [0x00]);

        var success = ArdIrecViewerLauncher.TryResolveComtradeCfg(
            directory,
            "Fault002",
            out var resolved,
            out var error);

        Assert.False(success);
        Assert.Equal(string.Empty, resolved);
        Assert.Contains("matching COMTRADE DAT", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ResolveComtradeCfg_PrefersPairMatchingSanitizedRecordName()
    {
        var directory = CreateDirectory("Multiple packages");
        File.WriteAllText(Path.Combine(directory, "Other.cfg"), "cfg");
        File.WriteAllBytes(Path.Combine(directory, "Other.dat"), [0x00]);

        var expectedCfg = Path.Combine(directory, "Relay_Fault.cfg");
        File.WriteAllText(expectedCfg, "cfg");
        File.WriteAllBytes(Path.Combine(directory, "Relay_Fault.dat"), [0x00]);

        var success = ArdIrecViewerLauncher.TryResolveComtradeCfg(
            directory,
            "Relay:Fault",
            out var resolved,
            out var error);

        Assert.True(success, error);
        Assert.Equal(Path.GetFullPath(expectedCfg), resolved, ignoreCase: true);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private string CreateDirectory(string name)
    {
        var directory = Path.Combine(_root, name);
        Directory.CreateDirectory(directory);
        return directory;
    }
}
